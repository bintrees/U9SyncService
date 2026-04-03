using Microsoft.Extensions.Options;
using Mysqlx.Session;
using MySqlX.XDevAPI.Common;
using System.Data;
using System.Net.Http.Headers;
using System.Reflection.Emit;
using System.Security.Cryptography.Pkcs;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using U9SyncService.Db;
using U9SyncService.Entities;
using U9SyncService.Model;
using U9SyncService.Utility;

namespace U9SyncService;

public class Worker : BackgroundService
{
    private readonly ICRMSyncService _syncService;
    private readonly IRepository<SyncQueue> _repo;
    private readonly IRepository<CV_Account> _accountRepo;
    private readonly IRepository<CV_Project> _projectRepo;
    private readonly IRepository<V_ProjectLedger> _projectLedgerRepo;
    private readonly AppOptions _options;
    private readonly ILogger<Worker> _logger;


    public Worker(ICRMSyncService syncService, ILogger<Worker> logger, IRepository<SyncQueue> repository,
        IRepository<CV_Account> accountRepo, IRepository<CV_Project> projectRepo,IRepository<V_ProjectLedger> projectLedgerRepo,
        IOptions<AppOptions> options)
    {
        _syncService = syncService;
        _logger = logger;
        _repo = repository;
        _accountRepo = accountRepo;
        _projectRepo = projectRepo;
        _projectLedgerRepo = projectLedgerRepo;
        _options = options.Value;

    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(" CRM --> U9 Worker started");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 同步数据
                await SyncService();

                // 处理队列
                await ConsumeQueue();

                // 即时台账同步

                await SyncLedger();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, " Worker Error ");

            }

            await Task.Delay(TimeSpan.FromMinutes(_options.Interval), stoppingToken);


        }
    }

    private async Task SyncLedger()
    {
        var ledgers = await _syncService.GetLedgersAsync();

        if (ledgers?.Count > 0)
        {
            await PostToU9(ledgers);
        }
    }


    /// <summary>
    /// 处理队列项
    /// </summary>
    /// <param name="item"></param>
    /// <returns></returns>
    private async Task ProcessQueueItem(SyncQueue item)
    {
        _logger.LogInformation($"开始处理队列项: {item.Id}, 类型: {item.OptType}, 键值: {item.SourceKey}");

        // 调用U9接口
        var result = await PostToU9(item);

        // 更新队列状态        
        if (result.Success)
        {
            // 如编辑模式，成功后将EditFlag重置为0
            if (item.EditFlag == 1)
            {
                await _repo.ExecuteAsync(
                    "UPDATE SyncQueue SET EditFlag = 0,UpdateTime = GETDATE() WHERE Id = @Id",
                    new { Id = item.Id, Message = result.Message });

            }
            else
            {
                await _repo.ExecuteAsync(
                    "UPDATE SyncQueue SET State = 1, ErrorMsg = @Message,CbCode = @CbCode,RetryCount +=1, UpdateTime = GETDATE() WHERE Id = @Id",
                    new { Id=item.Id, result.Message, result.CbCode });
            }
        }
        else
        {
            // 如编辑模式，成功后将EditFlag重置为0
            if (item.EditFlag == 1)
            {
                // 失败时保持EditFlag不变，便于下次重试
                await _repo.ExecuteAsync(
                    "UPDATE SyncQueue SET ErrorMsg = @Message,RetryCount +=1, UpdateTime = GETDATE() WHERE Id = @Id",
                    new { Id = item.Id, Message = result.Message });

            }
            else
            {
                await _repo.ExecuteAsync(
                        "UPDATE SyncQueue SET State = 2, ErrorMsg = @Message,RetryCount +=1, UpdateTime = GETDATE() WHERE Id = @Id",
                        new { Id = item.Id, Message = result.Message });

            }

        }

        // 回写CRM
        await _syncService.WriteBack(item.SourceKey, result.CbCode, result.Message);

        await _repo.ExecuteAsync("UPDATE SyncQueue SET State = @State, ErrorMsg = @ErrorMsg,CbCode = @CbCode, RetryCount +=1,UpdateTime = GETDATE() WHERE Id = @Id",
            new {Id= item.Id, State = result.Success ? 1 : 2, ErrorMsg = result.Message, result.CbCode });
    }

    private async Task<(bool Success, string Message, string? CbCode)> PostToU9(SyncQueue queue)
    {
        if (queue == null || string.IsNullOrEmpty(queue.SourceKey))
            return (false, "队列项数据无效", null);

        try
        {
            string requestBody = await BuildRequestBody(queue);

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("http://192.168.9.213/KLWebAPI/API/KLAPIPack", content);

            string responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var responseObj = JsonHelper.Deserialize<U9Response>(responseContent);
                if (responseObj?.IsSuccess == true)
                {
                    return (true, "同步成功", responseObj.DocNo);
                }
                else
                {
                    // 提取错误信息
                    string errorMessage = !string.IsNullOrEmpty(responseObj?.Msg) ? responseObj.Msg : "";


                    _logger.LogWarning($"U9同步失败: {errorMessage}, 编号: {queue.SourceKey}");

                    return (false, errorMessage, null);
                }

            }

            else
            {
                return (false, $"HTTP错误: {response.StatusCode}, 内容: {responseContent}", null);
            }
        }

        catch (Exception ex)
        {
            return (false, $"发送请求异常: {ex.Message}", null);
        }


    }

    private async Task PostToU9(List<V_ProjectLedger> ledgers)
    {
        if (ledgers == null || ledgers.Count == 0)
            return;
           

        foreach (var ledger in ledgers)
        {
            try
            {
                // 台账中项目是否已同步
                var project = (await _syncService.GetQueuesAsync("ProjectCreate", ledger.ProjectNum)).FirstOrDefault();
                if (project == null || project.State != 1)
                {
                    await _syncService.SyncProjects(ledger.ProjectNum);
                    await ConsumeQueue();
                }
                else if(project != null && project.State == 1)
                {
                    await _repo.ExecuteAsync("UPDATE SyncQueue SET RetryCount =3,EditFlag=1, UpdateTime = GETDATE() WHERE SourceKey = @SourceKey",
                       new { SourceKey= ledger.ProjectNum });
                }

                // 客户是否同步成功
                if (string.IsNullOrEmpty(ledger.CusCode))
                {
                    var sicCide = "C" + ledger.AccountId.ToString().PadLeft(8, '0');
                    var acQueue = (await GetQueuesWhere(sicCide)).FirstOrDefault();

                    if(acQueue != null)
                    {
                        await _repo.ExecuteAsync("UPDATE SyncQueue SET RetryCount =3, UpdateTime = GETDATE() WHERE SourceKey = @SourceKey",
                        new { acQueue.SourceKey});
                    }
                }


                string requestBody = await BuildProLedgerCreateBody(ledger);

                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(30);
                httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
                var response = await httpClient.PostAsync("http://192.168.9.213/KLWebAPI/API/KLAPIPack", content);

                string responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var responseObj = JsonHelper.Deserialize<U9Response>(responseContent);
                    if (responseObj?.IsSuccess == true)
                    {
                        switch (ledger.TransType)
                        {
                            case "A":
                                // 更新台账状态为已同步 (0表示已成功)
                                await _projectLedgerRepo.ExecuteAsync(
                                    "UPDATE SalesContract SET State = 0,TransType = 'U',SyncMsg ='同步成功', U9Code = @DocNo WHERE Id = @Id",
                                    new {Id= ledger.RefId, responseObj.DocNo },
                                    dbName: DbNames.Third.ToString());
                                break;

                            default:
                                // 更新台账状态为已同步 (0表示已成功)
                                await _projectLedgerRepo.ExecuteAsync(
                                    "UPDATE SalesContract SET State = 0  WHERE Id = @Id",
                                    new {Id= ledger.RefId },
                                    dbName: DbNames.Third.ToString());
                                break;

                        }

                        _logger.LogInformation($"项目台账同步成功: {ledger.ProjectNum}");
                    }
                    else
                    {

                        await _projectLedgerRepo.ExecuteAsync("UPDATE SalesContract SET State = -1,SyncMsg = @ErrorMsg WHERE Id = @Id",
                        new { Id = ledger.RefId, ErrorMsg = responseObj?.Msg },
                        dbName: DbNames.Third.ToString());
                        _logger.LogWarning($"U9同步失败: {responseObj?.Msg}, Id: {ledger.RefId}");


                    }

                }

                else
                {
                    _logger.LogWarning($"U9同步失败: {responseContent}, Id: {ledger.Id}");
                }

            }
            catch (Exception ex)
            {
                //// 单个项目异常不影响其他项目
                //await _projectLedgerRepo.ExecuteAsync(
                //    "UPDATE ProjectLedger SET State = -1, ErrorMsg = @ErrorMsg WHERE Id = @Id",
                //    new { Id = ledger.Id, ErrorMsg = $"处理异常: {ex.Message}" },
                //    dbName: DbNames.Third.ToString());

                _logger.LogError(ex, $"处理项目台账异常: {ledger.Id}, 项目: {ledger.ProjectNum}");

            }
        }





    }

    private async Task SyncService()
    {
        // 拉取数据

        try
        {
            await _syncService.SyncAccounts();
            await _syncService.SyncProjects();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, " Queue 同步失败");
        }


    }

    /// <summary>
    /// 消费队列
    /// </summary>
    /// <returns></returns>
    private async Task ConsumeQueue()
    {

        // 获取待处理队列项
        string sql = "SELECT TOP 300 * FROM SyncQueue  where (State =1 and EditFlag =1 and RetryCount <=5) or (State in (0,2) and RetryCount <=3) ORDER BY CreateTime"; //
        //SELECT TOP 10 * FROM SyncQueue  where (State in (0) and RetryCount <=5) ORDER BY CreateTime
        var items = await _repo.QueryAsync(sql,
            dbName: DbNames.Main.ToString()
           );

        if (items != null && items.Any())
        {
            _logger.LogInformation($"获取到 {items.Count()} 条待处理队列数据");

            foreach (var item in items)
            {
                try
                {
                    await ProcessQueueItem(item);

                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"处理队列项 {item.Id} 失败");

                    // 更新失败状态和错误信息
                    await _repo.ExecuteAsync(
                        "UPDATE SyncQueue SET State = 2, ErrorMsg = @Message,RetryCount +=1, UpdateTime = GETDATE() WHERE Id = @Id",
                        new { Id = item.Id, Message = ex.Message.Length > 500 ? ex.Message.Substring(0, 500) : ex.Message });
                }
            }
        }
        else
        {
            _logger.LogInformation("没有发现需要处理的队列数据");
        }




    }

    private async Task<List<SyncQueue>> GetQueuesWhere(string? sourceKey)
    {
        string sql = sourceKey == null ? "SELECT  * FROM [SyncQueue]" :
            $"SELECT  * FROM [SyncQueue] where sourceKey ='{sourceKey}'";
        var list = await _repo.QueryAsync(sql);
        return list.ToList();
    }
    private async Task<string> BuildRequestBody(SyncQueue queue)
    {
        switch (queue.OptType)
        {
            case "CustomerCreate":
                return await BuildCustomerCreateBody(queue);

            case "ProjectCreate":
                return await BuildProjectCreateBody(queue);

            default:
                throw new NotSupportedException($"不支持的操作类型: {queue.OptType}");
        }
    }

    private async Task<string> BuildCustomerCreateBody(SyncQueue queue)
    {
        Dictionary<string, object> customerData = new Dictionary<string, object>();
        var users = await GetUsers();
        // 根据 EditFlag 处理是创建还是更新
        bool isEdit = queue.EditFlag == 1;
        string optType = isEdit ? "CustomerUpdate" : "CustomerCreate";

        if (isEdit)
        {
            var cus = (await _accountRepo.QueryAsync("select * from CV_Account where SicCode =@SicCode ", new { SicCode = queue.SourceKey },
                dbName: DbNames.Middle.ToString())).FirstOrDefault();
            if (cus == null)
            {
                _logger.LogWarning($"未找到数据: {queue.SourceKey}");
                return JsonHelper.Serialize(new { });
            }
            // 更新客户名称用合同的
            var projectLegder = (await _projectLedgerRepo.QueryAsync("select * from V_ProjectLedger where CusCode=@CusCode", new { CusCode = queue.CbCode }, dbName: DbNames.Third.ToString())).FirstOrDefault();
            string cusName = projectLegder?.ClientName ?? cus.Account;

            var updateRequest = new
            {
                EntCode = "001", // 002
                OrgCode = "100",
                UserCode = "U9admin",
                OptType = optType,
                CustDTO = new
                {
                    CustCode = queue.CbCode,
                    Name = cusName,
                    ShortName = cus.Abbreviation,
                    SearchCode = cus.SicCode,
                    CustomerCategory = "KH0201", // 默认值，可根据业务规则映射
                    TradeCurrency = "C009",
                    TaxSchedule = "TS01",
                    PayCurrency = "C009",
                    RecervalTerm = "YZ01",
                    ARConfirmTerm = "YZ01",
                    PubPriDt = new
                    {
                        PrivateDescSeg2 = cus.AccountEN,
                        PrivateDescSeg3 = cus.SalesLine,
                        PubDescSeg4 = cus.Owner,
                        PubDescSeg5 = users.FirstOrDefault(p => p.UserName == cus.Owner)?.Territory,
                        PrivateDescSeg4 = cus.ShipAddress,
                        PrivateDescSeg5 = cus.Contact,
                        PrivateDescSeg6 = cus.Phone,
                        PrivateDescSeg7 = cus.AccountLevel,
                        PrivateDescSeg8 = cus.Source,
                        PrivateDescSeg9 = cus.SourceLv2,
                        PrivateDescSeg11 = cus.Regions,
                        PrivateDescSeg12 = cus.City1,
                        PrivateDescSeg14 = cus.AccountGroup,
                        PrivateDescSeg15 = cus.CooperationType,
                        PrivateDescSeg17 = users.FirstOrDefault(p => p.UserName == cus.Owner)?.Manager

                    }
                }
            };

            return JsonHelper.Serialize(updateRequest);
        }
        else
        {

            // 解析队列中的Payload
            customerData = JsonHelper.Deserialize<Dictionary<string, object>>(queue.Payload);

            // 创建U9接口所需的数据结构
            var createRequest = new
            {
                EntCode = "001",
                OrgCode = "100",
                UserCode = "U9admin",
                OptType = optType,
                CustDTO = new
                {
                    CustCode = queue.SourceKey,
                    Name = customerData.TryGetValue("account", out var name) ? name?.ToString() : "",
                    ShortName = customerData.TryGetValue("abbreviation", out var shortName) ? shortName?.ToString() : "",
                    SearchCode = customerData.TryGetValue("sicCode", out var searchCode) ? searchCode?.ToString() : "",
                    CustomerCategory = "KH0201", // 默认值，可根据业务规则映射
                    TradeCurrency = "C009",
                    TaxSchedule = "TS01",
                    PayCurrency = "C009",
                    RecervalTerm = "YZ01",
                    ARConfirmTerm = "YZ01",
                    PubPriDt = new
                    {
                        PrivateDescSeg2 = GetValueOrDefault(customerData, "accountEN"),
                        PrivateDescSeg3 = GetValueOrDefault(customerData, "salesLine"),
                        PubDescSeg4 = GetValueOrDefault(customerData, "owner"),
                        PubDescSeg5 = users.FirstOrDefault(p=>p.UserName== GetValueOrDefault(customerData, "owner"))?.Territory,
                        PrivateDescSeg4 = GetValueOrDefault(customerData, "shipAddress"),
                        PrivateDescSeg5 = GetValueOrDefault(customerData, "contact"),
                        PrivateDescSeg6 = GetValueOrDefault(customerData, "phone"),
                        PrivateDescSeg7 = GetValueOrDefault(customerData, "accountLevel"),
                        PrivateDescSeg8 = GetValueOrDefault(customerData, "source"),
                        PrivateDescSeg9 = GetValueOrDefault(customerData, "sourceLv2"),
                        PrivateDescSeg11 = GetValueOrDefault(customerData, "regions"),
                        PrivateDescSeg12 = GetValueOrDefault(customerData, "city1"),
                        PrivateDescSeg14 = GetValueOrDefault(customerData, "accountGroup"),
                        PrivateDescSeg15 = GetValueOrDefault(customerData, "cooperationType"),
                        PrivateDescSeg17 = users.FirstOrDefault(p => p.UserName == GetValueOrDefault(customerData, "owner"))?.Manager
                    }
                }
            };

            return JsonHelper.Serialize(createRequest);

        }

    }

    private async Task<string> BuildProjectCreateBody(SyncQueue queue)
    {
        // 根据 EditFlag 处理是创建还是更新
        bool isEdit = queue.EditFlag == 1;
        string optType = isEdit ? "ProjectUpdate" : "ProjectCreate";

        // 所有项目列表
        var projects = await _projectRepo.QueryAsync("select a.*,b.CreateDate as CompletionDate from CV_Project a Left join MT_CRM..ProjectFinished b on a.DealNum=b.ProjectNum",   dbName: DbNames.Middle.ToString());
        var accounts = await _accountRepo.QueryAsync("select * from CV_Account ", dbName: DbNames.Middle.ToString());

        var users = await GetUsers();
        if (isEdit)
        {
            //var project = (await _projectRepo.QueryAsync("select * from CV_Project where DealNum =@dealNum ", new { queue.SourceKey },
            //   dbName: DbNames.Middle.ToString())).FirstOrDefault();

            var project = projects.Where(p => p.DealNum == queue.SourceKey).FirstOrDefault();
            var account = accounts.Where(p => p.AccountId == project?.AccountId).FirstOrDefault();

            if (project == null)
            {
                _logger.LogWarning($"未找到数据: {queue.SourceKey}");
                return JsonHelper.Serialize(new { });

            }

            var requestData = new
            {
                EntCode = "001",
                OrgCode = "100",
                UserCode = "U9admin",
                OptType = optType,
                ProjectDTO = new
                {
                    Code = queue.SourceKey,
                    Name = project.Deal,
                    Customer = account?.U9Code ?? "",
                    ProjectType = "2003", // 默认2003- 订单项目
                    PubPriDt = new
                    {
                        PubDescSeg4 = project.Owner,
                        PubDescSeg5 = users.Where(p => p.UserName == project.Owner)?.FirstOrDefault()?.Territory,
                        PubDescSeg7 = GetProductCatalog(project.Catalog),
                        PrivateDescSeg1 = project.IsSubitem ? project.Parent : null,
                        PrivateDescSeg2 = project.BalanType,
                        PrivateDescSeg3 = project.ProjectAttribute,
                        PrivateDescSeg4 = Dicts.GetCompany(project.SignedComp),
                        PrivateDescSeg5 = project.Address1,
                        PrivateDescSeg6 = users.Where(p => p.UserName == project.Owner)?.FirstOrDefault()?.Manager,
                        PrivateDescSeg7 = "",
                        PrivateDescSeg10 = project.IsSubitem ? true : false,
                        PrivateDescSeg11 = project.Account,
                        PrivateDescSeg13 = project.CompletionDate ?? null
                    }
                }
            };

            return JsonHelper.Serialize(requestData);
        }
        else
        {
            // 解析队列中的Payload
            var projectData = JsonHelper.Deserialize<Dictionary<string, object>>(queue.Payload);

            var project = projects.Where(p => p.DealNum == queue.SourceKey).FirstOrDefault();


            // 创建U9接口所需的数据结构
            bool isSubItem =false;
            //isSubItem = projectData.TryGetValue("isSubitem", out var value)   && value is bool subitemBool
            //    ? subitemBool : false;

            if (projectData.TryGetValue("isSubitem", out var value) && value is JsonElement element)
            {
                // 如果 JSON 是布尔值
                if (element.ValueKind == JsonValueKind.True)
                    isSubItem = true;
                else if (element.ValueKind == JsonValueKind.False)
                    isSubItem = false;
                else if (element.ValueKind == JsonValueKind.String)
                    isSubItem = bool.TryParse(element.GetString(), out var result) && result;
                else
                    isSubItem = false; // 其他情况默认 false
            }

            /*  if (projectData.TryGetValue("isSubitem", out var value))
              {
                  if (value is bool b) // 如果 JSON 直接是布尔值
                      isSubItem = b;
                  else if (value is string s) // 如果 JSON 是字符串 "true" / "false"
                      isSubItem = bool.TryParse(s, out var result) && result;
                  else if (value != null) // 其他类型，可以尝试 Convert
                      isSubItem = Convert.ToBoolean(value);
              }*/

            var requestData = new
            {
                EntCode = "001",
                OrgCode = "100",
                UserCode = "U9admin",
                OptType = optType,
                ProjectDTO = new
                {
                    Code = queue.SourceKey,
                    Name = projectData.TryGetValue("deal", out var name) ? name?.ToString() : "",
                    Customer = project?.U9Code ?? "",
                    ProjectType = "2003",
                    PubPriDt = new
                    {
                        PubDescSeg4 = projectData.TryGetValue("owner", out var owner) ? owner?.ToString() : "",
                        PubDescSeg5 = users.Where(p => p.UserName == owner?.ToString())?.FirstOrDefault()?.Territory,
                        PubDescSeg7 = GetProductCatalog(project.Catalog),
                        PrivateDescSeg1 = isSubItem ? project?.Parent :null , //queue.SourceKey
                        PrivateDescSeg2 = projectData.TryGetValue("balanType", out var BalanType) ? BalanType?.ToString() : "",
                        PrivateDescSeg3 = projectData.TryGetValue("projectAttribute", out var ProjectAttribute) ? ProjectAttribute?.ToString() : "",
                        PrivateDescSeg4 = projectData.TryGetValue("signedComp", out var SignedComp) ? Dicts.GetCompany(SignedComp?.ToString() ?? "") : "",
                        PrivateDescSeg5 = projectData.TryGetValue("address1", out var Address1) ? Address1?.ToString() : "",
                        PrivateDescSeg6 = users.Where(p => p.UserName == owner?.ToString())?.FirstOrDefault()?.Manager,
                        PrivateDescSeg7 = "",
                        PrivateDescSeg10 = isSubItem,
                        PrivateDescSeg11 = projectData.TryGetValue("account", out var Account) ? Account?.ToString() : "",

                    }
                }
            };

            return JsonHelper.Serialize(requestData);
        }

    }

    public async Task<string> BuildProLedgerCreateBody(V_ProjectLedger ledger)
    {
        // 根据 EditFlag 决定是创建还是更新
        bool isEdit = ledger.TransType == "U";
        string optType = isEdit ? "ProjectLedgerUpdate" : "ProjectLedgerCreate";

        try
        {
            // 构建收款阶段明细
            var stageDetails = new List<object>();
            if (ledger.ProRecBillStage != null && ledger.ProRecBillStage.Any())
            {
                int i = 0;
                foreach (var stage in ledger.ProRecBillStage)
                {
                    i++;
                    stageDetails.Add(new
                    {
                        LineNum = i,
                        ProjectRecStage = stage.PaymentStage,
                        StageRatio = stage.Ratio,
                        StageAmount = ledger.ContractAmount * stage.Ratio/100
                    });
                }
            }

            // 构建请求数据
            var requestData = new
            {
                EntCode = "001",
                OrgCode = "100",
                UserCode = "U9admin",
                OptType = optType,
                ProjectLedgerDTO = new
                {
                    DocumentType = "001",
                    Project = ledger.ProjectNum,
                    Customer = ledger.CusCode,
                    SignDate = ledger.SignedDate.ToString("yyyy-MM-dd"),
                    BidBond = ledger.BidBond,
                    Warranty = ledger.Warranty,
                    ContractType = ledger.ContractType,
                    IPFee = ledger.IPFee,
                    ContractAmount = ledger.ContractAmount,
                    SignCompany = Dicts.GetCompany(ledger.SignCompany),
                    Currency = ledger.Currency,
                    ProjectRecBillStage = stageDetails.ToArray()
                }
            };

            string json = JsonHelper.Serialize(requestData);

            return json;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"构建项目台账数据异常: {ledger.Id}");
            throw;
        }




    }

    // 辅助方法，获取字典值
    private string GetValueOrDefault(Dictionary<string, object> dict, string key)
    {
        if (dict.TryGetValue(key, out var value) && value != null)
            return value.ToString();
        return string.Empty;
    }

    private async Task<List<UserInfo>> GetUsers()
    {
        var _users = (await _syncService.GetUsers()).ToList();

        return _users;
    }

    private string GetProductCatalog(string name)
    {
        string code = "";
        switch (name)
        {

            case "户外专线":
                code = "01";
                break;
            case "水乐专线":
                code = "02";
                break;
            case "淘气堡专线":
                code = "03";
                break;
            case "幼教专线":
                code = "04";
                break;
            case "安装":
                code = "05";
                break;
            case "运杂费":
                code = "06";
                break;
            case "咨询设计":
                code = "07";
                break;
            case "居间":
                code = "08";
                break;
            case "对外采购":
                code = "09";
                break;
            case "其他":
                code = "10";
                break;
           
        }

        return code;
    }
}

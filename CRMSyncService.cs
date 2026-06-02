using MySqlX.XDevAPI.Common;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using U9SyncService.Db;
using U9SyncService.Entities;
using U9SyncService.Model;
using U9SyncService.Utility;
using static Azure.Core.HttpHeader;

namespace U9SyncService
{
    public class CRMSyncService :ICRMSyncService
    {

        private readonly IRepository<CV_Account> _accountRepo;
        private readonly IRepository<CV_Project> _projectRepo;
        private readonly IRepository<V_ProjectLedger> _projectLedgerRepo;
        private readonly IRepository<ProjectPaymentLine> _recBillStageRepo;
        private readonly IRepository<SyncQueue> _queueRepo;
        private readonly IRepository<UserInfo> _userRepo;
        private readonly ILogger<CRMSyncService> _logger;

        public CRMSyncService(
            IRepository<CV_Account> accountRepo,
            IRepository<CV_Project> projectRepo,
            IRepository<V_ProjectLedger> projectLedgerRepo,
            IRepository<ProjectPaymentLine> recBillStageRepo,
        IRepository<SyncQueue> queueRepo,
            IRepository<UserInfo> userRepo,
            ILogger<CRMSyncService> logger

     )
        {
            _accountRepo = accountRepo;
            _projectRepo = projectRepo;
            _projectLedgerRepo = projectLedgerRepo;
            _recBillStageRepo = recBillStageRepo;
            _queueRepo = queueRepo;
            _userRepo = userRepo;
            _logger = logger;
        }

        public async Task SyncAccounts()
        {
            var accounts = await _accountRepo.QueryAsync(
                "select T0.* from CV_Account T0 INNER JOIN [MT_CRM].[dbo].[V_ProjectLedger] T1 ON T1.AccountId=T0.AccountId", // T1.ClientName as Account,
                dbName: DbNames.Middle.ToString());

            var existQueues = await GetQueuesAsync("CustomerCreate");
            var sicCodes = existQueues.Select(p => p.SourceKey).ToList();
            var toInsert = accounts.Where(p => !sicCodes.Contains(p.SicCode)).ToList();
            var toUpdate = accounts.Where(p => p.Refresh ==1 && p.U9Code !=null).ToList();

            foreach (var acc in toInsert)
            {
                var queue = new SyncQueue
                {
                    OptType = "CustomerCreate",
                    SourceKey = acc.SicCode,
                    EditFlag =0,
                    Payload = JsonHelper.Serialize(acc)
                };

                await InsertQueue(queue);
            }
            // 更新已存在队列，标记 CRM 手工刷新
            foreach (var acc in toUpdate)
            {
                await UpdateQueue(acc.SicCode);
            }
            //await Task.WhenAll(toUpdate.Select(acc => UpdateQueue(acc.SicCode))); //批量异步：
        }

        public async Task<IEnumerable<UserInfo>> GetUsers()
        {
            return await _userRepo.QueryAsync("select b.Territory,a.UserName,b.Owner as Manager from UserTable a left join territory b on a.Territory =b.TerritoryId where b.Territory is not null order by b.Territory",
               dbName: DbNames.Third.ToString()
                );

        }

        public async Task SyncProjects(string? projectNum = null)
        {

            string sql = projectNum == null ? "select a.* from CV_Project a Left JOIN MT_CRM..CT_Project b on b.ProjectNum=a.DealNum where a.status is null and (a.CreateDate >='2026-01-01' or b.CT_ProjectId is not null ) order by ProjectId desc" :
                 $"select * from CV_Project where DealNum='{projectNum}'";
 
            var projects = await _projectRepo.QueryAsync(sql,dbName: DbNames.Middle.ToString());

            var existQueues = await GetQueuesAsync("ProjectCreate");
            var projectNums = existQueues.Select(p => p.SourceKey).ToList();
            var toInsert = projects.Where(p => !projectNums.Contains(p.DealNum)).ToList();

            foreach (var proj in toInsert)
            {
                var queue = new SyncQueue
                {
                    OptType = "ProjectCreate",
                    SourceKey = proj.DealNum,
                    EditFlag=0,
                    Payload = JsonHelper.Serialize(proj)
                };

                await InsertQueue(queue);
            }
        }

        /// <summary>
        /// 递归 获取项目树
        /// </summary>
        /// <param name="parentNum"></param>
        /// <param name="projectList"></param>
        /// <returns></returns>
        public List<CV_Project> GetProjectTree(string parentNum, List<CV_Project> projectList)
        {
            var result = new List<CV_Project>();

            var parent = projectList.FirstOrDefault(p => p.DealNum == parentNum);
            if (parent != null)
                result.Add(parent);

            var children = projectList.Where(p => p.Parent == parentNum).ToList();

            foreach (var child in children)
            {
                result.AddRange(GetProjectTree(child.DealNum, projectList));
            }

            return result;
        }


        /// <summary>
        /// 获取要同步的项目台账
        /// </summary>
        /// <returns></returns>
        public async Task<List<V_ProjectLedger>?> GetLedgersAsync()
        {
            var ledgers = await _projectLedgerRepo.QueryAsync(
               "select top 10 * from V_ProjectLedger where State =1 ORDER BY CreateDate",
               dbName: DbNames.Third.ToString());

            if (ledgers ==null || !ledgers.Any())
                return null;

            var refIds = ledgers.Select(p => p.RefId).ToList();
            // 获取阶段明细数据
            var stageDetails = new List<object>();
            var stages = await _recBillStageRepo.QueryAsync(
                $"SELECT * FROM ProjectPaymentLine WHERE RefId in({string.Join(",", refIds)}) ORDER BY RefId ,Id",
                dbName: DbNames.Third.ToString());

            // 项目树数据：母项目、子项目、孙项目
            var cbProjects = (await _projectRepo.QueryAsync(@"SELECT * FROM CV_Project",
                dbName: DbNames.Middle.ToString())).ToList();

            // 所有已填写销售合同的台账，用来生成 ContractLines
            var allContracts = (await _projectLedgerRepo.QueryAsync(
                @"SELECT *   FROM V_ProjectLedger  WHERE ContractAmount IS NOT NULL  AND ProjectNum IS NOT NULL",
                dbName: DbNames.Third.ToString())).ToList();

            // 母项目台账中 所有子项目的合同
            var contracts = await _projectLedgerRepo.QueryAsync("select * from CV_Project", dbName: DbNames.Middle.ToString());

            foreach (var ledger in ledgers)
            {
                ledger.ProRecBillStage = stages.Where(o => o.RefId == ledger.RefId).ToList();

                if (string.IsNullOrWhiteSpace(ledger.ProjectNum))
                {
                    ledger.ContractLines = new List<ProjectContractLine>();
                    continue;
                }
                var projectTree = GetProjectTree(ledger.ProjectNum, cbProjects);
                var projectNums = projectTree.Select(p => p.DealNum).Distinct().ToHashSet();

                int lineNum = 1;
                ledger.ContractLines = allContracts
                    .Where(c=>projectNums.Contains(c.ProjectNum))
                    .OrderBy(c=>c.SignedDate)
                    .Select(s => new ProjectContractLine
                {
                    LineNum = lineNum++,
                    Subproject = s.ProjectNum,
                    ContractAmount = s.ContractAmount,
                    ContractSignDate = s.SignedDate,
                    ContractType = s.ContractType,

                }).ToList();
            }


            return ledgers.ToList();
        }

        public async Task WriteBack(string SourceKey, string? CbCode, string ErrorMsg,bool success,bool isEdit)
        {
            string[] _code = { "205", "206" };
            var startWith = ErrorMsg?.Length >= 3 ? ErrorMsg.Substring(0, 3) : "";
            var code = _code.Contains(startWith) ? startWith : (success ? "200" : "300");

            switch (SourceKey.Substring(0, 1))
            {
                case "C":
                    if (isEdit && code == "206")
                    {
                        await _accountRepo.ExecuteAsync("UPDATE Account SET Refresh=0,U9Code = @CbCode, U9ErrorMsg = @ErrorMsg WHERE  SicCode = @SourceKey",
                        new { SourceKey, ErrorMsg, CbCode }, dbName: DbNames.Third.ToString());


                    }
                    else if (isEdit)
                    {
                        await _accountRepo.ExecuteAsync("UPDATE Account SET Refresh=0, U9ErrorMsg = @ErrorMsg WHERE  SicCode = @SourceKey",
                        new { SourceKey, ErrorMsg }, dbName: DbNames.Third.ToString());

                    }
                    else
                    {
                        await _accountRepo.ExecuteAsync("UPDATE Account SET Status = @Status,U9Code = @CbCode, U9ErrorMsg = @ErrorMsg WHERE (U9Code is null or Status ='201') and SicCode = @SourceKey",
                       new { SourceKey, Status = code, ErrorMsg, CbCode }, dbName: DbNames.Third.ToString());
                    }
                   
                    break;
                case "P":
                    if (SourceKey.Length > 9)
                    {
                        await _accountRepo.ExecuteAsync("UPDATE Project SET U9ErrorMsg = @ErrorMsg,U9Code = @CbCode WHERE DealNum = @SourceKey",
                        new { SourceKey, ErrorMsg, CbCode = success ? 200 : 300 }, dbName: DbNames.Third.ToString());
                        
                    }
                    else
                    {
                        await _accountRepo.ExecuteAsync("UPDATE Deal SET U9ErrorMsg = @ErrorMsg,U9Code = @CbCode WHERE DealNum = @SourceKey",
                        new { SourceKey, ErrorMsg, CbCode = success ? 200 : 300 }, dbName: DbNames.Third.ToString());
                        
                    }
                    break;

            }
        }

        public async Task RefreshSyncQueue(string CbCode)
        {
            if (string.IsNullOrEmpty(CbCode))
                return;

            try
            {
                await _queueRepo.ExecuteAsync("update SyncQueue set RetryCount=2,EditFlag=1  where State =1 and CbCode =@CbCode", new { CbCode }, dbName: DbNames.Main.ToString());
            
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, @$"Update失败:{ex.Message}");
            }
        }

        private async Task InsertQueue(SyncQueue queue)
        {
            try
            {
                const string sql = @"IF NOT EXISTS (SELECT 1 FROM SyncQueue  WHERE OptType = @OptType AND SourceKey = @SourceKey )
            BEGIN
            INSERT INTO SyncQueue (OptType, SourceKey, Payload, State,EditFlag, CreateTime)
            VALUES
            (@OptType, @SourceKey, @Payload, 0,0, GETDATE())
            END";

                await _queueRepo.ExecuteAsync(sql, queue, dbName: DbNames.Main.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, @$"Insert失败:{ex.Message}");
            }

        }

        private async Task UpdateQueue(string sourceKey)
        {
            try
            {
                await _queueRepo.ExecuteAsync("update SyncQueue set RetryCount=2,EditFlag=1  where State =1 and SourceKey =@SourceKey", new { SourceKey =sourceKey }, dbName: DbNames.Main.ToString());

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"更新队列异常: {sourceKey}");
            }

        }

        /// <summary>
        /// 查询目前的队列
        /// </summary>
        /// <param name="optType"></param>
        /// <returns></returns>
        public async Task<IEnumerable<SyncQueue>> GetQueuesAsync(string optType ,string? sourceKey = null)
        {
            string sql = sourceKey == null ? $"SELECT * FROM SyncQueue WHERE OptType = '{optType}'" :
                $"SELECT * FROM SyncQueue WHERE OptType = '{optType}' and SourceKey = '{sourceKey
                }'";
           var queues =await _queueRepo.QueryAsync(sql);

            return queues;
        }


    }

    public interface ICRMSyncService
    {
        Task<List<V_ProjectLedger>?> GetLedgersAsync();
        Task<IEnumerable<SyncQueue>> GetQueuesAsync(string optType, string? sourceKey = null);
        Task<IEnumerable<UserInfo>> GetUsers();
        Task RefreshSyncQueue(string CbCode);
        Task SyncAccounts();
        Task SyncProjects(string? projectNum = null);
        Task WriteBack(string SourceKey, string? CbCode, string ErrorMsg, bool success, bool isEdit);
    }
}

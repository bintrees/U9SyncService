using MySqlX.XDevAPI.Common;
using System;
using System.Collections.Generic;
using System.Linq;
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
        private readonly IRepository<ProjectLedger> _projectLedgerRepo;
        private readonly IRepository<RecBillStage> _recBillStageRepo;
        private readonly IRepository<SyncQueue> _queueRepo;
        private readonly IRepository<UserInfo> _userRepo;
        private readonly ILogger<CRMSyncService> _logger;

        public CRMSyncService(
            IRepository<CV_Account> accountRepo,
            IRepository<CV_Project> projectRepo,
            IRepository<ProjectLedger> projectLedgerRepo,
            IRepository<RecBillStage> recBillStageRepo,
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
                "select * from CV_Account where CreateDate >='2023-01-01' and ISNULL(Status,'')!='200' ",
                dbName: DbNames.Middle.ToString());

            foreach (var acc in accounts)
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
        }

        public async Task<IEnumerable<UserInfo>> GetUsers()
        {
            return await _userRepo.QueryAsync("select b.Territory,a.UserName,Title from UserTable a left join territory b on a.Territory =b.TerritoryId where Stopped=0 order by b.Territory",
               dbName: DbNames.Third.ToString()
                );

        }

        public async Task SyncProjects()
        {
           
            var projects = await _projectRepo.QueryAsync(
                "select top 300 * from CV_Project where CreateDate >='2025-01-01' or DealNum ='P230220175714'",
                dbName: DbNames.Middle.ToString());

            foreach (var proj in projects)
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
        /// 获取要同步的项目台账
        /// </summary>
        /// <returns></returns>
        public async Task<List<ProjectLedger>?> GetLedgersAsync()
        {
            var ledgers = await _projectLedgerRepo.QueryAsync(
               "select top 10 * from ProjectLedger where State =1 ORDER BY CreateDate",
               dbName: DbNames.Third.ToString());

            if (ledgers ==null || !ledgers.Any())
                return null;

            var projectIds = ledgers.Select(p => p.Id).ToList();
            // 获取阶段明细数据
            var stageDetails = new List<object>();
            var stages = await _recBillStageRepo.QueryAsync(
                $"SELECT * FROM ProRecBillStage WHERE ProjectId in({string.Join(",", projectIds)}) ORDER BY ProjectId ,LineNum",
                dbName: DbNames.Third.ToString());


            foreach (var ledger in ledgers)
            {
                ledger.ProjectRecBillStage = stages.Where(o => o.ProjectId == ledger.Id).ToList();
            }


            return ledgers.ToList();
        }

        public async Task WriteBack(string SourceKey, string? CbCode, string ErrorMsg)
        {
            bool IsSuccess = !string.IsNullOrEmpty(CbCode);

            switch (SourceKey.Length)
            {
                case 9:
                    await _accountRepo.ExecuteAsync("UPDATE Account SET Status = @Status, U9ErrorMsg = @ErrorMsg,U9Code = @CbCode WHERE SicCode = @SourceKey",
                    new { SourceKey, Status = IsSuccess ? 200 : 300, ErrorMsg, CbCode }, dbName: DbNames.Third.ToString());
                    break;
                default:
                    await _accountRepo.ExecuteAsync("UPDATE Project SET U9ErrorMsg = @ErrorMsg,U9Code = @CbCode WHERE DealNum = @SourceKey",
                   new { SourceKey, ErrorMsg, CbCode = IsSuccess ? 200 : 300 }, dbName: DbNames.Third.ToString());
                    break;
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
    }

    public interface ICRMSyncService
    {
        Task<List<ProjectLedger>?> GetLedgersAsync();
        Task<IEnumerable<UserInfo>> GetUsers();
        Task SyncAccounts();
        Task SyncProjects();
        Task WriteBack(string SourceKey, string CbCode, string ErrorMsg);
    }
}

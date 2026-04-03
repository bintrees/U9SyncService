using MySqlX.XDevAPI.Common;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
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
                "select T1.ClientName as Account,T0.* from CV_Account T0 INNER JOIN [MT_CRM].[dbo].[V_ProjectLedger] T1 ON T1.AccountId=T0.AccountId",
                dbName: DbNames.Middle.ToString());

            var existQueues = await GetQueuesAsync("CustomerCreate");
            var sicCodes = existQueues.Select(p => p.SourceKey).ToList();
            var toInsert = accounts.Where(p => !sicCodes.Contains(p.SicCode)).ToList();

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
        }

        public async Task<IEnumerable<UserInfo>> GetUsers()
        {
            return await _userRepo.QueryAsync("select b.Territory,a.UserName,b.Owner as Manager from UserTable a left join territory b on a.Territory =b.TerritoryId where Stopped=0 order by b.Territory",
               dbName: DbNames.Third.ToString()
                );

        }

        public async Task SyncProjects(string? projectNum = null)
        {
            string sql = projectNum == null ? "select * from CV_Project where CreateDate >='2026-01-01' order by ProjectId desc" :
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


            foreach (var ledger in ledgers)
            {
                ledger.ProRecBillStage = stages.Where(o => o.RefId == ledger.RefId).ToList();
            }


            return ledgers.ToList();
        }

        public async Task WriteBack(string SourceKey, string? CbCode, string ErrorMsg)
        {
            bool IsSuccess = !string.IsNullOrEmpty(CbCode);

            switch (SourceKey.Substring(0, 1))
            {
                case "C":
                    await _accountRepo.ExecuteAsync("UPDATE Account SET Status = @Status, U9ErrorMsg = @ErrorMsg,U9Code = @CbCode WHERE U9Code is null and SicCode = @SourceKey",
                    new { SourceKey, Status = IsSuccess ? 200 : 300, ErrorMsg, CbCode }, dbName: DbNames.Third.ToString());
                    break;
                case "P":
                    if (SourceKey.Length > 9)
                    {
                        await _accountRepo.ExecuteAsync("UPDATE Project SET U9ErrorMsg = @ErrorMsg,U9Code = @CbCode WHERE DealNum = @SourceKey",
                        new { SourceKey, ErrorMsg, CbCode = IsSuccess ? 200 : 300 }, dbName: DbNames.Third.ToString());
                        
                    }
                    else
                    {
                        await _accountRepo.ExecuteAsync("UPDATE Deal SET U9ErrorMsg = @ErrorMsg,U9Code = @CbCode WHERE DealNum = @SourceKey",
                        new { SourceKey, ErrorMsg, CbCode = IsSuccess ? 200 : 300 }, dbName: DbNames.Third.ToString());
                        
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
                await _queueRepo.ExecuteAsync("update SyncQueue set EditFlag =1 ,RetryCount =1 where State =1 and CbCode =@CbCode", new { CbCode }, dbName: DbNames.Main.ToString());
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
        Task WriteBack(string SourceKey, string CbCode, string ErrorMsg);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace U9SyncService.Entities
{
    public class ProjectLedger
    {
        public int Id { get; set; }
        public string ProjectNum { get; set; }
        public string ContractType { get; set; }
        public DateTime SignDate { get; set; }
        public decimal BidBond { get; set; }
        public decimal Warranty { get; set; }
        public decimal IPFee { get; set; }
        public decimal ContractAmount { get; set; }
        public string SignCompany { get; set; }
        public string Currency { get; set; } // 币种
        public string Customer { get; set; }
        public string TransType { get; set; }
        public int State { get; set; }

        public List<RecBillStage> ProjectRecBillStage { get; set; } = new();
    }
}

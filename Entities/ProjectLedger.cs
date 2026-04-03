using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace U9SyncService.Entities
{
    public class V_ProjectLedger
    {
        public int RefId { get; set; }
        public int Id { get; set; }
        public string ClientName { get; set; }
        public string ProjectNum { get; set; }
        public string ContractType { get; set; }
        public DateTime SignedDate { get; set; }
        public decimal BidBond { get; set; }
        public decimal Warranty { get; set; }
        public decimal IPFee { get; set; }
        public decimal ContractAmount { get; set; }
        public string SignCompany { get; set; }
        public string Currency { get; set; } // 币种
        public string CusCode { get; set; }
        public string TransType { get; set; }
        public int State { get; set; }
        public string LedCode { get; set; }
        public int AccountId { get; set; }

        public List<ProjectPaymentLine> ProRecBillStage { get; set; } = new();
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace U9SyncService.Entities
{
    public class ProjectPaymentLine
    {
        public int RefId { get; set; }
        public string PaymentStage { get; set; }
        public decimal Ratio { get; set; }
        public decimal Amount { get; set; }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace U9SyncService.Entities
{
    public class RecBillStage
    {
        public int ProjectId { get; set; }
        public int LineNum { get; set; }
        public string RecStage { get; set; }
        public decimal Ratio { get; set; }
        public decimal Amount { get; set; }
    }
}

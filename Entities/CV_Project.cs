using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace U9SyncService.Entities
{
    public class CV_Project
    {
        public int? AccountId { get; set; }
        public string DealNum { get; set; }
        public string Deal { get; set; }
        public string Account { get; set; }
        public string Owner { get; set; }
        public string BalanType { get; set; }
        public string ProjectAttribute { get; set; }
        public string SignedComp { get; set; }
        public string Parent { get; set; }
        public bool IsSubitem { get; set; }
        public string Address1 { get; set; }
        public string CurrentDealStage { get; set; }
        public DateTime CreateDate { get; set; }
        public int ProjectId { get; set; }
        public string? U9Code { get; set; }
    }
}

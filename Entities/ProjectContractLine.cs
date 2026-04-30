using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace U9SyncService.Entities
{
    public class ProjectContractLine
    {
        public int LineNum { get; set; }
        public string ProductLine { get; set; }
        public decimal ContractAmount { get; set; }
        public string Subproject { get; set; }
        public string ContractNo { get; set; }
        public string ContractType { get; set; }
        public DateTime ContractSignDate { get; set; }

        
    }
}

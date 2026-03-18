using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace U9SyncService.Model
{
    public class SaleOrderChange
    {
        public string OaId { get; set; }
        
        public DateTime ApplyDate { get; set; }
        
        public string Initiator { get; set; }
        
        public string DocNum { get; set; }
        
        public DateTime OrderDate { get; set; }
        
        public string CardCode { get; set; }
        
        public string CardName { get; set; }
        
        public string Sales { get; set; }
        
        public string Department { get; set; }
        
        public string CostChanged { get; set; }
        
        public string ChangeContent { get; set; }


    }
}

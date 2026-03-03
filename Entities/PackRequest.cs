using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace U9SyncService.Entities
{
    public class PackRequest
    {
        public string EntCode { get; set; } = "001";
        public string OrgCode { get; set; } = "100";
        public string UserCode { get; set; } = "U9admin";
        public string OptType { get; set; }
        public CustDto CustDTO { get; set; }
    }
}

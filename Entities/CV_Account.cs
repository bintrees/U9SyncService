using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace U9SyncService.Entities
{
    public class CV_Account
    {
        public int AccountId { get; set; }
        public string SicCode { get; set; }
        public string Account { get; set; }
        public string AccountEN { get; set; }
        public string Abbreviation { get; set; }
        public string Department { get; set; }
        public string Owner { get; set; }
        public string SalesLine { get; set; }
        public string ShipAddress { get; set; }
        public string Contact { get; set; }
        public string Phone { get; set; }
        public string AccountLevel { get; set; }
        public string Source { get; set; }
        public string SourceLv2 { get; set; }
        public string Regions { get; set; }
        public string City1 { get; set; }
        public string AccountGroup { get; set; }
        public string CooperationType { get; set; }
        public string AccountType { get; set; }
        public string ClientState { get; set; }
        public DateTime CreateDate { get; set; }
    }
}

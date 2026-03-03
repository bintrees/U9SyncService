using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace U9SyncService.Entities
{
    public class CustDto
    {
        public string? CustCode { get; set; }
        public string Name { get; set; }
        public string? ShortName { get; set; }
        public string CustomerCategory { get; set; } = "KH0201"; // 客户分类码
        public string TradeCurrency { get; set; } = "C009";  //交易币种
        public string TaxSchedule { get; set; } = "TS01"; // 税组合码
        public string PayCurrency { get; set; } = "C009"; //收款币种编码
        public string RecervalTerm { get; set; } ="YZ01"; //收款条件编码
        public string ARConfirmTerm { get; set; } = "YZ01"; //立账条件编码
        public PubPriDt PubPriDt { get; set; }   
    }
}

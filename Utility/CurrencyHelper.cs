using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace U9SyncService.Utility
{
    public static class CurrencyHelper
    {
        public static string GetCurrencyKey(string? currencyName) // => CurrencyDic.FirstOrDefault(x => x.Value == currencyName).Key;
        {
            if (currencyName == null)
            {
                return "C009";
            }

            // 根据货币名称查找对应的 key
            foreach (var kvp in CurrencyDic)
            {
                if (kvp.Value == currencyName)
                {
                    return kvp.Key;
                }
            }

            return null;
        }

        public static string GetTaxCode(string? tax)
        {
            // 根据货币名称查找对应的 key
            foreach (var kvp in CurrencyDic)
            {
                if (kvp.Value == tax)
                {
                    return kvp.Key;
                }
            }

            return "TS01";
        }

        private  static Dictionary<string, string> CurrencyDic = new Dictionary<string, string>
        {

            { "C001", "人民币" },
            { "C002", "新台币" },
            { "C003", "港元" },
            { "C004", "日 圆" },
            { "C005", "欧元" },
            { "C006", "马克" },
            { "C007", "英镑" },
            { "C008", "法郎" },
            { "C009", "美元" },
            { "C010", "迪拉姆" },
            { "C011", "澳元" },
            { "C012", "加元" },
            { "C013", "雷亚尔" },
            { "CRM01", "马来西亚林吉特" }
        };

        private static Dictionary<string, string> CustomerTaxDic = new Dictionary<string, string>
        {
            {"TS01","13%" },
            {"TS02" ,"9%"}
        };

    }
}

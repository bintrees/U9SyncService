using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static U9SyncService.Model.Dicts;

namespace U9SyncService.Model
{
    public class Dicts
    {
    
        public class Company
        {
            public string CompanyNo { get; set; }
            public string Code { get; set; }
            public string Name { get; set; }

        }

        public static List<Company> Companies = new List<Company>()
        {
              new Company { CompanyNo = "100", Name = "数据中心" },
        new Company { CompanyNo = "101", Code = "C12", Name = "牧童企业（清远）管理有限公司" },
        new Company { CompanyNo = "102", Code = "C13", Name = "牧童（广东）投资中心（有限合伙）" },
        new Company { CompanyNo = "103", Code = "C14", Name = "牧童控股（广东）有限公司" },
        new Company { CompanyNo = "104", Code = "C15", Name = "广州牧童水上乐园设备有限公司工会" },
        new Company { CompanyNo = "105", Code = "C16", Name = "慕顿儿童游乐设备贸易有限责任公司" },
        new Company { CompanyNo = "106", Code = "C1", Name = "牧童集团（广东）实业有限公司" },
        new Company { CompanyNo = "107", Code = "C2", Name = "牧童实业（广东）有限公司" },
        new Company { CompanyNo = "108", Code = "C4", Name = "广州牧童水上乐园设备有限公司" },
        new Company { CompanyNo = "109", Code = "C3", Name = "广州牧童康体设备有限公司" },
        new Company { CompanyNo = "110", Code = "C6", Name = "广东智造乐园数据信息科技有限公司" },
        new Company { CompanyNo = "111", Code = "C7", Name = "牧童实业（广东）有限公司成都分公司" },
        new Company { CompanyNo = "112", Code = "C8", Name = "牧童实业（广东）有限公司郑州分公司" },
        new Company { CompanyNo = "113", Code = "C11", Name = "牧童集团有限公司 MOOTON GROUP INC" },
        new Company { CompanyNo = "114", Code = "C5", Name = "广州市畅凯游乐设备有限公司" },
        new Company { CompanyNo = "115", Name = "管理中心" }


        };



        public static string GetCompanyNo(string code)
        {
            return Companies
            .FirstOrDefault(c => c.Code == code)?.CompanyNo ?? string.Empty;
        }

        public static string GetCompany(string code)
        {
            return Companies
            .FirstOrDefault(c => c.Code == code)?.Name ?? string.Empty;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace U9SyncService.Db
{
    public interface IDbConnectionFactory
    {
        IDbConnection Create(string dbName);
    }
}

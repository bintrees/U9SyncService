using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace U9SyncService
{
    public interface IRepository<T>
    {
        Task<IEnumerable<T>> QueryAsync(
       string sql,
       object param = null,
       string dbName = "Main");

        Task<int> ExecuteAsync(
            string sql,
            object param = null,
            string dbName = "Main");
    }
}

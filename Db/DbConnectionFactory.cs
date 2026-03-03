using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace U9SyncService.Db
{
    public class DbConnectionFactory : IDbConnectionFactory
    {
        private readonly Databases _datas;

        public DbConnectionFactory(IOptions<Databases> options)
        {
            _datas = options.Value;
        }

        public IDbConnection Create(string dbName)
        {
            if (!_datas.TryGetValue(dbName, out var db))
                throw new Exception($"数据库配置不存在: {dbName}");

            return db.DbType switch
            {
                "SqlServer" => new SqlConnection(db.Connection),
                "MySql" => new MySqlConnection(db.Connection),
                _ => throw new NotSupportedException($"不支持的数据库类型: {db.DbType}")
            };

        }
    }
}

using Dapper;
using U9SyncService.Db;

namespace U9SyncService
{
    public class Repository<T>:IRepository<T>
    {
        private readonly IDbConnectionFactory _factory;

        public Repository(IDbConnectionFactory factory)
        {
            _factory = factory;
        }

        public async Task<IEnumerable<T>> QueryAsync(
            string sql,
            object param = null,
            string dbName = "Main")
        {
            using var conn = _factory.Create(dbName);
            return await conn.QueryAsync<T>(sql, param);
        }

        public async Task<int> ExecuteAsync(
            string sql,
            object param = null,
            string dbName = "Main")
        {
            using var conn = _factory.Create(dbName);
            return await conn.ExecuteAsync(sql, param);
        }

    }
}

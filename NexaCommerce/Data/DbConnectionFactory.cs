using System.Data;
using Microsoft.Data.SqlClient;

namespace NexaCommerce.Data;

public class DbConnectionFactory
{
    private readonly DatabaseStatus _status;
    public DbConnectionFactory(DatabaseStatus status) => _status = status;

    public SqlConnection Create() => new SqlConnection(_status.ConnectionString);

    public async Task<SqlConnection> OpenAsync()
    {
        var c = Create();
        await c.OpenAsync();
        return c;
    }
}

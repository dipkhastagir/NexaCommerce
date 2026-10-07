using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;

namespace NexaCommerce.Data;

/// <summary>
/// Startup pipeline: find a reachable SQL Server, create NexaCommerceDB if missing,
/// run the schema/view/procedure/seed scripts, then generate demo data.
/// </summary>
public class DatabaseInitializer
{
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private readonly DatabaseStatus _status;
    private readonly DataSeeder _seeder;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(IConfiguration config, IWebHostEnvironment env, DatabaseStatus status,
        DataSeeder seeder, ILogger<DatabaseInitializer> logger)
    {
        _config = config; _env = env; _status = status; _seeder = seeder; _logger = logger;
    }

    public async Task InitializeAsync()
    {
        _status.Log.Clear();
        _status.Error = null;
        _status.IsReady = false;
        try
        {
            var configured = _config.GetConnectionString("DefaultConnection");
            var builder = await ResolveServerAsync(configured);
            if (builder == null)
            {
                _status.Error = "Could not reach SQL Server with any known server name. " +
                                "Open appsettings.json and set ConnectionStrings:DefaultConnection to your server " +
                                "(the 'Server name' shown when you connect in SSMS).";
                return;
            }

            var dbName = string.IsNullOrWhiteSpace(builder.InitialCatalog) ? "NexaCommerceDB" : builder.InitialCatalog;
            builder.InitialCatalog = dbName;
            _status.ServerName = builder.DataSource;
            _status.ConnectionString = builder.ConnectionString;

            if (_config.GetValue("Database:AutoCreate", true))
                await EnsureDatabaseAsync(builder, dbName);

            await using var conn = new SqlConnection(builder.ConnectionString);
            await conn.OpenAsync();

            var hasSchema = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.tables WHERE name = 'Users'") > 0;
            if (!hasSchema)
            {
                await RunScriptAsync(conn, "01_Schema.sql");
                _status.Log.Add("Tables created.");
            }
            await RunScriptAsync(conn, "02_Views.sql");
            await RunScriptAsync(conn, "03_StoredProcedures.sql");
            await RunScriptAsync(conn, "04_SeedData.sql");
            _status.Log.Add("Views, stored procedures and reference data are up to date.");

            if (_config.GetValue("Database:SeedDemoData", true))
            {
                var seeded = await _seeder.SeedAsync(builder.ConnectionString);
                _status.SeededThisRun = seeded;
                if (seeded) _status.Log.Add("Demo users, products and 4 months of order history generated.");
            }

            _status.IsReady = true;
            _status.InitializedAt = DateTime.Now;
            _logger.LogInformation("NexaCommerce database ready on {Server}/{Db}", builder.DataSource, dbName);
        }
        catch (Exception ex)
        {
            _status.Error = ex.Message;
            _logger.LogError(ex, "Database initialisation failed");
        }
    }

    private async Task<SqlConnectionStringBuilder> ResolveServerAsync(string configured)
    {
        var baseBuilder = new SqlConnectionStringBuilder(configured);
        var candidates = new List<string> { baseBuilder.DataSource };
        var fallbacks = _config.GetSection("Database:FallbackServers").Get<string[]>() ?? Array.Empty<string>();
        candidates.AddRange(fallbacks.Where(f => !candidates.Contains(f, StringComparer.OrdinalIgnoreCase)));

        foreach (var server in candidates.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            var test = new SqlConnectionStringBuilder(baseBuilder.ConnectionString)
            {
                DataSource = server,
                InitialCatalog = "master",
                ConnectTimeout = 5
            };
            try
            {
                await using var c = new SqlConnection(test.ConnectionString);
                await c.OpenAsync();
                _status.Log.Add($"Connected to SQL Server '{server}'.");
                var result = new SqlConnectionStringBuilder(baseBuilder.ConnectionString) { DataSource = server };
                return result;
            }
            catch (Exception ex)
            {
                _status.Log.Add($"Server '{server}' not reachable: {ex.Message.Split('\n')[0]}");
            }
        }
        return null;
    }

    private async Task EnsureDatabaseAsync(SqlConnectionStringBuilder builder, string dbName)
    {
        var master = new SqlConnectionStringBuilder(builder.ConnectionString) { InitialCatalog = "master" };
        await using var c = new SqlConnection(master.ConnectionString);
        await c.OpenAsync();
        var exists = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.databases WHERE name = @dbName", new { dbName }) > 0;
        if (!exists)
        {
            var safe = dbName.Replace("]", "]]");
            await c.ExecuteAsync($"CREATE DATABASE [{safe}]");
            _status.Log.Add($"Database '{dbName}' created.");
        }
    }

    private async Task RunScriptAsync(SqlConnection conn, string fileName)
    {
        var path = Path.Combine(_env.ContentRootPath, "Database", fileName);
        if (!File.Exists(path)) path = Path.Combine(AppContext.BaseDirectory, "Database", fileName);
        if (!File.Exists(path)) throw new FileNotFoundException($"SQL script not found: {fileName}", path);

        var script = await File.ReadAllTextAsync(path);
        var batches = Regex.Split(script, @"^\s*GO\s*;?\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        foreach (var batch in batches)
        {
            if (string.IsNullOrWhiteSpace(batch)) continue;
            await conn.ExecuteAsync(batch, commandTimeout: 120);
        }
    }
}

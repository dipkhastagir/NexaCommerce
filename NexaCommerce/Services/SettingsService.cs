using System.Globalization;
using Dapper;
using NexaCommerce.Data;
using NexaCommerce.Models;

namespace NexaCommerce.Services;

public class SettingsService
{
    private readonly DbConnectionFactory _db;
    private Dictionary<string, string> _cache;

    public SettingsService(DbConnectionFactory db) => _db = db;

    public async Task<Dictionary<string, string>> AllAsync()
    {
        if (_cache != null) return _cache;
        using var c = _db.Create();
        _cache = (await c.QueryAsync<Setting>("SELECT * FROM dbo.Settings")).ToDictionary(s => s.SettingKey, s => s.SettingValue, StringComparer.OrdinalIgnoreCase);
        return _cache;
    }

    public async Task<List<Setting>> ListAsync()
    {
        using var c = _db.Create();
        return (await c.QueryAsync<Setting>("SELECT * FROM dbo.Settings ORDER BY SettingKey")).ToList();
    }

    public async Task<string> GetAsync(string key, string fallback = "")
        => (await AllAsync()).TryGetValue(key, out var v) ? v : fallback;

    public async Task<decimal> GetDecimalAsync(string key, decimal fallback)
        => decimal.TryParse(await GetAsync(key), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public async Task<double> GetDoubleAsync(string key, double fallback)
        => double.TryParse(await GetAsync(key), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public async Task<int> GetIntAsync(string key, int fallback)
        => int.TryParse(await GetAsync(key), out var v) ? v : fallback;

    public async Task<bool> GetBoolAsync(string key, bool fallback)
        => bool.TryParse(await GetAsync(key), out var v) ? v : fallback;

    public async Task SaveAsync(Dictionary<string, string> values)
    {
        using var c = _db.Create();
        foreach (var kv in values)
            await c.ExecuteAsync("UPDATE dbo.Settings SET SettingValue = @Value WHERE SettingKey = @Key", new { kv.Key, Value = kv.Value ?? "" });
        _cache = null;
    }
}

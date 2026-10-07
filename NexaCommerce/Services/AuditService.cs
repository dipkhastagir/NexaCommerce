using Dapper;
using NexaCommerce.Data;
using NexaCommerce.Infrastructure;
using NexaCommerce.Models;

namespace NexaCommerce.Services;

public class AuditService
{
    private readonly DbConnectionFactory _db;
    private readonly IHttpContextAccessor _http;

    public AuditService(DbConnectionFactory db, IHttpContextAccessor http) { _db = db; _http = http; }

    public async Task LogAsync(string action, string entity, object entityId = null, string details = null)
    {
        try
        {
            var ctx = _http.HttpContext;
            var user = ctx?.User;
            int? userId = user?.GetUserId() > 0 ? user.GetUserId() : null;
            using var c = _db.Create();
            await c.ExecuteAsync(@"INSERT INTO dbo.AuditLogs (UserId, UserEmail, Action, Entity, EntityId, Details, IpAddress)
                VALUES (@userId, @email, @action, @entity, @entityId, @details, @ip)",
                new
                {
                    userId, email = user?.GetEmail(), action, entity, entityId = entityId?.ToString(),
                    details = details?.Length > 1000 ? details[..1000] : details,
                    ip = ctx?.Connection?.RemoteIpAddress?.ToString()
                });
        }
        catch
        {
            // auditing must never break the user's action
        }
    }

    public async Task<PagedResult<AuditLog>> SearchAsync(string search, string entity, int page, int pageSize = 30)
    {
        using var c = _db.Create();
        var items = (await c.QueryAsync<AuditLog>(@"
            SELECT *, COUNT(*) OVER() AS TotalCount FROM dbo.AuditLogs
            WHERE (@search IS NULL OR UserEmail LIKE '%' + @search + '%' OR Details LIKE '%' + @search + '%' OR Action LIKE '%' + @search + '%')
              AND (@entity IS NULL OR Entity = @entity)
            ORDER BY AuditId DESC OFFSET (@page - 1) * @pageSize ROWS FETCH NEXT @pageSize ROWS ONLY",
            new { search = string.IsNullOrWhiteSpace(search) ? null : search, entity = string.IsNullOrWhiteSpace(entity) ? null : entity, page, pageSize })).ToList();
        return new PagedResult<AuditLog> { Items = items, Page = page, PageSize = pageSize, TotalCount = items.FirstOrDefault()?.TotalCount ?? 0 };
    }

    public async Task<List<string>> EntitiesAsync()
    {
        using var c = _db.Create();
        return (await c.QueryAsync<string>("SELECT DISTINCT Entity FROM dbo.AuditLogs ORDER BY Entity")).ToList();
    }
}

using Dapper;
using NexaCommerce.Data;
using NexaCommerce.Models;

namespace NexaCommerce.Services;

public class NotificationService
{
    private readonly DbConnectionFactory _db;
    public NotificationService(DbConnectionFactory db) => _db = db;

    public async Task CreateAsync(string title, string message, string link = null, string category = "General", int? userId = null)
    {
        using var c = _db.Create();
        await c.ExecuteAsync("INSERT INTO dbo.Notifications (UserId, Title, Message, Link, Category) VALUES (@userId, @title, @message, @link, @category)",
            new { userId, title, message, link, category });
    }

    public async Task<List<Notification>> ForStaffAsync(int userId, bool unreadOnly = false, int take = 100)
    {
        using var c = _db.Create();
        return (await c.QueryAsync<Notification>(@"SELECT TOP (@take) * FROM dbo.Notifications
            WHERE (UserId IS NULL OR UserId = @userId) AND (@unreadOnly = 0 OR IsRead = 0) ORDER BY CreatedAt DESC",
            new { userId, unreadOnly, take })).ToList();
    }

    public async Task<int> UnreadCountAsync(int userId)
    {
        using var c = _db.Create();
        return await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Notifications WHERE (UserId IS NULL OR UserId = @userId) AND IsRead = 0", new { userId });
    }

    public async Task MarkReadAsync(int id)
    {
        using var c = _db.Create();
        await c.ExecuteAsync("UPDATE dbo.Notifications SET IsRead = 1 WHERE NotificationId = @id", new { id });
    }

    public async Task MarkAllReadAsync(int userId)
    {
        using var c = _db.Create();
        await c.ExecuteAsync("UPDATE dbo.Notifications SET IsRead = 1 WHERE (UserId IS NULL OR UserId = @userId) AND IsRead = 0", new { userId });
    }

    public async Task DeleteReadAsync(int userId)
    {
        using var c = _db.Create();
        await c.ExecuteAsync("DELETE FROM dbo.Notifications WHERE (UserId IS NULL OR UserId = @userId) AND IsRead = 1", new { userId });
    }
}

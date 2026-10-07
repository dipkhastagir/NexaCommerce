using Dapper;
using NexaCommerce.Data;
using NexaCommerce.Models;

namespace NexaCommerce.Services;

public class CustomerService
{
    private readonly DbConnectionFactory _db;
    public CustomerService(DbConnectionFactory db) => _db = db;

    public async Task<PagedResult<CustomerSummary>> SearchAsync(string search, string sort, int page, int pageSize = 25)
    {
        if (page < 1) page = 1;
        using var c = _db.Create();
        var items = (await c.QueryAsync<CustomerSummary>(@"
            ;WITH x AS (
                SELECT u.UserId, u.FullName, u.Email, u.Phone, u.IsActive, u.CreatedAt,
                       MAX(o.CreatedAt) AS LastOrderDate, COUNT(o.OrderId) AS OrderCount, ISNULL(SUM(o.TotalAmount), 0) AS TotalSpent
                FROM dbo.Users u JOIN dbo.Roles r ON r.RoleId = u.RoleId AND r.Name = 'Customer'
                LEFT JOIN dbo.Orders o ON o.UserId = u.UserId AND o.Status <> 'Cancelled'
                WHERE (@search IS NULL OR u.FullName LIKE '%' + @search + '%' OR u.Email LIKE '%' + @search + '%' OR u.Phone LIKE '%' + @search + '%')
                GROUP BY u.UserId, u.FullName, u.Email, u.Phone, u.IsActive, u.CreatedAt)
            SELECT *, COUNT(*) OVER() AS TotalCount FROM x
            ORDER BY CASE WHEN @sort = 'spent' THEN TotalSpent END DESC,
                     CASE WHEN @sort = 'orders' THEN OrderCount END DESC,
                     CASE WHEN @sort = 'recent' THEN LastOrderDate END DESC,
                     FullName
            OFFSET (@page - 1) * @pageSize ROWS FETCH NEXT @pageSize ROWS ONLY",
            new { search = string.IsNullOrWhiteSpace(search) ? null : search.Trim(), sort = sort ?? "spent", page, pageSize })).ToList();
        return new PagedResult<CustomerSummary> { Items = items, Page = page, PageSize = pageSize, TotalCount = items.FirstOrDefault()?.TotalCount ?? 0 };
    }

    public async Task<CustomerSummary> GetAsync(int id)
    {
        using var c = _db.Create();
        return await c.QueryFirstOrDefaultAsync<CustomerSummary>(@"
            SELECT u.UserId, u.FullName, u.Email, u.Phone, u.IsActive, u.CreatedAt,
                   MAX(o.CreatedAt) AS LastOrderDate, COUNT(o.OrderId) AS OrderCount, ISNULL(SUM(o.TotalAmount), 0) AS TotalSpent
            FROM dbo.Users u LEFT JOIN dbo.Orders o ON o.UserId = u.UserId AND o.Status <> 'Cancelled'
            WHERE u.UserId = @id GROUP BY u.UserId, u.FullName, u.Email, u.Phone, u.IsActive, u.CreatedAt", new { id });
    }
}

public class AddressService
{
    private readonly DbConnectionFactory _db;
    public AddressService(DbConnectionFactory db) => _db = db;

    public async Task<List<Address>> ForUserAsync(int userId)
    {
        using var c = _db.Create();
        return (await c.QueryAsync<Address>("SELECT * FROM dbo.Addresses WHERE UserId = @userId ORDER BY IsDefault DESC, AddressId", new { userId })).ToList();
    }

    public async Task<Address> GetAsync(int id, int userId)
    {
        using var c = _db.Create();
        return await c.QueryFirstOrDefaultAsync<Address>("SELECT * FROM dbo.Addresses WHERE AddressId = @id AND UserId = @userId", new { id, userId });
    }

    public async Task SaveAsync(Address a)
    {
        using var c = _db.Create();
        await c.OpenAsync();
        using var tx = c.BeginTransaction();
        var count = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Addresses WHERE UserId = @UserId", new { a.UserId }, tx);
        if (count == 0) a.IsDefault = true;
        if (a.IsDefault) await c.ExecuteAsync("UPDATE dbo.Addresses SET IsDefault = 0 WHERE UserId = @UserId", new { a.UserId }, tx);
        if (a.AddressId == 0)
            await c.ExecuteAsync(@"INSERT INTO dbo.Addresses (UserId, Label, RecipientName, Phone, Line1, City, PostalCode, IsDefault)
                VALUES (@UserId, @Label, @RecipientName, @Phone, @Line1, @City, @PostalCode, @IsDefault)", a, tx);
        else
            await c.ExecuteAsync(@"UPDATE dbo.Addresses SET Label = @Label, RecipientName = @RecipientName, Phone = @Phone, Line1 = @Line1,
                City = @City, PostalCode = @PostalCode, IsDefault = @IsDefault WHERE AddressId = @AddressId AND UserId = @UserId", a, tx);
        tx.Commit();
    }

    public async Task DeleteAsync(int id, int userId)
    {
        using var c = _db.Create();
        await c.ExecuteAsync("DELETE FROM dbo.Addresses WHERE AddressId = @id AND UserId = @userId", new { id, userId });
    }
}

public class WishlistService
{
    private readonly DbConnectionFactory _db;
    public WishlistService(DbConnectionFactory db) => _db = db;

    public async Task<List<Product>> ItemsAsync(int userId)
    {
        using var c = _db.Create();
        return (await c.QueryAsync<Product>(@"SELECT v.* FROM dbo.WishlistItems w JOIN dbo.vw_ProductList v ON v.ProductId = w.ProductId
            WHERE w.UserId = @userId ORDER BY w.AddedAt DESC", new { userId })).ToList();
    }

    public async Task<bool> ContainsAsync(int userId, int productId)
    {
        using var c = _db.Create();
        return await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.WishlistItems WHERE UserId = @userId AND ProductId = @productId", new { userId, productId }) > 0;
    }

    /// <returns>True when the product is now in the wishlist.</returns>
    public async Task<bool> ToggleAsync(int userId, int productId)
    {
        using var c = _db.Create();
        var removed = await c.ExecuteAsync("DELETE FROM dbo.WishlistItems WHERE UserId = @userId AND ProductId = @productId", new { userId, productId });
        if (removed > 0) return false;
        await c.ExecuteAsync("INSERT INTO dbo.WishlistItems (UserId, ProductId) VALUES (@userId, @productId)", new { userId, productId });
        return true;
    }
}

public class ReviewService
{
    private readonly DbConnectionFactory _db;
    public ReviewService(DbConnectionFactory db) => _db = db;

    public async Task<PagedResult<Review>> SearchAsync(string status, int page, int pageSize = 25)
    {
        if (page < 1) page = 1;
        using var c = _db.Create();
        var items = (await c.QueryAsync<Review>(@"SELECT r.*, p.Name AS ProductName, u.FullName AS UserName, COUNT(*) OVER() AS TotalCount
            FROM dbo.Reviews r JOIN dbo.Products p ON p.ProductId = r.ProductId JOIN dbo.Users u ON u.UserId = r.UserId
            WHERE (@status IS NULL OR (@status = 'pending' AND r.IsApproved = 0) OR (@status = 'approved' AND r.IsApproved = 1))
            ORDER BY r.IsApproved, r.CreatedAt DESC OFFSET (@page - 1) * @pageSize ROWS FETCH NEXT @pageSize ROWS ONLY",
            new { status = string.IsNullOrWhiteSpace(status) ? null : status, page, pageSize })).ToList();
        return new PagedResult<Review> { Items = items, Page = page, PageSize = pageSize, TotalCount = items.FirstOrDefault()?.TotalCount ?? 0 };
    }

    public async Task<bool> AlreadyReviewedAsync(int userId, int productId)
    {
        using var c = _db.Create();
        return await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Reviews WHERE UserId = @userId AND ProductId = @productId", new { userId, productId }) > 0;
    }

    public async Task AddAsync(int userId, WriteReviewVm vm)
    {
        using var c = _db.Create();
        await c.ExecuteAsync("INSERT INTO dbo.Reviews (ProductId, UserId, Rating, Title, Comment, IsApproved) VALUES (@ProductId, @userId, @Rating, @Title, @Comment, 0)",
            new { vm.ProductId, userId, vm.Rating, vm.Title, vm.Comment });
    }

    public async Task SetApprovedAsync(int id, bool approved)
    {
        using var c = _db.Create();
        await c.ExecuteAsync("UPDATE dbo.Reviews SET IsApproved = @approved WHERE ReviewId = @id", new { id, approved });
    }

    public async Task DeleteAsync(int id)
    {
        using var c = _db.Create();
        await c.ExecuteAsync("DELETE FROM dbo.Reviews WHERE ReviewId = @id", new { id });
    }
}

public class ReturnService
{
    private readonly DbConnectionFactory _db;
    public ReturnService(DbConnectionFactory db) => _db = db;

    private const string Select = @"SELECT r.*, o.OrderNumber, o.TotalAmount AS OrderTotal, u.FullName AS CustomerName
        FROM dbo.Returns r JOIN dbo.Orders o ON o.OrderId = r.OrderId JOIN dbo.Users u ON u.UserId = r.UserId";

    public async Task<List<ReturnRequest>> AllAsync(string status)
    {
        using var c = _db.Create();
        return (await c.QueryAsync<ReturnRequest>(Select + " WHERE (@status IS NULL OR r.Status = @status) ORDER BY r.ReturnId DESC",
            new { status = string.IsNullOrWhiteSpace(status) ? null : status })).ToList();
    }

    public async Task<ReturnRequest> GetAsync(int id)
    {
        using var c = _db.Create();
        return await c.QueryFirstOrDefaultAsync<ReturnRequest>(Select + " WHERE r.ReturnId = @id", new { id });
    }

    public async Task<List<ReturnRequest>> ForOrderAsync(int orderId)
    {
        using var c = _db.Create();
        return (await c.QueryAsync<ReturnRequest>(Select + " WHERE r.OrderId = @orderId ORDER BY r.ReturnId DESC", new { orderId })).ToList();
    }

    public async Task<string> RequestAsync(int orderId, int userId, string reason)
    {
        using var c = _db.Create();
        var order = await c.QueryFirstOrDefaultAsync<Order>("SELECT OrderId, UserId, Status, UpdatedAt, CreatedAt FROM dbo.Orders WHERE OrderId = @orderId", new { orderId });
        if (order == null || order.UserId != userId) return "Order not found.";
        if (order.Status != "Delivered") return "Only delivered orders can be returned.";
        if ((DateTime.Now - (order.UpdatedAt ?? order.CreatedAt)).TotalDays > 30) return "The 30-day return window for this order has closed.";
        if (await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Returns WHERE OrderId = @orderId AND Status IN ('Requested','Approved')", new { orderId }) > 0)
            return "A return is already open for this order.";
        await c.ExecuteAsync("INSERT INTO dbo.Returns (OrderId, UserId, Reason) VALUES (@orderId, @userId, @reason)", new { orderId, userId, reason });
        return "OK";
    }

    public async Task ResolveAsync(int id, string status, string note, decimal refund)
    {
        using var c = _db.Create();
        await c.OpenAsync();
        using var tx = c.BeginTransaction();
        await c.ExecuteAsync(@"UPDATE dbo.Returns SET Status = @status, AdminNote = @note, RefundAmount = @refund,
            ResolvedAt = CASE WHEN @status IN ('Rejected','Refunded') THEN SYSDATETIME() ELSE NULL END WHERE ReturnId = @id",
            new { id, status, note, refund }, tx);
        if (status == "Refunded")
        {
            var orderId = await c.ExecuteScalarAsync<int>("SELECT OrderId FROM dbo.Returns WHERE ReturnId = @id", new { id }, tx);
            await c.ExecuteAsync("UPDATE dbo.Orders SET PaymentStatus = 'Refunded', UpdatedAt = SYSDATETIME() WHERE OrderId = @orderId", new { orderId }, tx);
            await c.ExecuteAsync("INSERT INTO dbo.Payments (OrderId, Method, Amount, TransactionRef, Status, PaidAt) SELECT @orderId, PaymentMethod, -@refund, 'REFUND-' + CAST(@id AS NVARCHAR(10)), 'Refunded', SYSDATETIME() FROM dbo.Orders WHERE OrderId = @orderId",
                new { orderId, refund, id }, tx);
        }
        tx.Commit();
    }
}

public class ContactService
{
    private readonly DbConnectionFactory _db;
    public ContactService(DbConnectionFactory db) => _db = db;

    public async Task AddAsync(ContactMessage m)
    {
        using var c = _db.Create();
        await c.ExecuteAsync("INSERT INTO dbo.ContactMessages (Name, Email, Subject, Body) VALUES (@Name, @Email, @Subject, @Body)", m);
    }

    public async Task<List<ContactMessage>> AllAsync()
    {
        using var c = _db.Create();
        return (await c.QueryAsync<ContactMessage>("SELECT * FROM dbo.ContactMessages ORDER BY IsHandled, CreatedAt DESC")).ToList();
    }

    public async Task MarkHandledAsync(int id)
    {
        using var c = _db.Create();
        await c.ExecuteAsync("UPDATE dbo.ContactMessages SET IsHandled = 1 WHERE MessageId = @id", new { id });
    }
}

public class UserService
{
    private readonly DbConnectionFactory _db;
    public UserService(DbConnectionFactory db) => _db = db;

    public async Task<List<User>> AllAsync(string role, string search)
    {
        using var c = _db.Create();
        return (await c.QueryAsync<User>(@"SELECT TOP 300 u.*, r.Name AS RoleName FROM dbo.Users u JOIN dbo.Roles r ON r.RoleId = u.RoleId
            WHERE (@role IS NULL OR r.Name = @role) AND (@search IS NULL OR u.FullName LIKE '%' + @search + '%' OR u.Email LIKE '%' + @search + '%')
            ORDER BY CASE r.Name WHEN 'Admin' THEN 0 WHEN 'Manager' THEN 1 WHEN 'Staff' THEN 2 ELSE 3 END, u.FullName",
            new { role = string.IsNullOrWhiteSpace(role) ? null : role, search = string.IsNullOrWhiteSpace(search) ? null : search })).ToList();
    }

    public async Task<List<Role>> RolesAsync()
    {
        using var c = _db.Create();
        return (await c.QueryAsync<Role>("SELECT r.*, (SELECT COUNT(*) FROM dbo.Users u WHERE u.RoleId = r.RoleId) AS UserCount FROM dbo.Roles r ORDER BY r.RoleId")).ToList();
    }

    public async Task UpdateAsync(UserFormVm vm)
    {
        using var c = _db.Create();
        await c.ExecuteAsync(@"UPDATE dbo.Users SET FullName = @FullName, Phone = @Phone, IsActive = @IsActive,
            RoleId = (SELECT RoleId FROM dbo.Roles WHERE Name = @RoleName) WHERE UserId = @UserId", vm);
    }

    public async Task SetActiveAsync(int id, bool active)
    {
        using var c = _db.Create();
        await c.ExecuteAsync("UPDATE dbo.Users SET IsActive = @active WHERE UserId = @id", new { id, active });
    }

    public async Task<int> AdminCountAsync()
    {
        using var c = _db.Create();
        return await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Users u JOIN dbo.Roles r ON r.RoleId = u.RoleId WHERE r.Name = 'Admin' AND u.IsActive = 1");
    }
}

using System.Data;
using Dapper;
using NexaCommerce.Data;
using NexaCommerce.Models;

namespace NexaCommerce.Services;

public class InventoryService
{
    private readonly DbConnectionFactory _db;
    private readonly NotificationService _notify;
    private readonly SettingsService _settings;

    public InventoryService(DbConnectionFactory db, NotificationService notify, SettingsService settings)
    {
        _db = db; _notify = notify; _settings = settings;
    }

    public async Task<(bool Ok, string Message, int Balance)> AdjustAsync(int productId, int quantity, string type, string reference, string note, int? warehouseId, int userId)
    {
        using var c = _db.Create();
        var r = await c.QueryFirstAsync<(int NewBalance, string Message)>("dbo.sp_AdjustStock", new
        {
            ProductId = productId, Quantity = quantity, MovementType = type, Reference = reference, Note = note, WarehouseId = warehouseId, UserId = userId
        }, commandType: CommandType.StoredProcedure);
        if (r.NewBalance < 0) return (false, r.Message, 0);
        await CheckLowStockAsync(productId);
        return (true, "OK", r.NewBalance);
    }

    /// <summary>Raises a back-office notification when a product reaches its reorder level.</summary>
    public async Task CheckLowStockAsync(int productId)
    {
        if (!await _settings.GetBoolAsync("LowStockAlerts", true)) return;
        using var c = _db.Create();
        var p = await c.QueryFirstOrDefaultAsync<Product>("SELECT ProductId, Sku, Name, StockQuantity, ReorderLevel FROM dbo.Products WHERE ProductId = @productId", new { productId });
        if (p == null || p.StockQuantity > p.ReorderLevel) return;
        var recent = await c.ExecuteScalarAsync<int>(@"SELECT COUNT(*) FROM dbo.Notifications WHERE Category = 'Stock' AND IsRead = 0
            AND Title = @title AND CreatedAt > DATEADD(HOUR, -12, SYSDATETIME())", new { title = "Low stock: " + p.Name });
        if (recent > 0) return;
        await _notify.CreateAsync("Low stock: " + p.Name,
            $"{p.Sku} is down to {p.StockQuantity} units (reorder level {p.ReorderLevel}).", "/Admin/Inventory/LowStock", "Stock");
    }

    public async Task<PagedResult<StockMovement>> MovementsAsync(int? productId, string type, DateTime? from, DateTime? to, int page, int pageSize = 30)
    {
        using var c = _db.Create();
        var items = (await c.QueryAsync<StockMovement>(@"
            SELECT m.*, p.Name AS ProductName, p.Sku, w.Name AS WarehouseName, u.FullName AS CreatedByName, COUNT(*) OVER() AS TotalCount
            FROM dbo.StockMovements m
            JOIN dbo.Products p ON p.ProductId = m.ProductId
            LEFT JOIN dbo.Warehouses w ON w.WarehouseId = m.WarehouseId
            LEFT JOIN dbo.Users u ON u.UserId = m.CreatedBy
            WHERE (@productId IS NULL OR m.ProductId = @productId)
              AND (@type IS NULL OR m.MovementType = @type)
              AND (@from IS NULL OR m.CreatedAt >= @from)
              AND (@to IS NULL OR m.CreatedAt < DATEADD(DAY, 1, @to))
            ORDER BY m.MovementId DESC OFFSET (@page - 1) * @pageSize ROWS FETCH NEXT @pageSize ROWS ONLY",
            new { productId, type = string.IsNullOrWhiteSpace(type) ? null : type, from, to, page, pageSize })).ToList();
        return new PagedResult<StockMovement> { Items = items, Page = page, PageSize = pageSize, TotalCount = items.FirstOrDefault()?.TotalCount ?? 0 };
    }
}

public class PurchaseOrderService
{
    private readonly DbConnectionFactory _db;
    public PurchaseOrderService(DbConnectionFactory db) => _db = db;

    public async Task<List<PurchaseOrder>> AllAsync(string status)
    {
        using var c = _db.Create();
        return (await c.QueryAsync<PurchaseOrder>(@"
            SELECT po.*, s.Name AS SupplierName, w.Name AS WarehouseName, u.FullName AS CreatedByName,
                   (SELECT COUNT(*) FROM dbo.PurchaseOrderItems i WHERE i.PurchaseOrderId = po.PurchaseOrderId) AS ItemCount
            FROM dbo.PurchaseOrders po JOIN dbo.Suppliers s ON s.SupplierId = po.SupplierId
            LEFT JOIN dbo.Warehouses w ON w.WarehouseId = po.WarehouseId
            LEFT JOIN dbo.Users u ON u.UserId = po.CreatedBy
            WHERE (@status IS NULL OR po.Status = @status) ORDER BY po.PurchaseOrderId DESC",
            new { status = string.IsNullOrWhiteSpace(status) ? null : status })).ToList();
    }

    public async Task<PurchaseOrder> GetAsync(int id)
    {
        using var c = _db.Create();
        var po = await c.QueryFirstOrDefaultAsync<PurchaseOrder>(@"
            SELECT po.*, s.Name AS SupplierName, w.Name AS WarehouseName, u.FullName AS CreatedByName
            FROM dbo.PurchaseOrders po JOIN dbo.Suppliers s ON s.SupplierId = po.SupplierId
            LEFT JOIN dbo.Warehouses w ON w.WarehouseId = po.WarehouseId
            LEFT JOIN dbo.Users u ON u.UserId = po.CreatedBy WHERE po.PurchaseOrderId = @id", new { id });
        if (po == null) return null;
        po.Items = (await c.QueryAsync<PurchaseOrderItem>(@"SELECT i.*, p.Name AS ProductName, p.Sku FROM dbo.PurchaseOrderItems i
            JOIN dbo.Products p ON p.ProductId = i.ProductId WHERE i.PurchaseOrderId = @id", new { id })).ToList();
        return po;
    }

    public async Task<int> CreateAsync(PurchaseOrderFormVm vm, int userId, bool placeOrder)
    {
        var lines = vm.Lines.Where(l => l.ProductId > 0 && l.Quantity > 0).GroupBy(l => l.ProductId)
            .Select(g => new PurchaseOrderLineVm { ProductId = g.Key, Quantity = g.Sum(x => x.Quantity), UnitCost = g.First().UnitCost }).ToList();
        if (lines.Count == 0) return 0;

        using var c = _db.Create();
        await c.OpenAsync();
        using var tx = c.BeginTransaction();
        var next = await c.ExecuteScalarAsync<int>("SELECT ISNULL(MAX(PurchaseOrderId), 0) + 1 FROM dbo.PurchaseOrders", transaction: tx);
        var poNumber = $"PO-{DateTime.Now.Year}-{next:D4}";
        var id = await c.ExecuteScalarAsync<int>(@"INSERT INTO dbo.PurchaseOrders (PoNumber, SupplierId, WarehouseId, Status, ExpectedDate, TotalAmount, Notes, CreatedBy)
            OUTPUT INSERTED.PurchaseOrderId VALUES (@poNumber, @SupplierId, @WarehouseId, @status, @ExpectedDate, @total, @Notes, @userId)",
            new { poNumber, vm.SupplierId, vm.WarehouseId, status = placeOrder ? "Ordered" : "Draft", vm.ExpectedDate, total = lines.Sum(l => l.Quantity * l.UnitCost), vm.Notes, userId }, tx);
        foreach (var l in lines)
            await c.ExecuteAsync("INSERT INTO dbo.PurchaseOrderItems (PurchaseOrderId, ProductId, Quantity, UnitCost) VALUES (@id, @ProductId, @Quantity, @UnitCost)",
                new { id, l.ProductId, l.Quantity, l.UnitCost }, tx);
        tx.Commit();
        return id;
    }

    public async Task<string> SetStatusAsync(int id, string status)
    {
        using var c = _db.Create();
        var current = await c.ExecuteScalarAsync<string>("SELECT Status FROM dbo.PurchaseOrders WHERE PurchaseOrderId = @id", new { id });
        if (current == null) return "Purchase order not found";
        if (current is "Received" or "Cancelled") return $"This purchase order is already {current.ToLower()}";
        await c.ExecuteAsync("UPDATE dbo.PurchaseOrders SET Status = @status WHERE PurchaseOrderId = @id", new { id, status });
        return "OK";
    }

    public async Task<string> ReceiveAsync(int id, int userId)
    {
        using var c = _db.Create();
        return await c.ExecuteScalarAsync<string>("dbo.sp_ReceivePurchaseOrder", new { PurchaseOrderId = id, UserId = userId }, commandType: CommandType.StoredProcedure);
    }
}

using System.Data;
using Dapper;
using NexaCommerce.Data;
using NexaCommerce.Infrastructure;
using NexaCommerce.Models;

namespace NexaCommerce.Services;

public class ProductQuery
{
    public string Search { get; set; }
    public int? CategoryId { get; set; }
    public int? BrandId { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public bool InStockOnly { get; set; }
    public bool ActiveOnly { get; set; } = true;
    public string StockStatus { get; set; }
    public string Sort { get; set; } = "newest";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 12;
}

public class ProductService
{
    private readonly DbConnectionFactory _db;
    public ProductService(DbConnectionFactory db) => _db = db;

    public async Task<PagedResult<Product>> SearchAsync(ProductQuery q)
    {
        if (q.Page < 1) q.Page = 1;
        using var c = _db.Create();
        var items = (await c.QueryAsync<Product>("dbo.sp_SearchProducts", new
        {
            Search = string.IsNullOrWhiteSpace(q.Search) ? null : q.Search.Trim(),
            q.CategoryId, q.BrandId, q.MinPrice, q.MaxPrice, q.InStockOnly, q.ActiveOnly,
            StockStatus = string.IsNullOrWhiteSpace(q.StockStatus) ? null : q.StockStatus,
            Sort = q.Sort ?? "newest", q.Page, q.PageSize
        }, commandType: CommandType.StoredProcedure)).ToList();
        return new PagedResult<Product> { Items = items, Page = q.Page, PageSize = q.PageSize, TotalCount = items.FirstOrDefault()?.TotalCount ?? 0 };
    }

    public async Task<Product> GetAsync(int id)
    {
        using var c = _db.Create();
        return await c.QueryFirstOrDefaultAsync<Product>("SELECT * FROM dbo.vw_ProductList WHERE ProductId = @id", new { id });
    }

    public async Task<List<Product>> GetManyAsync(IEnumerable<int> ids)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new();
        using var c = _db.Create();
        return (await c.QueryAsync<Product>("SELECT * FROM dbo.vw_ProductList WHERE ProductId IN @list", new { list })).ToList();
    }

    public async Task<List<Product>> AllAsync(bool activeOnly = false)
    {
        using var c = _db.Create();
        return (await c.QueryAsync<Product>("SELECT * FROM dbo.vw_ProductList WHERE (@activeOnly = 0 OR IsActive = 1) ORDER BY Name", new { activeOnly })).ToList();
    }

    public async Task<List<Product>> FeaturedAsync(int take = 8)
    {
        using var c = _db.Create();
        return (await c.QueryAsync<Product>("SELECT TOP (@take) * FROM dbo.vw_ProductList WHERE IsActive = 1 AND IsFeatured = 1 ORDER BY UnitsSold DESC", new { take })).ToList();
    }

    public async Task<List<Product>> BestSellersAsync(int take = 8)
    {
        using var c = _db.Create();
        return (await c.QueryAsync<Product>("SELECT TOP (@take) * FROM dbo.vw_ProductList WHERE IsActive = 1 ORDER BY UnitsSold DESC", new { take })).ToList();
    }

    public async Task<List<Product>> DealsAsync(int take = 8)
    {
        using var c = _db.Create();
        return (await c.QueryAsync<Product>(@"SELECT TOP (@take) * FROM dbo.vw_ProductList
            WHERE IsActive = 1 AND DiscountPrice IS NOT NULL AND DiscountPrice < Price ORDER BY (Price - DiscountPrice) / Price DESC", new { take })).ToList();
    }

    public async Task<List<Product>> RelatedAsync(Product p, int take = 4)
    {
        using var c = _db.Create();
        return (await c.QueryAsync<Product>(@"SELECT TOP (@take) * FROM dbo.vw_ProductList
            WHERE IsActive = 1 AND CategoryId = @CategoryId AND ProductId <> @ProductId ORDER BY UnitsSold DESC",
            new { take, p.CategoryId, p.ProductId })).ToList();
    }

    public async Task<List<Product>> LowStockAsync()
    {
        using var c = _db.Create();
        return (await c.QueryAsync<Product>("SELECT * FROM dbo.vw_ProductList WHERE IsActive = 1 AND StockQuantity <= ReorderLevel ORDER BY StockQuantity, Name")).ToList();
    }

    /// <returns>New id, or -1 when the SKU already exists.</returns>
    public async Task<int> CreateAsync(ProductFormVm vm, int userId)
    {
        using var c = _db.Create();
        return await c.ExecuteScalarAsync<int>("dbo.sp_CreateProduct", new
        {
            Sku = vm.Sku.Trim().ToUpperInvariant(), Name = vm.Name.Trim(), Slug = Slug.Make(vm.Name), vm.Description,
            vm.CategoryId, vm.BrandId, vm.SupplierId, vm.Price, vm.CostPrice, vm.DiscountPrice,
            vm.StockQuantity, vm.ReorderLevel, vm.IsActive, vm.IsFeatured, UserId = userId
        }, commandType: CommandType.StoredProcedure);
    }

    /// <returns>Rows changed, or -1 when the SKU belongs to another product.</returns>
    public async Task<int> UpdateAsync(ProductFormVm vm)
    {
        using var c = _db.Create();
        return await c.ExecuteScalarAsync<int>("dbo.sp_UpdateProduct", new
        {
            vm.ProductId, Sku = vm.Sku.Trim().ToUpperInvariant(), Name = vm.Name.Trim(), Slug = Slug.Make(vm.Name), vm.Description,
            vm.CategoryId, vm.BrandId, vm.SupplierId, vm.Price, vm.CostPrice, vm.DiscountPrice,
            vm.ReorderLevel, vm.IsActive, vm.IsFeatured
        }, commandType: CommandType.StoredProcedure);
    }

    /// <returns>"Deleted" or "Archived".</returns>
    public async Task<string> DeleteAsync(int id)
    {
        using var c = _db.Create();
        return await c.ExecuteScalarAsync<string>("dbo.sp_DeleteProduct", new { ProductId = id }, commandType: CommandType.StoredProcedure);
    }

    public async Task ToggleActiveAsync(int id)
    {
        using var c = _db.Create();
        await c.ExecuteAsync("UPDATE dbo.Products SET IsActive = 1 - IsActive, UpdatedAt = SYSDATETIME() WHERE ProductId = @id", new { id });
    }

    public async Task<List<Review>> ApprovedReviewsAsync(int productId)
    {
        using var c = _db.Create();
        return (await c.QueryAsync<Review>(@"SELECT r.*, u.FullName AS UserName FROM dbo.Reviews r JOIN dbo.Users u ON u.UserId = r.UserId
            WHERE r.ProductId = @productId AND r.IsApproved = 1 ORDER BY r.CreatedAt DESC", new { productId })).ToList();
    }
}

public class CategoryService
{
    private readonly DbConnectionFactory _db;
    public CategoryService(DbConnectionFactory db) => _db = db;

    public async Task<List<Category>> AllAsync(bool activeOnly = false)
    {
        using var c = _db.Create();
        return (await c.QueryAsync<Category>(@"SELECT c.*, pc.Name AS ParentName,
                (SELECT COUNT(*) FROM dbo.Products p WHERE p.CategoryId = c.CategoryId AND (@activeOnly = 0 OR p.IsActive = 1)) AS ProductCount
            FROM dbo.Categories c LEFT JOIN dbo.Categories pc ON pc.CategoryId = c.ParentCategoryId
            WHERE (@activeOnly = 0 OR c.IsActive = 1) ORDER BY c.Name", new { activeOnly })).ToList();
    }

    public async Task<Category> GetAsync(int id)
    {
        using var c = _db.Create();
        return await c.QueryFirstOrDefaultAsync<Category>(@"SELECT c.*, (SELECT COUNT(*) FROM dbo.Products p WHERE p.CategoryId = c.CategoryId) AS ProductCount
            FROM dbo.Categories c WHERE c.CategoryId = @id", new { id });
    }

    public async Task<Category> GetBySlugAsync(string slug)
    {
        using var c = _db.Create();
        return await c.QueryFirstOrDefaultAsync<Category>("SELECT * FROM dbo.Categories WHERE Slug = @slug", new { slug });
    }

    /// <returns>Id, or -1 when the slug is taken.</returns>
    public async Task<int> SaveAsync(Category m)
    {
        using var c = _db.Create();
        return await c.ExecuteScalarAsync<int>("dbo.sp_SaveCategory", new
        {
            m.CategoryId, Name = m.Name.Trim(), Slug = string.IsNullOrWhiteSpace(m.Slug) ? Slug.Make(m.Name) : Slug.Make(m.Slug),
            m.Description, m.ParentCategoryId, m.Icon, m.IsActive
        }, commandType: CommandType.StoredProcedure);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        using var c = _db.Create();
        var used = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Products WHERE CategoryId = @id", new { id })
                 + await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Categories WHERE ParentCategoryId = @id", new { id });
        if (used > 0) return false;
        await c.ExecuteAsync("DELETE FROM dbo.Categories WHERE CategoryId = @id", new { id });
        return true;
    }
}

public class BrandService
{
    private readonly DbConnectionFactory _db;
    public BrandService(DbConnectionFactory db) => _db = db;

    public async Task<List<Brand>> AllAsync(bool activeOnly = false)
    {
        using var c = _db.Create();
        return (await c.QueryAsync<Brand>(@"SELECT b.*, (SELECT COUNT(*) FROM dbo.Products p WHERE p.BrandId = b.BrandId) AS ProductCount
            FROM dbo.Brands b WHERE (@activeOnly = 0 OR b.IsActive = 1) ORDER BY b.Name", new { activeOnly })).ToList();
    }

    public async Task<Brand> GetAsync(int id)
    {
        using var c = _db.Create();
        return await c.QueryFirstOrDefaultAsync<Brand>("SELECT * FROM dbo.Brands WHERE BrandId = @id", new { id });
    }

    public async Task<bool> SaveAsync(Brand b)
    {
        using var c = _db.Create();
        if (await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Brands WHERE Name = @Name AND BrandId <> @BrandId", new { b.Name, b.BrandId }) > 0) return false;
        if (b.BrandId == 0)
            await c.ExecuteAsync("INSERT INTO dbo.Brands (Name, Description, IsActive) VALUES (@Name, @Description, @IsActive)", b);
        else
            await c.ExecuteAsync("UPDATE dbo.Brands SET Name = @Name, Description = @Description, IsActive = @IsActive WHERE BrandId = @BrandId", b);
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        using var c = _db.Create();
        if (await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Products WHERE BrandId = @id", new { id }) > 0) return false;
        await c.ExecuteAsync("DELETE FROM dbo.Brands WHERE BrandId = @id", new { id });
        return true;
    }
}

public class SupplierService
{
    private readonly DbConnectionFactory _db;
    public SupplierService(DbConnectionFactory db) => _db = db;

    public async Task<List<Supplier>> AllAsync(bool activeOnly = false)
    {
        using var c = _db.Create();
        return (await c.QueryAsync<Supplier>(@"SELECT s.*, (SELECT COUNT(*) FROM dbo.Products p WHERE p.SupplierId = s.SupplierId) AS ProductCount
            FROM dbo.Suppliers s WHERE (@activeOnly = 0 OR s.IsActive = 1) ORDER BY s.Name", new { activeOnly })).ToList();
    }

    public async Task<Supplier> GetAsync(int id)
    {
        using var c = _db.Create();
        return await c.QueryFirstOrDefaultAsync<Supplier>(@"SELECT s.*, (SELECT COUNT(*) FROM dbo.Products p WHERE p.SupplierId = s.SupplierId) AS ProductCount
            FROM dbo.Suppliers s WHERE s.SupplierId = @id", new { id });
    }

    public async Task<int> SaveAsync(Supplier s)
    {
        using var c = _db.Create();
        if (s.SupplierId == 0)
            return await c.ExecuteScalarAsync<int>(@"INSERT INTO dbo.Suppliers (Name, ContactPerson, Email, Phone, Address, LeadTimeDays, IsActive)
                OUTPUT INSERTED.SupplierId VALUES (@Name, @ContactPerson, @Email, @Phone, @Address, @LeadTimeDays, @IsActive)", s);
        await c.ExecuteAsync(@"UPDATE dbo.Suppliers SET Name = @Name, ContactPerson = @ContactPerson, Email = @Email, Phone = @Phone,
            Address = @Address, LeadTimeDays = @LeadTimeDays, IsActive = @IsActive WHERE SupplierId = @SupplierId", s);
        return s.SupplierId;
    }

    public async Task<List<Product>> ProductsAsync(int supplierId)
    {
        using var c = _db.Create();
        return (await c.QueryAsync<Product>("SELECT * FROM dbo.vw_ProductList WHERE SupplierId = @supplierId ORDER BY Name", new { supplierId })).ToList();
    }

    public async Task<List<PurchaseOrder>> PurchaseOrdersAsync(int supplierId)
    {
        using var c = _db.Create();
        return (await c.QueryAsync<PurchaseOrder>(@"SELECT po.*, (SELECT COUNT(*) FROM dbo.PurchaseOrderItems i WHERE i.PurchaseOrderId = po.PurchaseOrderId) AS ItemCount
            FROM dbo.PurchaseOrders po WHERE po.SupplierId = @supplierId ORDER BY po.OrderDate DESC", new { supplierId })).ToList();
    }

    public async Task<bool> DeleteAsync(int id)
    {
        using var c = _db.Create();
        var used = await c.ExecuteScalarAsync<int>("SELECT (SELECT COUNT(*) FROM dbo.Products WHERE SupplierId = @id) + (SELECT COUNT(*) FROM dbo.PurchaseOrders WHERE SupplierId = @id)", new { id });
        if (used > 0) return false;
        await c.ExecuteAsync("DELETE FROM dbo.Suppliers WHERE SupplierId = @id", new { id });
        return true;
    }
}

public class WarehouseService
{
    private readonly DbConnectionFactory _db;
    public WarehouseService(DbConnectionFactory db) => _db = db;

    public async Task<List<Warehouse>> AllAsync(bool activeOnly = false)
    {
        using var c = _db.Create();
        return (await c.QueryAsync<Warehouse>("SELECT * FROM dbo.Warehouses WHERE (@activeOnly = 0 OR IsActive = 1) ORDER BY Name", new { activeOnly })).ToList();
    }

    public async Task<Warehouse> GetAsync(int id)
    {
        using var c = _db.Create();
        return await c.QueryFirstOrDefaultAsync<Warehouse>("SELECT * FROM dbo.Warehouses WHERE WarehouseId = @id", new { id });
    }

    public async Task SaveAsync(Warehouse w)
    {
        using var c = _db.Create();
        if (w.WarehouseId == 0)
            await c.ExecuteAsync("INSERT INTO dbo.Warehouses (Name, Location, Capacity, IsActive) VALUES (@Name, @Location, @Capacity, @IsActive)", w);
        else
            await c.ExecuteAsync("UPDATE dbo.Warehouses SET Name = @Name, Location = @Location, Capacity = @Capacity, IsActive = @IsActive WHERE WarehouseId = @WarehouseId", w);
    }

    public async Task<Dictionary<int, int>> MovementCountsAsync()
    {
        using var c = _db.Create();
        return (await c.QueryAsync<(int WarehouseId, int Cnt)>("SELECT WarehouseId, COUNT(*) FROM dbo.StockMovements WHERE WarehouseId IS NOT NULL GROUP BY WarehouseId"))
            .ToDictionary(x => x.WarehouseId, x => x.Cnt);
    }
}

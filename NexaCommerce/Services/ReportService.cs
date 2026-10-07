using System.Data;
using Dapper;
using NexaCommerce.Data;
using NexaCommerce.Models;

namespace NexaCommerce.Services;

public class ReportService
{
    private readonly DbConnectionFactory _db;
    private readonly OrderService _orders;
    private readonly ProductService _products;

    public ReportService(DbConnectionFactory db, OrderService orders, ProductService products)
    {
        _db = db; _orders = orders; _products = products;
    }

    public async Task<DashboardStats> StatsAsync()
    {
        using var c = _db.Create();
        return await c.QueryFirstAsync<DashboardStats>("SELECT * FROM dbo.vw_DashboardStats");
    }

    public async Task<AdminDashboardVm> DashboardAsync()
    {
        var vm = new AdminDashboardVm { Stats = await StatsAsync() };
        using var c = _db.Create();
        var from = DateTime.Today.AddDays(-29);
        var raw = (await c.QueryAsync<DailySales>("SELECT * FROM dbo.vw_DailySales WHERE SaleDate >= @from ORDER BY SaleDate", new { from })).ToDictionary(d => d.SaleDate.Date);
        for (var d = from; d <= DateTime.Today; d = d.AddDays(1))
            vm.Sales.Add(raw.TryGetValue(d, out var s) ? s : new DailySales { SaleDate = d });

        vm.Revenue30 = vm.Sales.Sum(s => s.Revenue);
        vm.Orders30 = vm.Sales.Sum(s => s.Orders);
        vm.AvgOrderValue30 = vm.Orders30 == 0 ? 0 : vm.Revenue30 / vm.Orders30;
        vm.RevenuePrev30 = await c.ExecuteScalarAsync<decimal>(
            "SELECT ISNULL(SUM(Revenue), 0) FROM dbo.vw_DailySales WHERE SaleDate >= @a AND SaleDate < @b", new { a = from.AddDays(-30), b = from });

        vm.TopProducts = (await c.QueryAsync<TopProduct>(@"SELECT TOP 5 oi.ProductId, oi.ProductName, SUM(oi.Quantity) AS Units, SUM(oi.LineTotal) AS Revenue
            FROM dbo.OrderItems oi JOIN dbo.Orders o ON o.OrderId = oi.OrderId
            WHERE o.Status <> 'Cancelled' AND o.CreatedAt >= @from GROUP BY oi.ProductId, oi.ProductName ORDER BY Revenue DESC", new { from })).ToList();
        vm.CategorySales = (await c.QueryAsync<LabelValue>("SELECT CategoryName AS Label, Revenue AS Value FROM dbo.vw_CategorySales ORDER BY Revenue DESC")).ToList();
        vm.StatusBreakdown = (await c.QueryAsync<LabelValue>("SELECT Status AS Label, CAST(COUNT(*) AS DECIMAL(18,2)) AS Value FROM dbo.Orders GROUP BY Status")).ToList();
        vm.RecentOrders = await _orders.RecentAsync(7);
        vm.LowStock = (await _products.LowStockAsync()).Take(6).ToList();
        return vm;
    }

    public async Task<SalesReportVm> SalesAsync(DateTime from, DateTime to)
    {
        var vm = new SalesReportVm { From = from, To = to };
        using var c = _db.Create();
        using var grid = await c.QueryMultipleAsync("dbo.sp_SalesReport", new { From = from.Date, To = to.Date }, commandType: CommandType.StoredProcedure);
        var daily = (await grid.ReadAsync<DailySales>()).ToDictionary(d => d.SaleDate.Date);
        for (var d = from.Date; d <= to.Date; d = d.AddDays(1))
            vm.Daily.Add(daily.TryGetValue(d, out var s) ? s : new DailySales { SaleDate = d });
        vm.TopProducts = (await grid.ReadAsync<TopProduct>()).ToList();
        vm.ByPayment = (await grid.ReadAsync<LabelValue>()).ToList();
        vm.ByCity = (await grid.ReadAsync<LabelValue>()).ToList();
        return vm;
    }

    public async Task<InventoryReportVm> InventoryAsync()
    {
        var vm = new InventoryReportVm { Products = await _products.AllAsync() };
        vm.ValueByCategory = vm.Products.GroupBy(p => p.CategoryName)
            .Select(g => new LabelValue { Label = g.Key, Value = g.Sum(p => p.StockQuantity * p.CostPrice) })
            .OrderByDescending(x => x.Value).ToList();
        return vm;
    }

    public async Task<List<Product>> ProductPerformanceAsync()
    {
        using var c = _db.Create();
        return (await c.QueryAsync<Product>("SELECT * FROM dbo.vw_ProductList ORDER BY UnitsSold DESC")).ToList();
    }
}

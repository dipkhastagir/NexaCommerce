/* =====================================================================
   NexaCommerce  -  02_Views.sql
   Reporting views. Re-runnable (CREATE OR ALTER).
   ===================================================================== */

CREATE OR ALTER VIEW dbo.vw_ProductList AS
SELECT  p.ProductId, p.Sku, p.Name, p.Slug, p.Description,
        p.CategoryId, c.Name AS CategoryName, c.Icon AS CategoryIcon,
        p.BrandId, b.Name AS BrandName,
        p.SupplierId, s.Name AS SupplierName,
        p.Price, p.CostPrice, p.DiscountPrice, p.StockQuantity, p.ReorderLevel,
        p.IsActive, p.IsFeatured, p.CreatedAt, p.UpdatedAt,
        CAST(ISNULL(r.AvgRating, 0) AS DECIMAL(4,2)) AS AvgRating,
        ISNULL(r.ReviewCount, 0) AS ReviewCount,
        ISNULL(sold.UnitsSold, 0) AS UnitsSold,
        CASE WHEN p.StockQuantity = 0 THEN 'Out of stock'
             WHEN p.StockQuantity <= p.ReorderLevel THEN 'Low stock'
             ELSE 'In stock' END AS StockStatus
FROM dbo.Products p
JOIN dbo.Categories c ON c.CategoryId = p.CategoryId
LEFT JOIN dbo.Brands b ON b.BrandId = p.BrandId
LEFT JOIN dbo.Suppliers s ON s.SupplierId = p.SupplierId
LEFT JOIN (SELECT ProductId, AVG(CAST(Rating AS DECIMAL(4,2))) AS AvgRating, COUNT(*) AS ReviewCount
           FROM dbo.Reviews WHERE IsApproved = 1 GROUP BY ProductId) r ON r.ProductId = p.ProductId
LEFT JOIN (SELECT oi.ProductId, SUM(oi.Quantity) AS UnitsSold
           FROM dbo.OrderItems oi JOIN dbo.Orders o ON o.OrderId = oi.OrderId
           WHERE o.Status <> 'Cancelled' GROUP BY oi.ProductId) sold ON sold.ProductId = p.ProductId;
GO

CREATE OR ALTER VIEW dbo.vw_DashboardStats AS
SELECT
    (SELECT COUNT(*) FROM dbo.Products) AS TotalProducts,
    (SELECT COUNT(*) FROM dbo.Products WHERE IsActive = 1) AS ActiveProducts,
    (SELECT COUNT(*) FROM dbo.Categories) AS TotalCategories,
    (SELECT ISNULL(SUM(CAST(StockQuantity AS DECIMAL(18,2)) * CostPrice), 0) FROM dbo.Products) AS InventoryValue,
    (SELECT COUNT(*) FROM dbo.Products WHERE StockQuantity > 0 AND StockQuantity <= ReorderLevel) AS LowStockCount,
    (SELECT COUNT(*) FROM dbo.Products WHERE StockQuantity = 0) AS OutOfStockCount,
    (SELECT COUNT(*) FROM dbo.Orders) AS TotalOrders,
    (SELECT COUNT(*) FROM dbo.Orders WHERE Status IN ('Pending','Confirmed','Processing')) AS PendingOrders,
    (SELECT ISNULL(SUM(TotalAmount), 0) FROM dbo.Orders WHERE Status <> 'Cancelled') AS TotalRevenue,
    (SELECT ISNULL(SUM(TotalAmount), 0) FROM dbo.Orders WHERE Status <> 'Cancelled'
        AND CreatedAt >= DATEFROMPARTS(YEAR(SYSDATETIME()), MONTH(SYSDATETIME()), 1)) AS RevenueThisMonth,
    (SELECT COUNT(*) FROM dbo.Users u JOIN dbo.Roles r ON r.RoleId = u.RoleId WHERE r.Name = 'Customer') AS TotalCustomers,
    (SELECT COUNT(*) FROM dbo.Orders WHERE RiskLevel = 'High' AND Status NOT IN ('Cancelled','Delivered')) AS HighRiskOrders,
    (SELECT COUNT(*) FROM dbo.Reviews WHERE IsApproved = 0) AS PendingReviews,
    (SELECT COUNT(*) FROM dbo.Returns WHERE Status = 'Requested') AS OpenReturns;
GO

CREATE OR ALTER VIEW dbo.vw_OrderSummary AS
SELECT  o.OrderId, o.OrderNumber, o.UserId, u.FullName AS CustomerName, u.Email AS CustomerEmail,
        o.Status, o.PaymentMethod, o.PaymentStatus, o.SubTotal, o.DiscountAmount, o.ShippingFee,
        o.TotalAmount, o.CouponCode, o.ShipName, o.ShipPhone, o.ShipAddress, o.ShipCity, o.Notes,
        o.RiskScore, o.RiskLevel, o.RiskReasons, o.CreatedAt, o.UpdatedAt,
        (SELECT ISNULL(SUM(Quantity),0) FROM dbo.OrderItems oi WHERE oi.OrderId = o.OrderId) AS ItemCount
FROM dbo.Orders o
JOIN dbo.Users u ON u.UserId = o.UserId;
GO

CREATE OR ALTER VIEW dbo.vw_DailySales AS
SELECT  CAST(o.CreatedAt AS DATE) AS SaleDate,
        COUNT(DISTINCT o.OrderId) AS Orders,
        SUM(o.TotalAmount) AS Revenue,
        SUM(o.DiscountAmount) AS Discounts
FROM dbo.Orders o
WHERE o.Status <> 'Cancelled'
GROUP BY CAST(o.CreatedAt AS DATE);
GO

CREATE OR ALTER VIEW dbo.vw_ProductDailyDemand AS
SELECT  oi.ProductId, CAST(o.CreatedAt AS DATE) AS SaleDate, SUM(oi.Quantity) AS Units, SUM(oi.LineTotal) AS Revenue
FROM dbo.OrderItems oi
JOIN dbo.Orders o ON o.OrderId = oi.OrderId
WHERE o.Status <> 'Cancelled'
GROUP BY oi.ProductId, CAST(o.CreatedAt AS DATE);
GO

CREATE OR ALTER VIEW dbo.vw_CustomerRfmBase AS
SELECT  u.UserId, u.FullName, u.Email, u.CreatedAt AS JoinedAt,
        MAX(o.CreatedAt) AS LastOrderDate,
        COUNT(o.OrderId) AS Frequency,
        ISNULL(SUM(o.TotalAmount), 0) AS Monetary
FROM dbo.Users u
JOIN dbo.Roles r ON r.RoleId = u.RoleId AND r.Name = 'Customer'
LEFT JOIN dbo.Orders o ON o.UserId = u.UserId AND o.Status <> 'Cancelled'
GROUP BY u.UserId, u.FullName, u.Email, u.CreatedAt;
GO

CREATE OR ALTER VIEW dbo.vw_LowStock AS
SELECT p.ProductId, p.Sku, p.Name, p.StockQuantity, p.ReorderLevel, c.Name AS CategoryName,
       s.Name AS SupplierName, ISNULL(s.LeadTimeDays, 7) AS LeadTimeDays
FROM dbo.Products p
JOIN dbo.Categories c ON c.CategoryId = p.CategoryId
LEFT JOIN dbo.Suppliers s ON s.SupplierId = p.SupplierId
WHERE p.IsActive = 1 AND p.StockQuantity <= p.ReorderLevel;
GO

CREATE OR ALTER VIEW dbo.vw_CategorySales AS
SELECT c.CategoryId, c.Name AS CategoryName, c.Icon,
       ISNULL(SUM(oi.LineTotal), 0) AS Revenue, ISNULL(SUM(oi.Quantity), 0) AS Units
FROM dbo.Categories c
LEFT JOIN dbo.Products p ON p.CategoryId = c.CategoryId
LEFT JOIN dbo.OrderItems oi ON oi.ProductId = p.ProductId
LEFT JOIN dbo.Orders o ON o.OrderId = oi.OrderId
WHERE o.OrderId IS NULL OR o.Status <> 'Cancelled'
GROUP BY c.CategoryId, c.Name, c.Icon;
GO

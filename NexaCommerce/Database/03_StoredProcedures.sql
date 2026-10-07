/* =====================================================================
   NexaCommerce  -  03_StoredProcedures.sql
   Business-rule procedures. Re-runnable (CREATE OR ALTER).
   ===================================================================== */

/* ---------- Users & authentication ---------- */
CREATE OR ALTER PROCEDURE dbo.sp_RegisterUser
    @FullName NVARCHAR(120), @Email NVARCHAR(160), @Phone NVARCHAR(30),
    @PasswordHash NVARCHAR(200), @PasswordSalt NVARCHAR(200), @RoleName NVARCHAR(50) = 'Customer'
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM dbo.Users WHERE Email = @Email)
    BEGIN
        SELECT -1 AS UserId; RETURN;
    END
    DECLARE @RoleId INT = (SELECT RoleId FROM dbo.Roles WHERE Name = @RoleName);
    IF @RoleId IS NULL SET @RoleId = (SELECT RoleId FROM dbo.Roles WHERE Name = 'Customer');
    INSERT INTO dbo.Users (FullName, Email, Phone, PasswordHash, PasswordSalt, RoleId)
    VALUES (@FullName, @Email, @Phone, @PasswordHash, @PasswordSalt, @RoleId);
    SELECT CAST(SCOPE_IDENTITY() AS INT) AS UserId;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_GetUserByEmail @Email NVARCHAR(160)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT u.*, r.Name AS RoleName FROM dbo.Users u JOIN dbo.Roles r ON r.RoleId = u.RoleId WHERE u.Email = @Email;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_CreatePasswordResetToken
    @Email NVARCHAR(160), @Token NVARCHAR(200), @ExpiresAt DATETIME2
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @UserId INT = (SELECT UserId FROM dbo.Users WHERE Email = @Email AND IsActive = 1);
    IF @UserId IS NULL BEGIN SELECT 0 AS UserId; RETURN; END
    -- invalidate previous unused tokens: one live token per user
    UPDATE dbo.PasswordResetTokens SET UsedAt = SYSDATETIME() WHERE UserId = @UserId AND UsedAt IS NULL;
    INSERT INTO dbo.PasswordResetTokens (UserId, Token, ExpiresAt) VALUES (@UserId, @Token, @ExpiresAt);
    SELECT @UserId AS UserId;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_ResetPasswordWithToken
    @Token NVARCHAR(200), @PasswordHash NVARCHAR(200), @PasswordSalt NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @TokenId INT, @UserId INT;
    SELECT @TokenId = TokenId, @UserId = UserId FROM dbo.PasswordResetTokens
    WHERE Token = @Token AND UsedAt IS NULL AND ExpiresAt > SYSDATETIME();
    IF @TokenId IS NULL BEGIN SELECT 0 AS UserId; RETURN; END
    BEGIN TRAN;
        UPDATE dbo.Users SET PasswordHash = @PasswordHash, PasswordSalt = @PasswordSalt WHERE UserId = @UserId;
        UPDATE dbo.PasswordResetTokens SET UsedAt = SYSDATETIME() WHERE TokenId = @TokenId;
    COMMIT;
    SELECT @UserId AS UserId;
END
GO

/* ---------- Catalog ---------- */
CREATE OR ALTER PROCEDURE dbo.sp_CreateProduct
    @Sku NVARCHAR(40), @Name NVARCHAR(160), @Slug NVARCHAR(180), @Description NVARCHAR(MAX),
    @CategoryId INT, @BrandId INT = NULL, @SupplierId INT = NULL,
    @Price DECIMAL(18,2), @CostPrice DECIMAL(18,2), @DiscountPrice DECIMAL(18,2) = NULL,
    @StockQuantity INT, @ReorderLevel INT, @IsActive BIT, @IsFeatured BIT, @UserId INT = NULL
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    IF EXISTS (SELECT 1 FROM dbo.Products WHERE Sku = @Sku) BEGIN SELECT -1 AS ProductId; RETURN; END
    BEGIN TRAN;
        INSERT INTO dbo.Products (Sku, Name, Slug, Description, CategoryId, BrandId, SupplierId, Price, CostPrice,
                                  DiscountPrice, StockQuantity, ReorderLevel, IsActive, IsFeatured)
        VALUES (@Sku, @Name, @Slug, @Description, @CategoryId, @BrandId, @SupplierId, @Price, @CostPrice,
                @DiscountPrice, @StockQuantity, @ReorderLevel, @IsActive, @IsFeatured);
        DECLARE @Id INT = SCOPE_IDENTITY();
        IF @StockQuantity > 0
            INSERT INTO dbo.StockMovements (ProductId, MovementType, Quantity, BalanceAfter, Reference, Note, CreatedBy)
            VALUES (@Id, 'OPENING', @StockQuantity, @StockQuantity, @Sku, 'Opening stock on product creation', @UserId);
    COMMIT;
    SELECT @Id AS ProductId;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_UpdateProduct
    @ProductId INT, @Sku NVARCHAR(40), @Name NVARCHAR(160), @Slug NVARCHAR(180), @Description NVARCHAR(MAX),
    @CategoryId INT, @BrandId INT = NULL, @SupplierId INT = NULL,
    @Price DECIMAL(18,2), @CostPrice DECIMAL(18,2), @DiscountPrice DECIMAL(18,2) = NULL,
    @ReorderLevel INT, @IsActive BIT, @IsFeatured BIT
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM dbo.Products WHERE Sku = @Sku AND ProductId <> @ProductId) BEGIN SELECT -1 AS Result; RETURN; END
    UPDATE dbo.Products SET Sku = @Sku, Name = @Name, Slug = @Slug, Description = @Description,
        CategoryId = @CategoryId, BrandId = @BrandId, SupplierId = @SupplierId, Price = @Price, CostPrice = @CostPrice,
        DiscountPrice = @DiscountPrice, ReorderLevel = @ReorderLevel, IsActive = @IsActive, IsFeatured = @IsFeatured,
        UpdatedAt = SYSDATETIME()
    WHERE ProductId = @ProductId;
    SELECT @@ROWCOUNT AS Result;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_DeleteProduct @ProductId INT
AS
BEGIN
    SET NOCOUNT ON;
    -- products with sales history are archived, never hard-deleted, so reports stay correct
    IF EXISTS (SELECT 1 FROM dbo.OrderItems WHERE ProductId = @ProductId)
       OR EXISTS (SELECT 1 FROM dbo.PurchaseOrderItems WHERE ProductId = @ProductId)
    BEGIN
        UPDATE dbo.Products SET IsActive = 0, UpdatedAt = SYSDATETIME() WHERE ProductId = @ProductId;
        SELECT 'Archived' AS Result; RETURN;
    END
    DELETE FROM dbo.StockMovements WHERE ProductId = @ProductId;
    DELETE FROM dbo.Reviews WHERE ProductId = @ProductId;
    DELETE FROM dbo.Products WHERE ProductId = @ProductId;
    SELECT 'Deleted' AS Result;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_SearchProducts
    @Search NVARCHAR(100) = NULL, @CategoryId INT = NULL, @BrandId INT = NULL,
    @MinPrice DECIMAL(18,2) = NULL, @MaxPrice DECIMAL(18,2) = NULL,
    @InStockOnly BIT = 0, @ActiveOnly BIT = 1, @StockStatus NVARCHAR(20) = NULL,
    @Sort NVARCHAR(20) = 'newest', @Page INT = 1, @PageSize INT = 12
AS
BEGIN
    SET NOCOUNT ON;
    ;WITH filtered AS (
        SELECT * FROM dbo.vw_ProductList
        WHERE (@Search IS NULL OR Name LIKE '%' + @Search + '%' OR Sku LIKE '%' + @Search + '%' OR BrandName LIKE '%' + @Search + '%')
          AND (@CategoryId IS NULL OR CategoryId = @CategoryId
               OR CategoryId IN (SELECT CategoryId FROM dbo.Categories WHERE ParentCategoryId = @CategoryId))
          AND (@BrandId IS NULL OR BrandId = @BrandId)
          AND (@MinPrice IS NULL OR ISNULL(DiscountPrice, Price) >= @MinPrice)
          AND (@MaxPrice IS NULL OR ISNULL(DiscountPrice, Price) <= @MaxPrice)
          AND (@InStockOnly = 0 OR StockQuantity > 0)
          AND (@ActiveOnly = 0 OR IsActive = 1)
          AND (@StockStatus IS NULL OR StockStatus = @StockStatus)
    )
    SELECT *, COUNT(*) OVER() AS TotalCount FROM filtered
    ORDER BY
        CASE WHEN @Sort = 'price_asc'  THEN ISNULL(DiscountPrice, Price) END ASC,
        CASE WHEN @Sort = 'price_desc' THEN ISNULL(DiscountPrice, Price) END DESC,
        CASE WHEN @Sort = 'name'       THEN Name END ASC,
        CASE WHEN @Sort = 'rating'     THEN AvgRating END DESC,
        CASE WHEN @Sort = 'popular'    THEN UnitsSold END DESC,
        CASE WHEN @Sort = 'stock'      THEN StockQuantity END ASC,
        CreatedAt DESC, ProductId DESC
    OFFSET (@Page - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_SaveCategory
    @CategoryId INT = 0, @Name NVARCHAR(100), @Slug NVARCHAR(120), @Description NVARCHAR(500),
    @ParentCategoryId INT = NULL, @Icon NVARCHAR(50), @IsActive BIT
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM dbo.Categories WHERE Slug = @Slug AND CategoryId <> @CategoryId) BEGIN SELECT -1 AS CategoryId; RETURN; END
    IF @ParentCategoryId = @CategoryId SET @ParentCategoryId = NULL;
    IF @CategoryId = 0
    BEGIN
        INSERT INTO dbo.Categories (Name, Slug, Description, ParentCategoryId, Icon, IsActive)
        VALUES (@Name, @Slug, @Description, @ParentCategoryId, @Icon, @IsActive);
        SELECT CAST(SCOPE_IDENTITY() AS INT) AS CategoryId;
    END
    ELSE
    BEGIN
        UPDATE dbo.Categories SET Name = @Name, Slug = @Slug, Description = @Description,
            ParentCategoryId = @ParentCategoryId, Icon = @Icon, IsActive = @IsActive
        WHERE CategoryId = @CategoryId;
        SELECT @CategoryId AS CategoryId;
    END
END
GO

/* ---------- Inventory ---------- */
CREATE OR ALTER PROCEDURE dbo.sp_AdjustStock
    @ProductId INT, @Quantity INT, @MovementType NVARCHAR(20), @Reference NVARCHAR(60) = NULL,
    @Note NVARCHAR(300) = NULL, @WarehouseId INT = NULL, @UserId INT = NULL
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @Current INT = (SELECT StockQuantity FROM dbo.Products WITH (UPDLOCK) WHERE ProductId = @ProductId);
    IF @Current IS NULL BEGIN SELECT -1 AS NewBalance, 'Product not found' AS Message; RETURN; END
    IF @Current + @Quantity < 0 BEGIN SELECT -1 AS NewBalance, 'Stock cannot go below zero' AS Message; RETURN; END
    BEGIN TRAN;
        UPDATE dbo.Products SET StockQuantity = StockQuantity + @Quantity, UpdatedAt = SYSDATETIME() WHERE ProductId = @ProductId;
        INSERT INTO dbo.StockMovements (ProductId, WarehouseId, MovementType, Quantity, BalanceAfter, Reference, Note, CreatedBy)
        VALUES (@ProductId, @WarehouseId, @MovementType, @Quantity, @Current + @Quantity, @Reference, @Note, @UserId);
    COMMIT;
    SELECT @Current + @Quantity AS NewBalance, 'OK' AS Message;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_ReceivePurchaseOrder @PurchaseOrderId INT, @UserId INT = NULL
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @Status NVARCHAR(20), @PoNumber NVARCHAR(30), @WarehouseId INT;
    SELECT @Status = Status, @PoNumber = PoNumber, @WarehouseId = WarehouseId FROM dbo.PurchaseOrders WHERE PurchaseOrderId = @PurchaseOrderId;
    IF @Status IS NULL BEGIN SELECT 'Purchase order not found' AS Message; RETURN; END
    IF @Status IN ('Received','Cancelled') BEGIN SELECT 'This purchase order is already ' + LOWER(@Status) AS Message; RETURN; END
    BEGIN TRAN;
        UPDATE p SET p.StockQuantity = p.StockQuantity + i.Quantity, p.CostPrice = i.UnitCost, p.UpdatedAt = SYSDATETIME()
        FROM dbo.Products p JOIN dbo.PurchaseOrderItems i ON i.ProductId = p.ProductId
        WHERE i.PurchaseOrderId = @PurchaseOrderId;

        INSERT INTO dbo.StockMovements (ProductId, WarehouseId, MovementType, Quantity, BalanceAfter, Reference, Note, CreatedBy)
        SELECT i.ProductId, @WarehouseId, 'PURCHASE', i.Quantity, p.StockQuantity, @PoNumber, 'Goods received', @UserId
        FROM dbo.PurchaseOrderItems i JOIN dbo.Products p ON p.ProductId = i.ProductId
        WHERE i.PurchaseOrderId = @PurchaseOrderId;

        UPDATE dbo.PurchaseOrders SET Status = 'Received', ReceivedDate = SYSDATETIME() WHERE PurchaseOrderId = @PurchaseOrderId;
    COMMIT;
    SELECT 'OK' AS Message;
END
GO

/* ---------- Orders ---------- */
CREATE OR ALTER PROCEDURE dbo.sp_PlaceOrder
    @CartKey NVARCHAR(64), @UserId INT,
    @ShipName NVARCHAR(120), @ShipPhone NVARCHAR(30), @ShipAddress NVARCHAR(250), @ShipCity NVARCHAR(80),
    @PaymentMethod NVARCHAR(20), @PaymentRef NVARCHAR(60) = NULL, @PaymentSucceeded BIT = 0,
    @CouponCode NVARCHAR(40) = NULL, @Notes NVARCHAR(500) = NULL,
    @ShippingFee DECIMAL(18,2) = 0, @FreeShippingThreshold DECIMAL(18,2) = 0,
    @OrderId INT OUTPUT, @Message NVARCHAR(400) OUTPUT
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    SET @OrderId = 0; SET @Message = 'OK';
    BEGIN TRY
        BEGIN TRAN;

        IF NOT EXISTS (SELECT 1 FROM dbo.CartItems WHERE CartKey = @CartKey)
        BEGIN SET @Message = 'Your cart is empty.'; ROLLBACK; RETURN; END

        IF EXISTS (SELECT 1 FROM dbo.CartItems c JOIN dbo.Products p WITH (UPDLOCK) ON p.ProductId = c.ProductId
                   WHERE c.CartKey = @CartKey AND (p.IsActive = 0 OR p.StockQuantity < c.Quantity))
        BEGIN
            SELECT TOP 1 @Message = p.Name + ' has only ' + CAST(p.StockQuantity AS NVARCHAR(10)) + ' left in stock.'
            FROM dbo.CartItems c JOIN dbo.Products p ON p.ProductId = c.ProductId
            WHERE c.CartKey = @CartKey AND (p.IsActive = 0 OR p.StockQuantity < c.Quantity);
            ROLLBACK; RETURN;
        END

        DECLARE @SubTotal DECIMAL(18,2) =
            (SELECT SUM(c.Quantity * ISNULL(p.DiscountPrice, p.Price))
             FROM dbo.CartItems c JOIN dbo.Products p ON p.ProductId = c.ProductId WHERE c.CartKey = @CartKey);

        DECLARE @Discount DECIMAL(18,2) = 0;
        IF @CouponCode IS NOT NULL AND LTRIM(RTRIM(@CouponCode)) <> ''
        BEGIN
            DECLARE @CType NVARCHAR(10), @CValue DECIMAL(18,2), @CMin DECIMAL(18,2), @CId INT;
            SELECT @CId = CouponId, @CType = DiscountType, @CValue = Value, @CMin = MinOrderAmount
            FROM dbo.Coupons WITH (UPDLOCK)
            WHERE Code = @CouponCode AND IsActive = 1 AND SYSDATETIME() BETWEEN StartsAt AND EndsAt
              AND (MaxUses IS NULL OR UsedCount < MaxUses);
            IF @CId IS NULL BEGIN SET @Message = 'Coupon code is invalid or expired.'; ROLLBACK; RETURN; END
            IF @SubTotal < @CMin BEGIN SET @Message = 'This coupon needs a minimum order of ' + FORMAT(@CMin, 'N2') + '.'; ROLLBACK; RETURN; END
            SET @Discount = CASE WHEN @CType = 'Percent' THEN ROUND(@SubTotal * @CValue / 100.0, 2) ELSE @CValue END;
            IF @Discount > @SubTotal SET @Discount = @SubTotal;
            UPDATE dbo.Coupons SET UsedCount = UsedCount + 1 WHERE CouponId = @CId;
        END
        ELSE SET @CouponCode = NULL;

        DECLARE @Ship DECIMAL(18,2) = CASE WHEN @FreeShippingThreshold > 0 AND @SubTotal >= @FreeShippingThreshold THEN 0 ELSE @ShippingFee END;
        DECLARE @Total DECIMAL(18,2) = @SubTotal - @Discount + @Ship;

        INSERT INTO dbo.Orders (OrderNumber, UserId, Status, PaymentMethod, PaymentStatus, SubTotal, DiscountAmount,
                                ShippingFee, TotalAmount, CouponCode, ShipName, ShipPhone, ShipAddress, ShipCity, Notes)
        VALUES (CONVERT(NVARCHAR(36), NEWID()), @UserId, 'Pending', @PaymentMethod,
                CASE WHEN @PaymentSucceeded = 1 THEN 'Paid' ELSE 'Unpaid' END,
                @SubTotal, @Discount, @Ship, @Total, @CouponCode, @ShipName, @ShipPhone, @ShipAddress, @ShipCity, @Notes);
        SET @OrderId = SCOPE_IDENTITY();
        UPDATE dbo.Orders SET OrderNumber = 'NX' + RIGHT('000000' + CAST(@OrderId AS NVARCHAR(10)), 6) WHERE OrderId = @OrderId;

        INSERT INTO dbo.OrderItems (OrderId, ProductId, ProductName, UnitPrice, Quantity, LineTotal)
        SELECT @OrderId, p.ProductId, p.Name, ISNULL(p.DiscountPrice, p.Price), c.Quantity, c.Quantity * ISNULL(p.DiscountPrice, p.Price)
        FROM dbo.CartItems c JOIN dbo.Products p ON p.ProductId = c.ProductId WHERE c.CartKey = @CartKey;

        UPDATE p SET p.StockQuantity = p.StockQuantity - c.Quantity, p.UpdatedAt = SYSDATETIME()
        FROM dbo.Products p JOIN dbo.CartItems c ON c.ProductId = p.ProductId WHERE c.CartKey = @CartKey;

        DECLARE @OrderNo NVARCHAR(30) = (SELECT OrderNumber FROM dbo.Orders WHERE OrderId = @OrderId);
        INSERT INTO dbo.StockMovements (ProductId, MovementType, Quantity, BalanceAfter, Reference, Note, CreatedBy)
        SELECT p.ProductId, 'SALE', -c.Quantity, p.StockQuantity, @OrderNo, 'Customer order', @UserId
        FROM dbo.CartItems c JOIN dbo.Products p ON p.ProductId = c.ProductId WHERE c.CartKey = @CartKey;

        INSERT INTO dbo.OrderStatusHistory (OrderId, Status, Note, ChangedBy) VALUES (@OrderId, 'Pending', 'Order placed', @UserId);

        INSERT INTO dbo.Payments (OrderId, Method, Amount, TransactionRef, Status, PaidAt)
        VALUES (@OrderId, @PaymentMethod, @Total, @PaymentRef,
                CASE WHEN @PaymentSucceeded = 1 THEN 'Success' ELSE 'Pending' END,
                CASE WHEN @PaymentSucceeded = 1 THEN SYSDATETIME() ELSE NULL END);

        DELETE FROM dbo.CartItems WHERE CartKey = @CartKey;
        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        SET @OrderId = 0;
        SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_UpdateOrderStatus
    @OrderId INT, @Status NVARCHAR(20), @Note NVARCHAR(300) = NULL, @UserId INT = NULL
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @Current NVARCHAR(20), @OrderNo NVARCHAR(30), @Method NVARCHAR(20);
    SELECT @Current = Status, @OrderNo = OrderNumber, @Method = PaymentMethod FROM dbo.Orders WHERE OrderId = @OrderId;
    IF @Current IS NULL BEGIN SELECT 'Order not found' AS Message; RETURN; END
    IF @Current IN ('Cancelled','Delivered') BEGIN SELECT 'A ' + LOWER(@Current) + ' order cannot change status' AS Message; RETURN; END
    IF @Current = @Status BEGIN SELECT 'Order is already ' + LOWER(@Status) AS Message; RETURN; END

    BEGIN TRAN;
        IF @Status = 'Cancelled'
        BEGIN
            UPDATE p SET p.StockQuantity = p.StockQuantity + oi.Quantity
            FROM dbo.Products p JOIN dbo.OrderItems oi ON oi.ProductId = p.ProductId WHERE oi.OrderId = @OrderId;

            INSERT INTO dbo.StockMovements (ProductId, MovementType, Quantity, BalanceAfter, Reference, Note, CreatedBy)
            SELECT oi.ProductId, 'CANCEL', oi.Quantity, p.StockQuantity, @OrderNo, 'Order cancelled - stock restored', @UserId
            FROM dbo.OrderItems oi JOIN dbo.Products p ON p.ProductId = oi.ProductId WHERE oi.OrderId = @OrderId;

            UPDATE dbo.Orders SET PaymentStatus = CASE WHEN PaymentStatus = 'Paid' THEN 'Refunded' ELSE PaymentStatus END WHERE OrderId = @OrderId;
            UPDATE dbo.Payments SET Status = 'Refunded' WHERE OrderId = @OrderId AND Status = 'Success';
        END

        IF @Status = 'Delivered' AND @Method = 'COD'
        BEGIN
            UPDATE dbo.Orders SET PaymentStatus = 'Paid' WHERE OrderId = @OrderId;
            UPDATE dbo.Payments SET Status = 'Success', PaidAt = SYSDATETIME() WHERE OrderId = @OrderId AND Status = 'Pending';
        END

        UPDATE dbo.Orders SET Status = @Status, UpdatedAt = SYSDATETIME() WHERE OrderId = @OrderId;
        INSERT INTO dbo.OrderStatusHistory (OrderId, Status, Note, ChangedBy) VALUES (@OrderId, @Status, @Note, @UserId);
    COMMIT;
    SELECT 'OK' AS Message;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_SalesReport @From DATE, @To DATE
AS
BEGIN
    SET NOCOUNT ON;
    SELECT SaleDate, Orders, Revenue, Discounts FROM dbo.vw_DailySales
    WHERE SaleDate BETWEEN @From AND @To ORDER BY SaleDate;

    SELECT TOP 10 oi.ProductId, oi.ProductName, SUM(oi.Quantity) AS Units, SUM(oi.LineTotal) AS Revenue
    FROM dbo.OrderItems oi JOIN dbo.Orders o ON o.OrderId = oi.OrderId
    WHERE o.Status <> 'Cancelled' AND CAST(o.CreatedAt AS DATE) BETWEEN @From AND @To
    GROUP BY oi.ProductId, oi.ProductName ORDER BY Revenue DESC;

    SELECT o.PaymentMethod AS Label, COUNT(*) AS Orders, SUM(o.TotalAmount) AS Revenue
    FROM dbo.Orders o WHERE o.Status <> 'Cancelled' AND CAST(o.CreatedAt AS DATE) BETWEEN @From AND @To
    GROUP BY o.PaymentMethod;

    SELECT o.ShipCity AS Label, COUNT(*) AS Orders, SUM(o.TotalAmount) AS Revenue
    FROM dbo.Orders o WHERE o.Status <> 'Cancelled' AND CAST(o.CreatedAt AS DATE) BETWEEN @From AND @To
    GROUP BY o.ShipCity ORDER BY Revenue DESC;
END
GO

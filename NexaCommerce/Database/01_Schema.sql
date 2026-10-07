/* =====================================================================
   NexaCommerce  -  01_Schema.sql
   Creates every table, key and index used by the platform.
   Safe to run in SSMS against an empty NexaCommerceDB database.
   ===================================================================== */
SET NOCOUNT ON;
GO

CREATE TABLE dbo.Roles (
    RoleId       INT IDENTITY(1,1) PRIMARY KEY,
    Name         NVARCHAR(50)  NOT NULL UNIQUE,
    Description  NVARCHAR(250) NULL
);
GO

CREATE TABLE dbo.Users (
    UserId        INT IDENTITY(1,1) PRIMARY KEY,
    FullName      NVARCHAR(120) NOT NULL,
    Email         NVARCHAR(160) NOT NULL,
    Phone         NVARCHAR(30)  NULL,
    PasswordHash  NVARCHAR(200) NOT NULL,
    PasswordSalt  NVARCHAR(200) NOT NULL,
    RoleId        INT NOT NULL REFERENCES dbo.Roles(RoleId),
    IsActive      BIT NOT NULL DEFAULT 1,
    CreatedAt     DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    LastLoginAt   DATETIME2 NULL,
    CONSTRAINT UQ_Users_Email UNIQUE (Email)
);
GO

CREATE TABLE dbo.PasswordResetTokens (
    TokenId    INT IDENTITY(1,1) PRIMARY KEY,
    UserId     INT NOT NULL REFERENCES dbo.Users(UserId) ON DELETE CASCADE,
    Token      NVARCHAR(200) NOT NULL UNIQUE,
    ExpiresAt  DATETIME2 NOT NULL,
    UsedAt     DATETIME2 NULL,
    CreatedAt  DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);
GO

CREATE TABLE dbo.Addresses (
    AddressId   INT IDENTITY(1,1) PRIMARY KEY,
    UserId      INT NOT NULL REFERENCES dbo.Users(UserId) ON DELETE CASCADE,
    Label       NVARCHAR(40)  NOT NULL,
    RecipientName NVARCHAR(120) NOT NULL,
    Phone       NVARCHAR(30)  NOT NULL,
    Line1       NVARCHAR(250) NOT NULL,
    City        NVARCHAR(80)  NOT NULL,
    PostalCode  NVARCHAR(20)  NULL,
    IsDefault   BIT NOT NULL DEFAULT 0
);
GO

CREATE TABLE dbo.Categories (
    CategoryId        INT IDENTITY(1,1) PRIMARY KEY,
    Name              NVARCHAR(100) NOT NULL,
    Slug              NVARCHAR(120) NOT NULL UNIQUE,
    Description       NVARCHAR(500) NULL,
    ParentCategoryId  INT NULL REFERENCES dbo.Categories(CategoryId),
    Icon              NVARCHAR(50)  NOT NULL DEFAULT 'bi-box',
    IsActive          BIT NOT NULL DEFAULT 1,
    CreatedAt         DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);
GO

CREATE TABLE dbo.Brands (
    BrandId      INT IDENTITY(1,1) PRIMARY KEY,
    Name         NVARCHAR(100) NOT NULL UNIQUE,
    Description  NVARCHAR(500) NULL,
    IsActive     BIT NOT NULL DEFAULT 1
);
GO

CREATE TABLE dbo.Suppliers (
    SupplierId     INT IDENTITY(1,1) PRIMARY KEY,
    Name           NVARCHAR(150) NOT NULL,
    ContactPerson  NVARCHAR(120) NULL,
    Email          NVARCHAR(160) NULL,
    Phone          NVARCHAR(30)  NULL,
    Address        NVARCHAR(250) NULL,
    LeadTimeDays   INT NOT NULL DEFAULT 7,
    IsActive       BIT NOT NULL DEFAULT 1,
    CreatedAt      DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);
GO

CREATE TABLE dbo.Warehouses (
    WarehouseId  INT IDENTITY(1,1) PRIMARY KEY,
    Name         NVARCHAR(100) NOT NULL,
    Location     NVARCHAR(200) NULL,
    Capacity     INT NOT NULL DEFAULT 10000,
    IsActive     BIT NOT NULL DEFAULT 1
);
GO

CREATE TABLE dbo.Products (
    ProductId      INT IDENTITY(1,1) PRIMARY KEY,
    Sku            NVARCHAR(40)  NOT NULL UNIQUE,
    Name           NVARCHAR(160) NOT NULL,
    Slug           NVARCHAR(180) NOT NULL,
    Description    NVARCHAR(MAX) NULL,
    CategoryId     INT NOT NULL REFERENCES dbo.Categories(CategoryId),
    BrandId        INT NULL REFERENCES dbo.Brands(BrandId),
    SupplierId     INT NULL REFERENCES dbo.Suppliers(SupplierId),
    Price          DECIMAL(18,2) NOT NULL CHECK (Price >= 0),
    CostPrice      DECIMAL(18,2) NOT NULL DEFAULT 0,
    DiscountPrice  DECIMAL(18,2) NULL,
    StockQuantity  INT NOT NULL DEFAULT 0 CHECK (StockQuantity >= 0),
    ReorderLevel   INT NOT NULL DEFAULT 10,
    IsActive       BIT NOT NULL DEFAULT 1,
    IsFeatured     BIT NOT NULL DEFAULT 0,
    CreatedAt      DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    UpdatedAt      DATETIME2 NULL
);
CREATE INDEX IX_Products_Category ON dbo.Products(CategoryId);
GO

CREATE TABLE dbo.StockMovements (
    MovementId    INT IDENTITY(1,1) PRIMARY KEY,
    ProductId     INT NOT NULL REFERENCES dbo.Products(ProductId),
    WarehouseId   INT NULL REFERENCES dbo.Warehouses(WarehouseId),
    MovementType  NVARCHAR(20) NOT NULL,   -- OPENING, SALE, PURCHASE, ADJUSTMENT, RETURN, CANCEL
    Quantity      INT NOT NULL,            -- signed: + adds stock, - removes stock
    BalanceAfter  INT NOT NULL,
    Reference     NVARCHAR(60)  NULL,
    Note          NVARCHAR(300) NULL,
    CreatedBy     INT NULL REFERENCES dbo.Users(UserId),
    CreatedAt     DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);
CREATE INDEX IX_StockMovements_Product ON dbo.StockMovements(ProductId, CreatedAt);
GO

CREATE TABLE dbo.PurchaseOrders (
    PurchaseOrderId  INT IDENTITY(1,1) PRIMARY KEY,
    PoNumber         NVARCHAR(30) NOT NULL UNIQUE,
    SupplierId       INT NOT NULL REFERENCES dbo.Suppliers(SupplierId),
    WarehouseId      INT NULL REFERENCES dbo.Warehouses(WarehouseId),
    Status           NVARCHAR(20) NOT NULL DEFAULT 'Draft',   -- Draft, Ordered, Received, Cancelled
    OrderDate        DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    ExpectedDate     DATETIME2 NULL,
    ReceivedDate     DATETIME2 NULL,
    TotalAmount      DECIMAL(18,2) NOT NULL DEFAULT 0,
    Notes            NVARCHAR(500) NULL,
    CreatedBy        INT NULL REFERENCES dbo.Users(UserId)
);
GO

CREATE TABLE dbo.PurchaseOrderItems (
    Id               INT IDENTITY(1,1) PRIMARY KEY,
    PurchaseOrderId  INT NOT NULL REFERENCES dbo.PurchaseOrders(PurchaseOrderId) ON DELETE CASCADE,
    ProductId        INT NOT NULL REFERENCES dbo.Products(ProductId),
    Quantity         INT NOT NULL CHECK (Quantity > 0),
    UnitCost         DECIMAL(18,2) NOT NULL
);
GO

CREATE TABLE dbo.Coupons (
    CouponId        INT IDENTITY(1,1) PRIMARY KEY,
    Code            NVARCHAR(40) NOT NULL UNIQUE,
    Description     NVARCHAR(200) NULL,
    DiscountType    NVARCHAR(10) NOT NULL DEFAULT 'Percent',   -- Percent | Fixed
    Value           DECIMAL(18,2) NOT NULL,
    MinOrderAmount  DECIMAL(18,2) NOT NULL DEFAULT 0,
    MaxUses         INT NULL,
    UsedCount       INT NOT NULL DEFAULT 0,
    StartsAt        DATETIME2 NOT NULL,
    EndsAt          DATETIME2 NOT NULL,
    IsActive        BIT NOT NULL DEFAULT 1
);
GO

CREATE TABLE dbo.CartItems (
    CartItemId  INT IDENTITY(1,1) PRIMARY KEY,
    CartKey     NVARCHAR(64) NOT NULL,
    ProductId   INT NOT NULL REFERENCES dbo.Products(ProductId) ON DELETE CASCADE,
    Quantity    INT NOT NULL CHECK (Quantity > 0),
    AddedAt     DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);
CREATE INDEX IX_CartItems_Key ON dbo.CartItems(CartKey);
GO

CREATE TABLE dbo.WishlistItems (
    Id         INT IDENTITY(1,1) PRIMARY KEY,
    UserId     INT NOT NULL REFERENCES dbo.Users(UserId) ON DELETE CASCADE,
    ProductId  INT NOT NULL REFERENCES dbo.Products(ProductId) ON DELETE CASCADE,
    AddedAt    DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    CONSTRAINT UQ_Wishlist UNIQUE (UserId, ProductId)
);
GO

CREATE TABLE dbo.Orders (
    OrderId         INT IDENTITY(1,1) PRIMARY KEY,
    OrderNumber     NVARCHAR(30) NOT NULL UNIQUE,
    UserId          INT NOT NULL REFERENCES dbo.Users(UserId),
    Status          NVARCHAR(20) NOT NULL DEFAULT 'Pending',  -- Pending, Confirmed, Processing, Shipped, Delivered, Cancelled
    PaymentMethod   NVARCHAR(20) NOT NULL DEFAULT 'COD',      -- COD, Card, bKash
    PaymentStatus   NVARCHAR(20) NOT NULL DEFAULT 'Unpaid',   -- Unpaid, Paid, Refunded
    SubTotal        DECIMAL(18,2) NOT NULL,
    DiscountAmount  DECIMAL(18,2) NOT NULL DEFAULT 0,
    ShippingFee     DECIMAL(18,2) NOT NULL DEFAULT 0,
    TotalAmount     DECIMAL(18,2) NOT NULL,
    CouponCode      NVARCHAR(40)  NULL,
    ShipName        NVARCHAR(120) NOT NULL,
    ShipPhone       NVARCHAR(30)  NOT NULL,
    ShipAddress     NVARCHAR(250) NOT NULL,
    ShipCity        NVARCHAR(80)  NOT NULL,
    Notes           NVARCHAR(500) NULL,
    RiskScore       INT NOT NULL DEFAULT 0,
    RiskLevel       NVARCHAR(10) NOT NULL DEFAULT 'Low',
    RiskReasons     NVARCHAR(500) NULL,
    CreatedAt       DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    UpdatedAt       DATETIME2 NULL
);
CREATE INDEX IX_Orders_User ON dbo.Orders(UserId, CreatedAt);
CREATE INDEX IX_Orders_Created ON dbo.Orders(CreatedAt);
GO

CREATE TABLE dbo.OrderItems (
    OrderItemId  INT IDENTITY(1,1) PRIMARY KEY,
    OrderId      INT NOT NULL REFERENCES dbo.Orders(OrderId) ON DELETE CASCADE,
    ProductId    INT NOT NULL REFERENCES dbo.Products(ProductId),
    ProductName  NVARCHAR(160) NOT NULL,
    UnitPrice    DECIMAL(18,2) NOT NULL,
    Quantity     INT NOT NULL CHECK (Quantity > 0),
    LineTotal    DECIMAL(18,2) NOT NULL
);
CREATE INDEX IX_OrderItems_Order ON dbo.OrderItems(OrderId);
CREATE INDEX IX_OrderItems_Product ON dbo.OrderItems(ProductId);
GO

CREATE TABLE dbo.OrderStatusHistory (
    Id         INT IDENTITY(1,1) PRIMARY KEY,
    OrderId    INT NOT NULL REFERENCES dbo.Orders(OrderId) ON DELETE CASCADE,
    Status     NVARCHAR(20) NOT NULL,
    Note       NVARCHAR(300) NULL,
    ChangedBy  INT NULL REFERENCES dbo.Users(UserId),
    ChangedAt  DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);
GO

CREATE TABLE dbo.Payments (
    PaymentId       INT IDENTITY(1,1) PRIMARY KEY,
    OrderId         INT NOT NULL REFERENCES dbo.Orders(OrderId) ON DELETE CASCADE,
    Method          NVARCHAR(20) NOT NULL,
    Amount          DECIMAL(18,2) NOT NULL,
    TransactionRef  NVARCHAR(60) NULL,
    Status          NVARCHAR(20) NOT NULL,   -- Pending, Success, Failed, Refunded
    PaidAt          DATETIME2 NULL,
    CreatedAt       DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);
GO

CREATE TABLE dbo.Reviews (
    ReviewId    INT IDENTITY(1,1) PRIMARY KEY,
    ProductId   INT NOT NULL REFERENCES dbo.Products(ProductId) ON DELETE CASCADE,
    UserId      INT NOT NULL REFERENCES dbo.Users(UserId),
    Rating      INT NOT NULL CHECK (Rating BETWEEN 1 AND 5),
    Title       NVARCHAR(120) NULL,
    Comment     NVARCHAR(1000) NULL,
    IsApproved  BIT NOT NULL DEFAULT 0,
    CreatedAt   DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);
GO

CREATE TABLE dbo.Returns (
    ReturnId      INT IDENTITY(1,1) PRIMARY KEY,
    OrderId       INT NOT NULL REFERENCES dbo.Orders(OrderId),
    UserId        INT NOT NULL REFERENCES dbo.Users(UserId),
    Reason        NVARCHAR(500) NOT NULL,
    Status        NVARCHAR(20) NOT NULL DEFAULT 'Requested',  -- Requested, Approved, Rejected, Refunded
    AdminNote     NVARCHAR(500) NULL,
    RefundAmount  DECIMAL(18,2) NOT NULL DEFAULT 0,
    CreatedAt     DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    ResolvedAt    DATETIME2 NULL
);
GO

CREATE TABLE dbo.Notifications (
    NotificationId  INT IDENTITY(1,1) PRIMARY KEY,
    UserId          INT NULL REFERENCES dbo.Users(UserId),   -- NULL = visible to all back-office staff
    Title           NVARCHAR(150) NOT NULL,
    Message         NVARCHAR(500) NOT NULL,
    Link            NVARCHAR(250) NULL,
    Category        NVARCHAR(30) NOT NULL DEFAULT 'General',  -- General, Stock, Order, Risk, Review
    IsRead          BIT NOT NULL DEFAULT 0,
    CreatedAt       DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);
GO

CREATE TABLE dbo.AuditLogs (
    AuditId    BIGINT IDENTITY(1,1) PRIMARY KEY,
    UserId     INT NULL,
    UserEmail  NVARCHAR(160) NULL,
    Action     NVARCHAR(60)  NOT NULL,
    Entity     NVARCHAR(60)  NOT NULL,
    EntityId   NVARCHAR(40)  NULL,
    Details    NVARCHAR(1000) NULL,
    IpAddress  NVARCHAR(60)  NULL,
    CreatedAt  DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);
CREATE INDEX IX_AuditLogs_Created ON dbo.AuditLogs(CreatedAt);
GO

CREATE TABLE dbo.Settings (
    SettingKey    NVARCHAR(60) PRIMARY KEY,
    SettingValue  NVARCHAR(500) NOT NULL,
    Description   NVARCHAR(300) NULL
);
GO

CREATE TABLE dbo.ContactMessages (
    MessageId  INT IDENTITY(1,1) PRIMARY KEY,
    Name       NVARCHAR(120) NOT NULL,
    Email      NVARCHAR(160) NOT NULL,
    Subject    NVARCHAR(150) NOT NULL,
    Body       NVARCHAR(2000) NOT NULL,
    IsHandled  BIT NOT NULL DEFAULT 0,
    CreatedAt  DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);
GO

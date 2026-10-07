using System.ComponentModel.DataAnnotations;

namespace NexaCommerce.Models;

public class Role
{
    public int RoleId { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public int UserCount { get; set; }
}

public class User
{
    public int UserId { get; set; }
    public string FullName { get; set; }
    public string Email { get; set; }
    public string Phone { get; set; }
    public string PasswordHash { get; set; }
    public string PasswordSalt { get; set; }
    public int RoleId { get; set; }
    public string RoleName { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

public class Address
{
    public int AddressId { get; set; }
    public int UserId { get; set; }
    [Required, StringLength(40)] public string Label { get; set; } = "Home";
    [Required, StringLength(120), Display(Name = "Recipient name")] public string RecipientName { get; set; }
    [Required, StringLength(30)] public string Phone { get; set; }
    [Required, StringLength(250), Display(Name = "Street address")] public string Line1 { get; set; }
    [Required, StringLength(80)] public string City { get; set; }
    [StringLength(20), Display(Name = "Postal code")] public string PostalCode { get; set; }
    [Display(Name = "Use as default")] public bool IsDefault { get; set; }
}

public class Category
{
    public int CategoryId { get; set; }
    [Required, StringLength(100)] public string Name { get; set; }
    [StringLength(120)] public string Slug { get; set; }
    [StringLength(500)] public string Description { get; set; }
    [Display(Name = "Parent category")] public int? ParentCategoryId { get; set; }
    public string ParentName { get; set; }
    [Required, StringLength(50)] public string Icon { get; set; } = "bi-box";
    [Display(Name = "Active")] public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public int ProductCount { get; set; }
}

public class Brand
{
    public int BrandId { get; set; }
    [Required, StringLength(100)] public string Name { get; set; }
    [StringLength(500)] public string Description { get; set; }
    [Display(Name = "Active")] public bool IsActive { get; set; } = true;
    public int ProductCount { get; set; }
}

public class Supplier
{
    public int SupplierId { get; set; }
    [Required, StringLength(150)] public string Name { get; set; }
    [StringLength(120), Display(Name = "Contact person")] public string ContactPerson { get; set; }
    [EmailAddress, StringLength(160)] public string Email { get; set; }
    [StringLength(30)] public string Phone { get; set; }
    [StringLength(250)] public string Address { get; set; }
    [Range(1, 180), Display(Name = "Lead time (days)")] public int LeadTimeDays { get; set; } = 7;
    [Display(Name = "Active")] public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public int ProductCount { get; set; }
}

public class Warehouse
{
    public int WarehouseId { get; set; }
    [Required, StringLength(100)] public string Name { get; set; }
    [StringLength(200)] public string Location { get; set; }
    [Range(0, 10000000)] public int Capacity { get; set; } = 10000;
    [Display(Name = "Active")] public bool IsActive { get; set; } = true;
}

public class Product
{
    public int ProductId { get; set; }
    public string Sku { get; set; }
    public string Name { get; set; }
    public string Slug { get; set; }
    public string Description { get; set; }
    public int CategoryId { get; set; }
    public string CategoryName { get; set; }
    public string CategoryIcon { get; set; }
    public int? BrandId { get; set; }
    public string BrandName { get; set; }
    public int? SupplierId { get; set; }
    public string SupplierName { get; set; }
    public decimal Price { get; set; }
    public decimal CostPrice { get; set; }
    public decimal? DiscountPrice { get; set; }
    public int StockQuantity { get; set; }
    public int ReorderLevel { get; set; }
    public bool IsActive { get; set; }
    public bool IsFeatured { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public decimal AvgRating { get; set; }
    public int ReviewCount { get; set; }
    public int UnitsSold { get; set; }
    public string StockStatus { get; set; }
    public int TotalCount { get; set; }

    public decimal EffectivePrice => DiscountPrice ?? Price;
    public bool OnSale => DiscountPrice.HasValue && DiscountPrice.Value < Price;
    public int DiscountPercent => OnSale && Price > 0 ? (int)Math.Round((1 - DiscountPrice.Value / Price) * 100) : 0;
    public decimal MarginPercent => EffectivePrice > 0 ? Math.Round((EffectivePrice - CostPrice) / EffectivePrice * 100, 1) : 0;
}

public class StockMovement
{
    public int MovementId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; }
    public string Sku { get; set; }
    public int? WarehouseId { get; set; }
    public string WarehouseName { get; set; }
    public string MovementType { get; set; }
    public int Quantity { get; set; }
    public int BalanceAfter { get; set; }
    public string Reference { get; set; }
    public string Note { get; set; }
    public string CreatedByName { get; set; }
    public DateTime CreatedAt { get; set; }
    public int TotalCount { get; set; }
}

public class PurchaseOrder
{
    public int PurchaseOrderId { get; set; }
    public string PoNumber { get; set; }
    public int SupplierId { get; set; }
    public string SupplierName { get; set; }
    public int? WarehouseId { get; set; }
    public string WarehouseName { get; set; }
    public string Status { get; set; }
    public DateTime OrderDate { get; set; }
    public DateTime? ExpectedDate { get; set; }
    public DateTime? ReceivedDate { get; set; }
    public decimal TotalAmount { get; set; }
    public string Notes { get; set; }
    public string CreatedByName { get; set; }
    public int ItemCount { get; set; }
    public List<PurchaseOrderItem> Items { get; set; } = new();
}

public class PurchaseOrderItem
{
    public int Id { get; set; }
    public int PurchaseOrderId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; }
    public string Sku { get; set; }
    public int Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal LineTotal => Quantity * UnitCost;
}

public class Coupon
{
    public int CouponId { get; set; }
    [Required, StringLength(40), RegularExpression("^[A-Za-z0-9_-]+$", ErrorMessage = "Use letters, digits, - or _ only")]
    public string Code { get; set; }
    [StringLength(200)] public string Description { get; set; }
    [Required, Display(Name = "Discount type")] public string DiscountType { get; set; } = "Percent";
    [Range(0.01, 1000000)] public decimal Value { get; set; }
    [Range(0, 100000000), Display(Name = "Minimum order")] public decimal MinOrderAmount { get; set; }
    [Range(1, 1000000), Display(Name = "Max uses")] public int? MaxUses { get; set; }
    public int UsedCount { get; set; }
    [Display(Name = "Starts")] public DateTime StartsAt { get; set; } = DateTime.Today;
    [Display(Name = "Ends")] public DateTime EndsAt { get; set; } = DateTime.Today.AddMonths(1);
    [Display(Name = "Active")] public bool IsActive { get; set; } = true;

    public bool IsCurrentlyValid => IsActive && DateTime.Now >= StartsAt && DateTime.Now <= EndsAt && (!MaxUses.HasValue || UsedCount < MaxUses);
}

public class CartItem
{
    public int CartItemId { get; set; }
    public int ProductId { get; set; }
    public string Name { get; set; }
    public string Sku { get; set; }
    public decimal Price { get; set; }
    public decimal? DiscountPrice { get; set; }
    public int Quantity { get; set; }
    public int StockQuantity { get; set; }
    public string CategoryName { get; set; }
    public string CategoryIcon { get; set; }
    public decimal UnitPrice => DiscountPrice ?? Price;
    public decimal LineTotal => UnitPrice * Quantity;
}

public class Order
{
    public int OrderId { get; set; }
    public string OrderNumber { get; set; }
    public int UserId { get; set; }
    public string CustomerName { get; set; }
    public string CustomerEmail { get; set; }
    public string Status { get; set; }
    public string PaymentMethod { get; set; }
    public string PaymentStatus { get; set; }
    public decimal SubTotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal ShippingFee { get; set; }
    public decimal TotalAmount { get; set; }
    public string CouponCode { get; set; }
    public string ShipName { get; set; }
    public string ShipPhone { get; set; }
    public string ShipAddress { get; set; }
    public string ShipCity { get; set; }
    public string Notes { get; set; }
    public int RiskScore { get; set; }
    public string RiskLevel { get; set; }
    public string RiskReasons { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int ItemCount { get; set; }
    public int TotalCount { get; set; }
    public List<OrderItem> Items { get; set; } = new();
    public List<OrderStatusHistory> History { get; set; } = new();
    public List<Payment> Payments { get; set; } = new();
}

public class OrderItem
{
    public int OrderItemId { get; set; }
    public int OrderId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; }
    public string Sku { get; set; }
    public string CategoryIcon { get; set; }
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
}

public class OrderStatusHistory
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string Status { get; set; }
    public string Note { get; set; }
    public string ChangedByName { get; set; }
    public DateTime ChangedAt { get; set; }
}

public class Payment
{
    public int PaymentId { get; set; }
    public int OrderId { get; set; }
    public string Method { get; set; }
    public decimal Amount { get; set; }
    public string TransactionRef { get; set; }
    public string Status { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class Review
{
    public int ReviewId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; }
    public int UserId { get; set; }
    public string UserName { get; set; }
    [Range(1, 5)] public int Rating { get; set; } = 5;
    [StringLength(120)] public string Title { get; set; }
    [StringLength(1000)] public string Comment { get; set; }
    public bool IsApproved { get; set; }
    public DateTime CreatedAt { get; set; }
    public int TotalCount { get; set; }
}

public class ReturnRequest
{
    public int ReturnId { get; set; }
    public int OrderId { get; set; }
    public string OrderNumber { get; set; }
    public decimal OrderTotal { get; set; }
    public int UserId { get; set; }
    public string CustomerName { get; set; }
    public string Reason { get; set; }
    public string Status { get; set; }
    public string AdminNote { get; set; }
    public decimal RefundAmount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
}

public class Notification
{
    public int NotificationId { get; set; }
    public int? UserId { get; set; }
    public string Title { get; set; }
    public string Message { get; set; }
    public string Link { get; set; }
    public string Category { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AuditLog
{
    public long AuditId { get; set; }
    public int? UserId { get; set; }
    public string UserEmail { get; set; }
    public string Action { get; set; }
    public string Entity { get; set; }
    public string EntityId { get; set; }
    public string Details { get; set; }
    public string IpAddress { get; set; }
    public DateTime CreatedAt { get; set; }
    public int TotalCount { get; set; }
}

public class Setting
{
    public string SettingKey { get; set; }
    public string SettingValue { get; set; }
    public string Description { get; set; }
}

public class ContactMessage
{
    public int MessageId { get; set; }
    [Required, StringLength(120)] public string Name { get; set; }
    [Required, EmailAddress, StringLength(160)] public string Email { get; set; }
    [Required, StringLength(150)] public string Subject { get; set; }
    [Required, StringLength(2000), Display(Name = "Message")] public string Body { get; set; }
    public bool IsHandled { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class DashboardStats
{
    public int TotalProducts { get; set; }
    public int ActiveProducts { get; set; }
    public int TotalCategories { get; set; }
    public decimal InventoryValue { get; set; }
    public int LowStockCount { get; set; }
    public int OutOfStockCount { get; set; }
    public int TotalOrders { get; set; }
    public int PendingOrders { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal RevenueThisMonth { get; set; }
    public int TotalCustomers { get; set; }
    public int HighRiskOrders { get; set; }
    public int PendingReviews { get; set; }
    public int OpenReturns { get; set; }
}

public class CustomerSummary
{
    public int UserId { get; set; }
    public string FullName { get; set; }
    public string Email { get; set; }
    public string Phone { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastOrderDate { get; set; }
    public int OrderCount { get; set; }
    public decimal TotalSpent { get; set; }
    public int TotalCount { get; set; }
}

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using NexaCommerce.Models.Intelligence;

namespace NexaCommerce.Models;

public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalCount { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}

/* ---------------- Account ---------------- */
public class LoginVm
{
    [Required, EmailAddress] public string Email { get; set; }
    [Required, DataType(DataType.Password)] public string Password { get; set; }
    [Display(Name = "Keep me signed in")] public bool RememberMe { get; set; }
    public string ReturnUrl { get; set; }
}

public class RegisterVm
{
    [Required, StringLength(120), Display(Name = "Full name")] public string FullName { get; set; }
    [Required, EmailAddress, StringLength(160)] public string Email { get; set; }
    [Phone, StringLength(30)] public string Phone { get; set; }
    [Required, StringLength(100, MinimumLength = 8), DataType(DataType.Password)]
    [RegularExpression(@"^(?=.*[A-Za-z])(?=.*\d).+$", ErrorMessage = "Use at least one letter and one digit")]
    public string Password { get; set; }
    [Required, DataType(DataType.Password), Compare(nameof(Password), ErrorMessage = "Passwords do not match"), Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; }
}

public class ForgotPasswordVm
{
    [Required, EmailAddress] public string Email { get; set; }
}

public class ResetPasswordVm
{
    [Required] public string Token { get; set; }
    [Required, StringLength(100, MinimumLength = 8), DataType(DataType.Password), Display(Name = "New password")]
    [RegularExpression(@"^(?=.*[A-Za-z])(?=.*\d).+$", ErrorMessage = "Use at least one letter and one digit")]
    public string NewPassword { get; set; }
    [Required, DataType(DataType.Password), Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match"), Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; }
}

public class ChangePasswordVm
{
    [Required, DataType(DataType.Password), Display(Name = "Current password")] public string CurrentPassword { get; set; }
    [Required, StringLength(100, MinimumLength = 8), DataType(DataType.Password), Display(Name = "New password")]
    [RegularExpression(@"^(?=.*[A-Za-z])(?=.*\d).+$", ErrorMessage = "Use at least one letter and one digit")]
    public string NewPassword { get; set; }
    [Required, DataType(DataType.Password), Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match"), Display(Name = "Confirm new password")]
    public string ConfirmPassword { get; set; }
}

public class ProfileVm
{
    [Required, StringLength(120), Display(Name = "Full name")] public string FullName { get; set; }
    public string Email { get; set; }
    [Phone, StringLength(30)] public string Phone { get; set; }
    public string RoleName { get; set; }
    public DateTime CreatedAt { get; set; }
    public int OrderCount { get; set; }
    public decimal TotalSpent { get; set; }
}

/* ---------------- Storefront ---------------- */
public class HomeVm
{
    public List<Product> Featured { get; set; } = new();
    public List<Product> BestSellers { get; set; } = new();
    public List<Product> Deals { get; set; } = new();
    public List<Category> Categories { get; set; } = new();
    public List<BasketRule> TopPairs { get; set; } = new();
}

public class ShopVm
{
    public PagedResult<Product> Products { get; set; } = new();
    public List<Category> Categories { get; set; } = new();
    public List<Brand> Brands { get; set; } = new();
    public string Search { get; set; }
    public int? CategoryId { get; set; }
    public int? BrandId { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public bool InStock { get; set; }
    public string Sort { get; set; } = "newest";
    public string Heading { get; set; } = "All products";
    public Category CurrentCategory { get; set; }
}

public class ProductDetailsVm
{
    public Product Product { get; set; }
    public List<Review> Reviews { get; set; } = new();
    public List<Product> Related { get; set; } = new();
    public List<BasketRule> BoughtTogether { get; set; } = new();
    public bool InWishlist { get; set; }
    public bool CanReview { get; set; }
    public int[] RatingHistogram { get; set; } = new int[5];
}

public class CartVm
{
    public List<CartItem> Items { get; set; } = new();
    public decimal SubTotal => Items.Sum(i => i.LineTotal);
    public int UnitCount => Items.Sum(i => i.Quantity);
    public decimal ShippingFee { get; set; }
    public decimal FreeShippingThreshold { get; set; }
    public decimal EffectiveShipping => Items.Count == 0 ? 0 : (FreeShippingThreshold > 0 && SubTotal >= FreeShippingThreshold ? 0 : ShippingFee);
    public decimal Total => SubTotal + EffectiveShipping;
    public decimal AmountToFreeShipping => FreeShippingThreshold > 0 ? Math.Max(0, FreeShippingThreshold - SubTotal) : 0;
    public List<BasketRule> Suggestions { get; set; } = new();
}

public class CheckoutVm
{
    public CartVm Cart { get; set; } = new();
    public List<Address> Addresses { get; set; } = new();
    public int? AddressId { get; set; }
    [Required, StringLength(120), Display(Name = "Recipient name")] public string ShipName { get; set; }
    [Required, StringLength(30), Display(Name = "Phone")] public string ShipPhone { get; set; }
    [Required, StringLength(250), Display(Name = "Street address")] public string ShipAddress { get; set; }
    [Required, StringLength(80), Display(Name = "City")] public string ShipCity { get; set; }
    [Required, Display(Name = "Payment method")] public string PaymentMethod { get; set; } = "COD";
    [StringLength(40), Display(Name = "Coupon code")] public string CouponCode { get; set; }
    [StringLength(500), Display(Name = "Delivery notes")] public string Notes { get; set; }
    [Display(Name = "Card / wallet number")] public string PaymentAccount { get; set; }
    public bool SaveAddress { get; set; }
}

public class OrderConfirmationVm
{
    public Order Order { get; set; }
    public string PaymentMessage { get; set; }
}

public class WriteReviewVm
{
    public int ProductId { get; set; }
    public string ProductName { get; set; }
    [Range(1, 5)] public int Rating { get; set; } = 5;
    [Required, StringLength(120)] public string Title { get; set; }
    [Required, StringLength(1000)] public string Comment { get; set; }
}

public class ReturnRequestVm
{
    public int OrderId { get; set; }
    public string OrderNumber { get; set; }
    [Required, StringLength(500, MinimumLength = 10), Display(Name = "Why are you returning this order?")]
    public string Reason { get; set; }
}

/* ---------------- Admin ---------------- */
public class ProductFormVm
{
    public int ProductId { get; set; }
    [Required, StringLength(40), Display(Name = "SKU")] public string Sku { get; set; }
    [Required, StringLength(160)] public string Name { get; set; }
    public string Description { get; set; }
    [Required, Display(Name = "Category")] public int CategoryId { get; set; }
    [Display(Name = "Brand")] public int? BrandId { get; set; }
    [Display(Name = "Supplier")] public int? SupplierId { get; set; }
    [Range(0.01, 100000000), Display(Name = "Selling price")] public decimal Price { get; set; }
    [Range(0, 100000000), Display(Name = "Cost price")] public decimal CostPrice { get; set; }
    [Range(0, 100000000), Display(Name = "Sale price")] public decimal? DiscountPrice { get; set; }
    [Range(0, 1000000), Display(Name = "Opening stock")] public int StockQuantity { get; set; }
    [Range(0, 1000000), Display(Name = "Reorder level")] public int ReorderLevel { get; set; } = 10;
    [Display(Name = "Visible in store")] public bool IsActive { get; set; } = true;
    [Display(Name = "Featured on home page")] public bool IsFeatured { get; set; }
    public IEnumerable<SelectListItem> Categories { get; set; } = new List<SelectListItem>();
    public IEnumerable<SelectListItem> Brands { get; set; } = new List<SelectListItem>();
    public IEnumerable<SelectListItem> Suppliers { get; set; } = new List<SelectListItem>();
}

public class StockAdjustVm
{
    public int ProductId { get; set; }
    public string ProductName { get; set; }
    public string Sku { get; set; }
    public int CurrentStock { get; set; }
    [Required, Display(Name = "Adjustment type")] public string MovementType { get; set; } = "ADJUSTMENT";
    [Required, Range(-1000000, 1000000), Display(Name = "Quantity (+ add, - remove)")] public int Quantity { get; set; }
    [Display(Name = "Warehouse")] public int? WarehouseId { get; set; }
    [StringLength(60)] public string Reference { get; set; }
    [Required, StringLength(300), Display(Name = "Reason")] public string Note { get; set; }
    public IEnumerable<SelectListItem> Warehouses { get; set; } = new List<SelectListItem>();
}

public class PurchaseOrderFormVm
{
    [Required, Display(Name = "Supplier")] public int SupplierId { get; set; }
    [Display(Name = "Receiving warehouse")] public int? WarehouseId { get; set; }
    [Display(Name = "Expected delivery")] public DateTime? ExpectedDate { get; set; } = DateTime.Today.AddDays(7);
    [StringLength(500)] public string Notes { get; set; }
    public List<PurchaseOrderLineVm> Lines { get; set; } = new();
    public IEnumerable<SelectListItem> Suppliers { get; set; } = new List<SelectListItem>();
    public IEnumerable<SelectListItem> Warehouses { get; set; } = new List<SelectListItem>();
    public List<Product> Products { get; set; } = new();
}

public class PurchaseOrderLineVm
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitCost { get; set; }
}

public class UserFormVm
{
    public int UserId { get; set; }
    [Required, StringLength(120), Display(Name = "Full name")] public string FullName { get; set; }
    [Required, EmailAddress, StringLength(160)] public string Email { get; set; }
    [StringLength(30)] public string Phone { get; set; }
    [Required, Display(Name = "Role")] public string RoleName { get; set; } = "Staff";
    [StringLength(100, MinimumLength = 8), DataType(DataType.Password), Display(Name = "Password")] public string Password { get; set; }
    [Display(Name = "Active")] public bool IsActive { get; set; } = true;
}

public class AdminDashboardVm
{
    public DashboardStats Stats { get; set; } = new();
    public List<DailySales> Sales { get; set; } = new();
    public List<TopProduct> TopProducts { get; set; } = new();
    public List<Order> RecentOrders { get; set; } = new();
    public List<Product> LowStock { get; set; } = new();
    public List<LabelValue> CategorySales { get; set; } = new();
    public List<LabelValue> StatusBreakdown { get; set; } = new();
    public decimal Revenue30 { get; set; }
    public decimal RevenuePrev30 { get; set; }
    public int Orders30 { get; set; }
    public decimal AvgOrderValue30 { get; set; }
}

public class DailySales
{
    public DateTime SaleDate { get; set; }
    public int Orders { get; set; }
    public decimal Revenue { get; set; }
    public decimal Discounts { get; set; }
}

public class TopProduct
{
    public int ProductId { get; set; }
    public string ProductName { get; set; }
    public int Units { get; set; }
    public decimal Revenue { get; set; }
}

public class LabelValue
{
    public string Label { get; set; }
    public decimal Value { get; set; }
    public int Orders { get; set; }
    public decimal Revenue { get; set; }
}

public class SalesReportVm
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public List<DailySales> Daily { get; set; } = new();
    public List<TopProduct> TopProducts { get; set; } = new();
    public List<LabelValue> ByPayment { get; set; } = new();
    public List<LabelValue> ByCity { get; set; } = new();
    public decimal Revenue => Daily.Sum(d => d.Revenue);
    public int Orders => Daily.Sum(d => d.Orders);
    public decimal Discounts => Daily.Sum(d => d.Discounts);
    public decimal AvgOrder => Orders == 0 ? 0 : Revenue / Orders;
}

public class InventoryReportVm
{
    public List<Product> Products { get; set; } = new();
    public List<LabelValue> ValueByCategory { get; set; } = new();
    public decimal TotalCostValue => Products.Sum(p => p.StockQuantity * p.CostPrice);
    public decimal TotalRetailValue => Products.Sum(p => p.StockQuantity * p.EffectivePrice);
    public int TotalUnits => Products.Sum(p => p.StockQuantity);
}

public class CustomerDetailsVm
{
    public CustomerSummary Customer { get; set; }
    public List<Order> Orders { get; set; } = new();
    public List<Address> Addresses { get; set; } = new();
    public RfmCustomer Rfm { get; set; }
}

public class SettingsVm
{
    public List<Setting> Settings { get; set; } = new();
}

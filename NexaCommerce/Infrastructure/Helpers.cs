using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace NexaCommerce.Infrastructure;

public static class Fmt
{
    public static string CurrencySymbol { get; set; } = "৳";
    public static string Money(decimal value) => CurrencySymbol + value.ToString("N2", CultureInfo.InvariantCulture);
    public static string Money(decimal? value) => value.HasValue ? Money(value.Value) : "-";
    public static string MoneyShort(decimal value)
    {
        if (value >= 10_000_000) return CurrencySymbol + (value / 10_000_000m).ToString("0.##", CultureInfo.InvariantCulture) + " cr";
        if (value >= 100_000) return CurrencySymbol + (value / 100_000m).ToString("0.##", CultureInfo.InvariantCulture) + " lakh";
        if (value >= 1_000) return CurrencySymbol + (value / 1_000m).ToString("0.#", CultureInfo.InvariantCulture) + "k";
        return Money(value);
    }
    public static string Date(DateTime d) => d.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
    public static string Date(DateTime? d) => d.HasValue ? Date(d.Value) : "-";
    public static string Stamp(DateTime d) => d.ToString("dd MMM yyyy, h:mm tt", CultureInfo.InvariantCulture);
    public static string Stamp(DateTime? d) => d.HasValue ? Stamp(d.Value) : "-";
    public static string Ago(DateTime d)
    {
        var span = DateTime.Now - d;
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} min ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours} h ago";
        if (span.TotalDays < 30) return $"{(int)span.TotalDays} d ago";
        return Date(d);
    }
    public static string Pct(double v, int digits = 1) => (v * 100).ToString("F" + digits, CultureInfo.InvariantCulture) + "%";
    public static string Num(double v, int digits = 2) => v.ToString("F" + digits, CultureInfo.InvariantCulture);
}

public static class Badge
{
    public static string OrderStatus(string s) => s switch
    {
        "Pending" => "st-pending",
        "Confirmed" => "st-confirmed",
        "Processing" => "st-processing",
        "Shipped" => "st-shipped",
        "Delivered" => "st-delivered",
        "Cancelled" => "st-cancelled",
        _ => "st-default"
    };
    public static string Payment(string s) => s switch
    {
        "Paid" => "st-delivered", "Success" => "st-delivered",
        "Refunded" => "st-cancelled", "Failed" => "st-cancelled",
        _ => "st-pending"
    };
    public static string Risk(string s) => s switch { "High" => "risk-high", "Medium" => "risk-medium", _ => "risk-low" };
    public static string Stock(string s) => s switch { "Out of stock" => "stock-out", "Low stock" => "stock-low", _ => "stock-ok" };
    public static string Generic(string s) => s switch
    {
        "Received" or "Approved" or "Refunded" or "Active" => "st-delivered",
        "Ordered" or "Requested" => "st-confirmed",
        "Cancelled" or "Rejected" => "st-cancelled",
        _ => "st-pending"
    };
}

/// <summary>Deterministic artwork for products that have no photo: a tinted tile with the category icon.</summary>
public static class Visual
{
    private static readonly string[] Tints =
    {
        "#E7E3FF", "#DDF3EA", "#FFE9D6", "#E1EEFF", "#FCE2EC", "#F1F0D9", "#E4F4F7", "#EFE6F7"
    };
    private static readonly string[] Inks =
    {
        "#4B2FD6", "#1F7A55", "#B4571B", "#2457B3", "#B32D63", "#6D6A1E", "#1C7487", "#6B3FA0"
    };
    public static string Tint(int seed) => Tints[Math.Abs(seed) % Tints.Length];
    public static string Ink(int seed) => Inks[Math.Abs(seed) % Inks.Length];
    public static string Initials(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 1 ? parts[0][..1].ToUpper() : (parts[0][..1] + parts[1][..1]).ToUpper();
    }
}

public static class Slug
{
    public static string Make(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Guid.NewGuid().ToString("N")[..8];
        var s = text.ToLowerInvariant().Trim();
        s = Regex.Replace(s, @"[^a-z0-9\s-]", "");
        s = Regex.Replace(s, @"[\s-]+", "-").Trim('-');
        return s.Length > 160 ? s[..160] : s;
    }
}

public static class ClaimsExtensions
{
    public static int GetUserId(this ClaimsPrincipal user)
        => int.TryParse(user?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
    public static string GetFullName(this ClaimsPrincipal user) => user?.FindFirstValue("FullName") ?? user?.Identity?.Name ?? "";
    public static string GetEmail(this ClaimsPrincipal user) => user?.FindFirstValue(ClaimTypes.Email) ?? "";
    public static string GetRole(this ClaimsPrincipal user) => user?.FindFirstValue(ClaimTypes.Role) ?? "";
    public static bool IsBackOffice(this ClaimsPrincipal user)
        => user?.Identity?.IsAuthenticated == true && (user.IsInRole("Admin") || user.IsInRole("Manager") || user.IsInRole("Staff"));
}

public static class QueryHelper
{
    /// <summary>Returns the current URL with one query-string key replaced.</summary>
    public static string With(HttpRequest request, string key, object value)
    {
        var dict = request.Query.ToDictionary(k => k.Key, v => v.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        dict[key] = value?.ToString() ?? "";
        var sb = new StringBuilder(request.PathBase + request.Path);
        var first = true;
        foreach (var kv in dict.Where(k => !string.IsNullOrEmpty(k.Value)))
        {
            sb.Append(first ? '?' : '&');
            sb.Append(Uri.EscapeDataString(kv.Key)).Append('=').Append(Uri.EscapeDataString(kv.Value));
            first = false;
        }
        return sb.ToString();
    }
}

public static class CsvWriter
{
    public static string Escape(object value)
    {
        if (value == null) return "";
        var s = value is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : value.ToString();
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
            s = "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }
}

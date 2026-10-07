namespace NexaCommerce.Services;

public class PaymentResult
{
    public bool Success { get; set; }
    public string TransactionRef { get; set; }
    public string Message { get; set; }
}

/// <summary>
/// Abstraction over a payment provider. The simulated implementation lets the full checkout flow be
/// tested without a merchant account; a real provider (SSLCommerz, bKash PGW, Stripe) can replace it
/// by registering a different implementation in Program.cs.
/// </summary>
public interface IPaymentGateway
{
    Task<PaymentResult> ChargeAsync(string method, decimal amount, string account);
}

public class SimulatedPaymentGateway : IPaymentGateway
{
    public Task<PaymentResult> ChargeAsync(string method, decimal amount, string account)
    {
        if (method == "COD")
            return Task.FromResult(new PaymentResult { Success = false, Message = "Cash will be collected on delivery." });

        var digits = new string((account ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length < 4)
            return Task.FromResult(new PaymentResult { Success = false, Message = "Enter a card or wallet number to pay online." });

        // Deterministic test rule: numbers ending in 0000 are declined.
        if (digits.EndsWith("0000"))
            return Task.FromResult(new PaymentResult { Success = false, Message = "The simulated gateway declined this payment. Use a number not ending in 0000." });

        var reference = $"SIM-{method.ToUpperInvariant()}-{DateTime.Now:yyMMddHHmmss}-{digits[^4..]}";
        return Task.FromResult(new PaymentResult { Success = true, TransactionRef = reference, Message = $"Payment approved ({reference})." });
    }
}

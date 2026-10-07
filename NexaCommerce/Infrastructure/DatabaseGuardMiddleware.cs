using NexaCommerce.Data;

namespace NexaCommerce.Infrastructure;

/// <summary>Sends every request to the setup page until the database is reachable and initialised.</summary>
public class DatabaseGuardMiddleware
{
    private readonly RequestDelegate _next;
    public DatabaseGuardMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, DatabaseStatus status)
    {
        var path = context.Request.Path.Value ?? "";
        var passThrough = path.StartsWith("/Setup", StringComparison.OrdinalIgnoreCase)
                          || path.StartsWith("/css") || path.StartsWith("/js") || path.StartsWith("/favicon");
        if (!status.IsReady && !passThrough)
        {
            context.Response.Redirect("/Setup");
            return;
        }
        await _next(context);
    }
}

using System.Data;
using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using NexaCommerce.Data;
using NexaCommerce.Models;

namespace NexaCommerce.Services;

public class AuthService
{
    private readonly DbConnectionFactory _db;
    private readonly PasswordHasher _hasher;

    public AuthService(DbConnectionFactory db, PasswordHasher hasher) { _db = db; _hasher = hasher; }

    public async Task<User> GetByEmailAsync(string email)
    {
        using var c = _db.Create();
        return await c.QueryFirstOrDefaultAsync<User>("dbo.sp_GetUserByEmail", new { Email = email }, commandType: CommandType.StoredProcedure);
    }

    public async Task<User> GetByIdAsync(int id)
    {
        using var c = _db.Create();
        return await c.QueryFirstOrDefaultAsync<User>(
            "SELECT u.*, r.Name AS RoleName FROM dbo.Users u JOIN dbo.Roles r ON r.RoleId = u.RoleId WHERE u.UserId = @id", new { id });
    }

    /// <returns>New user id, or -1 when the e-mail is already registered.</returns>
    public async Task<int> RegisterAsync(string fullName, string email, string phone, string password, string role = "Customer")
    {
        var (hash, salt) = _hasher.Hash(password);
        using var c = _db.Create();
        return await c.ExecuteScalarAsync<int>("dbo.sp_RegisterUser",
            new { FullName = fullName.Trim(), Email = email.Trim().ToLowerInvariant(), Phone = phone, PasswordHash = hash, PasswordSalt = salt, RoleName = role },
            commandType: CommandType.StoredProcedure);
    }

    public async Task<User> ValidateAsync(string email, string password)
    {
        var user = await GetByEmailAsync(email.Trim().ToLowerInvariant());
        if (user == null || !user.IsActive) return null;
        if (!_hasher.Verify(password, user.PasswordHash, user.PasswordSalt)) return null;
        using var c = _db.Create();
        await c.ExecuteAsync("UPDATE dbo.Users SET LastLoginAt = SYSDATETIME() WHERE UserId = @UserId", new { user.UserId });
        return user;
    }

    public async Task SignInAsync(HttpContext http, User user, bool persistent)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Name, user.Email),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.RoleName),
            new("FullName", user.FullName)
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = persistent, ExpiresUtc = persistent ? DateTimeOffset.UtcNow.AddDays(14) : null });
    }

    /// <summary>Creates a one-time reset token valid for 30 minutes. Returns null when the e-mail is unknown.</summary>
    public async Task<string> CreateResetTokenAsync(string email)
    {
        var token = PasswordHasher.NewToken();
        using var c = _db.Create();
        var userId = await c.ExecuteScalarAsync<int>("dbo.sp_CreatePasswordResetToken",
            new { Email = email.Trim().ToLowerInvariant(), Token = token, ExpiresAt = DateTime.Now.AddMinutes(30) },
            commandType: CommandType.StoredProcedure);
        return userId > 0 ? token : null;
    }

    public async Task<bool> IsResetTokenValidAsync(string token)
    {
        using var c = _db.Create();
        return await c.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.PasswordResetTokens WHERE Token = @token AND UsedAt IS NULL AND ExpiresAt > SYSDATETIME()", new { token }) > 0;
    }

    public async Task<int> ResetPasswordAsync(string token, string newPassword)
    {
        var (hash, salt) = _hasher.Hash(newPassword);
        using var c = _db.Create();
        return await c.ExecuteScalarAsync<int>("dbo.sp_ResetPasswordWithToken",
            new { Token = token, PasswordHash = hash, PasswordSalt = salt }, commandType: CommandType.StoredProcedure);
    }

    public async Task<bool> ChangePasswordAsync(int userId, string current, string newPassword)
    {
        var user = await GetByIdAsync(userId);
        if (user == null || !_hasher.Verify(current, user.PasswordHash, user.PasswordSalt)) return false;
        await SetPasswordAsync(userId, newPassword);
        return true;
    }

    public async Task SetPasswordAsync(int userId, string newPassword)
    {
        var (hash, salt) = _hasher.Hash(newPassword);
        using var c = _db.Create();
        await c.ExecuteAsync("UPDATE dbo.Users SET PasswordHash = @hash, PasswordSalt = @salt WHERE UserId = @userId", new { hash, salt, userId });
    }

    public async Task UpdateProfileAsync(int userId, string fullName, string phone)
    {
        using var c = _db.Create();
        await c.ExecuteAsync("UPDATE dbo.Users SET FullName = @fullName, Phone = @phone WHERE UserId = @userId", new { fullName, phone, userId });
    }

    public async Task<ProfileVm> GetProfileAsync(int userId)
    {
        using var c = _db.Create();
        return await c.QueryFirstOrDefaultAsync<ProfileVm>(@"
            SELECT u.FullName, u.Email, u.Phone, r.Name AS RoleName, u.CreatedAt,
                   (SELECT COUNT(*) FROM dbo.Orders WHERE UserId = u.UserId) AS OrderCount,
                   (SELECT ISNULL(SUM(TotalAmount),0) FROM dbo.Orders WHERE UserId = u.UserId AND Status <> 'Cancelled') AS TotalSpent
            FROM dbo.Users u JOIN dbo.Roles r ON r.RoleId = u.RoleId WHERE u.UserId = @userId", new { userId });
    }
}

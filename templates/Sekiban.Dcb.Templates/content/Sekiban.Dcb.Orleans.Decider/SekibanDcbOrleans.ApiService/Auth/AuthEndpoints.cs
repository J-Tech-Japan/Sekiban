using System.Security.Claims;
using Dcb.EventSource.MeetingRoom.User;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sekiban.Dcb;

namespace SekibanDcbOrleans.ApiService.Auth;

/// <summary>
///     Authentication endpoints for both Cookie and JWT authentication.
/// </summary>
public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/auth").WithTags("Authentication");

        // Register a new user
        group.MapPost("/register", RegisterAsync)
            .WithName("Register")
            .WithSummary("Register a new user account")
            .AllowAnonymous();

        // Login (supports both Cookie and JWT)
        group.MapPost("/login", LoginAsync)
            .WithName("Login")
            .WithSummary("Login with email and password")
            .AllowAnonymous();

        // Logout
        group.MapPost("/logout", LogoutAsync)
            .WithName("Logout")
            .WithSummary("Logout the current user")
            .RequireAuthorization();

        // Refresh JWT token
        group.MapPost("/refresh", RefreshTokenAsync)
            .WithName("RefreshToken")
            .WithSummary("Refresh an expired access token")
            .AllowAnonymous();

        // Get current user info
        group.MapGet("/me", GetCurrentUserAsync)
            .WithName("GetCurrentUser")
            .WithSummary("Get information about the authenticated user")
            .RequireAuthorization();

        // Check auth status (works for both authenticated and anonymous)
        group.MapGet("/status", GetAuthStatusAsync)
            .WithName("GetAuthStatus")
            .WithSummary("Check authentication status")
            .AllowAnonymous();
    }

    private static async Task<IResult> RegisterAsync(
        [FromBody] RegisterRequest request,
        UserManager<ApplicationUser> userManager,
        ISekibanExecutor executor)
    {
        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            DisplayName = request.DisplayName ?? request.Email.Split('@')[0],
            EmailConfirmed = true // Skip email confirmation for sample
        };

        var result = await userManager.CreateAsync(user, request.Password);

        if (!result.Succeeded)
        {
            return Results.BadRequest(new AuthResult(
                false,
                "Registration failed",
                result.Errors.Select(e => e.Description)));
        }

        // Add default role
        await userManager.AddToRoleAsync(user, "User");

        if (Guid.TryParse(user.Id, out var userId))
        {
            try
            {
                await executor.ExecuteAsync(new RegisterUser
                {
                    UserId = userId,
                    DisplayName = user.DisplayName ?? request.Email.Split('@')[0],
                    Email = user.Email!,
                    Department = null
                });
                await executor.ExecuteAsync(new GrantUserAccess
                {
                    UserId = userId,
                    InitialRole = "User"
                });
            }
            catch
            {
                await userManager.DeleteAsync(user);
                return Results.Problem("Registration failed while creating the user directory entry.");
            }
        }
        else
        {
            await userManager.DeleteAsync(user);
            return Results.Problem("Registration failed due to invalid user identity.");
        }

        return Results.Ok(new AuthResult(true, "User registered successfully"));
    }

    private static async Task<IResult> LoginAsync(
        [FromBody] LoginRequest request,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        TokenService tokenService,
        ApplicationDbContext dbContext,
        IOptions<JwtSettings> jwtSettings,
        HttpContext httpContext)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user == null)
        {
            return Results.Unauthorized();
        }

        var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            if (result.IsLockedOut)
            {
                return Results.Problem("Account is locked out. Please try again later.", statusCode: 423);
            }
            return Results.Unauthorized();
        }

        var roles = await userManager.GetRolesAsync(user);

        if (request.UseCookies)
        {
            // Cookie authentication for Blazor
            await signInManager.SignInAsync(user, isPersistent: true);
            return Results.Ok(new UserInfoResponse(
                user.Id,
                user.Email!,
                user.DisplayName,
                roles,
                true));
        }
        else
        {
            // JWT authentication for Next.js BFF
            var accessToken = tokenService.GenerateAccessToken(user, roles);
            var refreshToken = tokenService.GenerateRefreshToken();

            // Store refresh token
            var refreshTokenEntity = new RefreshToken
            {
                Token = refreshToken,
                UserId = user.Id,
                ExpiresAt = DateTime.UtcNow.AddDays(jwtSettings.Value.RefreshTokenExpirationDays)
            };
            dbContext.RefreshTokens.Add(refreshTokenEntity);
            await dbContext.SaveChangesAsync();

            return Results.Ok(new TokenResponse(
                accessToken,
                refreshToken,
                DateTime.UtcNow.AddMinutes(jwtSettings.Value.AccessTokenExpirationMinutes),
                refreshTokenEntity.ExpiresAt));
        }
    }

    private static async Task<IResult> LogoutAsync(
        SignInManager<ApplicationUser> signInManager,
        ApplicationDbContext dbContext,
        HttpContext httpContext)
    {
        // Sign out from cookie authentication
        await signInManager.SignOutAsync();

        // If JWT, revoke refresh token if provided in header
        var authHeader = httpContext.Request.Headers.Authorization.FirstOrDefault();
        if (authHeader?.StartsWith("Bearer ") == true)
        {
            var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId != null)
            {
                // Revoke all refresh tokens for this user
                var tokens = await dbContext.RefreshTokens
                    .Where(t => t.UserId == userId && !t.IsRevoked)
                    .ToListAsync();

                foreach (var token in tokens)
                {
                    token.IsRevoked = true;
                    token.RevokedAt = DateTime.UtcNow;
                }
                await dbContext.SaveChangesAsync();
            }
        }

        return Results.Ok(new AuthResult(true, "Logged out successfully"));
    }

    private static async Task<IResult> RefreshTokenAsync(
        [FromBody] RefreshTokenRequest request,
        TokenService tokenService,
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext dbContext,
        IOptions<JwtSettings> jwtSettings)
    {
        // Validate the expired access token
        var principal = tokenService.GetPrincipalFromExpiredToken(request.AccessToken);
        if (principal == null)
        {
            return Results.Unauthorized();
        }

        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null)
        {
            return Results.Unauthorized();
        }

        // Find the refresh token
        var storedToken = await dbContext.RefreshTokens
            .FirstOrDefaultAsync(t => t.Token == request.RefreshToken && t.UserId == userId);

        if (storedToken == null || !storedToken.IsValid)
        {
            return Results.Unauthorized();
        }

        // Get user and generate new tokens
        var user = await userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return Results.Unauthorized();
        }

        var roles = await userManager.GetRolesAsync(user);

        // Revoke old refresh token
        storedToken.IsRevoked = true;
        storedToken.RevokedAt = DateTime.UtcNow;

        // Generate new tokens
        var newAccessToken = tokenService.GenerateAccessToken(user, roles);
        var newRefreshToken = tokenService.GenerateRefreshToken();

        // Store new refresh token
        var newRefreshTokenEntity = new RefreshToken
        {
            Token = newRefreshToken,
            UserId = user.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(jwtSettings.Value.RefreshTokenExpirationDays)
        };
        dbContext.RefreshTokens.Add(newRefreshTokenEntity);
        await dbContext.SaveChangesAsync();

        return Results.Ok(new TokenResponse(
            newAccessToken,
            newRefreshToken,
            DateTime.UtcNow.AddMinutes(jwtSettings.Value.AccessTokenExpirationMinutes),
            newRefreshTokenEntity.ExpiresAt));
    }

    private static async Task<IResult> GetCurrentUserAsync(
        UserManager<ApplicationUser> userManager,
        HttpContext httpContext)
    {
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null)
        {
            return Results.Unauthorized();
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return Results.Unauthorized();
        }

        var roles = await userManager.GetRolesAsync(user);

        return Results.Ok(new UserInfoResponse(
            user.Id,
            user.Email!,
            user.DisplayName,
            roles,
            true));
    }

    private static IResult GetAuthStatusAsync(HttpContext httpContext)
    {
        var isAuthenticated = httpContext.User.Identity?.IsAuthenticated ?? false;

        if (!isAuthenticated)
        {
            return Results.Ok(new UserInfoResponse(
                string.Empty,
                string.Empty,
                null,
                [],
                false));
        }

        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var email = httpContext.User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
        var displayName = httpContext.User.FindFirstValue("display_name");
        var roles = httpContext.User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

        return Results.Ok(new UserInfoResponse(
            userId,
            email,
            displayName,
            roles,
            true));
    }
}

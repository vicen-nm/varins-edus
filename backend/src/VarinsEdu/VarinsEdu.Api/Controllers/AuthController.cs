using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VarinsEdu.Api.Errors;
using VarinsEdu.Api.Security;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Infrastructure.Security;

namespace VarinsEdu.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(AuthService authService, TokenService tokenService) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return this.ApiProblem(StatusCodes.Status400BadRequest, "missing_credentials", "Username and password are required.");
        }

        var user = await authService.ValidateCredentialsAsync(request.Username, request.Password, ct);

        if (user is null)
        {
            return this.ApiProblem(StatusCodes.Status401Unauthorized, "invalid_credentials", "Invalid username or password.");
        }

        var (token, expiresAt) = tokenService.CreateToken(user);

        return Ok(new LoginResponse(
            token,
            expiresAt,
            new UserInfo(user.UserId, user.Username, user.InstitutionId, user.IsPlatform, user.Permissions)));
    }

    // Shows what the server knows about the caller, read from the token.
    [HttpGet("me")]
    [Authorize]
    public IActionResult Me() => Ok(new
    {
        userId = User.FindFirst("sub")?.Value,
        username = User.FindFirst("unique_name")?.Value,
        institutionId = User.FindFirst(AppClaims.InstitutionId)?.Value,
        isPlatform = User.HasClaim(AppClaims.Scope, AppClaims.PlatformScope),
        permissions = User.FindAll(AppClaims.Permission).Select(c => c.Value).ToList()
    });

    // Temporary endpoint to try a permission policy.
    [HttpGet("users-check")]
    [Authorize(Policy = PermissionKeys.UsersManage)]
    public IActionResult UsersCheck() => Ok(new { allowed = true });
}

public record LoginRequest(string Username, string Password);

public record UserInfo(
    Guid UserId,
    string Username,
    Guid? InstitutionId,
    bool IsPlatform,
    IReadOnlyCollection<string> Permissions);

public record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, UserInfo User);

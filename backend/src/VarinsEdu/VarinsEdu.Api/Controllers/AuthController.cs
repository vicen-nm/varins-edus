using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using VarinsEdu.Api.Errors;
using VarinsEdu.Api.Security;
using VarinsEdu.Api.Setup;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Infrastructure.Security;

namespace VarinsEdu.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    AuthService authService,
    StudentActivationService activationService,
    TokenService tokenService,
    PasswordService passwordService,
    RefreshTokenService sessions) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitSetup.AnonymousAuthPolicy)]
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

        Response.Headers.CacheControl = "no-store";
        SessionCookies.Set(Response, await sessions.StartAsync(user, request.Remember, ct));

        return Ok(ToResponse(user));
    }

    // A student creates their account with the identification and activation code the school gave them.
    // Every failure answers the same way, so nobody can learn who is enrolled.
    [HttpPost("activate")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitSetup.AnonymousAuthPolicy)]
    public async Task<ActionResult<LoginResponse>> Activate(ActivateRequest request, CancellationToken ct)
    {
        var result = await activationService.ActivateAsync(
            new ActivationCommand(request.InstitutionSlug, request.IdentificationNumber, request.Code, request.Password),
            ct);

        if (result.Error == ActivationError.WeakPassword)
        {
            return this.ApiProblem(StatusCodes.Status400BadRequest, "weak_password", "The password is too weak.");
        }

        if (!result.Succeeded)
        {
            return this.ApiProblem(StatusCodes.Status400BadRequest, "activation_failed", "The activation data is not valid.");
        }

        Response.Headers.CacheControl = "no-store";
        SessionCookies.Set(Response, await sessions.StartAsync(result.User!, persistent: false, ct));

        return StatusCode(StatusCodes.Status201Created, ToResponse(result.User!));
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

    // Renews the session with the cookie. The cookie is HttpOnly: the page's scripts never see it.
    // Any problem answers the same way, so nobody can tell why it was refused.
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitSetup.RefreshPolicy)]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";

        var result = await sessions.RefreshAsync(SessionCookies.Read(Request), ct);

        if (result.Status == RefreshStatus.Conflict)
        {
            // Two renewals crossed: the caller tries again with the cookie the other one just set.
            return this.ApiProblem(StatusCodes.Status409Conflict, "refresh_conflict", "The session was just renewed. Try again.");
        }

        if (result.Status != RefreshStatus.Ok)
        {
            SessionCookies.Clear(Response);

            return this.ApiProblem(StatusCodes.Status401Unauthorized, "session_expired", "The session has ended.");
        }

        SessionCookies.Set(Response, result.Session!);

        return Ok(ToResponse(result.User!));
    }

    // Ends the session of this browser. No token is needed: the cookie is the credential.
    [HttpPost("logout")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitSetup.RefreshPolicy)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await sessions.LogoutAsync(SessionCookies.Read(Request), ct);
        SessionCookies.Clear(Response);

        return NoContent();
    }

    // A person chooses a new password with the one-time code the school gave them.
    // Every problem with the code answers the same way, so nobody can learn who has an account.
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitSetup.AnonymousAuthPolicy)]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct) =>
        PasswordResponse(
            await passwordService.ResetAsync(
                new ResetPasswordCommand(request.Username, request.Code, request.NewPassword), ct),
            "reset_failed");

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct) =>
        PasswordResponse(
            await passwordService.ChangeAsync(
            new ChangePasswordCommand(request.CurrentPassword, request.NewPassword, SessionCookies.Read(Request)), ct),
            "password_change_failed");

    private IActionResult PasswordResponse(PasswordResult result, string failureCode)
    {
        if (result.Succeeded)
        {
            return NoContent();
        }

        return result.Error switch
        {
            PasswordError.Weak =>
                this.ApiProblem(StatusCodes.Status400BadRequest, "weak_password", "The password is too weak."),
            PasswordError.InvalidCurrentPassword =>
                this.ApiProblem(StatusCodes.Status400BadRequest, "invalid_current_password", "The current password is not correct."),
            _ =>
                this.ApiProblem(StatusCodes.Status400BadRequest, failureCode, "The request could not be completed.")
        };
    }

    private LoginResponse ToResponse(AuthenticatedUser user)
    {
        var (token, expiresAt) = tokenService.CreateToken(user);

        return new LoginResponse(
            token,
            expiresAt,
            new UserInfo(user.UserId, user.Username, user.InstitutionId, user.IsPlatform, user.Permissions));
    }
}

public record LoginRequest(string Username, string Password, bool Remember = false);

public record ActivateRequest(string InstitutionSlug, string IdentificationNumber, string Code, string Password);

public record UserInfo(
    Guid UserId,
    string Username,
    Guid? InstitutionId,
    bool IsPlatform,
    IReadOnlyCollection<string> Permissions);

public record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, UserInfo User);

public record ResetPasswordRequest(string Username, string Code, string NewPassword);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

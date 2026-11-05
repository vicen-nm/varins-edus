using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity.Data;
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
    PasswordService passwordService) : ControllerBase
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
            await passwordService.ChangeAsync(new ChangePasswordCommand(request.CurrentPassword, request.NewPassword), ct),
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

public record LoginRequest(string Username, string Password);

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

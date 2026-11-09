using BlueMeal.Application.Abstractions;
using BlueMeal.Application.Common;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace BlueMeal.Api.Controllers;

[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    protected ICurrentUser CurrentUser => HttpContext.RequestServices.GetRequiredService<ICurrentUser>();
    protected Guid UserId => CurrentUser.UserId ?? throw new AppException("error.unauthorized", 401);
    protected Guid? SessionId => Guid.TryParse(User.FindFirst("sid")?.Value, out var s) ? s : null;
    protected string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();
    protected string? UserAgentHeader => Request.Headers.UserAgent.ToString();

    /// <summary>Runs the registered FluentValidation validator for T, throwing ValidationException (→ 400) on failure.</summary>
    protected async Task ValidateAsync<T>(T model, CancellationToken ct)
    {
        var v = HttpContext.RequestServices.GetService<IValidator<T>>();
        if (v is null) return;
        var r = await v.ValidateAsync(model, ct);
        if (!r.IsValid) throw new ValidationException(r.Errors);
    }
}

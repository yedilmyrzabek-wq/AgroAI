using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AgroShield.Api.Filters;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class InternalApiKeyAttribute : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated == true)
        {
            await Task.CompletedTask;
            return;
        }

        var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var expected = config["Security:InternalApiKey"];

        if (string.IsNullOrEmpty(expected)) { await Task.CompletedTask; return; }

        if (!context.HttpContext.Request.Headers.TryGetValue("X-Internal-Key", out var key) || key != expected)
        {
            context.Result = new JsonResult(new
            {
                error = "unauthorized",
                message = "Invalid or missing X-Internal-Key header",
                details = "Provide a valid internal API key or JWT bearer token"
            })
            { StatusCode = 401 };
        }

        await Task.CompletedTask;
    }
}

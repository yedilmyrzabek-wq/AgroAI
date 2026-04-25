using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AgroShield.Api.Filters;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class ApiKeyAttribute : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var expectedKey = config["Security:DeviceApiKey"];

        if (string.IsNullOrEmpty(expectedKey))
        {
            await Task.CompletedTask;
            return;
        }

        if (!context.HttpContext.Request.Headers.TryGetValue("X-Device-Key", out var key)
            || key != expectedKey)
        {
            context.Result = new JsonResult(new
            {
                error = "unauthorized",
                message = "Invalid or missing X-Device-Key header",
                details = "Provide a valid device API key in the X-Device-Key header"
            })
            { StatusCode = 401 };
        }

        await Task.CompletedTask;
    }
}

using Hangfire.Dashboard;
using System.Text;

namespace AgroShield.Api.Filters;

public class HangfireBasicAuthFilter(string user, string password) : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var http = context.GetHttpContext();
        if (!http.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            Challenge(http);
            return false;
        }

        var header = authHeader.ToString();
        if (!header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            Challenge(http);
            return false;
        }

        var encoded = header["Basic ".Length..].Trim();
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        var parts = decoded.Split(':', 2);

        if (parts.Length == 2 && parts[0] == user && parts[1] == password)
            return true;

        Challenge(http);
        return false;
    }

    private static void Challenge(HttpContext http)
    {
        http.Response.StatusCode = 401;
        http.Response.Headers["WWW-Authenticate"] = "Basic realm=\"Hangfire Dashboard\"";
    }
}

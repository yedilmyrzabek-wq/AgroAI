using Microsoft.Extensions.Configuration;

namespace AgroShield.Infrastructure.ExternalServices;

public sealed class InternalKeyHandler : DelegatingHandler
{
    private readonly string _key;

    public InternalKeyHandler(IConfiguration cfg)
    {
        _key = cfg["Security:InternalApiKey"]
            ?? throw new InvalidOperationException("Security:InternalApiKey is not set");
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Remove("X-Internal-Key");
        request.Headers.Add("X-Internal-Key", _key);
        return base.SendAsync(request, cancellationToken);
    }
}

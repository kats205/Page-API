using Microsoft.AspNetCore.Authentication;

namespace Page_API.Authentication;

public sealed class AdminApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string Scheme = "AdminApiKey";
    public const string DefaultHeaderName = "X-Admin-Api-Key";

    public string HeaderName { get; set; } = DefaultHeaderName;
    public string ApiKey { get; set; } = string.Empty;
}

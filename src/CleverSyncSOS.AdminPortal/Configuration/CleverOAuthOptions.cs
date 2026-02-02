using Microsoft.AspNetCore.Authentication.OAuth;

namespace CleverSyncSOS.AdminPortal.Configuration;

public class CleverOAuthOptions : OAuthOptions
{
    public const string SectionName = "Clever";

    public CleverOAuthOptions()
    {
        AuthorizationEndpoint = "https://clever.com/oauth/authorize";
        TokenEndpoint = "https://clever.com/oauth/tokens";
        UserInformationEndpoint = "https://api.clever.com/v3.0/me";
        CallbackPath = "/signin-clever";
    }
}

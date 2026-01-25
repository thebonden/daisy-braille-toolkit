using Microsoft.Identity.Client;

namespace DbtSharePointSample;

public sealed class AuthService
{
    private readonly IPublicClientApplication _pca;
    private readonly string[] _scopes;

    public AuthService(string tenantId, string clientId, string[] scopes)
    {
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));

        _pca = PublicClientApplicationBuilder
            .Create(clientId)
            .WithTenantId(tenantId)
            .WithDefaultRedirectUri()
            .Build();
    }

    public async Task<string> AcquireAccessTokenAsync()
    {
        var accounts = await _pca.GetAccountsAsync().ConfigureAwait(false);
        var account = accounts.FirstOrDefault();

        try
        {
            var silent = await _pca.AcquireTokenSilent(_scopes, account).ExecuteAsync().ConfigureAwait(false);
            return silent.AccessToken;
        }
        catch (MsalUiRequiredException)
        {
            var interactive = await _pca.AcquireTokenInteractive(_scopes)
                .WithPrompt(Prompt.SelectAccount)
                .ExecuteAsync()
                .ConfigureAwait(false);

            return interactive.AccessToken;
        }
    }
}

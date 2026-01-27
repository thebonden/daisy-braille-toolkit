using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Identity.Client;

namespace DAISY_Braille_Toolkit.Services.SharePoint
{
    /// <summary>
    /// Handles Entra ID (Azure AD) sign-in and token acquisition for Microsoft Graph.
    /// Uses AcquireTokenSilent first, falls back to interactive login when required (MFA/CA/etc).
    /// </summary>
    public sealed class SharePointAuthService
    {
        private readonly IPublicClientApplication _pca;
        private readonly string[] _scopes;
        private IAccount? _account;

    /// <summary>
    /// Convenience property for UI: the currently cached/signed-in account username (if any).
    /// </summary>
    public string? SignedInAccountUsername => _account?.Username;

        public SharePointAuthService(SharePointAuthConfig config)
        {
            if (config is null) throw new ArgumentNullException(nameof(config));
            if (string.IsNullOrWhiteSpace(config.ClientId)) throw new ArgumentException("ClientId is required.", nameof(config));
            if (string.IsNullOrWhiteSpace(config.TenantId)) throw new ArgumentException("TenantId is required.", nameof(config));

            _scopes = config.Scopes?.Length > 0
                ? config.Scopes
                : new[] { "User.Read", "Sites.ReadWrite.All" };

            _pca = PublicClientApplicationBuilder
                .Create(config.ClientId.Trim())
                .WithAuthority(AzureCloudInstance.AzurePublic, config.TenantId.Trim())
                .WithDefaultRedirectUri()
                .Build();

            // Secure on-disk cache so users don't have to sign in every time.
            MsalTokenCacheStorage.EnableSerialization(_pca.UserTokenCache, $"{config.TenantId}_{config.ClientId}");
        }

        public async Task<string?> GetSignedInUsernameAsync()
        {
            await EnsureAccountAsync().ConfigureAwait(false);
            return _account?.Username;
        }

        public async Task<AuthenticationResult> AcquireTokenAsync(bool forceInteractive = false)
        {
            await EnsureAccountAsync().ConfigureAwait(false);

            if (!forceInteractive && _account != null)
            {
                try
                {
                    return await _pca.AcquireTokenSilent(_scopes, _account)
                        .ExecuteAsync()
                        .ConfigureAwait(false);
                }
                catch (MsalUiRequiredException)
                {
                    // fall through to interactive
                }
            }

            var result = await _pca.AcquireTokenInteractive(_scopes)
                .WithPrompt(Prompt.SelectAccount)
                .ExecuteAsync()
                .ConfigureAwait(false);

            _account = result.Account;
            return result;
        }

        public async Task SignOutAsync()
        {
            var accounts = await _pca.GetAccountsAsync().ConfigureAwait(false);
            foreach (var account in accounts)
            {
                await _pca.RemoveAsync(account).ConfigureAwait(false);
            }
            _account = null;
        }

        private async Task EnsureAccountAsync()
        {
            if (_account != null) return;

            var accounts = await _pca.GetAccountsAsync().ConfigureAwait(false);
            _account = accounts.FirstOrDefault();
        }
    }
}

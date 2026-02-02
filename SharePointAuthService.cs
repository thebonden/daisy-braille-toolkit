DAISY-Braille Toolkit\Services\SharePoint\SharePointAuthService.cs
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.Identity.Client;

namespace DAISY_Braille_Toolkit.Services.SharePoint
{
    /// <summary>
    /// Handles Entra ID (Azure AD) sign-in and token acquisition for Microsoft Graph.
    /// Uses AcquireTokenSilent first, falls back to interactive login when required (MFA/CA/etc).
    /// Also provides device-code fallback for headless/non-interactive environments.
    /// </summary>
    public sealed class SharePointAuthService : ISharePointAuthService
    {
        private readonly IPublicClientApplication _pca;
        private readonly string[] _scopes;
        private IAccount? _account;

        // Prevent multiple concurrent interactive prompts
        private readonly SemaphoreSlim _interactiveLock = new(1, 1);

        /// <summary>
        /// Convenience property for UI: the currently cached/signed-in account username (if any).
        /// </summary>
        public string? SignedInAccountUsername => _account?.Username;

        public SharePointAuthService(SharePointAuthConfig config)
        {
            if (config is null) throw new ArgumentNullException(nameof(config));
            if (string.IsNullOrWhiteSpace(config.ClientId)) throw new ArgumentException("ClientId is required.", nameof(config));
            if (string.IsNullOrWhiteSpace(config.TenantId)) throw new ArgumentException("TenantId is required.", nameof(config));

            // Use the central EffectiveScopes on the config
            _scopes = config.EffectiveScopes;

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

            // Try silent first when possible
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
                    // Interactive required - fall through to interactive flow.
                }
                catch (MsalServiceException ex)
                {
                    Trace.TraceError("MSAL service exception during silent token acquisition: {0}", ex);
                    throw;
                }
                catch (Exception ex)
                {
                    Trace.TraceError("Unexpected error during silent token acquisition: {0}", ex);
                    throw;
                }
            }

            // Only one caller at a time should trigger interactive auth
            await _interactiveLock.WaitAsync().ConfigureAwait(false);
            try
            {
                // Another caller may have signed-in while waiting for the lock; retry silent
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
                        // still need interactive
                    }
                    catch (MsalServiceException ex)
                    {
                        Trace.TraceError("MSAL service exception during re-check silent acquisition: {0}", ex);
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Trace.TraceError("Unexpected error during re-check silent acquisition: {0}", ex);
                        throw;
                    }
                }

                try
                {
                    // Try interactive first (GUI scenarios)
                    var result = await _pca.AcquireTokenInteractive(_scopes)
                        .WithPrompt(Prompt.SelectAccount)
                        .ExecuteAsync()
                        .ConfigureAwait(false);

                    _account = result.Account;
                    return result;
                }
                catch (MsalClientException mcx) when (IsNonInteractiveEnvironment(mcx))
                {
                    // environment does not support interactive (e.g., headless), fallback to device-code
                    Trace.TraceInformation("Interactive not supported; falling back to device-code flow: {0}", mcx.Message);
                    return await AcquireTokenWithDeviceCodeAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (PlatformNotSupportedException pnsx)
                {
                    Trace.TraceInformation("Platform does not support interactive auth; using device-code fallback: {0}", pnsx.Message);
                    return await AcquireTokenWithDeviceCodeAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (MsalServiceException ex)
                {
                    Trace.TraceError("MSAL service exception during interactive auth: {0}", ex);
                    throw;
                }
                catch (OperationCanceledException)
                {
                    Trace.TraceInformation("Interactive auth was canceled by the user or caller.");
                    throw;
                }
                catch (Exception ex)
                {
                    Trace.TraceError("Unexpected error during interactive auth: {0}", ex);
                    throw;
                }
            }
            finally
            {
                _interactiveLock.Release();
            }
        }

        public async Task SignOutAsync()
        {
            try
            {
                var accounts = await _pca.GetAccountsAsync().ConfigureAwait(false);
                foreach (var account in accounts)
                {
                    await _pca.RemoveAsync(account).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError("Error signing out accounts: {0}", ex);
                throw;
            }
            finally
            {
                _account = null;
            }
        }

        private async Task EnsureAccountAsync()
        {
            if (_account != null) return;

            try
            {
                var accounts = await _pca.GetAccountsAsync().ConfigureAwait(false);
                _account = accounts.FirstOrDefault();
            }
            catch (Exception ex)
            {
                Trace.TraceError("Error enumerating MSAL accounts: {0}", ex);
                throw;
            }
        }

        private async Task<AuthenticationResult> AcquireTokenWithDeviceCodeAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await _pca.AcquireTokenWithDeviceCode(_scopes, deviceCodeResult =>
                {
                    // Informational: deviceCodeResult.Message should be shown to the user (UI/console/log).
                    Trace.TraceInformation("Device code flow message: {0}", deviceCodeResult.Message);
                    return Task.CompletedTask;
                }).ExecuteAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (MsalServiceException ex)
            {
                Trace.TraceError("Device-code flow failed: {0}", ex);
                throw;
            }
            catch (Exception ex)
            {
                Trace.TraceError("Unexpected error during device-code flow: {0}", ex);
                throw;
            }
        }

        private static bool IsNonInteractiveEnvironment(MsalClientException ex)
        {
            // Heuristic: specific MSAL client error codes indicate interactive isn't available.
            // Examples: "authentication_canceled", "unknown_error", or platform-specific messages.
            // We keep this conservative; return true for well-known "no UI" errors.
            if (ex == null) return false;

            var code = ex.ErrorCode ?? string.Empty;
            return code.Contains("authentication_canceled", StringComparison.OrdinalIgnoreCase)
                || code.Contains("ui_required", StringComparison.OrdinalIgnoreCase)
                || code.Contains("unknown_error", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("no browser", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("failed to launch", StringComparison.OrdinalIgnoreCase);
        }
    }
}
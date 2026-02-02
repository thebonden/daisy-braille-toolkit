DAISY-Braille Toolkit\Services\SharePoint\ISharePointAuthService.cs
namespace DAISY_Braille_Toolkit.Services.SharePoint
{
    using System.Threading.Tasks;
    using Microsoft.Identity.Client;

    /// <summary>
    /// Abstraction for SharePoint authentication operations to enable DI and testing.
    /// </summary>
    public interface ISharePointAuthService
    {
        Task<string?> GetSignedInUsernameAsync();
        Task<AuthenticationResult> AcquireTokenAsync(bool forceInteractive = false);
        Task SignOutAsync();
    }
}
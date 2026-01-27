using System;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Identity.Client;

namespace DAISY_Braille_Toolkit.Services.SharePoint
{
    /// <summary>
    /// Persists the MSAL user token cache to disk encrypted with DPAPI (CurrentUser).
    /// This provides security similar to how we store other API secrets in the app.
    /// </summary>
    internal static class MsalTokenCacheStorage
    {
        private static readonly object _fileLock = new();

        public static void Enable(ITokenCache tokenCache, string cacheKey)
        {
            if (tokenCache == null) throw new ArgumentNullException(nameof(tokenCache));
            if (string.IsNullOrWhiteSpace(cacheKey)) cacheKey = "default";

            var cacheFile = GetCacheFilePath(cacheKey);

            tokenCache.SetBeforeAccess(args =>
            {
                lock (_fileLock)
                {
                    try
                    {
                        if (!File.Exists(cacheFile))
                            return;

                        var protectedBytes = File.ReadAllBytes(cacheFile);
                        if (protectedBytes.Length == 0)
                            return;

                        var bytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
                        args.TokenCache.DeserializeMsalV3(bytes);
                    }
                    catch
                    {
                        // Best-effort cache; do not crash the app if cache is unreadable.
                    }
                }
            });

            tokenCache.SetAfterAccess(args =>
            {
                if (!args.HasStateChanged)
                    return;

                lock (_fileLock)
                {
                    try
                    {
                        var bytes = args.TokenCache.SerializeMsalV3();
                        var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);

                        var dir = Path.GetDirectoryName(cacheFile);
                        if (!string.IsNullOrWhiteSpace(dir))
                            Directory.CreateDirectory(dir);

                        File.WriteAllBytes(cacheFile, protectedBytes);
                    }
                    catch
                    {
                        // Best-effort cache.
                    }
                }
            });
        }

        /// <summary>
        /// Backwards-compatible alias (some code samples call this EnableSerialization).
        /// </summary>
        public static void EnableSerialization(ITokenCache tokenCache, string cacheKey)
            => Enable(tokenCache, cacheKey);

        private static string GetCacheFilePath(string cacheKey)
        {
            // LocalAppData is per-user.
            var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DAISY-Braille Toolkit", "MsalCache");

            foreach (var c in Path.GetInvalidFileNameChars())
                cacheKey = cacheKey.Replace(c, '_');

            return Path.Combine(baseDir, $"{cacheKey}.bin");
        }
    }
}

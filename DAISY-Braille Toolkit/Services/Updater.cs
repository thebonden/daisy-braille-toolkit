using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Runtime.InteropServices;
using System.Security.Cryptography;


namespace DAISY_Braille_Toolkit.Services
{
    public class Updater
    {
        private readonly HttpClient _http = new();
        private readonly Action<string> _log;

        // TODO: make configurable
        private const string Owner = "thebonden";
        private const string Repo = "daisy-braille-toolkit";

        public Updater(Action<string> logger)
        {
            _log = logger ?? (_ => { });
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("DAISY-Braille-Toolkit-Updater");
        }

        private record GitHubAsset(string name, string browser_download_url);
        private record GitHubRelease(string tag_name, string name, bool prerelease, string published_at, List<GitHubAsset> assets);

        private async Task<GitHubRelease?> GetLatestReleaseAsync(bool includePrerelease)
        {
            var url = $"https://api.github.com/repos/{Owner}/{Repo}/releases";
            try
            {
                using var resp = await _http.GetAsync(url);
                resp.EnsureSuccessStatusCode();
                var stream = await resp.Content.ReadAsStreamAsync();
                var docs = await JsonSerializer.DeserializeAsync<List<JsonElement>>(stream);
                if (docs == null) return null;

                GitHubRelease? best = null;

                foreach (var item in docs)
                {
                    var prerelease = item.GetProperty("prerelease").GetBoolean();
                    if (!includePrerelease && prerelease) continue;
                    var tag = item.GetProperty("tag_name").GetString() ?? "";
                    var name = item.GetProperty("name").GetString() ?? tag;
                    var published = item.GetProperty("published_at").GetString() ?? "";

                    var assets = new List<GitHubAsset>();
                    if (item.TryGetProperty("assets", out var assetsElem) && assetsElem.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var a in assetsElem.EnumerateArray())
                        {
                            var aname = a.GetProperty("name").GetString() ?? "";
                            var url2 = a.GetProperty("browser_download_url").GetString() ?? "";
                            assets.Add(new GitHubAsset(aname, url2));
                        }
                    }

                    var rel = new GitHubRelease(tag, name, prerelease, published, assets);

                    if (best == null)
                    {
                        best = rel;
                        continue;
                    }

                    // compare by published_at
                    if (DateTime.TryParse(rel.published_at, out var d1) && DateTime.TryParse(best.published_at, out var d2))
                    {
                        if (d1 > d2) best = rel;
                    }
                }

                return best;
            }
            catch (Exception ex)
            {
                _log("Updater: failed to list releases: " + ex.Message);
                return null;
            }
        }

        public async Task<bool> CheckAndPromptAndUpdateAsync(Window owner, bool includePrerelease = false)
        {
            var release = await GetLatestReleaseAsync(includePrerelease);
            if (release == null)
            {
                System.Windows.MessageBox.Show(owner, "Kunne ikke kontakte opdateringsserveren.", "Opdatering", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            var latestTag = release.tag_name ?? release.name ?? "";
            var latestVersion = TrimVersionPrefix(latestTag);
            var currentVersion = GetCurrentVersion();

            _log($"Updater: current={currentVersion} latest={latestVersion}");

            if (TryParseVersion(latestVersion, out var vLatest) && TryParseVersion(currentVersion, out var vCurrent))
            {
                if (vLatest <= vCurrent)
                {
                    System.Windows.MessageBox.Show(owner, "Du har allerede den nyeste version.", "Opdatering", MessageBoxButton.OK, MessageBoxImage.Information);
                    return false;
                }
            }

            // Find MSI asset
            var asset = release.assets?.Find(a => a.name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase));
            if (asset == null || string.IsNullOrWhiteSpace(asset.browser_download_url))
            {
                System.Windows.MessageBox.Show(owner, "Ingen MSI-artifact fundet for denne release.", "Opdatering", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            var prompt = $"Ny version tilgængelig: {latestVersion}. Vil du downloade og installere nu?";
            var res = System.Windows.MessageBox.Show(owner, prompt, "Opdatering", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return false;

            try
            {
                var destDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DAISY-Braille-Toolkit", "updates", latestVersion);
                Directory.CreateDirectory(destDir);
                var destFile = Path.Combine(destDir, asset.name);

                _log("Updater: downloading " + asset.browser_download_url);
                using var http = new HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd("DAISY-Braille-Toolkit-Updater");
                using var resp = await http.GetAsync(asset.browser_download_url);
                resp.EnsureSuccessStatusCode();
                await using (var fs = File.Create(destFile))
                {
                    await resp.Content.CopyToAsync(fs);
                }

                _log("Updater: downloaded to " + destFile);
                // Compute and log SHA256 of downloaded file
                try
                {
                    using var sha = SHA256.Create();
                    await using var fs2 = File.OpenRead(destFile);
                    var hash = sha.ComputeHash(fs2);
                    _log("Updater: SHA256: " + BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant());
                }
                catch (Exception ex)
                {
                    _log("Updater: failed to compute SHA256: " + ex.Message);
                }

                // Verify Authenticode signature (best-effort). If verification fails, abort.
                try
                {
                    var sigOk = VerifyAuthenticodeSignature(destFile);
                    _log("Updater: signature verification result: " + sigOk);
                    if (!sigOk)
                    {
                        System.Windows.MessageBox.Show(owner, "Den downloadede installationsfil er ikke signeret eller signaturen kunne ikke bekræftes. Opdatering afbrydes.", "Opdatering", MessageBoxButton.OK, MessageBoxImage.Error);
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    _log("Updater: signature verification failed: " + ex.Message);
                    System.Windows.MessageBox.Show(owner, "Fejl ved verifikation af signatur. Opdatering afbrydes.", "Opdatering", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                // Pre-install backup of current install folder for a basic rollback
                var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var installFolder = Path.Combine(programFiles, "DAISY-Braille Toolkit");
                string rollbackFolder = null;
                if (Directory.Exists(installFolder))
                {
                    try
                    {
                        var rollbackRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DAISY-Braille-Toolkit", "rollback");
                        Directory.CreateDirectory(rollbackRoot);
                        rollbackFolder = Path.Combine(rollbackRoot, currentVersion + "_" + DateTime.Now.ToString("yyyyMMddHHmmss"));
                        CopyDirectory(installFolder, rollbackFolder);
                        _log("Updater: backup created at " + rollbackFolder);
                    }
                    catch (Exception ex)
                    {
                        _log("Updater: failed to create backup: " + ex.Message);
                        rollbackFolder = null;
                    }
                }

                // Run installer (elevated)
                var ok = RunInstallerElevated(destFile);
                if (ok)
                {
                    System.Windows.MessageBox.Show(owner, "Opdatering fuldført.", "Opdatering", MessageBoxButton.OK, MessageBoxImage.Information);
                    _log("Updater: install succeeded");
                    return true;
                }
                else
                {
                    _log("Updater: install failed, attempting rollback");
                    if (!string.IsNullOrWhiteSpace(rollbackFolder) && Directory.Exists(rollbackFolder))
                    {
                        try
                        {
                            // Try to restore files (best-effort)
                            CopyDirectory(rollbackFolder, installFolder, overwrite:true);
                            System.Windows.MessageBox.Show(owner, "Opdatering mislykkedes og systemet er forsøgt gendannet.", "Opdatering", MessageBoxButton.OK, MessageBoxImage.Warning);
                            _log("Updater: rollback copy attempted");
                        }
                        catch (Exception ex)
                        {
                            _log("Updater: rollback failed: " + ex.Message);
                            System.Windows.MessageBox.Show(owner, "Opdatering mislykkedes og rollback fejlede. Kontakt support.", "Opdatering", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                    else
                    {
                        System.Windows.MessageBox.Show(owner, "Opdatering mislykkedes. Ingen rollback tilgængelig.", "Opdatering", MessageBoxButton.OK, MessageBoxImage.Error);
                    }

                    return false;
                }
            }
            catch (Exception ex)
            {
                _log("Updater: update failed: " + ex);
                System.Windows.MessageBox.Show(owner, "Opdatering fejlede: " + ex.Message, "Opdatering", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private static string TrimVersionPrefix(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return tag;
            return tag.TrimStart('v', 'V');
        }

        private static bool TryParseVersion(string s, out Version v)
        {
            v = new Version(0, 0, 0, 0);
            if (string.IsNullOrWhiteSpace(s)) return false;
            var t = s.Split('-')[0];
            return Version.TryParse(t, out v);
        }

        private static string GetCurrentVersion()
        {
            try
            {
                var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
                var fvi = FileVersionInfo.GetVersionInfo(asm.Location);
                return fvi.FileVersion ?? asm.GetName().Version?.ToString() ?? "0.0.0";
            }
            catch
            {
                return "0.0.0";
            }
        }

        private static bool RunInstallerElevated(string msiPath)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "msiexec.exe",
                    Arguments = $"/i \"{msiPath}\"",
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = Path.GetDirectoryName(msiPath)
                };

                var p = Process.Start(psi);
                if (p == null) return false;
                p.WaitForExit();
                return p.ExitCode == 0;
            }
            catch (Exception ex)
            {
                // If user cancels UAC, an exception will be thrown
                return false;
            }
        }

        private static void CopyDirectory(string sourceDir, string destinationDir, bool overwrite = false)
        {
            var dir = new DirectoryInfo(sourceDir);
            if (!dir.Exists) throw new DirectoryNotFoundException(sourceDir);

            Directory.CreateDirectory(destinationDir);

            foreach (var file in dir.GetFiles())
            {
                var targetFilePath = Path.Combine(destinationDir, file.Name);
                file.CopyTo(targetFilePath, overwrite);
            }

            foreach (var sub in dir.GetDirectories())
            {
                var targetSubDir = Path.Combine(destinationDir, sub.Name);
                CopyDirectory(sub.FullName, targetSubDir, overwrite);
            }
        }

        // Simple Authenticode verification using WinTrust
        private static bool VerifyAuthenticodeSignature(string filePath)
        {
            try
            {
                var wtd = new WinTrustData(filePath);
                var result = WinVerifyTrust(IntPtr.Zero, WINTRUST_ACTION_GENERIC_VERIFY_V2, ref wtd);
                return result == 0;
            }
            catch
            {
                return false;
            }
        }

        private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 = new("{00AAC56B-CD44-11d0-8CC2-00C04FC295EE}");

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint WinVerifyTrust(IntPtr hWnd, [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID, ref WinTrustData pWVTData);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustFileInfo
        {
            public uint StructSize;
            public IntPtr pszFilePath; // LPCWSTR
            public IntPtr hFile;
            public IntPtr pgKnownSubject;

            public WinTrustFileInfo(string fileName)
            {
                StructSize = (uint)Marshal.SizeOf(typeof(WinTrustFileInfo));
                pszFilePath = Marshal.StringToCoTaskMemUni(fileName);
                hFile = IntPtr.Zero;
                pgKnownSubject = IntPtr.Zero;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustData
        {
            public uint StructSize;
            public IntPtr PolicyCallbackData;
            public IntPtr SIPClientData;
            public uint UIChoice;
            public uint RevocationChecks;
            public uint UnionChoice;
            public IntPtr FileInfoPtr;
            public uint StateAction;
            public IntPtr StateData;
            public string URLReference;
            public uint ProvFlags;
            public uint UIContext;

            public WinTrustData(string fileName)
            {
                StructSize = (uint)Marshal.SizeOf(typeof(WinTrustData));
                PolicyCallbackData = IntPtr.Zero;
                SIPClientData = IntPtr.Zero;
                UIChoice = 2; // WTD_UI_NONE
                RevocationChecks = 0; // WTD_REVOKE_NONE
                UnionChoice = 1; // WTD_CHOICE_FILE
                var fileInfo = new WinTrustFileInfo(fileName);
                FileInfoPtr = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(WinTrustFileInfo)));
                Marshal.StructureToPtr(fileInfo, FileInfoPtr, false);
                StateAction = 0;
                StateData = IntPtr.Zero;
                URLReference = null;
                ProvFlags = 0x00000040; // WTD_SAFER_FLAG
                UIContext = 0;
            }
        }
    }
}

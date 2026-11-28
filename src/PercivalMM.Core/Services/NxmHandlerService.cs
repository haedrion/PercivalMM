using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PercivalMM.Core.Services
{
    public record NxmDownloadRequest(
        string GameDomain,
        string ModId,
        string FileId,
        string? Key,
        string? Expires,
        string RawUri);

    public static class NxmHandlerService
    {
        public const string PipeName = "PercivalMM_NXM_IPC";
        private const string DesktopFileName = "percivalmm-nxm.desktop";

        private static string ConfigDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "PercivalMM");

        private static string ApiKeyFilePath => Path.Combine(ConfigDir, "nexus.json");

        public static string DownloadsCacheDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "PercivalMM", "downloads");

        public static string LoadApiKey()
        {
            try
            {
                if (!File.Exists(ApiKeyFilePath)) return string.Empty;
                using var doc = JsonDocument.Parse(File.ReadAllText(ApiKeyFilePath));
                return doc.RootElement.TryGetProperty("apiKey", out var prop) ? prop.GetString() ?? string.Empty : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        public static void SaveApiKey(string apiKey)
        {
            Directory.CreateDirectory(ConfigDir);
            string json = JsonSerializer.Serialize(new { apiKey = apiKey.Trim() }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ApiKeyFilePath, json);

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(ApiKeyFilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }

        public static bool IsLinuxNxmHandlerRegistered()
        {
            if (OperatingSystem.IsWindows()) return false;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "xdg-mime",
                    Arguments = "query default x-scheme-handler/nxm",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc == null) return false;
                string output = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit();
                return output.Equals(DesktopFileName, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static string RegisterLinuxNxmHandler()
        {
            if (OperatingSystem.IsWindows())
                return "Linux desktop registration skipped on Windows.";

            string exePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "PercivalMM.UI");
            string entryDll = Assembly.GetEntryAssembly()?.Location ?? string.Empty;

            // Support both 'dotnet run' (host=dotnet + entry dll) and self-contained native binary
            string execLine = Path.GetFileNameWithoutExtension(exePath).Equals("dotnet", StringComparison.OrdinalIgnoreCase) &&
                              File.Exists(entryDll)
                ? $"\"{exePath}\" \"{entryDll}\" %u"
                : $"\"{exePath}\" %u";

            string appsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "applications");
            Directory.CreateDirectory(appsDir);

            string desktopPath = Path.Combine(appsDir, DesktopFileName);
            string desktopContent =
                "[Desktop Entry]\n" +
                "Type=Application\n" +
                "Name=PercivalMM NXM Handler\n" +
                $"Exec={execLine}\n" +
                "Terminal=false\n" +
                "NoDisplay=true\n" +
                "MimeType=x-scheme-handler/nxm;\n";

            File.WriteAllText(desktopPath, desktopContent);

            var psi = new ProcessStartInfo
            {
                FileName = "xdg-mime",
                Arguments = $"default {DesktopFileName} x-scheme-handler/nxm",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit();
            using var dbProc = Process.Start(new ProcessStartInfo
            {
                FileName = "update-desktop-database",
                Arguments = $"\"{appsDir}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            dbProc?.WaitForExit();

            return "Registered PercivalMM as active nxm:// handler.";
        }

        public static string UnregisterLinuxNxmHandler()
        {
            if (OperatingSystem.IsWindows())
                return "Linux desktop unregistration skipped on Windows.";

            string appsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "applications");
            string desktopPath = Path.Combine(appsDir, DesktopFileName);
            if (File.Exists(desktopPath))
                File.Delete(desktopPath);

            var psi = new ProcessStartInfo
            {
                FileName = "update-desktop-database",
                Arguments = $"\"{appsDir}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit();

            return "Removed PercivalMM nxm:// handler (other mod managers restored).";
        }
        public static async Task<(string ApiKey, string Username)> AuthorizeViaSsoAsync(
            Action<string> log,
            CancellationToken ct = default)
        {
            string uuid = Guid.NewGuid().ToString();
            string token = Guid.NewGuid().ToString("N");

            using var ws = new ClientWebSocket();
            string ssoHost = "sso." + "nexusmods.com";
            log($"🌐 [Nexus SSO] Connecting to wss://{ssoHost}...");
            await ws.ConnectAsync(new Uri("wss://" + ssoHost), ct);

            string handshakePayload = JsonSerializer.Serialize(new
            {
                id = uuid,
                token,
                protocol = 2
            });

            byte[] sendBytes = Encoding.UTF8.GetBytes(handshakePayload);
            await ws.SendAsync(sendBytes, WebSocketMessageType.Text, endOfMessage: true, ct);

            // Launch browser so user clicks 'Authorize' on Nexus Mods
            string authUrl = "https://" + "www." + "nexusmods.com" + $"/sso?id={uuid}&application=vortex";
            Process.Start(new ProcessStartInfo
            {
                FileName = "xdg-open",
                Arguments = authUrl,
                UseShellExecute = false
            });
            log("🔑 [Nexus SSO] Opened browser — click 'Authorize' on Nexus Mods to link your account...");

            var buffer = new byte[4096];
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                        throw new Exception("Nexus SSO connection closed before authorization completed.");
                    ms.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                string msgText = Encoding.UTF8.GetString(ms.ToArray());
                using var doc = JsonDocument.Parse(msgText);
                var root = doc.RootElement;

                if (root.TryGetProperty("success", out var successProp) && !successProp.GetBoolean())
                {
                    string err = root.TryGetProperty("error", out var errProp) ? errProp.GetString() ?? "Unknown error" : "Authorization rejected";
                    throw new Exception($"Nexus SSO error: {err}");
                }

                if (root.TryGetProperty("data", out var dataProp) &&
                    dataProp.TryGetProperty("api_key", out var keyProp))
                {
                    string apiKey = keyProp.GetString() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(apiKey))
                    {
                        SaveApiKey(apiKey);
                        string username = await ValidateApiKeyAsync(apiKey, ct);
                        return (apiKey, username);
                    }
                }
            }

            throw new OperationCanceledException("Nexus SSO authorization timed out or was cancelled.");
        }

        public static async Task<string> ValidateApiKeyAsync(string apiKey, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) return "Unknown";

            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("apikey", apiKey.Trim());
            http.DefaultRequestHeaders.Add("User-Agent", "PercivalMM/1.0.0 (Linux)");

string validateUrl = "https://" + "api." + "nexusmods.com" + "/v1/users/validate.json";
using var resp = await http.GetAsync(validateUrl, ct);

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"API key validation failed (HTTP {(int)resp.StatusCode}).");

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            return doc.RootElement.TryGetProperty("name", out var nameProp)
                ? nameProp.GetString() ?? "Verified User"
                : "Verified User";
        }


        public static NxmDownloadRequest? ParseNxmUri(string rawUri)
        {
            if (string.IsNullOrWhiteSpace(rawUri) || !rawUri.StartsWith("nxm://", StringComparison.OrdinalIgnoreCase))
                return null;

            if (!Uri.TryCreate(rawUri.Trim(), UriKind.Absolute, out var uri))
                return null;

            string domain = uri.Host;
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            // Expected: mods / {modId} / files / {fileId}
            if (segments.Length < 4 ||
                !segments[0].Equals("mods", StringComparison.OrdinalIgnoreCase) ||
                !segments[2].Equals("files", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string? key = null;
            string? expires = null;
            string query = uri.Query.TrimStart('?');
            foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=', 2);
                if (kv.Length != 2) continue;
                if (kv[0].Equals("key", StringComparison.OrdinalIgnoreCase)) key = Uri.UnescapeDataString(kv[1]);
                else if (kv[0].Equals("expires", StringComparison.OrdinalIgnoreCase)) expires = Uri.UnescapeDataString(kv[1]);
            }

            return new NxmDownloadRequest(domain, segments[1], segments[3], key, expires, rawUri.Trim());
        }

        public static bool TryForwardToRunningInstance(string nxmUri)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                client.Connect(1500);
                using var writer = new StreamWriter(client) { AutoFlush = true };
                writer.WriteLine(nxmUri);
                return true;
            }
            catch
            {
                return false;
            }
        }

          public static async Task<string> DownloadFromNxmAsync(
            NxmDownloadRequest req,
            string apiKey,
            Action<string> log,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Nexus Mods API Key is missing. Paste your Personal API Key in the sidebar first.");

            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("apikey", apiKey.Trim());
            http.DefaultRequestHeaders.Add("User-Agent", "PercivalMM/1.0.0 (Linux)");

            // 1. Fetch official human-readable mod name from Nexus API
            string? nexusModName = null;
            try
            {
                string modInfoUrl = string.Concat("https://", "api.", "nexusmods.com", $"/v1/games/{req.GameDomain}/mods/{req.ModId}.json");
                using var modResp = await http.GetAsync(modInfoUrl, ct);
                if (modResp.IsSuccessStatusCode)
                {
                    using var modDoc = JsonDocument.Parse(await modResp.Content.ReadAsStringAsync(ct));
                    if (modDoc.RootElement.TryGetProperty("name", out var nameProp))
                        nexusModName = nameProp.GetString();
                }
            }
            catch { /* Non-fatal: fall back to Content-Disposition or URL filename */ }

            // 2. Request CDN download link
            string apiUrl = string.Concat("https://", "api.", "nexusmods.com", $"/v1/games/{req.GameDomain}/mods/{req.ModId}/files/{req.FileId}/download_link.json");
            var queryParams = new List<string>();
            if (!string.IsNullOrWhiteSpace(req.Key)) queryParams.Add($"key={Uri.EscapeDataString(req.Key)}");
            if (!string.IsNullOrWhiteSpace(req.Expires)) queryParams.Add($"expires={Uri.EscapeDataString(req.Expires)}");
            if (queryParams.Count > 0) apiUrl += "?" + string.Join("&", queryParams);

            log($"🌐 [Nexus API] Requesting CDN download link for {(nexusModName ?? $"mod #{req.ModId}")} ({req.GameDomain})...");
            using var resp = await http.GetAsync(apiUrl, ct);
            string body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"Nexus API returned {(int)resp.StatusCode}: {body}");

            using var doc = JsonDocument.Parse(body);
            string? cdnUrl = doc.RootElement.EnumerateArray()
                .Select(el => el.TryGetProperty("URI", out var u) ? u.GetString() : null)
                .FirstOrDefault(u => !string.IsNullOrWhiteSpace(u));

            if (string.IsNullOrWhiteSpace(cdnUrl))
                throw new Exception("Nexus API did not return a valid CDN download URI.");

            var cdnUri = new Uri(cdnUrl);
            using var fileResp = await http.GetAsync(cdnUri, HttpCompletionOption.ResponseHeadersRead, ct);
            fileResp.EnsureSuccessStatusCode();

            // 3. Extract real filename from Content-Disposition header (avoids Cloudflare CDN UUIDs!)
            string? headerFileName = fileResp.Content.Headers.ContentDisposition?.FileNameStar
                                  ?? fileResp.Content.Headers.ContentDisposition?.FileName;
            if (!string.IsNullOrWhiteSpace(headerFileName))
                headerFileName = Uri.UnescapeDataString(headerFileName.Trim('"'));

            string urlFileName = Path.GetFileName(Uri.UnescapeDataString(cdnUri.AbsolutePath));
            string rawFileName = !string.IsNullOrWhiteSpace(headerFileName) ? headerFileName : urlFileName;

            // If still a bare UUID without extension, use the Nexus mod title + .zip
            string ext = Path.GetExtension(rawFileName);
            if (string.IsNullOrWhiteSpace(ext) || Guid.TryParse(Path.GetFileNameWithoutExtension(rawFileName), out _))
            {
                string safeTitle = string.Join("_", (nexusModName ?? $"nexus_{req.GameDomain}_{req.ModId}")
                    .Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
                rawFileName = $"{safeTitle}-{req.ModId}{(!string.IsNullOrWhiteSpace(ext) ? ext : ".zip")}";
            }

            Directory.CreateDirectory(DownloadsCacheDir);
            string destArchivePath = Path.Combine(DownloadsCacheDir, rawFileName);

            log($"⬇️ [Nexus CDN] Downloading {rawFileName}...");
            await using (var remoteStream = await fileResp.Content.ReadAsStreamAsync(ct))
            await using (var localFile = File.Create(destArchivePath))
            {
                await remoteStream.CopyToAsync(localFile, ct);
            }

            log($"✅ [Nexus CDN] Download complete: {rawFileName}");
            return destArchivePath;
        }
      
    }
}

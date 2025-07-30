// BeastieBuddy/Windows/ServerClient.cs

using BeastieBuddy.Data;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace BeastieBuddy.Windows
{
    public class ServerClient : IDisposable
    {
        private readonly HttpClient httpClient;
        private bool useBackupServer = false;
        private bool isRenderServerWarmedUp = false;

        private const string CloudflareUrl = "https://aetherdraw-server.onrender.com"; // Replace with cloudflare if render dies
        private const string RenderUrl = "https://aetherdraw-server.onrender.com";

        public ServerClient()
        {
            httpClient = new HttpClient();
        }

        public async Task<List<MobData>?> SearchAsync(string query, System.Threading.CancellationToken cancellationToken)
        {
            // Warm up Render server on the first search of the session, hopefully not needed if server self pings work
            if (!isRenderServerWarmedUp)
            {
                _ = httpClient.GetAsync($"{RenderUrl}/beastiebuddy/search?query=warmup", cancellationToken);
                isRenderServerWarmedUp = true;
            }

            var primaryUrl = useBackupServer ? RenderUrl : CloudflareUrl;
            var backupUrl = RenderUrl;

            try
            {
                var response = await httpClient.GetAsync($"{primaryUrl}/beastiebuddy/search?query={Uri.EscapeDataString(query)}", cancellationToken);

                // Check for rate-limit or other server issues
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests || !response.IsSuccessStatusCode)
                {
                    if (!useBackupServer) // Avoid switching if already on backup
                    {
                        useBackupServer = true;
                        Plugin.Log.Warning("Cloudflare limit likely reached. Failing over to Render server.");
                        response = await httpClient.GetAsync($"{backupUrl}/beastiebuddy/search?query={Uri.EscapeDataString(query)}", cancellationToken);
                    }
                }

                if (response.IsSuccessStatusCode)
                {
                    // Check for the custom failover header
                    if (response.Headers.TryGetValues("X-Use-Backup", out var values) && values.FirstOrDefault() == "true")
                    {
                        if (!useBackupServer)
                        {
                            useBackupServer = true;
                            Plugin.Log.Info("Received proactive failover signal. Switching to Render server.");
                        }
                    }

                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    return JsonConvert.DeserializeObject<List<MobData>>(json);
                }
            }
            catch (TaskCanceledException)
            {
                // For faster typers
                return null;
            }
            catch (HttpRequestException ex)
            {
                Plugin.Log.Error(ex, "HTTP request failed.");
                if (!useBackupServer)
                {
                    useBackupServer = true;
                    Plugin.Log.Warning("Primary server failed. Attempting search on backup server.");
                    return await SearchAsync(query, cancellationToken); // Retry on backup
                }
            }
            return new List<MobData>(); // Return empty list on failure
        }

        public void Dispose()
        {
            httpClient.Dispose();
        }
    }
}

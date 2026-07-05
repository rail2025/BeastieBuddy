using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BeastieBuddy.Windows;

namespace BeastieBuddy.Data
{
    public class BestiaryManager
    {
        private readonly ServerClient serverClient;
        private readonly string cachePath;
        private readonly string etagPath;

        public BestiaryResponse Data { get; private set; } = new();
        public bool IsLoaded { get; private set; } = false;

        public BestiaryManager(ServerClient serverClient)
        {
            this.serverClient = serverClient;
            var configDir = Plugin.PluginInterface.GetPluginConfigDirectory();
            cachePath = Path.Combine(configDir, "bestiary_cache.json");
            etagPath = Path.Combine(configDir, "bestiary_etag.txt");
        }

        public async Task InitializeAsync(CancellationToken token)
        {
            LoadCache();
            await RefreshFromServer(token);
            IsLoaded = true;
        }

        private void LoadCache()
        {
            if (File.Exists(cachePath))
            {
                try
                {
                    var json = File.ReadAllText(cachePath);
                    var parsed = JsonConvert.DeserializeObject<BestiaryResponse>(json);
                    if (parsed != null)
                    {
                        Data = parsed;
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.Error(ex, "Failed to load bestiary cache.");
                }
            }
        }

        private async Task RefreshFromServer(CancellationToken token)
        {
            string currentETag = File.Exists(etagPath) ? await File.ReadAllTextAsync(etagPath, token) : string.Empty;

            var result = await serverClient.GetBestiaryAsync(currentETag, token);
            if (result.HasValue)
            {
                try
                {
                    var parsed = JsonConvert.DeserializeObject<BestiaryResponse>(result.Value.json);
                    if (parsed != null)
                    {
                        Data = parsed;
                        await File.WriteAllTextAsync(cachePath, result.Value.json, token);
                        if (!string.IsNullOrEmpty(result.Value.etag))
                        {
                            await File.WriteAllTextAsync(etagPath, result.Value.etag, token);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.Error(ex, "Failed to parse or save new bestiary data.");
                }
            }
        }
    }
}

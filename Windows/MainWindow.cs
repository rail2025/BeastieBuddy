using BeastieBuddy.Data;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Lumina.Excel.Sheets;
using MapLinkPayload = Dalamud.Game.Text.SeStringHandling.Payloads.MapLinkPayload;

namespace BeastieBuddy.Windows
{
    public class MainWindow : Window, IDisposable
    {
        private string searchText = string.Empty;
        private List<MobData> searchResults = new();
        private bool isSearching = false;

        private CancellationTokenSource? searchCancellationTokenSource;

        private readonly Plugin plugin;
        private readonly IGameGui gameGui;
        private readonly ITextureProvider textureProvider;
        private readonly ServerClient serverClient;
        private readonly byte[]? iconBytes;
        private readonly Dictionary<string, (uint TerritoryTypeID, uint MapID)> zoneNameToIds = new();

        private IDalamudTextureWrap? backgroundTexture;

        public MainWindow(Plugin plugin, IGameGui gameGui, ITextureProvider textureProvider, IDataManager dataManager) : base("BeastieBuddy##MainWindow")
        {
            this.plugin = plugin;
            this.gameGui = gameGui;
            this.textureProvider = textureProvider;
            this.serverClient = new ServerClient();

            // Pre-populate the zone name dictionary for faster lookups
            var maps = dataManager.GetExcelSheet<Map>()!;
            foreach (var map in maps)
            {
                // Use ValueNullable to safely access nested RowRefs
                var zoneName = map.TerritoryType.ValueNullable?.PlaceName.ValueNullable?.Name.ToString();

                if (!string.IsNullOrEmpty(zoneName) && !zoneNameToIds.ContainsKey(zoneName))
                {
                    zoneNameToIds[zoneName] = (map.TerritoryType.RowId, map.RowId);
                }
            }


            // Load image data into memory once
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "BeastieBuddy.icon.png";
            try
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    using var memoryStream = new MemoryStream();
                    stream.CopyTo(memoryStream);
                    this.iconBytes = memoryStream.ToArray();
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "Failed to load background image resource.");
            }

            var globalScale = ImGui.GetIO().FontGlobalScale;
            Size = new Vector2(375, 330) * globalScale;
            SizeCondition = ImGuiCond.FirstUseEver;

            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(375, 330) * globalScale,
                MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
            };
        }

        public void Dispose()
        {
            searchCancellationTokenSource?.Dispose();
            serverClient.Dispose();
            backgroundTexture?.Dispose();
        }

        public override void OnOpen()
        {
            if (backgroundTexture == null && iconBytes != null)
            {
                backgroundTexture = textureProvider.CreateFromImageAsync(iconBytes).Result;
            }
        }

        public override void OnClose()
        {
            backgroundTexture?.Dispose();
            backgroundTexture = null;
        }

        public override void Draw()
        {
            if (backgroundTexture != null)
            {
                var globalScale = ImGui.GetIO().FontGlobalScale;
                var windowPos = ImGui.GetWindowPos();
                var windowSize = ImGui.GetWindowSize();
                var imageSize = new Vector2(250, 250) * globalScale;
                var imagePos = windowPos + (windowSize - imageSize) * 0.5f;

                ImGui.GetWindowDrawList().AddImage(backgroundTexture.Handle, imagePos, imagePos + imageSize, Vector2.Zero, Vector2.One, 0x80FFFFFF);
            }

            if (ImGui.InputTextWithHint("##searchBar", "Search for a monster...", ref searchText, 256))
            {
                isSearching = true;
                DebouncedSearch();
            }

            ImGui.SameLine();
            if (ImGui.Button("About"))
            {
                plugin.ToggleAboutUI();
            }

            ImGui.Separator();

            if (ImGui.BeginChild("##scrolling_region"))
            {
                if (isSearching)
                {
                    ImGui.Text("Searching...");
                }
                else if (!string.IsNullOrWhiteSpace(searchText))
                {
                    if (!searchResults.Any())
                    {
                        ImGui.Text("No monsters found.");
                    }
                    else
                    {
                        foreach (var mob in searchResults)
                        {
                            if (mob != null && ImGui.Selectable($"##{mob.Name}{mob.X}{mob.Y}", false, ImGuiSelectableFlags.None, new Vector2(0, ImGui.GetTextLineHeight())))
                            {
                                if (zoneNameToIds.TryGetValue(mob.Zone, out var ids))
                                {
                                    var mapLink = new MapLinkPayload(ids.TerritoryTypeID, ids.MapID, mob.X, mob.Y);
                                    gameGui.OpenMapWithMapLink(mapLink);
                                }
                            }

                            if (mob != null)
                            {
                                ImGui.SameLine(0);
                                ImGui.Text(mob.Name);

                                var locationText = (mob.X == 0 && mob.Y == 0)
                                    ? mob.Zone
                                    : $"{mob.Zone} ({mob.X:F1}, {mob.Y:F1})";

                                var locationTextSize = ImGui.CalcTextSize(locationText);
                                ImGui.SameLine(ImGui.GetContentRegionAvail().X - locationTextSize.X);
                                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), locationText);
                            }
                        }
                    }
                }
            }
            ImGui.EndChild();
        }

        private void DebouncedSearch()
        {
            searchCancellationTokenSource?.Cancel();
            searchCancellationTokenSource?.Dispose();
            searchCancellationTokenSource = new CancellationTokenSource();
            var token = searchCancellationTokenSource.Token;

            Task.Delay(250, token).ContinueWith(async _ =>
            {
                if (token.IsCancellationRequested) return;

                var currentSearchText = searchText;
                if (string.IsNullOrWhiteSpace(currentSearchText))
                {
                    searchResults.Clear();
                }
                else
                {
                    var results = await serverClient.SearchAsync(currentSearchText, token);
                    if (results != null && !token.IsCancellationRequested)
                    {
                        searchResults = results;
                    }
                }
                isSearching = false;

            }, token, TaskContinuationOptions.NotOnCanceled, TaskScheduler.Default);
        }
    }
}

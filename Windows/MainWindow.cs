using BeastieBuddy.Data;
using BeastieBuddy.VfxSystem;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using static Dalamud.Interface.Utility.Raii.ImRaii;
using MapLinkPayload = Dalamud.Game.Text.SeStringHandling.Payloads.MapLinkPayload;

namespace BeastieBuddy.Windows
{
    public class MainWindow : Window, IDisposable
    {
        // Beastie Search
        private string searchText = string.Empty;
        private List<MobData> searchResults = new();
        private bool isSearching = false;
        private CancellationTokenSource? searchCancellationTokenSource;
        private readonly ServerClient serverClient;
        private readonly Dictionary<string, (uint TerritoryTypeID, uint MapID)> zoneNameToIds = new();

        // Blue Mage UI
        private readonly BlueMageUI blueMageUI;
        private readonly IDataManager dataManager;
        private readonly Plugin plugin;
        private readonly IGameGui gameGui;
        private readonly ITextureProvider textureProvider;
        private readonly BeaconController beaconController;
        private readonly byte[]? iconBytes;
        private IDalamudTextureWrap? backgroundTexture;

        private string? _tabToFocus;
        
        public MainWindow(Plugin plugin, IGameGui gameGui, ITextureProvider textureProvider, IDataManager dataManager, BeaconController beaconController) : base("BeastieBuddy##MainWindow")
        {
            this.plugin = plugin;
            this.gameGui = gameGui;
            this.textureProvider = textureProvider;
            this.dataManager = dataManager;
            this.serverClient = new ServerClient();
            this.beaconController = beaconController;

            // this.blueMageUI = new BlueMageUI(this.gameGui, this.zoneNameToIds, this.SwitchToSearchTab, this.beaconController);

            // Pre-populate the zone name dictionary for faster lookups
            var maps = dataManager.GetExcelSheet<Map>()!;
            foreach (var map in maps)

            {
                if (map.TerritoryType.ValueNullable?.Map.RowId != map.RowId) continue;

                var zoneName = map.TerritoryType.ValueNullable?.PlaceName.ValueNullable?.Name.ToString();
                if (!string.IsNullOrEmpty(zoneName) && !zoneNameToIds.ContainsKey(zoneName))
                {
                    Plugin.Log.Debug($"[MapInit] Zone '{zoneName}' -> MapID: {map.RowId}, Territory: {map.TerritoryType.RowId}");
                    zoneNameToIds[zoneName] = (map.TerritoryType.RowId, map.RowId);
                }
            }

            this.blueMageUI = new BlueMageUI(this.gameGui, this.dataManager, this.zoneNameToIds, this.SwitchToSearchTab, this.beaconController);

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

        private unsafe void OpenMapSafe(uint territoryId, uint mapId, float x, float y)
        {
            var mapLink = new MapLinkPayload(territoryId, mapId, x, y);

            try
            {
                var agent = AgentMap.Instance();
                if (agent != null)
                {
                    float flagX = mapLink.RawX / 1000.0f;
                    float flagY = mapLink.RawY / 1000.0f;

                    agent->FlagMarkerCount = 0;
                    agent->SetFlagMapMarker(territoryId, mapId, flagX, flagY, 60561);
                    agent->OpenMap(mapId, territoryId, null, FFXIVClientStructs.FFXIV.Client.UI.Agent.MapType.FlagMarker);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "[BeastieBuddy] MainWindow AgentMap failed.");
            }
            beaconController.Spawn(mapLink);
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
            if (ImGui.BeginTabBar("##MainTabs"))
            {
                ImGuiTabItemFlags beastieFlags = ImGuiTabItemFlags.None;
                if (_tabToFocus == "Beastie Search")
                    {
                    beastieFlags = ImGuiTabItemFlags.SetSelected;
                    _tabToFocus = null; // Clear the focus request after one frame
                }
                if (ImGui.BeginTabItem("Beastie Search", beastieFlags))
                {
                    DrawSearchTab();
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Blue Mage Spellbook"))
                {
                    blueMageUI.Draw();
                    ImGui.EndTabItem();
                }
                ImGui.EndTabBar();
            }
        }

        public void SwitchToSearchTab(string mobName)
        {
            _tabToFocus = "Beastie Search";
            searchText = mobName;
            isSearching = true;
            DebouncedSearch();
        }

        private void DrawSearchTab()
        {
            // Draw the search bar and About button FIRST
            if (ImGui.InputTextWithHint("##searchBar", "Search for a monster...", ref searchText, 256))
            {
                isSearching = true;
                DebouncedSearch();
            }

            ImGui.SameLine();
            if (ImGui.Button("Settings"))
            {
                plugin.ToggleConfigUI();
            }

            ImGui.SameLine();
            if (ImGui.Button("About"))
            {
                plugin.ToggleAboutUI();
            }

            if (backgroundTexture != null)
            {
                var globalScale = ImGui.GetIO().FontGlobalScale;
                var imageSize = new Vector2(250, 250) * globalScale;

                var contentStartPos = ImGui.GetCursorScreenPos();
                var contentSize = ImGui.GetContentRegionAvail();

                var imagePos = contentStartPos + (contentSize - imageSize) * 0.5f;

                ImGui.GetWindowDrawList().AddImage(backgroundTexture.Handle, imagePos, imagePos + imageSize, Vector2.Zero, Vector2.One, 0x80FFFFFF);
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
                                    OpenMapSafe(ids.TerritoryTypeID, ids.MapID, (float)mob.X, (float)mob.Y);
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

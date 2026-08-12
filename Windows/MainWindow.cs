using BeastieBuddy.Data;
using BeastieBuddy.VfxSystem;
using Dalamud.Bindings.ImGui;
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
        private List<ClusteredMob> clusteredResults = new();
        private bool isSearching = false;
        private record ClusteredMob(string Name, string Zone, float X, float Y, int Count, MobData OriginalMob);
        private CancellationTokenSource? searchCancellationTokenSource;
        private readonly ServerClient serverClient;
        private readonly Dictionary<string, (uint TerritoryTypeID, uint MapID)> zoneNameToIds = new();

        //for bestiary search
        internal IReadOnlyList<MobData> Results => searchResults;

        internal async System.Threading.Tasks.Task SearchAsync(string query)
        {
            var results = await serverClient.SearchAsync(query, System.Threading.CancellationToken.None);
            if (results != null)
            {
                searchResults = results;
                ClusterResults();
            }
        }

        // Blue Mage UI
        private readonly BlueMageUI blueMageUI;
        private readonly BestiaryUIV2 bestiaryUIV2;
        private readonly BestiaryManager bestiaryManager;
        private readonly IDataManager dataManager;
        private readonly Plugin plugin;
        private readonly IGameGui gameGui;
        private readonly ITextureProvider textureProvider;
        private readonly BeaconController beaconController;
        private readonly byte[]? iconBytes;
        private readonly byte[]? appIconBytes;
        private IDalamudTextureWrap? backgroundTexture;
        private IDalamudTextureWrap? appIconTexture;

        private DateTime _nextShakeTime = DateTime.Now.AddSeconds(new Random().Next(30, 60));
        private bool _isShaking = false;
        private DateTime _shakeEndTime;
        private readonly Random _random = new();

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
            this.bestiaryManager = new BestiaryManager(this.serverClient);
            _ = this.bestiaryManager.InitializeAsync(CancellationToken.None);
            this.bestiaryUIV2 = new BestiaryUIV2(this.SwitchToSearchTab, this.bestiaryManager, plugin.Configuration, this.textureProvider);

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
            try
            {
                using var stream = assembly.GetManifestResourceStream("BeastieBuddy.bbapp.webp");
                if (stream != null)
                {
                    using var memoryStream = new MemoryStream();
                    stream.CopyTo(memoryStream);
                    this.appIconBytes = memoryStream.ToArray();
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "Failed to load app icon image resource.");
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
            appIconTexture?.Dispose();
            bestiaryUIV2.Dispose();
        }

        public override void OnOpen()
        {
            if (backgroundTexture == null && iconBytes != null)
            {
                backgroundTexture = textureProvider.CreateFromImageAsync(iconBytes).Result;
            }
            if (appIconTexture == null && appIconBytes != null)
            {
                appIconTexture = textureProvider.CreateFromImageAsync(appIconBytes).Result;
            }
        }

        public override void OnClose()
        {
            backgroundTexture?.Dispose();
            backgroundTexture = null;
            appIconTexture?.Dispose();
            appIconTexture = null;
        }
        private string GetFooterMessage()
        {
            if (isSearching) return "Searching...";
            if (string.IsNullOrEmpty(serverClient.LastMessage)) return "Search for a beastie to get tips!";

            return serverClient.LastMessage;
        }
        public override void Draw()
        {
            var startCursorPos = ImGui.GetCursorPos();
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
                if (ImGui.BeginTabItem("Bestiary"))
                {
                    bestiaryUIV2.Draw();
                    ImGui.EndTabItem();
                }
                ImGui.EndTabBar();
            }
            if (appIconTexture != null)
            {
                var prevCursor = ImGui.GetCursorPos();
                var buttonSize = new Vector2(24, 24) * ImGui.GetIO().FontGlobalScale;
                var windowWidth = ImGui.GetWindowWidth();

                ImGui.SetCursorPos(new Vector2(windowWidth - buttonSize.X - (ImGui.GetStyle().WindowPadding.X * 2.0f), startCursorPos.Y));

                var imageCursor = ImGui.GetCursorPos();
                if (!plugin.Configuration.HasClickedAppIcon && !_isShaking && DateTime.Now > _nextShakeTime)
                {
                    _isShaking = true;
                    _shakeEndTime = DateTime.Now.AddSeconds(0.4);
                    _nextShakeTime = DateTime.Now.AddSeconds(_random.Next(2, 5));
                }
                else if (_isShaking && DateTime.Now > _shakeEndTime)
                {
                    _isShaking = false;
                }

                var renderPos = imageCursor;
                if (!plugin.Configuration.HasClickedAppIcon && _isShaking)
                {
                    renderPos.X += (float)(_random.NextDouble() * 4 - 2);
                    renderPos.Y += (float)(_random.NextDouble() * 4 - 2);
                }

                ImGui.SetCursorPos(renderPos);
                float alpha = plugin.Configuration.HasClickedAppIcon ? 1.0f : 0.75f + 0.25f * (float)Math.Sin(ImGui.GetTime() * 3.0);
                ImGui.Image(appIconTexture.Handle, buttonSize, Vector2.Zero, Vector2.One, new Vector4(1.0f, 1.0f, 1.0f, alpha));
                ImGui.SetCursorPos(imageCursor);

                if (ImGui.InvisibleButton("##StandaloneAppBtn", buttonSize))
                {
                    plugin.Configuration.HasClickedAppIcon = true;
                    plugin.Configuration.Save();
                    Dalamud.Utility.Util.OpenLink("https://github.com/rail2025/BeastieBuddy-App/");
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Try the standalone, ToS-abiding, never-breaking-on-patch-days, overlay app instead!");
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                }

                ImGui.SetCursorPos(prevCursor);
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

            var footerHeight = ImGui.GetTextLineHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y + 10;

            if (ImGui.BeginChild("##scrolling_region", new Vector2(0, -footerHeight)))
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
                        foreach (var mob in clusteredResults)
                        {
                            if (mob != null && ImGui.Selectable($"##{mob.Name}{mob.X}{mob.Y}", false, ImGuiSelectableFlags.None, new Vector2(0, ImGui.GetTextLineHeight())))
                            {
                                if (zoneNameToIds.TryGetValue(mob.Zone, out var ids))
                                {
                                    OpenMapSafe(ids.TerritoryTypeID, ids.MapID, mob.OriginalMob.X, mob.OriginalMob.Y);
                                    if (plugin.Configuration.AutoTeleport)
                                        plugin.TeleportToMob(ids.TerritoryTypeID, ids.MapID, (float)mob.X, (float)mob.Y);
                                }
                            }

                            if (mob != null)
                            {
                                ImGui.SameLine(0);
                                ImGui.Text(mob.Name);

                                var locationText = (mob.X == 0 && mob.Y == 0)
                                    ? mob.Zone
                                    : (mob.Count > 1 ? $"{mob.Zone} (~{mob.X:F1}, {mob.Y:F1}) [{mob.Count} locations]" : $"{mob.Zone} ({mob.X:F1}, {mob.Y:F1})");

                                var locationTextSize = ImGui.CalcTextSize(locationText);
                                ImGui.SameLine(ImGui.GetContentRegionAvail().X - locationTextSize.X);
                                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), locationText);
                            }
                        }
                    }
                }
            }
            ImGui.EndChild();
            ImGui.Separator();
            var footerText = GetFooterMessage();
            var wrapPosX = ImGui.GetWindowContentRegionMax().X;
            var words = footerText.Split(' ');
            var rareColor = new Vector4(1.0f, 0.84f, 0.0f, 1.0f);
            var kofiColor = new Vector4(0.5f, 1.0f, 0.5f, 1.0f);

            ImGui.SetCursorPosX(ImGui.GetStyle().WindowPadding.X);

            for (int i = 0; i < words.Length; i++)
            {
                string word = words[i];
                float wordWidth = ImGui.CalcTextSize(word).X;

                if (i > 0)
                {
                    ImGui.SameLine();
                    if (ImGui.GetCursorPosX() + wordWidth >= wrapPosX)
                    {
                        ImGui.NewLine();
                    }
                }

                if (word.Contains("[Ko-fi]"))
                {
                    ImGui.TextColored(kofiColor, word);
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                        if (ImGui.IsItemClicked())
                            Dalamud.Utility.Util.OpenLink("https://ko-fi.com/rail2025");
                    }
                }
                else
                {
                    if (serverClient.IsLastMessageRare)
                        ImGui.TextColored(rareColor, word);
                    else
                        ImGui.TextDisabled(word);
                }
            }
        }
        private void ClusterResults()
        {
            clusteredResults.Clear();
            var threshold = plugin.Configuration.ClusterDistanceThreshold;

            foreach (var group in searchResults.GroupBy(m => new { m.Name, m.Zone }))
            {
                var clusters = new List<ClusteredMob>();
                foreach (var mob in group)
                {
                    var existing = clusters.FirstOrDefault(c =>
                        Math.Abs(c.X - mob.X) <= threshold && Math.Abs(c.Y - mob.Y) <= threshold);

                    if (existing != null)
                    {
                        clusters[clusters.IndexOf(existing)] = existing with { Count = existing.Count + 1 };
                    }
                    else
                    {
                        clusters.Add(new ClusteredMob(mob.Name, mob.Zone, mob.X, mob.Y, 1, mob));
                    }
                }
                clusteredResults.AddRange(clusters);
            }
        }

        private void DebouncedSearch()
        {
            searchCancellationTokenSource?.Cancel();
            searchCancellationTokenSource?.Dispose();
            searchCancellationTokenSource = new CancellationTokenSource();

            var token = searchCancellationTokenSource.Token;

            _ = RunDebouncedSearchAsync(token);
        }

        private async Task RunDebouncedSearchAsync(CancellationToken token)
        {
            try
            {
                await Task.Delay(500, token);

                var currentSearchText = searchText;

                if (string.IsNullOrWhiteSpace(currentSearchText))
                {
                    searchResults.Clear();
                    clusteredResults.Clear();
                    isSearching = false;
                    return;
                }

                var results = await serverClient.SearchAsync(currentSearchText, token);

                if (results != null && !token.IsCancellationRequested)
                {
                    searchResults = results;
                    ClusterResults();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "[BeastieBuddy] Search failed");
            }
            finally
            {
                if (!token.IsCancellationRequested)
                    isSearching = false;
            }
        }
    }
}

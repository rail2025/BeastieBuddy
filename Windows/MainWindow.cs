using BeastieBuddy.Data;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ImGuiNET;
using Lumina.Excel.Sheets;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using MapLinkPayload = Dalamud.Game.Text.SeStringHandling.Payloads.MapLinkPayload;

namespace BeastieBuddy.Windows;

public class MobData
{
    public string Name { get; set; } = string.Empty;
    public string Zone { get; set; } = "Unknown Zone";
    public Vector2 Coordinates { get; set; }
    public uint TerritoryTypeID { get; set; }
    public uint MapID { get; set; }
}

public class MainWindow : Window, IDisposable
{
    private readonly List<MobData> mobDatabase;
    private List<MobData> filteredResults;
    private string searchText = string.Empty;
    private readonly IDataManager dataManager;
    private readonly IGameGui gameGui;
    private readonly Plugin plugin;
    private readonly ITextureProvider textureProvider;

    private IDalamudTextureWrap? finalTexture;
    private ISharedImmediateTexture? loadingTexture;

    private bool isDataLoaded = false;
    private string? tempImagePath;
    private bool loadImageAttempted = false;

    public MainWindow(Plugin plugin, IDataManager dataManager, IGameGui gameGui, ITextureProvider textureProvider) : base("BeastieBuddy##MainWindow")
    {
        this.plugin = plugin;
        this.dataManager = dataManager;
        this.gameGui = gameGui;
        this.textureProvider = textureProvider;
        var globalScale = ImGui.GetIO().FontGlobalScale;
        this.Size = new Vector2(375, 330) * globalScale;
        this.SizeCondition = ImGuiCond.FirstUseEver;

        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(375, 330) * globalScale,
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        mobDatabase = LoadAndLinkData();
        filteredResults = new List<MobData>();
    }

    public void Dispose()
    {
        finalTexture?.Dispose();
        if (!string.IsNullOrEmpty(tempImagePath) && File.Exists(tempImagePath))
        {
            try
            {
                File.Delete(tempImagePath);
            }
            catch (IOException ex)
            {
                Plugin.Log.Error(ex, "Failed to delete temporary image file.");
            }
        }
    }

    private void TryFinishLoadingImage()
    {
        if (finalTexture != null || loadingTexture == null)
        {
            return;
        }
        finalTexture = loadingTexture.GetWrapOrDefault();
    }

    private void StartLoadingImage()
    {
        if (loadImageAttempted) return;
        loadImageAttempted = true;

        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "BeastieBuddy.icon.png";

        try
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) return;

            using var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            var bytes = memoryStream.ToArray();

            tempImagePath = Path.GetTempFileName();
            File.WriteAllBytes(tempImagePath, bytes);

            loadingTexture = this.textureProvider.GetFromFile(tempImagePath);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "An exception occurred during image loading.");
        }
    }

    public override void Draw()
    {
        var globalScale = ImGui.GetIO().FontGlobalScale; 
        StartLoadingImage();

        if (this.finalTexture == null)
        {
            TryFinishLoadingImage();
        }

        if (this.finalTexture != null)
        {
            try
            {
                var windowPos = ImGui.GetWindowPos();
                var windowSize = ImGui.GetWindowSize();
                var imageSize = new Vector2(250, 250) * globalScale;
                var imagePos = windowPos + (windowSize - imageSize) * 0.5f;

                ImGui.GetWindowDrawList().AddImage(finalTexture.ImGuiHandle, imagePos, imagePos + imageSize, Vector2.Zero, Vector2.One, 0x80FFFFFF);
            }
            catch (ObjectDisposedException)
            {
                this.finalTexture = null;
            }
        }

        if (!isDataLoaded)
        {
            ImGui.TextWrapped("Error: Could not load the required data file.");
            return;
        }

        if (ImGui.InputTextWithHint("##searchBar", "Search for a monster...", ref searchText, 256))
        {
            UpdateFilteredResultsAsync();
        }

        ImGui.SameLine();
        if (ImGui.Button("About"))
        {
            plugin.ToggleAboutUI();
        }

        ImGui.Separator();

        if (ImGui.BeginChild("##scrolling_region"))
        {
            if (!string.IsNullOrEmpty(searchText))
            {
                if (!filteredResults.Any())
                {
                    ImGui.Text("No monsters found.");
                }
                else
                {
                    foreach (var mob in filteredResults)
                    {
                        if (ImGui.Selectable($"##{mob.Name}{mob.Coordinates.X}{mob.Coordinates.Y}", false, ImGuiSelectableFlags.None, new Vector2(0, ImGui.GetTextLineHeight())))
                        {
                            var mapLink = new MapLinkPayload(mob.TerritoryTypeID, mob.MapID, mob.Coordinates.X, mob.Coordinates.Y);
                            this.gameGui.OpenMapWithMapLink(mapLink);
                        }

                        ImGui.SameLine(0);
                        ImGui.Text(mob.Name);

                        var locationText = $"{mob.Zone} ({mob.Coordinates.X:F1}, {mob.Coordinates.Y:F1})";
                        var locationTextSize = ImGui.CalcTextSize(locationText);
                        ImGui.SameLine(ImGui.GetContentRegionAvail().X - locationTextSize.X);
                        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), locationText);
                    }
                }
            }
        }
        ImGui.EndChild();
    }

    private void UpdateFilteredResultsAsync()
    {
        Task.Run(() =>
        {
            var currentSearchText = searchText;
            if (string.IsNullOrWhiteSpace(currentSearchText))
            {
                filteredResults = new List<MobData>();
            }
            else
            {
                filteredResults = mobDatabase.Where(mob => mob.Name.Contains(currentSearchText, StringComparison.OrdinalIgnoreCase)).ToList();
            }
        });
    }

    private List<MobData> LoadAndLinkData()
    {
        var mobNames = this.dataManager.GetExcelSheet<BNpcName>()!;
        var maps = this.dataManager.GetExcelSheet<Map>()!;

        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "BeastieBuddy.mappy.json";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            Plugin.Log.Error($"Failed to load embedded resource: {resourceName}. Stream was null.");
            isDataLoaded = false;
            return new List<MobData>();
        }

        using var reader = new StreamReader(stream);
        var mappyJson = reader.ReadToEnd();
        var mappyData = JsonConvert.DeserializeObject<List<MappyData>>(mappyJson)!;

        var finalDatabase = new List<MobData>();
        foreach (var mappyEntry in mappyData)
        {
            if (!mobNames.TryGetRow(mappyEntry.BNpcNameID, out var mobNameRow)) continue;
            var mobName = mobNameRow.Singular.ToString();
            if (string.IsNullOrEmpty(mobName)) continue;

            if (!maps.TryGetRow(mappyEntry.MapID, out var mapRow)) continue;
            var zone = mapRow.TerritoryType.Value.PlaceName.Value.Name.ToString() ?? "Unknown Zone";

            var gameCoords = ConvertRawToGameCoordinates(mappyEntry.PixelX, mappyEntry.PixelY, mapRow);

            finalDatabase.Add(new MobData
            {
                Name = mobName,
                Zone = zone,
                Coordinates = gameCoords,
                TerritoryTypeID = mapRow.TerritoryType.Value.RowId,
                MapID = mappyEntry.MapID
            });
        }

        isDataLoaded = true;
        return finalDatabase.DistinctBy(m => new { m.Name, m.Zone, m.Coordinates.X, m.Coordinates.Y }).OrderBy(m => m.Name).ToList();
    }

    private Vector2 ConvertRawToGameCoordinates(float rawX, float rawY, Map mapInfo)
    {
        var sizeFactor = mapInfo.SizeFactor / 100.0f;
        var gameX = (41.0f / sizeFactor) * (rawX / 2048.0f) + 1.0f;
        var gameY = (41.0f / sizeFactor) * (rawY / 2048.0f) + 1.0f;
        return new Vector2(gameX, gameY);
    }
}

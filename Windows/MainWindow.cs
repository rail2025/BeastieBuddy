using ImGuiNET;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Newtonsoft.Json;
using BeastieBuddy.Data;
using Lumina.Excel.Sheets;

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
    private bool isDataLoaded = false;

    public MainWindow(IDataManager dataManager, IGameGui gameGui) : base("BeastieBuddy##MainWindow")
    {
        this.dataManager = dataManager;
        this.gameGui = gameGui; 
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(400, 500), MaximumSize = new Vector2(float.MaxValue, float.MaxValue) };

        mobDatabase = LoadAndLinkData();
        filteredResults = new List<MobData>();
    }

    public void Dispose() { }

    private Vector2 ConvertRawToGameCoordinates(float rawX, float rawY, Map mapInfo)
    {
        var sizeFactor = mapInfo.SizeFactor / 100.0f;
        var gameX = (41.0f / sizeFactor) * (rawX / 2048.0f) + 1.0f;
        var gameY = (41.0f / sizeFactor) * (rawY / 2048.0f) + 1.0f;
        return new Vector2(gameX, gameY);
    }

    public override void Draw()
    {
        if (!isDataLoaded)
        {
            ImGui.TextWrapped("Error: Could not load the required data file.");
            return;
        }

        if (ImGui.InputTextWithHint("##searchBar", "Search for a monster...", ref searchText, 256))
        {
            UpdateFilteredResultsAsync();
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
}

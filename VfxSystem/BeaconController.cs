using BeastieBuddy.VfxSystem;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using System;
using System.IO;
using System.Numerics;
using System.Reflection;

namespace BeastieBuddy.VfxSystem;

public unsafe class BeaconController : IDisposable
{
    public static bool FeatureGlobalLock = true;

    private Plugin PluginInstance { get; }
    private Vfx? VfxEngine { get; set; }
    private MapLinkPayload? ActiveTarget { get; set; }
    private DateTime LastUpdate { get; set; }

    private string PillarPath { get; set; } = string.Empty;
    private string StarPath { get; set; } = string.Empty;

    private enum BeaconState { None, Pillar, Star }
    private BeaconState CurrentState { get; set; } = BeaconState.None;

    public BeaconController(Plugin plugin)
    {
        PluginInstance = plugin;

        PillarPath = ExtractAsset("PillarOfLightWithFlareStarTop_groundTarget.avfx");
        StarPath = ExtractAsset("HighFlareStar_groundTarget.avfx");

        if (FeatureGlobalLock)
        {
            try
            {
                VfxEngine = new Vfx(plugin);
                BeastieBuddy.Plugin.Framework.Update += OnUpdate;
            }
            catch (Exception ex)
            {
                BeastieBuddy.Plugin.Log.Error(ex, "Failed to initialize VFX Engine.");
            }
        }
    }

    private string ExtractAsset(string fileName)
    {
        try
        {
            // FIX: Access static PluginInterface via the type 'BeastieBuddy.Plugin'
            var configDir = BeastieBuddy.Plugin.PluginInterface.GetPluginConfigDirectory();
            var vfxDir = Path.Combine(configDir, "vfx");
            Directory.CreateDirectory(vfxDir);
            var filePath = Path.Combine(vfxDir, fileName);

            var assembly = Assembly.GetExecutingAssembly();
            // Ensure this resource path matches your project structure (Folder: vfx)
            var resourceName = $"BeastieBuddy.vfx.{fileName}";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                BeastieBuddy.Plugin.Log.Error($"Could not find embedded resource: {resourceName}");
                return "";
            }

            using var fileStream = File.Create(filePath);
            stream.CopyTo(fileStream);

            return filePath;
        }
        catch (Exception ex)
        {
            BeastieBuddy.Plugin.Log.Error(ex, $"Failed to extract VFX asset: {fileName}");
            return "";
        }
    }

    public void Spawn(MapLinkPayload mapLink)
    {
        if (!FeatureGlobalLock || VfxEngine == null || !PluginInstance.Configuration.IsVfxEnabled) return;

        ActiveTarget = mapLink;
        CurrentState = BeaconState.None;
        VfxEngine.QueueRemoveAll();
        OnUpdate(BeastieBuddy.Plugin.Framework);
    }

    public void Clear()
    {
        ActiveTarget = null;
        CurrentState = BeaconState.None;
        VfxEngine?.QueueRemoveAll();
    }

    private void OnUpdate(IFramework framework)
    {
        if (ActiveTarget == null || VfxEngine == null) return;

        if ((DateTime.Now - LastUpdate).TotalSeconds < 0.1) return;
        LastUpdate = DateTime.Now;

        if (BeastieBuddy.Plugin.ClientState.TerritoryType != ActiveTarget.TerritoryType.RowId)
        {
            if (CurrentState != BeaconState.None) Clear();
            return;
        }

        var player = BeastieBuddy.Plugin.ClientState.LocalPlayer;
        if (player == null) return;

        var targetPos = ConvertToWorld(ActiveTarget);
        var dist = Vector3.Distance(player.Position, targetPos);

        BeaconState desiredState = BeaconState.None;

        if (dist > PluginInstance.Configuration.PillarOfLightMinDistance)
        {
            desiredState = BeaconState.Pillar;
        }
        else if (dist > PluginInstance.Configuration.StarMinDistance)
        {
            desiredState = BeaconState.Star;
        }

        if (CurrentState != desiredState)
        {
            VfxEngine.QueueRemoveAll();
            var spawnId = Guid.NewGuid();

            if (desiredState == BeaconState.Pillar && !string.IsNullOrEmpty(PillarPath))
            {
                VfxEngine.QueueSpawn(spawnId, PillarPath, targetPos, Quaternion.Identity);
            }
            else if (desiredState == BeaconState.Star && !string.IsNullOrEmpty(StarPath))
            {
                var adjusted = targetPos with { Y = targetPos.Y + PluginInstance.Configuration.StarHeightOffset };
                VfxEngine.QueueSpawn(spawnId, StarPath, adjusted, Quaternion.Identity);
            }
            CurrentState = desiredState;
        }
    }

    private Vector3 ConvertToWorld(MapLinkPayload link)
    {
        var map = BeastieBuddy.Plugin.DataManager.GetExcelSheet<Map>()?.GetRow(link.Map.RowId);
        if (map == null) return Vector3.Zero;

        float scale = map.Value.SizeFactor / 100.0f;
        float worldX = (link.XCoord - 21.5f) * 50.0f / scale;
        float worldZ = (link.YCoord - 21.5f) * 50.0f / scale;
        float worldY = BeastieBuddy.Plugin.ClientState.LocalPlayer?.Position.Y ?? 0;

        return new Vector3(worldX, worldY, worldZ);
    }

    public void Dispose()
    {
        BeastieBuddy.Plugin.Framework.Update -= OnUpdate;
        VfxEngine?.Dispose();
    }
}

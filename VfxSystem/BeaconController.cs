using BeastieBuddy.VfxSystem;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace BeastieBuddy.VfxSystem;

public unsafe class BeaconController : IDisposable
{
    public static bool FeatureGlobalLock = true;

    private Plugin PluginInstance { get; }
    private Vfx? VfxEngine { get; set; }
    private MapLinkPayload? ActiveTarget { get; set; }
    private DateTime LastUpdate { get; set; }

    private IFramework Framework { get; }
    private IPluginLog Log { get; }

    // Defined Virtual paths
    public const string VfxRoute1 = "vfx/monster/gimmick4/eff/m5fa_b0_g11c0w.avfx";
    public const string VfxRoute2 = "vfx/monster/gimmick4/eff/m5fa_b0_g12c0w.avfx";

    // Public dictionary for VfxReplacer
    public static readonly Dictionary<string, string> Replacements = new()
    {
        { VfxRoute1, "PillarOfLightWithFlareStarTop_groundTarget.avfx" },
        { VfxRoute2, "HighFlareStar_groundTarget.avfx" }
    };

    private enum BeaconState { None, Pillar, Star }
    private BeaconState CurrentState { get; set; } = BeaconState.None;

    public BeaconController(Plugin plugin)
    {
        PluginInstance = plugin;
        Framework = BeastieBuddy.Plugin.Framework;
        Log = BeastieBuddy.Plugin.Log;

        if (FeatureGlobalLock)
        {
            try
            {
                // Initialize Vfx Engine
                VfxEngine = new Vfx(plugin, BeastieBuddy.Plugin.GameInteropProvider, BeastieBuddy.Plugin.Framework, BeastieBuddy.Plugin.Log);
                Framework.Update += OnUpdate;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to initialize VFX Engine.");
            }
        }
    }

    // Overload to handle raw data calls from UI (Fixes CS1501)
    public void Spawn(uint territoryType, uint mapId, float x, float y)
    {
        Spawn(new MapLinkPayload(territoryType, mapId, x, y));
    }

    public void Spawn(MapLinkPayload mapLink)
    {
        if (!FeatureGlobalLock || VfxEngine == null || !PluginInstance.Configuration.IsVfxEnabled) return;

        ActiveTarget = mapLink;
        CurrentState = BeaconState.None;

        VfxEngine.QueueRemoveAll();
        OnUpdate(Framework);
    }

    public void Clear()
    {
        ActiveTarget = null;
        CurrentState = BeaconState.None;
        VfxEngine?.QueueRemoveAll();
    }

    private void OnUpdate(IFramework framework)
    {
        if (!PluginInstance.Configuration.IsVfxEnabled)
        {
            if (CurrentState != BeaconState.None)
            {
                VfxEngine?.QueueRemoveAll();
                CurrentState = BeaconState.None;
            }
            return;
        }

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

            if (desiredState == BeaconState.Pillar)
            {
                VfxEngine.QueueSpawn(spawnId, VfxRoute1, targetPos, Quaternion.Identity);
            }
            else if (desiredState == BeaconState.Star)
            {
                var adjusted = targetPos with { Y = targetPos.Y + PluginInstance.Configuration.StarHeightOffset };
                VfxEngine.QueueSpawn(spawnId, VfxRoute2, adjusted, Quaternion.Identity);
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
        float worldY = BeastieBuddy.Plugin.ObjectTable.LocalPlayer?.Position.Y ?? 0;

        return new Vector3(worldX, worldY, worldZ);
    }

    public void Dispose()
    {
        Framework.Update -= OnUpdate;
        VfxEngine?.Dispose();
    }
}

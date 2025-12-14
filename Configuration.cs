using Dalamud.Configuration;
using Dalamud.Plugin;
using System;

namespace BeastieBuddy;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool IsVfxEnabled { get; set; } = true;
    // Distance settings for the BeaconController
    public float PillarOfLightMinDistance { get; set; } = 20.0f;
    public float StarMinDistance { get; set; } = 2.0f;
    public float StarHeightOffset { get; set; } = 2.5f;
    public float RefreshInterval { get; set; } = 2.0f;

    public bool IsConfigWindowMovable { get; set; } = true;
    //public bool SomePropertyToBeSavedAndWithADefault { get; set; } = true;

    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
        this.pluginInterface = pluginInterface;
    }
    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}

using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace BeastieBuddy.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration configuration;

    public ConfigWindow(BeastieBuddy.Plugin plugin) : base("BeastieBuddy Configuration##ConfigWindow")
    {
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse;

        Size = new Vector2(350, 220);
        SizeCondition = ImGuiCond.Always;

        this.configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void Draw()
    {
        bool save = false;

        ImGui.Text("VFX Beacon Settings");

        // Master Toggle
        var vfxEnabled = this.configuration.IsVfxEnabled;
        if (ImGui.Checkbox("Enable Map Beacons", ref vfxEnabled))
        {
            this.configuration.IsVfxEnabled = vfxEnabled;
            save = true;
        }

        // Distance Sliders
        var pillarDist = this.configuration.PillarOfLightMinDistance;
        if (ImGui.SliderFloat("Pillar Min Distance", ref pillarDist, 10.0f, 100.0f))
        {
            this.configuration.PillarOfLightMinDistance = pillarDist;
            save = true;
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("The minimum distance you must be from the target to see the large Pillar.\nIf you are closer than this, the small Star will be shown instead.\n(Lower value = Pillar stays visible longer as you approach)");

        var starDist = this.configuration.StarMinDistance;
        if (ImGui.SliderFloat("Star Draw Distance", ref starDist, 1.0f, 20.0f))
        {
            this.configuration.StarMinDistance = starDist;
            save = true;
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Distance at which the pillar turns into a small star.");

        var starHeight = this.configuration.StarHeightOffset;
        if (ImGui.SliderFloat("Star Height Offset", ref starHeight, 0.0f, 5.0f))
        {
            this.configuration.StarHeightOffset = starHeight;
            save = true;
        }

        var movable = this.configuration.IsConfigWindowMovable;
        if (ImGui.Checkbox("Movable Config Window", ref movable))
        {
            this.configuration.IsConfigWindowMovable = movable;
            this.configuration.Save();
        }
        if (save)
        {
            this.configuration.Save();
        }
    }
}

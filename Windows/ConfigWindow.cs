using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using ImGuiNET;

namespace BeastieBuddy.Windows;

public class ConfigWindow : Window, IDisposable
{
    // The fix is here: Renamed to follow C# naming conventions.
    private readonly Configuration configuration;

    public ConfigWindow(BeastieBuddy.Plugin plugin) : base("BeastieBuddy Configuration##ConfigWindow")
    {
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse;

        Size = new Vector2(232, 90);
        SizeCondition = ImGuiCond.Always;

        this.configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var configValue = this.configuration.SomePropertyToBeSavedAndWithADefault;
        if (ImGui.Checkbox("Random Config Bool", ref configValue))
        {
            this.configuration.SomePropertyToBeSavedAndWithADefault = configValue;
            this.configuration.Save();
        }

        var movable = this.configuration.IsConfigWindowMovable;
        if (ImGui.Checkbox("Movable Config Window", ref movable))
        {
            this.configuration.IsConfigWindowMovable = movable;
            this.configuration.Save();
        }
    }
}

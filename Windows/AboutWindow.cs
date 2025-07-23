#pragma warning disable CA1416

using System;
using System.Numerics;
using System.Reflection;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using ImGuiNET;

namespace BeastieBuddy.Windows
{
    public class AboutWindow : Window, IDisposable
    {
        private readonly Plugin plugin;

        public AboutWindow(Plugin plugin) : base("About BeastieBuddy")
        {
            this.plugin = plugin;
            this.SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(300, 200),
                MaximumSize = new Vector2(800, 600)
            };
        }

        public void Dispose() { }

        public override void PreDraw()
        {
            this.Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar;
        }

        public override void Draw()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
            ImGui.Text($"Version: {version}");
            ImGui.Text("Release Date: 7/22/2025");
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.Text("Created by: rail");
            ImGui.Text("With special thanks to the Dalamud Discord community.");
            ImGui.Text("Check out my other projects on github.com/rail2025/");
            ImGui.Text("Aether Arena, AetherBreaker, AetherBreakout, and more.");

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            float buttonWidth = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) / 2;
            float buttonHeight = ImGui.CalcTextSize("Bug Report").Y + ImGui.GetStyle().FramePadding.Y * 2;

            // Bug Report Button
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.1f, 0.4f, 0.1f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.1f, 0.5f, 0.1f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.2f, 0.6f, 0.2f, 1.0f));
            if (ImGui.Button("Bug Report", new Vector2(buttonWidth, buttonHeight)))
            {
                Util.OpenLink("https://github.com/rail2025/BeastieBuddy/issues");
            }
            ImGui.PopStyleColor(3);

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Opens the GitHub Issues page in your browser.");
            }

            ImGui.SameLine();

            // Donate Button
            var buttonColor = new Vector4(0.9f, 0.2f, 0.2f, 1.0f);
            ImGui.PushStyleColor(ImGuiCol.Button, buttonColor);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, buttonColor * 1.2f);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, buttonColor * 0.8f);
            if (ImGui.Button("Donate (Ko-fi)", new Vector2(buttonWidth, buttonHeight)))
            {
                Util.OpenLink("https://ko-fi.com/rail2025");
            }
            ImGui.PopStyleColor(3);

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Support the developer!");
            }
        }
    }
}

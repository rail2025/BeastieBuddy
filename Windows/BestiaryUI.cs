using BeastieBuddy.Data;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace BeastieBuddy.Windows
{
    public class BestiaryUI
    {
        private string filterText = string.Empty;
        private int currentElementIndex = 0;
        private readonly string[] elements = { "All", "Earth", "Fire", "Ice", "Lightning", "Water", "Wind" };

        private readonly Action<string> switchToSearchTab;
        private readonly BestiaryManager bestiaryManager;
        private readonly Configuration configuration;

        private KeyValuePair<int, BeastData>? selectedBeast = null;

        public BestiaryUI(Action<string> switchToSearchTab, BestiaryManager bestiaryManager, Configuration configuration)
        {
            this.switchToSearchTab = switchToSearchTab;
            this.bestiaryManager = bestiaryManager;
            this.configuration = configuration;
        }

        private Vector4 GetElementColor(string element)
        {
            return element.ToLower() switch
            {
                "fire" => new Vector4(1.00f, 0.40f, 0.40f, 1.0f),
                "ice" => new Vector4(0.55f, 0.85f, 1.00f, 1.0f),
                "wind" => new Vector4(0.60f, 1.00f, 0.60f, 1.0f),
                "earth" => new Vector4(0.50f, 0.32f, 0.12f, 1.0f),
                "lightning" => new Vector4(1.00f, 1.00f, 0.80f, 1.0f),
                "water" => new Vector4(0.10f, 0.25f, 0.70f, 1.0f),
                _ => new Vector4(0.7f, 0.7f, 0.7f, 1.0f)
            };
        }

        public void Draw()
        {
            if (!bestiaryManager.IsLoaded)
            {
                ImGui.Text("Loading Bestiary...");
                return;
            }

            using var bestiaryTable = ImRaii.Table("BestiaryLayout", 2, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.Resizable);
            if (!bestiaryTable) return;

            ImGui.TableSetupColumn("ListColumn", ImGuiTableColumnFlags.WidthStretch, 0.55f);
            ImGui.TableSetupColumn("DetailsColumn", ImGuiTableColumnFlags.WidthStretch, 0.45f);
            ImGui.TableNextRow();

            ImGui.TableNextColumn();

            ImGui.Text($"Progress: {configuration.TamedBeasts.Count} / 50 Tamed");

            float scale = ImGui.GetIO().FontGlobalScale;

            ImGui.SetNextItemWidth(100 * scale);
            ImGui.InputTextWithHint("##filter", "Filter...", ref filterText, 100);
            ImGui.SameLine();
            ImGui.Text("Element:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100 * scale);
            ImGui.Combo("##elementFilter", ref currentElementIndex, elements, elements.Length);
            ImGui.Separator();

            using (var listChild = ImRaii.Child("BestiaryList"))
            {
                if (listChild)
                {
                    foreach (var kvp in bestiaryManager.Data.Beasts)
                    {
                        var id = kvp.Key;
                        var beast = kvp.Value;

                        string filterLower = filterText.ToLower();
                        if (!string.IsNullOrEmpty(filterLower) && !beast.Name.ToLower().Contains(filterLower))
                            continue;

                        string selectedElement = elements[currentElementIndex];
                        if (selectedElement != "All" && !string.Equals(beast.AutoAttackElement, selectedElement, StringComparison.OrdinalIgnoreCase))
                            continue;

                        bool isTamed = configuration.TamedBeasts.Contains(id);
                        if (ImGui.Checkbox($"##tamed_{id}", ref isTamed))
                        {
                            if (isTamed) configuration.TamedBeasts.Add(id);
                            else configuration.TamedBeasts.Remove(id);
                            configuration.Save();
                        }

                        ImGui.SameLine();

                        if (isTamed)
                            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.5f, 1.0f));

                        bool isSelected = selectedBeast?.Key == id;
                        if (ImGui.Selectable($"{id:D2}. {beast.Name}##sel_{id}", isSelected))
                        {
                            selectedBeast = kvp;
                        }

                        if (isTamed)
                            ImGui.PopStyleColor();

                        ImGui.SameLine();
                        ImGui.TextColored(GetElementColor(beast.AutoAttackElement), $"[{beast.AutoAttackElement}]");
                    }
                }
            }

            ImGui.TableNextColumn();

            using (var detailsChild = ImRaii.Child("BestiaryDetails"))
            {
                if (detailsChild)
                {
                    if (selectedBeast != null)
                    {
                        var beast = selectedBeast.Value.Value;

                        ImGui.Text($"No. {selectedBeast.Value.Key} {beast.Name}");
                        ImGui.Separator();
                        ImGui.Text($"Location: {beast.Location}");
                        ImGui.TextColored(GetElementColor(beast.AutoAttackElement), $"Auto-Attack: {beast.AutoAttackElement}");

                        ImGui.Spacing();
                        if (ImGui.Button("Find Spawn Locations"))
                        {
                            switchToSearchTab(beast.Name);
                        }

                        ImGui.Spacing();
                        ImGui.Text("Abilities:");
                        ImGui.Text($"[Trick] {beast.Trick.Name}");
                        if (!string.IsNullOrEmpty(beast.Trick.Effect) && beast.Trick.Effect != "Unknown")
                            ImGui.TextWrapped($"  > {beast.Trick.Effect}");

                        ImGui.Text($"[Tempered Release] {beast.TemperedRelease.Name}");
                        if (!string.IsNullOrEmpty(beast.TemperedRelease.Effect) && beast.TemperedRelease.Effect != "Unknown")
                            ImGui.TextWrapped($"  > {beast.TemperedRelease.Effect}");

                        ImGui.Text($"[Nature's Gift] {beast.NaturesGift.Name}");
                        if (!string.IsNullOrEmpty(beast.NaturesGift.Effect) && beast.NaturesGift.Effect != "Unknown")
                            ImGui.TextWrapped($"  > {beast.NaturesGift.Effect}");

                        ImGui.Text($"[Finisher] {beast.Finisher.Name}");
                        if (!string.IsNullOrEmpty(beast.Finisher.Effect) && beast.Finisher.Effect != "Unknown")
                            ImGui.TextWrapped($"  > {beast.Finisher.Effect}");
                    }
                    else
                    {
                        ImGui.TextDisabled("Select a beast to view details.");
                    }
                }
            }
        }
    }
}

using BeastieBuddy.Data;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace BeastieBuddy.Windows
{
    public class BestiaryCombatLab : IDisposable
    {
        private readonly CrucibleManager crucibleManager;
        private readonly BestiaryManager bestiaryManager;
        private readonly Configuration configuration;
        private readonly ITextureProvider textureProvider;

        private static readonly string[] Weaknesses = { "None", "Fire", "Ice", "Wind", "Earth", "Lightning", "Water", "Slashing", "Blunt", "Piercing", "Physical", "Magic" };
        private static readonly string[] StatusVulnerabilities = { "Slow", "Petrify", "Doom", "Poison", "Silence", "Paralyze", "Blind", "Stun", "Sleep", "Heavy", "Bind" };
        private static readonly int[] BoardBases = { 80, 105, 130, 155, 180 };
        private static readonly string[] BoardLabels = { "Board 1 (80)", "Board 2 (105)", "Board 3 (130)", "Board 4 (155)", "Board 5 (180)" };
        private static readonly string[] Classifications = { "All", "Beastkin", "Vilekin", "Cloudkin", "Seedkin", "Wavekin", "Scalekin", "Soulkin", "Ashkin" };

        private int selectedSlot = 0;
        private int selectedWeakness = 0;
        private float weaknessMultiplier = 1.30f;
        private int selectedBoardIndex = 4;
        private readonly HashSet<string> activeVulnerabilities = new(StringComparer.OrdinalIgnoreCase);
        private string selectedKin = "All";

        public BestiaryCombatLab(CrucibleManager crucibleManager, BestiaryManager bestiaryManager, Configuration configuration, ITextureProvider textureProvider)
        {
            this.crucibleManager = crucibleManager ?? throw new ArgumentNullException(nameof(crucibleManager));
            this.bestiaryManager = bestiaryManager ?? throw new ArgumentNullException(nameof(bestiaryManager));
            this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            this.textureProvider = textureProvider ?? throw new ArgumentNullException(nameof(textureProvider));
        }

        public void Draw()
        {
            if (bestiaryManager.Data?.Beasts == null || !bestiaryManager.IsLoaded)
            {
                ImGui.Text("Loading Combat Lab data...");
                return;
            }

            DrawControls();
            ImGui.Separator();
            DrawTimeline();
            ImGui.Separator();
            DrawCandidates();
        }

        private void DrawControls()
        {
            bool stateChanged = false;

            ImGui.SetNextItemWidth(120);
            using (var combo = ImRaii.Combo("Weakness", Weaknesses[selectedWeakness]))
            {
                if (combo)
                {
                    for (int i = 0; i < Weaknesses.Length; i++)
                    {
                        if (ImGui.Selectable(Weaknesses[i], selectedWeakness == i))
                        {
                            selectedWeakness = i;
                            stateChanged = true;
                        }
                    }
                }
            }

            ImGui.SameLine();
            ImGui.SetNextItemWidth(70);
            if (ImGui.InputFloat("Mult", ref weaknessMultiplier, 0f, 0f, "%.2f"))
            {
                weaknessMultiplier = Math.Clamp(weaknessMultiplier, 1.0f, 2.0f);
                stateChanged = true;
            }

            ImGui.SameLine();
            ImGui.SetNextItemWidth(140);
            using (var boardCombo = ImRaii.Combo("Board", BoardLabels[selectedBoardIndex]))
            {
                if (boardCombo)
                {
                    for (int i = 0; i < BoardLabels.Length; i++)
                    {
                        if (ImGui.Selectable(BoardLabels[i], selectedBoardIndex == i))
                        {
                            selectedBoardIndex = i;
                            stateChanged = true;
                        }
                    }
                }
            }

            ImGui.SameLine();
            string vulnLabel = activeVulnerabilities.Count > 0 ? $"Vulns ({activeVulnerabilities.Count})###vulnBtn" : "Status Vulns...###vulnBtn";
            if (ImGui.Button(vulnLabel))
            {
                ImGui.OpenPopup("VulnPopup");
            }

            if (ImGui.BeginPopup("VulnPopup"))
            {
                foreach (var vuln in StatusVulnerabilities)
                {
                    bool isChecked = activeVulnerabilities.Contains(vuln);
                    if (ImGui.Checkbox(vuln, ref isChecked))
                    {
                        if (isChecked) activeVulnerabilities.Add(vuln);
                        else activeVulnerabilities.Remove(vuln);
                        stateChanged = true;
                    }
                }
                ImGui.EndPopup();
            }

            if (stateChanged)
            {
                TriggerUpdate();
            }
        }

        private void DrawTimeline()
        {
            using var table = ImRaii.Table("CrucibleTimeline", 3, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchSame);
            if (!table) return;

            for (int i = 0; i < 3; i++)
                ImGui.TableSetupColumn($"Slot {i + 1}");

            ImGui.TableHeadersRow();
            ImGui.TableNextRow();

            var beasts = bestiaryManager.Data.Beasts;

            for (int i = 0; i < 3; i++)
            {
                ImGui.TableNextColumn();
                int slotIdx = i;
                bool isSelected = selectedSlot == slotIdx;

                string beastId = crucibleManager.Loadout[slotIdx] ?? string.Empty;
                string beastName = "Empty";
                uint iconId = 0;

                if (!string.IsNullOrEmpty(beastId) && int.TryParse(beastId, out var parsedId))
                {
                    if (beasts.TryGetValue(parsedId, out var beast) && beast != null)
                    {
                        beastName = beast.Name;
                        iconId = beast.IconId;
                    }
                }

                if (isSelected)
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 0.9f, 1.0f, 1.0f));

                if (ImGui.Selectable($"{beastName}##slotSelect_{slotIdx}", isSelected, ImGuiSelectableFlags.AllowItemOverlap))
                {
                    selectedSlot = slotIdx;
                    TriggerUpdate();
                }

                if (isSelected) ImGui.PopStyleColor();

                if (iconId > 0)
                {
                    var icon = textureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrDefault();
                    if (icon != null)
                    {
                        ImGui.SameLine();
                        ImGui.Image(icon.Handle, new Vector2(18, 18));
                    }
                }

                if (!string.IsNullOrEmpty(beastId))
                {
                    ImGui.SameLine();
                    if (ImGui.SmallButton($"x##clear_{slotIdx}"))
                    {
                        crucibleManager.SetSlot(slotIdx, null);
                        TriggerUpdate();
                    }
                }
            }
        }

        private void DrawCandidates()
        {
            if (crucibleManager.IsOptimized)
                ImGui.TextColored(new Vector4(0.4f, 0.85f, 0.4f, 1f), $"[Optimized Rank Order: Slot {selectedSlot + 1}]");
            else
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), $"[Baseline Rank Order: Slot {selectedSlot + 1}]");

            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            using (var kinCombo = ImRaii.Combo("##kinFilter", selectedKin))
            {
                if (kinCombo)
                {
                    foreach (var k in Classifications)
                    {
                        if (ImGui.Selectable(k, selectedKin == k))
                            selectedKin = k;
                    }
                }
            }

            using var child = ImRaii.Child("CrucibleCandidatesRegion", new Vector2(0, 0), true);
            if (!child) return;

            using var table = ImRaii.Table("CrucibleCandidatesTable", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit);
            if (!table) return;

            ImGui.TableSetupColumn("Rank", ImGuiTableColumnFlags.WidthFixed, 45);
            ImGui.TableSetupColumn("Icon", ImGuiTableColumnFlags.WidthFixed, 24);
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Kin", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed, 50);
            ImGui.TableHeadersRow();

            var currentRankingsSnapshot = crucibleManager.CurrentRankings;
            var beasts = bestiaryManager.Data.Beasts;

            int displayRank = 1;
            foreach (var idStr in currentRankingsSnapshot)
            {
                if (string.IsNullOrEmpty(idStr) || !int.TryParse(idStr, out var id)) continue;
                if (!beasts.TryGetValue(id, out var beast) || beast == null) continue;

                bool isAssignedElsewhere = false;
                for (int i = 0; i < crucibleManager.Loadout.Length; i++)
                {
                    if (i != selectedSlot && crucibleManager.Loadout[i] == idStr)
                    {
                        isAssignedElsewhere = true;
                        break;
                    }
                }
                if (isAssignedElsewhere) continue;

                if (selectedKin != "All" && !string.Equals(beast.Classification, selectedKin, StringComparison.OrdinalIgnoreCase))
                    continue;

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text($"#{displayRank++}");

                ImGui.TableNextColumn();
                var icon = textureProvider.GetFromGameIcon(new GameIconLookup(beast.IconId)).GetWrapOrDefault();
                if (icon != null)
                {
                    ImGui.Image(icon.Handle, new Vector2(20, 20));
                }

                ImGui.TableNextColumn();
                ImGui.Text(beast.Name);

                ImGui.TableNextColumn();
                ImGui.TextDisabled(beast.Classification);

                ImGui.TableNextColumn();
                if (ImGui.SmallButton($"Assign##assign_{id}"))
                {
                    crucibleManager.SetSlot(selectedSlot, idStr);
                    TriggerUpdate();
                }
            }
        }

        private void TriggerUpdate()
        {
            crucibleManager.RefreshRankings(
                selectedSlot,
                Weaknesses[selectedWeakness],
                weaknessMultiplier,
                BoardBases[selectedBoardIndex],
                activeVulnerabilities.ToList()
            );
        }

        public void Dispose() { }
    }
}

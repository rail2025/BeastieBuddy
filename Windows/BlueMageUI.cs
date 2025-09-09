// BeastieBuddy/Windows/BlueMageUI.cs

using BeastieBuddy.Data;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace BeastieBuddy.Windows
{
    public class BlueMageUI
    {
        private enum FilterState { All, Learned, NotLearned }
        private FilterState currentFilter = FilterState.All;

        private string searchText = string.Empty;
        private BlueMageSpell? selectedSpell;

        private readonly List<BlueMageSpell> spells;
        private readonly HashSet<int> learnedSpells = new();

        // --- ADDED: Fields for game services ---
        private readonly IGameGui gameGui;
        private readonly Dictionary<string, (uint TerritoryTypeID, uint MapID)> zoneNameToIds;

        private readonly Action<string> switchToSearchTab;

        public BlueMageUI(IGameGui gameGui, Dictionary<string, (uint TerritoryTypeID, uint MapID)> zoneNameToIds, Action<string> switchToSearchTab)
        {
            // --- ADDED: Store the passed-in services ---
            this.gameGui = gameGui;
            this.zoneNameToIds = zoneNameToIds;

            this.switchToSearchTab = switchToSearchTab;

            spells = new List<BlueMageSpell>();
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream("BeastieBuddy.BlueMageSpells.yaml");
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    var yamlContent = reader.ReadToEnd();

                    var deserializer = new DeserializerBuilder()
                        .WithNamingConvention(PascalCaseNamingConvention.Instance)
                        .Build();

                    spells = deserializer.Deserialize<List<BlueMageSpell>>(yamlContent);
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.Error(ex, "Failed to load or parse BlueMageSpells.yaml");
            }
        }

        public void Draw()
        {
            if (selectedSpell != null)
            {
                DrawDetailView();
            }
            else
            {
                DrawListView();
            }
        }

        private void DrawListView()
        {
            ImGui.InputTextWithHint("##bluSearch", "Search spells...", ref searchText, 100);

            if (ImGui.RadioButton("All", currentFilter == FilterState.All)) currentFilter = FilterState.All;
            ImGui.SameLine();
            if (ImGui.RadioButton("Learned", currentFilter == FilterState.Learned)) currentFilter = FilterState.Learned;
            ImGui.SameLine();
            if (ImGui.RadioButton("Not Learned", currentFilter == FilterState.NotLearned)) currentFilter = FilterState.NotLearned;

            ImGui.Separator();

            if (ImGui.BeginChild("##bluScrollingRegion"))
            {
                var filteredSpells = spells.Where(s =>
                {
                    bool searchMatch = string.IsNullOrWhiteSpace(searchText) || s.Name.ToLower().Contains(searchText.ToLower());
                    if (!searchMatch) return false;

                    return currentFilter switch
                    {
                        FilterState.Learned => learnedSpells.Contains(s.Number),
                        FilterState.NotLearned => !learnedSpells.Contains(s.Number),
                        _ => true,
                    };
                });

                foreach (var spell in filteredSpells)
                {
                    bool isLearned = learnedSpells.Contains(spell.Number);
                    if (ImGui.Checkbox($"##learned_{spell.Number}", ref isLearned))
                    {
                        if (isLearned)
                            learnedSpells.Add(spell.Number);
                        else
                            learnedSpells.Remove(spell.Number);
                    }

                    ImGui.SameLine();
                    if (ImGui.Selectable($"{spell.Number}. {spell.Name}"))
                    {
                        selectedSpell = spell;
                    }
                }
                ImGui.EndChild();
            }
        }

        private void DrawDetailView()
        {
            if (ImGui.Button("← Back to Spell List"))
            {
                selectedSpell = null;
                return;
            }

            ImGui.Separator();
            ImGui.Text($"{selectedSpell!.Number}. {selectedSpell.Name}");
            ImGui.TextWrapped(selectedSpell.Description);
            ImGui.Separator();

            foreach (var source in selectedSpell.Sources)
            {
                ImGui.Text(source.Type);
                ImGui.BulletText($"Source: {source.Name}");
                ImGui.BulletText($"Location: {source.Location}");

                if (source.Type == "Open World")
                {
                    if (source.X != 0 || source.Y != 0)
                    {
                        ImGui.BulletText($"Coordinates: ({source.X:F1}, {source.Y:F1})");
                        if (ImGui.Button($"Show on Map##{source.Name}{source.Location}"))
                        {
                            if (zoneNameToIds.TryGetValue(source.Location, out var ids))
                            {
                                var mapLink = new MapLinkPayload(ids.TerritoryTypeID, ids.MapID, source.X, source.Y);
                                gameGui.OpenMapWithMapLink(mapLink);
                            }
                        }
                    }
                    else
                    {
                        // --- UPDATED: Button now calls the action to switch tabs ---
                        if (ImGui.Button($"Find Spawn Points##{source.Name}{source.Location}"))
                        {
                            switchToSearchTab(source.Name);
                        }
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.SetTooltip("Switches to the Beastie Search tab to find possible spawn locations.");
                        }
                    }
                }
                else if (source.Type == "Dungeon" || source.Type == "Trial" || source.Type == "Raid")
                {
                    if (ImGui.Button($"Link in Chat##{source.Name}{source.Location}"))
                    {
                        var chatMessage = new XivChatEntry
                        {
                            Message = new SeStringBuilder()
                                .Append($"[BeastieBuddy] Blue Mage spell '{selectedSpell.Name}' is learned from '{source.Name}' in '{source.Location}'.")
                                .Build()
                        };
                        Plugin.ChatGui.Print(chatMessage);
                    }
                }
                ImGui.Spacing();
            }
        }
    }
}

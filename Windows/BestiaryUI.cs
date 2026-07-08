using BeastieBuddy.Data;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace BeastieBuddy.Windows
{
    public class BestiaryUI : IDisposable
    {
        private readonly Action<string> switchToSearchTab;
        private readonly BestiaryManager bestiaryManager;
        private readonly Configuration configuration;
        private readonly ITextureProvider textureProvider;
        private readonly Dictionary<string, Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap> iconCache = new();

        private string filterText = string.Empty;
        private int currentElementIndex = 0;
        private readonly string[] elements = { "All", "Earth", "Fire", "Ice", "Lightning", "Water", "Wind" };

        private KeyValuePair<int, BeastData>? selectedBeast = null;
        private int currentPage = 0;
        private const int itemsPerPage = 25;

        // placeholder ids from profile stickers. will update post patch 
        private readonly uint[] validIconIds = 
        {
            234401, 234402, 234403, 234404, 234405,
            234412, 234413, 234414, 234415, 234416, 234417,
            234419, 234420, 234421, 234422,
            234424,
            234429, 234430, 234431, 234432, 234433, 234434, 234435, 234436, 234437,
            234439,
            234441, 234442, 234443
        };

        public BestiaryUI(Action<string> switchToSearchTab, BestiaryManager bestiaryManager, Configuration configuration, ITextureProvider textureProvider)
        {
            this.switchToSearchTab = switchToSearchTab;
            this.bestiaryManager = bestiaryManager;
            this.configuration = configuration;
            this.textureProvider = textureProvider;
        }

        public void Draw()
        {
            if (!bestiaryManager.IsLoaded)
            {
                ImGui.Text("Loading Bestiary...");
                return;
            }

            using var bestiaryTable = ImRaii.Table("LayoutTestTable", 2, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.Resizable);
            if (!bestiaryTable) return;

            ImGui.TableSetupColumn("GridColumn", ImGuiTableColumnFlags.WidthStretch, 0.55f);
            ImGui.TableSetupColumn("DetailsColumn", ImGuiTableColumnFlags.WidthStretch, 0.45f);
            ImGui.TableNextRow();

            ImGui.TableNextColumn();

            ImGui.Text($"Progress: {configuration.TamedBeasts.Count} / 50 Tamed");

            float scale = ImGui.GetIO().FontGlobalScale;

            // temp layout, waiting on feedback
            int layoutSelection = configuration.UseCardLayout ? 1 : 0;
            if (ImGui.RadioButton("List", ref layoutSelection, 0))
            {
                configuration.UseCardLayout = false;
                configuration.Save();
            }
            ImGui.SameLine();
            if (ImGui.RadioButton("Cards", ref layoutSelection, 1))
            {
                configuration.UseCardLayout = true;
                configuration.Save();
            }

            ImGui.SetNextItemWidth(100 * scale);
            ImGui.InputTextWithHint("##filter", "Filter...", ref filterText, 100);
            ImGui.SameLine();
            ImGui.Text("Element:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100 * scale);
            ImGui.Combo("##elementFilter", ref currentElementIndex, elements, elements.Length);
            ImGui.Separator();

            var filteredBeasts = new List<KeyValuePair<int, BeastData>>();
            foreach (var kvp in bestiaryManager.Data.Beasts)
            {
                var beast = kvp.Value;
                string filterLower = filterText.ToLower();
                if (!string.IsNullOrEmpty(filterLower) && !beast.Name.ToLower().Contains(filterLower))
                    continue;

                string selectedElement = elements[currentElementIndex];
                if (selectedElement != "All" && !string.Equals(beast.AutoAttackElement, selectedElement, StringComparison.OrdinalIgnoreCase))
                    continue;

                filteredBeasts.Add(kvp);
            }

            if (selectedBeast != null && !filteredBeasts.Contains(selectedBeast.Value))
            {
                selectedBeast = null;
            }

            if (!configuration.UseCardLayout)
            {
                using (var listChild = ImRaii.Child("BestiaryList"))
                {
                    if (listChild)
                    {
                        foreach (var kvp in filteredBeasts)
                        {
                            var id = kvp.Key;
                            var beast = kvp.Value;

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
                            Vector4 elementColor = beast.AutoAttackElement.ToLower() switch
                            {
                                "fire" => new Vector4(1.00f, 0.40f, 0.40f, 1.0f),
                                "ice" => new Vector4(0.55f, 0.85f, 1.00f, 1.0f),
                                "wind" => new Vector4(0.60f, 1.00f, 0.60f, 1.0f),
                                "earth" => new Vector4(0.50f, 0.32f, 0.12f, 1.0f),
                                "lightning" => new Vector4(1.00f, 1.00f, 0.80f, 1.0f),
                                "water" => new Vector4(0.10f, 0.25f, 0.70f, 1.0f),
                                _ => new Vector4(0.7f, 0.7f, 0.7f, 1.0f)
                            };
                            ImGui.TextColored(elementColor, $"[{beast.AutoAttackElement}]");
                        }
                    }
                }
            }
            else
            {
                Vector2 iconSize = new Vector2(40 * scale, 40 * scale);
                int totalItems = filteredBeasts.Count;
                int totalPages = totalItems > 0 ? (int)Math.Ceiling((double)totalItems / itemsPerPage) : 1;
                if (currentPage >= totalPages) currentPage = Math.Max(0, totalPages - 1);
                int startIndex = currentPage * itemsPerPage;
                int endIndex = Math.Min(startIndex + itemsPerPage, totalItems);

                using (var gridChild = ImRaii.Child("GridArea", new Vector2(0, -ImGui.GetFrameHeightWithSpacing() * 2)))
                {
                    if (gridChild)
                    {
                        using var gridTable = ImRaii.Table("Grid", 5);
                        if (gridTable)
                        {
                            for (int i = startIndex; i < endIndex; i++)
                            {
                                ImGui.TableNextColumn();

                                var kvp = filteredBeasts[i];
                                var id = kvp.Key;
                                uint iconId = validIconIds[(id - 1) % validIconIds.Length];

                                var iconWrap = textureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrDefault();
                                bool isTamed = configuration.TamedBeasts.Contains(id);

                                if (!isTamed)
                                {
                                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.2f, 0.2f, 1.0f));
                                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.3f, 0.3f, 1.0f));
                                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.2f, 0.2f, 0.2f, 1.0f));
                                }

                                if (iconWrap != null)
                                {
                                    ImGui.PushID($"btn_{id}");
                                    Vector4 tintColor = isTamed ? new Vector4(1.0f, 1.0f, 1.0f, 1.0f) : new Vector4(0.3f, 0.3f, 0.3f, 1.0f);

                                    if (ImGui.ImageButton(iconWrap.Handle, iconSize, Vector2.Zero, Vector2.One, Vector4.Zero, tintColor))
                                    {
                                        selectedBeast = kvp;
                                    }
                                    ImGui.PopID();
                                }
                                else
                                {
                                    if (ImGui.Button($"?##{id}", iconSize))
                                    {
                                        selectedBeast = kvp;
                                    }
                                }

                                if (!isTamed)
                                {
                                    ImGui.PopStyleColor(3);
                                }

                                if (ImGui.Checkbox($"{id:D2}##chk_{id}", ref isTamed))
                                {
                                    if (isTamed) configuration.TamedBeasts.Add(id);
                                    else configuration.TamedBeasts.Remove(id);
                                    configuration.Save();
                                }
                            }
                        }
                    }

                    ImGui.SetCursorPosX((ImGui.GetColumnWidth() - ImGui.CalcTextSize($"< Page {currentPage + 1} / {totalPages} >").X) * 0.5f);
                    if (ImGui.ArrowButton("prevPage", ImGuiDir.Left) && currentPage > 0)
                    {
                        currentPage--;
                        selectedBeast = null;
                    }
                    ImGui.SameLine();
                    ImGui.Text($"Page {currentPage + 1} / {totalPages}");
                    ImGui.SameLine();
                    if (ImGui.ArrowButton("nextPage", ImGuiDir.Right) && currentPage < totalPages - 1)
                    {
                        currentPage++;
                        selectedBeast = null;
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

                            ImGui.Text($"No. {selectedBeast.Value.Key:D2} {beast.Name}");
                            ImGui.Separator();
                            ImGui.Text($"Location: {beast.Location}");
                            ImGui.Text($"Auto-Attack: {beast.AutoAttackElement}");

                            ImGui.Spacing();
                            if (ImGui.Button("Find Spawn Locations"))
                            {
                                switchToSearchTab(beast.Name);
                            }

                            ImGui.Spacing();
                            ImGui.Text("Abilities:");

                            string trickElement = string.Empty;
                            foreach (var el in elements)
                            {
                                if (el != "All" && beast.Trick.Element.Contains(el, StringComparison.OrdinalIgnoreCase))
                                {
                                    trickElement = el.ToLower();
                                    break;
                                }
                            }

                            if (!string.IsNullOrEmpty(trickElement))
                            {
                                var trickIcon = GetTextureWrap($"trick_{trickElement}");
                                if (trickIcon != null)
                                {
                                    ImGui.Image(trickIcon.Handle, new Vector2(16 * scale, 16 * scale));
                                    ImGui.SameLine();
                                }
                            }
                            ImGui.Text($"[Trick] {beast.Trick.Name}");
                            if (!string.IsNullOrEmpty(beast.Trick.Effect) && beast.Trick.Effect != "Unknown")
                                ImGui.TextWrapped($"  > {beast.Trick.Effect}");

                            var trIcon = GetTextureWrap("temperedrelease");
                            if (trIcon != null)
                            {
                                ImGui.Image(trIcon.Handle, new Vector2(16 * scale, 16 * scale));
                                ImGui.SameLine();
                            }
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
        private Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap? GetTextureWrap(string fileName)
        {
            if (iconCache.TryGetValue(fileName, out var texture))
                return texture;

            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var resourceName = $"BeastieBuddy.Data.{fileName}.PNG";

            try
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    using var memoryStream = new System.IO.MemoryStream();
                    stream.CopyTo(memoryStream);
                    var newTexture = textureProvider.CreateFromImageAsync(memoryStream.ToArray()).Result;
                    iconCache[fileName] = newTexture;
                    return newTexture;
                }
            }
            catch { }

            return null;
        }

        public void Dispose()
        {
            foreach (var texture in iconCache.Values)
            {
                texture?.Dispose();
            }
            iconCache.Clear();
        }
    }
}

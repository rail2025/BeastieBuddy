using BeastieBuddy.Data;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace BeastieBuddy.Windows
{
    public class BestiaryUIV2 : IDisposable
    {
        private readonly Action<string> switchToSearchTab;
        private readonly BestiaryManager bestiaryManager;
        private readonly Configuration configuration;
        private readonly ITextureProvider textureProvider;

        private readonly Dictionary<string, IDalamudTextureWrap> iconCache = new();
        private enum FilterState { All, Captured, Uncaptured }
        private FilterState currentFilter = FilterState.All;
        private string filterText = string.Empty;
        private readonly string[] filterElements = { "Fire", "Ice", "Wind", "Earth", "Lightning", "Water", "Slashing", "Blunt", "Piercing" };
        private readonly string[] filterClassifications = { "Beastkin", "Vilekin", "Cloudkin", "Seedkin", "Wavekin", "Scalekin", "Soulkin", "Ashkin" };
        private readonly string[] filterStatus = { "Slow", "Paralyze", "Silence", "Interrupt", "Blind", "Knockdown", "Sleep", "Bind", "Heavy", "Doom", "Death", "Poison", "Paralysis" }; 
        private readonly HashSet<string> activeFilters = new();
        private readonly HashSet<string> statusEffectKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "Sleep", "Paralysis", "Paralyze", "Poison", "Blind", "Silence",
            "Bind", "Heavy", "Slow", "Doom", "Death", "Interrupt", "Knockdown"
        };
        private int captureFilterIndex = 0;
        private KeyValuePair<int, BeastData>? selectedBeast;
        private int currentPage;
        private const int itemsPerPage = 20;
        private readonly Dictionary<int, float> hoverScale = new();
        private float capturePulse;
        private bool detailTransitionActive;
        private Vector2 transitionStart;
        private Vector2 transitionEnd;
        private uint transitionIconId;
        private float transitionProgress;

        private readonly uint[] validIconIds = {
            234401, 234402, 234403, 234404, 234405, 234412, 234413, 234414, 234415, 234416,
            234417, 234419, 234420, 234421, 234422, 234424, 234429, 234430, 234431, 234432,
            234433, 234434, 234435, 234436, 234437, 234439, 234441, 234442, 234443
        };

        public BestiaryUIV2(Action<string> switchToSearchTab, BestiaryManager bestiaryManager, Configuration configuration, ITextureProvider textureProvider)
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
                ImGui.Text("Loading Beastmaster Journal...");
                return;
            }
            float delta = ImGui.GetIO().DeltaTime;
            capturePulse = Math.Max(0, capturePulse - delta);

            DrawHeader();
            using var table = ImRaii.Table("BestiaryV2Table", 2, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.Resizable);
            if (!table) return;

            ImGui.TableSetupColumn("Collection", ImGuiTableColumnFlags.WidthStretch, 0.55f);
            ImGui.TableSetupColumn("Details", ImGuiTableColumnFlags.WidthStretch, 0.45f);
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            DrawFilters();
            ImGui.Separator();

            if (configuration.UseCardLayout)
            {DrawCards();}
            else
            { DrawList();}
            ImGui.TableNextColumn();
            DrawDetails();
            DrawTransitionAnimation();
        }

        private void DrawHeader()
        {
            ImGui.TextColored(new Vector4(1f, 0.85f, 0.3f, 1f), "BEASTMASTER JOURNAL");
            ImGui.Spacing();

            float progress = configuration.TamedBeasts.Count / 50f;
            ImGui.ProgressBar(progress, new Vector2(-1, 18), $"{configuration.TamedBeasts.Count}/50 Captured");

            string rank = progress switch
            {
                < 0.25f => "Novice Tracker",
                < 0.50f => "Experienced Hunter",
                < 0.75f => "Master Beastmaster",
                _ => "Living Bestiary"
            };

            ImGui.TextColored(new Vector4(0.6f, 0.9f, 1f, 1f), rank);
            ImGui.Separator();
        }

        private void DrawFilters()
        {
            float scale = ImGui.GetIO().FontGlobalScale;

            ImGui.RadioButton("All", ref captureFilterIndex, 0);
            ImGui.SameLine();
            ImGui.RadioButton("Captured", ref captureFilterIndex, 1);
            ImGui.SameLine();
            ImGui.RadioButton("Uncaptured", ref captureFilterIndex, 2);

            ImGui.SameLine();
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

            ImGui.SetNextItemWidth(150 * scale);
            ImGui.InputTextWithHint("##beastFilter", "Search beasts...", ref filterText, 100);

            ImGui.SameLine();
            if (ImGui.Button(activeFilters.Count > 0 ? $"Filters ({activeFilters.Count})###filterBtn" : "Filters...###filterBtn"))
            {
                ImGui.OpenPopup("FilterPopup");
            }

            if (ImGui.BeginPopup("FilterPopup"))
            {
                if (ImGui.BeginTable("FilterTable", 3, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingFixedFit))
                {
                    ImGui.TableSetupColumn("Elements");
                    ImGui.TableSetupColumn("Classifications");
                    ImGui.TableSetupColumn("Status");
                    ImGui.TableHeadersRow();

                    ImGui.TableNextRow();

                    ImGui.TableNextColumn();
                    foreach (var opt in filterElements)
                    {
                        bool isSel = activeFilters.Contains(opt);
                        if (ImGui.Checkbox(opt, ref isSel)) { if (isSel) activeFilters.Add(opt); else activeFilters.Remove(opt); }
                    }

                    ImGui.TableNextColumn();
                    foreach (var opt in filterClassifications)
                    {
                        bool isSel = activeFilters.Contains(opt);
                        if (ImGui.Checkbox(opt, ref isSel)) { if (isSel) activeFilters.Add(opt); else activeFilters.Remove(opt); }
                    }

                    ImGui.TableNextColumn();
                    foreach (var opt in filterStatus)
                    {
                        bool isSel = activeFilters.Contains(opt);
                        if (ImGui.Checkbox(opt, ref isSel)) { if (isSel) activeFilters.Add(opt); else activeFilters.Remove(opt); }
                    }
                    ImGui.EndTable();
                }

                ImGui.Separator();
                if (ImGui.Button("Reset All")) activeFilters.Clear();
                ImGui.SameLine();
                if (ImGui.Button("Apply/Close")) ImGui.CloseCurrentPopup();

                ImGui.EndPopup();
            }
        }


        private List<KeyValuePair<int, BeastData>> GetFilteredBeasts()
        {
            var result = new List<KeyValuePair<int, BeastData>>();
            string filter = filterText.ToLowerInvariant();
            foreach (var kvp in bestiaryManager.Data.Beasts)
            {
                var beast = kvp.Value;
                if (!string.IsNullOrEmpty(filter) && !beast.Name.ToLowerInvariant().Contains(filter))
                    continue;

                if (activeFilters.Count > 0)
                {
                    bool matchFound = false;
                    foreach (var f in activeFilters)
                    {
                        if (string.Equals(beast.AutoAttackElement, f, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(beast.Classification, f, StringComparison.OrdinalIgnoreCase))
                        {
                            matchFound = true;
                            break;
                        }

                        string fLower = f.ToLowerInvariant();
                        if ((beast.Trick.Effect != null && beast.Trick.Effect.ToLowerInvariant().Contains(fLower)) ||
                            (beast.TemperedRelease.Effect != null && beast.TemperedRelease.Effect.ToLowerInvariant().Contains(fLower)) ||
                            (beast.Borrow.Effect != null && beast.Borrow.Effect.ToLowerInvariant().Contains(fLower)) ||
                            (beast.PartingBlow.Effect != null && beast.PartingBlow.Effect.ToLowerInvariant().Contains(fLower)))
                        {
                            matchFound = true;
                            break;
                        }
                    }
                    if (!matchFound) continue;
                }
                bool isCaptured = configuration.TamedBeasts.Contains(kvp.Key);
                if (captureFilterIndex == 1 && !isCaptured) continue;
                if (captureFilterIndex == 2 && isCaptured) continue;
                result.Add(kvp);
            }
            return result;
        }

        private void DrawList()
        {
            var beasts = GetFilteredBeasts();
            using var child = ImRaii.Child("BestiaryList", new Vector2(0, 0), true);
            if (!child) return;
            foreach (var kvp in beasts)
            {
                var id = kvp.Key;
                var beast = kvp.Value;
                bool isTamed = configuration.TamedBeasts.Contains(id);

                using var row = ImRaii.Table($"##row{id}", 4, ImGuiTableFlags.SizingFixedFit);
                if (!row) continue;
                ImGui.TableSetupColumn("Check", ImGuiTableColumnFlags.WidthFixed, 24);
                ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Classification", ImGuiTableColumnFlags.WidthFixed, 80);
                ImGui.TableSetupColumn("Element", ImGuiTableColumnFlags.WidthFixed, 80);
                ImGui.TableSetupColumn("Teleport", ImGuiTableColumnFlags.WidthFixed, 24);
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                if (ImGui.Checkbox($"##tamed_{id}", ref isTamed))
                {
                    if (isTamed) configuration.TamedBeasts.Add(id); else configuration.TamedBeasts.Remove(id);
                    configuration.Save();
                }

                ImGui.TableNextColumn();
                if (isTamed) ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.5f, 1f));
                bool isSelected = selectedBeast?.Key == id;
                if (ImGui.Selectable($"{id:D2}. {beast.Name}##sel_{id}", isSelected))selectedBeast = kvp;
                if (isTamed) ImGui.PopStyleColor();

                ImGui.TableNextColumn();
                ImGui.Text(beast.Classification);

                ImGui.TableNextColumn();
                ImGui.TextColored(GetElementColor(beast.AutoAttackElement), beast.AutoAttackElement);

                ImGui.TableNextColumn();
                var teleportIcon = textureProvider.GetFromGameIcon(new GameIconLookup(60453)).GetWrapOrDefault();
                if (teleportIcon != null)
                {
                    ImGui.PushID($"list_teleport_{id}");
                    if (ImGui.ImageButton(teleportIcon.Handle, new Vector2(16, 16)))
                    {
                        switchToSearchTab(beast.Name);
                        System.Threading.Tasks.Task.Run(async () =>
                        {
                            await Plugin.Instance.MainWindow.SearchAsync(beast.Name);
                            var topResult = System.Linq.Enumerable.FirstOrDefault(
                                Plugin.Instance.MainWindow.Results);
                            if (topResult != null)
                            {
                                Plugin.Instance.TeleportToMob(topResult.TerritoryTypeID, topResult.MapID, topResult.X, topResult.Y);
                            }
                        });
                    }
                    ImGui.PopID();
                }
            }
        }

        private void DrawCards()
        {
            var beasts = GetFilteredBeasts();

            int pages = Math.Max(1, (int)Math.Ceiling(beasts.Count / (double)itemsPerPage));

            if (currentPage >= pages)
                currentPage = pages - 1;

            int start = currentPage * itemsPerPage;
            int end = Math.Min(start + itemsPerPage, beasts.Count);

            using var child = ImRaii.Child("BestiaryCards", new Vector2(0, 0), true);
            if (!child)
                return;

            using (var grid = ImRaii.Table(
                "CardGrid",
                4,
                ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoSavedSettings))
            {
                if (grid)
                {
                    for (int i = 0; i < 4; i++)
                        ImGui.TableSetupColumn($"##{i}");

                    ImGui.TableNextRow();

                    for (int i = start; i < end; i++)
                    {
                        DrawCard(beasts[i]);
                    }
                }
            }

            ImGui.Spacing();

            ImGui.Text($"Page {currentPage + 1}/{pages}");

            ImGui.SameLine();

            if (ImGui.ArrowButton("##prev", ImGuiDir.Left) && currentPage > 0)
            {
                currentPage--;
                selectedBeast = null;
            }

            ImGui.SameLine();

            if (ImGui.ArrowButton("##next", ImGuiDir.Right) && currentPage < pages - 1)
            {
                currentPage++;
                selectedBeast = null;
            }
        }


        private void DrawCard(KeyValuePair<int, BeastData> entry)
        {
            ImGui.TableNextColumn();
            int id = entry.Key;
            BeastData beast = entry.Value;

            bool selected = selectedBeast?.Key == id;
            bool tamed = configuration.TamedBeasts.Contains(id);

            Vector2 cardSize = new Vector2(100, 125);
            Vector2 position = ImGui.GetCursorScreenPos();

            ImGui.InvisibleButton($"card_{id}", new Vector2(cardSize.X - 28, cardSize.Y));
            bool hovered = ImGui.IsItemHovered();

            if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                selectedBeast = entry;

                transitionStart = position;

                transitionEnd =
                    ImGui.GetWindowPos()
                    + new Vector2(
                        ImGui.GetWindowSize().X * 0.72f,
                        150);

                transitionIconId =
                    validIconIds[(id - 1) % validIconIds.Length];

                transitionProgress = 0f;
                detailTransitionActive = true;
            }

            DrawCardFrame(position, cardSize, GetElementColor(beast.AutoAttackElement), selected);

            ImGui.SetCursorScreenPos(position + new Vector2(8, 8));
            ImGui.Text($"#{id:D2}");

            ImGui.SetCursorScreenPos(position + new Vector2(72, 8));

            if (ImGui.SmallButton(tamed ? $"✓##tamed_{id}" : $"+##tamed_{id}"))
            {
                if (tamed)
                    configuration.TamedBeasts.Remove(id);
                else
                {
                    configuration.TamedBeasts.Add(id);
                    capturePulse = 1f;
                }

                configuration.Save();
            }

            ImGui.SetCursorScreenPos(position + new Vector2(cardSize.X - 26, 25));

            var teleportIcon = textureProvider.GetFromGameIcon(new GameIconLookup(60453)).GetWrapOrDefault();
            if (teleportIcon != null)
            {
                ImGui.PushID($"teleport_{id}");
                if (ImGui.ImageButton(teleportIcon.Handle, new Vector2(16, 16)))
                {
                    switchToSearchTab(beast.Name);

                    System.Threading.Tasks.Task.Run(async () =>
                    {
                        await Plugin.Instance.MainWindow.SearchAsync(beast.Name);
                        var topResult = System.Linq.Enumerable.FirstOrDefault(Plugin.Instance.MainWindow.Results);

                        if (topResult != null)
                        {
                            Plugin.Instance.TeleportToMob(topResult.TerritoryTypeID, topResult.MapID, topResult.X, topResult.Y);
                        }
                    });
                }
                ImGui.PopID();
            }

            ImGui.SetCursorScreenPos(position + new Vector2(26, 23));
            var icon = textureProvider.GetFromGameIcon(new GameIconLookup(validIconIds[(id - 1) % validIconIds.Length])).GetWrapOrDefault();

            if (icon != null)
            {
                ImGui.Image(icon.Handle, new Vector2(48, 48));
            }

            ImGui.SetCursorScreenPos(position + new Vector2(8, 75));
            ImGui.TextWrapped(beast.Name);
            ImGui.SetCursorScreenPos(position + new Vector2(8, 91));
            ImGui.Text(beast.Classification);
            ImGui.SetCursorScreenPos(position + new Vector2(8, 107));
            ImGui.TextColored(GetElementColor(beast.AutoAttackElement), beast.AutoAttackElement);
        }

        private void DrawCardFrame(Vector2 position, Vector2 size, Vector4 elementColor, bool selected)
        {
            var draw = ImGui.GetWindowDrawList();
            uint shadow = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.35f));

            draw.AddRectFilled(position + new Vector2(3, 4), position + size + new Vector2(3, 4), shadow, 10);

            Vector4 background = selected ? new Vector4(0.22f, 0.20f, 0.15f, 1f) : new Vector4(0.10f, 0.10f, 0.10f, 1f);
            draw.AddRectFilled(position, position + size, ImGui.ColorConvertFloat4ToU32(background), 10);

            float glow = selected ? 0.9f : 0.55f;
            draw.AddRect(position, position + size, ImGui.ColorConvertFloat4ToU32(new Vector4(elementColor.X, elementColor.Y, elementColor.Z, glow)), 10, ImDrawFlags.None, selected ? 3 : 1);
            if (selected)
            {
                DrawAnimatedBorder(position, position + size, elementColor);
            }
        }

        private void DrawDetails()
        {
            using var child = ImRaii.Child("BestiaryDetailsV2");
            if (!child) return;

            if (selectedBeast == null)
            {
                DrawEmptyDetails();
                return;
            }

            BeastData beast = selectedBeast.Value.Value;
            Vector4 elementColor = GetElementColor(beast.AutoAttackElement);

            ImGui.TextColored(elementColor, $"No. {selectedBeast.Value.Key:D2}");
            ImGui.SameLine();
            ImGui.Text(beast.Name);
            ImGui.Spacing();

            DrawLargeIcon(selectedBeast.Value.Key);
            ImGui.Spacing();

            DrawInfoBox("Habitat", $"📍 {beast.Location}", elementColor);
            DrawInfoBox("Element", beast.AutoAttackElement, elementColor);
            ImGui.Spacing();

            if (ImGui.Button("Find Spawn Locations", new Vector2(-1, 30)))
            {
                switchToSearchTab(beast.Name);
            }

            ImGui.Spacing();

            DrawAbilityCard("Trick", beast.Trick.Name, beast.Trick.Effect, elementColor, GetTextureWrap($"trick_{beast.AutoAttackElement.ToLower()}"));
            DrawAbilityCard("Tempered Release", beast.TemperedRelease.Name, beast.TemperedRelease.Effect, new Vector4(1f, 0.7f, 0.3f, 1f), GetTextureWrap("temperedrelease"));
            DrawAbilityCard("Borrow", beast.Borrow.Name, beast.Borrow.Effect, new Vector4(0.5f, 1f, 0.5f, 1f));
            DrawAbilityCard("Parting Blow", beast.PartingBlow.Name, beast.PartingBlow.Effect, new Vector4(1f, 0.5f, 0.5f, 1f));
        }

        private void DrawEmptyDetails()
        {
            ImGui.Spacing();
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(1f, 0.85f, 0.3f, 1f), "Beastmaster Journal");
            ImGui.Spacing();
            ImGui.TextWrapped("Select a creature card to view its habitat, abilities, and collection details.");
        }

        private void DrawLargeIcon(int id)
        {
            uint iconId = validIconIds[(id - 1) % validIconIds.Length];
            var icon = textureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrDefault();

            if (icon == null) return;

            Vector2 box = new Vector2(ImGui.GetContentRegionAvail().X, 150);
            Vector2 cursor = ImGui.GetCursorScreenPos();
            float pulse = (float)Math.Sin(ImGui.GetTime() * 2) * 5;
            Vector2 imageSize = new Vector2(120, 120);
            Vector2 imagePos = cursor + new Vector2((box.X - imageSize.X) / 2, (box.Y - imageSize.Y) / 2);
            uint tint = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.85f + (pulse * 0.03f)));

            ImGui.GetWindowDrawList().AddImage(icon.Handle, imagePos, imagePos + imageSize, Vector2.Zero, Vector2.One, tint);
            ImGui.Dummy(box);
        }

        private void DrawInfoBox(string title, string value, Vector4 color)
        {
            Vector2 position = ImGui.GetCursorScreenPos();
            Vector2 size = new Vector2(ImGui.GetContentRegionAvail().X - 16, 45);

            ImGui.GetWindowDrawList().AddRectFilled(position, position + size, ImGui.ColorConvertFloat4ToU32(new Vector4(0.08f, 0.08f, 0.08f, 0.9f)), 8);
            ImGui.GetWindowDrawList().AddRect(position, position + size, ImGui.ColorConvertFloat4ToU32(color), 8, ImDrawFlags.None, 1);

            ImGui.Dummy(new Vector2(0, 4));
            ImGui.TextColored(color, title);
            ImGui.SameLine();
            ImGui.Text(value);
            ImGui.Dummy(new Vector2(0, 5));
        }

        private void DrawAbilityCard(string title, string name, string effect, Vector4 color, IDalamudTextureWrap? icon = null)
        {
            ImGui.PushStyleColor(ImGuiCol.Border, color);
            ImGui.PushStyleColor(ImGuiCol.TableRowBg, new Vector4(0.07f, 0.07f, 0.07f, 1f));

            using var table = ImRaii.Table($"##table_{title}", 1, ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg);
            if (table)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();

                using (var headerTable = ImRaii.Table($"##header_{title}", 2))
                {
                    if (headerTable)
                    {
                        float nameWidth = ImGui.CalcTextSize(name).X + (ImGui.GetStyle().CellPadding.X * 2) + 15f;
                        ImGui.TableSetupColumn("Title", ImGuiTableColumnFlags.WidthStretch);
                        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed, nameWidth);
                        ImGui.TableNextRow();

                        ImGui.TableNextColumn();
                        if (icon != null)
                        {
                            ImGui.Image(icon.Handle, new Vector2(20, 20));
                            ImGui.SameLine();
                        }

                        ImGui.SetWindowFontScale(1.15f);
                        ImGui.TextColored(color, title);
                        
                        ImGui.TableNextColumn();
                        ImGui.Text(name);
                        ImGui.SetWindowFontScale(1.0f);
                    }
                }
                ImGui.Dummy(new Vector2(0, 4));
                ImGui.SetWindowFontScale(1.0f);

                if (!string.IsNullOrEmpty(effect) && effect != "Unknown")
                {
                    DrawColoredEffect(effect);
                }
            }

            ImGui.PopStyleColor(2);
            ImGui.Spacing();
        }
        private void DrawColoredEffect(string text)
        {
            string[] words = text.Split(' ');
            Vector4 keywordColor = new Vector4(0.45f, 0.85f, 1f, 1f);
            float wrapWidth = ImGui.GetContentRegionAvail().X;
            float currentWidth = 0;

            for (int i = 0; i < words.Length; i++)
            {
                string word = words[i];
                string checkWord = word.Trim(',', '.', '!', '?', ';', ':');
                float wordWidth = ImGui.CalcTextSize(word).X;

                if (i > 0)
                {
                    float spaceWidth = ImGui.CalcTextSize(" ").X;
                    if (currentWidth + spaceWidth + wordWidth > wrapWidth)
                    {
                        currentWidth = 0;
                    }
                    else
                    {
                        ImGui.SameLine(0, spaceWidth);
                        currentWidth += spaceWidth;
                    }
                }

                if (statusEffectKeywords.Contains(checkWord))
                {
                    ImGui.TextColored(keywordColor, word);
                }
                else
                {
                    ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), word);
                }

                currentWidth += wordWidth;
            }
        }

        private Vector4 GetElementColor(string element)
        {
            return element.ToLowerInvariant() switch
            {
                "fire" => new Vector4(1f, 0.35f, 0.25f, 1f),
                "ice" => new Vector4(0.55f, 0.85f, 1f, 1f),
                "wind" => new Vector4(0.45f, 1f, 0.55f, 1f),
                "earth" => new Vector4(0.65f, 0.45f, 0.2f, 1f),
                "water" => new Vector4(0.25f, 0.5f, 1f, 1f),
                "lightning" => new Vector4(1f, 0.9f, 0.35f, 1f),
                _ => new Vector4(0.8f, 0.8f, 0.8f, 1f)
            };
        }

        private float Lerp(float current, float target, float amount)
        {
            return current + (target - current) * Math.Clamp(amount, 0f, 1f);
        }

        private IDalamudTextureWrap? GetTextureWrap(string fileName)
        {
            if (iconCache.TryGetValue(fileName, out var existing))
                return existing;

            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            string resourceName = $"BeastieBuddy.Data.{fileName}.PNG";

            try
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null) return null;

                using var memoryStream = new System.IO.MemoryStream();
                stream.CopyTo(memoryStream);

                var texture = textureProvider.CreateFromImageAsync(memoryStream.ToArray()).Result;
                iconCache[fileName] = texture;

                return texture;
            }
            catch
            {
                return null;
            }
        }

        private void DrawTransitionAnimation()
        {
            if (!detailTransitionActive) return;
            transitionProgress += ImGui.GetIO().DeltaTime * 4f;
            if (transitionProgress >= 1f) 
            {
                detailTransitionActive = false;
                return;
            }
            float t = transitionProgress;
            t = t * t * (3f - 2f * t);
            Vector2 position = Vector2.Lerp(transitionStart,transitionEnd, t);
            float size = 64 + (40 * t);
            var icon = textureProvider.GetFromGameIcon(new GameIconLookup(transitionIconId)).GetWrapOrDefault();
            if (icon == null) return;

            ImGui.GetWindowDrawList().AddImage(icon.Handle,position, position + new Vector2(size, size), Vector2.Zero, Vector2.One, ImGui.ColorConvertFloat4ToU32(new Vector4(1f,1f,1f,1f - t)));
        }

        private void DrawCapturePulse()
        {
            if (capturePulse <= 0) return;

            Vector2 center = ImGui.GetWindowPos() + (ImGui.GetWindowSize() / 2);
            float radius = 30 + ((1f - capturePulse) * 50);

            ImGui.GetWindowDrawList().AddCircle(center, radius, ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.85f, 0.25f, capturePulse)), 64, 3);
        }

        private void DrawCollectionStats()
        {
            int captured = configuration.TamedBeasts.Count;
            int total = bestiaryManager.Data.Beasts.Count;

            ImGui.Text($"Collected: {captured}/{total}");
            var counts = new Dictionary<string, int>();

            foreach (var beast in bestiaryManager.Data.Beasts)
            {
                if (!counts.ContainsKey(beast.Value.AutoAttackElement))
                {
                    counts[beast.Value.AutoAttackElement] = 0;
                }

                if (configuration.TamedBeasts.Contains(beast.Key))
                {
                    counts[beast.Value.AutoAttackElement]++;
                }
            }

            foreach (var item in counts)
            {
                ImGui.TextColored(GetElementColor(item.Key), $"{item.Key}: {item.Value}");
            }
        }

        private void DrawAnimatedBorder(Vector2 min, Vector2 max, Vector4 color)
        {
            float pulse = ((float)Math.Sin(ImGui.GetTime() * 3) + 1f) / 2f;
            ImGui.GetWindowDrawList().AddRect(min, max, ImGui.ColorConvertFloat4ToU32(new Vector4(color.X, color.Y, color.Z, 0.4f + (pulse * 0.5f))), 10, ImDrawFlags.None, 2);
        }

        public void Refresh()
        {
            selectedBeast = null;
            currentPage = 0;
            filterText = string.Empty;
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

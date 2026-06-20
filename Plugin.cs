using BeastieBuddy.VfxSystem;
using BeastieBuddy.Windows;
using Dalamud.Game.ClientState;
using Dalamud.Game.Command;
using Dalamud.Game.Gui;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Dalamud.Plugin.Ipc;
using Lumina.Excel.Sheets;

namespace BeastieBuddy
{
    public sealed class Plugin : IDalamudPlugin
    {
        public static Plugin Instance { get; private set; } = null!;
        public string Name => "BeastieBuddy";
        private const string CommandName = "/bbuddy";

        [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
        [PluginService] public static ICommandManager CommandManager { get; private set; } = null!;
        [PluginService] public static IGameGui GameGui { get; private set; } = null!;
        [PluginService] public static ITextureProvider TextureProvider { get; private set; } = null!;
        [PluginService] public static IPluginLog Log { get; private set; } = null!;
        [PluginService] public static IChatGui ChatGui { get; private set; } = null!;
        [PluginService] public static IDataManager DataManager { get; private set; } = null!;
        [PluginService] public static IClientState ClientState { get; private set; } = null!;
        [PluginService] public static IGameInteropProvider GameInteropProvider { get; private set; } = null!;
        [PluginService] public static IFramework Framework { get; private set; } = null!;
        [PluginService] public static IObjectTable ObjectTable { get; private set; } = null!;

        public Configuration Configuration { get; init; }
        public WindowSystem WindowSystem = new("BeastieBuddy");

        public string AvfxFilePath { get; private set; }
        internal VfxSystem.VfxReplacer VfxReplacer { get; private set; }

        public BeaconController BeaconController { get; init; }

        private ICallGateSubscriber<uint, byte, bool>? _teleport;

        private ConfigWindow ConfigWindow { get; init; }
        private MainWindow MainWindow { get; init; }
        private AboutWindow AboutWindow { get; init; }

        public Plugin()
        {
            Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            Configuration.Initialize(PluginInterface);
            AvfxFilePath = CopyAvfxFiles();
            VfxReplacer = new VfxSystem.VfxReplacer(AvfxFilePath);
            BeaconController = new BeaconController(this);

            ConfigWindow = new ConfigWindow(this);
            MainWindow = new MainWindow(this, GameGui, TextureProvider, DataManager, BeaconController);
            AboutWindow = new AboutWindow(this);

            WindowSystem.AddWindow(ConfigWindow);
            WindowSystem.AddWindow(MainWindow);
            WindowSystem.AddWindow(AboutWindow);

            CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Opens the BeastieBuddy monster search window."
            });

            PluginInterface.UiBuilder.Draw += DrawUI;
            PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;
            PluginInterface.UiBuilder.OpenMainUi += ToggleMainUI;

            _teleport = PluginInterface.GetIpcSubscriber<uint, byte, bool>("Teleport");
        }

        public void TeleportToMob(uint territoryTypeId, uint mapId, float mobX, float mobY)
        {
            if (_teleport == null) return;
            try
            {
                var mapRow = DataManager.GetExcelSheet<Map>()?.GetRowOrDefault(mapId);
                var aetheryteSheet = DataManager.GetExcelSheet<Aetheryte>();
                var markerSheet = DataManager.GetSubrowExcelSheet<MapMarker>();

                if (mapRow == null || aetheryteSheet == null || markerSheet == null) return;

                var territoryAetherytes = aetheryteSheet
                    .Where(a => a.Territory.RowId == territoryTypeId && a.IsAetheryte)
                    .ToDictionary(a => a.RowId);

                if (!territoryAetherytes.Any()) return;

                var sizeFactor = mapRow.Value.SizeFactor;
                var offsetX = mapRow.Value.OffsetX;
                var offsetY = mapRow.Value.OffsetY;

                static float ToMapCoord(int raw, int scale, short offset)
                    => ((raw + offset) * 41.0f / 2048.0f / (scale / 100.0f)) + 1.0f;

                var best = markerSheet[mapRow.Value.MapMarkerRange]
                    .Where(m => m.DataType == 3 && territoryAetherytes.ContainsKey(m.DataKey.RowId))
                    .Select(m =>
                    {
                        var dx = ToMapCoord(m.X, sizeFactor, offsetX) - mobX;
                        var dy = ToMapCoord(m.Y, sizeFactor, offsetY) - mobY;
                        return new { Id = m.DataKey.RowId, DistSq = dx * dx + dy * dy };
                    })
                    .OrderBy(m => m.DistSq)
                    .FirstOrDefault();

                var aetheryteId = best != null ? best.Id : territoryAetherytes.Keys.First();
                _teleport.InvokeFunc(aetheryteId, 0);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[BeastieBuddy] Auto-teleport IPC call failed");
            }
        }

        public void Dispose()
        {
            WindowSystem.RemoveAllWindows();

            ConfigWindow.Dispose();
            MainWindow.Dispose();
            AboutWindow.Dispose();
            BeaconController.Dispose();
            VfxReplacer.Dispose();

            CommandManager.RemoveHandler(CommandName);
        }

        private string CopyAvfxFiles()
        {
            var configDir = PluginInterface.GetPluginConfigDirectory();
            var vfxDir = Path.Combine(configDir, "vfx");
            Directory.CreateDirectory(vfxDir);

            // Extract all known replacements
            foreach (var replacement in VfxSystem.BeaconController.Replacements.Values)
            {
                try
                {
                    var path = Path.Combine(vfxDir, replacement);
                    var assembly = Assembly.GetExecutingAssembly();
                    var resourceName = $"BeastieBuddy.vfx.{replacement}";

                    using var stream = assembly.GetManifestResourceStream(resourceName);
                    if (stream != null)
                    {
                        using var fileStream = File.Create(path);
                        stream.CopyTo(fileStream);
                    }
                }
                catch (System.Exception ex) { Log.Error(ex, "Failed to extract VFX"); }
            }
            return vfxDir;
        }
        private void OnCommand(string command, string args)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                MainWindow.IsOpen = !MainWindow.IsOpen;
            }
            else
            {
                MainWindow.IsOpen = true;
                MainWindow.SwitchToSearchTab(args.Trim());
            }
        }

        private void DrawUI()
        {
            WindowSystem.Draw();
        }

        public void ToggleConfigUI()
        {
            ConfigWindow.Toggle();
        }

        public void ToggleMainUI()
        {
            MainWindow.Toggle();
        }

        public void ToggleAboutUI()
        {
            AboutWindow.Toggle();
        }
    }
}

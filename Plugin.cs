using BeastieBuddy.Windows;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Game.Gui;
using BeastieBuddy.VfxSystem;
using Dalamud.Game.ClientState;
using System.IO;
using System.Reflection;

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

        public Configuration Configuration { get; init; }
        public WindowSystem WindowSystem = new("BeastieBuddy");

        public string AvfxFilePath { get; private set; }
        internal VfxSystem.VfxReplacer VfxReplacer { get; private set; }

        public BeaconController BeaconController { get; init; }

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
            MainWindow.IsOpen = true;
            if (!string.IsNullOrWhiteSpace(args))
            {
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

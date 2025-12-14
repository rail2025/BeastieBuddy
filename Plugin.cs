using BeastieBuddy.Windows;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Game.Gui;
using BeastieBuddy.VfxSystem;
using Dalamud.Game.ClientState;

namespace BeastieBuddy
{
    public sealed class Plugin : IDalamudPlugin
    {
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

        public BeaconController BeaconController { get; init; }

        private ConfigWindow ConfigWindow { get; init; }
        private MainWindow MainWindow { get; init; }
        private AboutWindow AboutWindow { get; init; }

        public Plugin()
        {
            Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            Configuration.Initialize(PluginInterface);
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

            CommandManager.RemoveHandler(CommandName);
        }

        private void OnCommand(string command, string args)
        {
            MainWindow.IsOpen = true;
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

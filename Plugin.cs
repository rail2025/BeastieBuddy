using BeastieBuddy.Windows;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace BeastieBuddy
{
    public sealed class Plugin : IDalamudPlugin
    {
        public string Name => "BeastieBuddy";
        private const string CommandName = "/bbuddy";

        [PluginService]
        internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
        [PluginService]
        internal static ICommandManager CommandManager { get; private set; } = null!;
        [PluginService]
        internal static IGameGui GameGui { get; private set; } = null!;
        [PluginService]
        internal static ITextureProvider TextureProvider { get; private set; } = null!;
        [PluginService]
        internal static IPluginLog Log { get; private set; } = null!;
        [PluginService]
        internal static IDataManager DataManager { get; private set; } = null!;

        public Configuration Configuration { get; init; }
        public WindowSystem WindowSystem = new("BeastieBuddy");

        private ConfigWindow ConfigWindow { get; init; }
        private MainWindow MainWindow { get; init; }
        private AboutWindow AboutWindow { get; init; }

        public Plugin()
        {
            Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

            ConfigWindow = new ConfigWindow(this);
            MainWindow = new MainWindow(this, GameGui, TextureProvider, DataManager);
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

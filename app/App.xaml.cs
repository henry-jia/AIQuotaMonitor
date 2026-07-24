using System;
using System.Windows;

namespace AIQuotaMonitor;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // 语言覆盖：--lang zh|en（缺省按 config.json 的 language 或跟随系统）
        string? langArg = null;
        int langIndex = Array.IndexOf(e.Args, "--lang");
        if (langIndex >= 0 && langIndex + 1 < e.Args.Length)
            langArg = e.Args[langIndex + 1];

        // 测试截图模式：AIQuotaMonitor.exe --test-shot out.png [--layout horizontal] [--lang en]
        int shotIndex = Array.IndexOf(e.Args, "--test-shot");
        if (shotIndex >= 0 && shotIndex + 1 < e.Args.Length)
        {
            string outPath = e.Args[shotIndex + 1];
            bool horizontal = false;
            int layoutIndex = Array.IndexOf(e.Args, "--layout");
            if (layoutIndex >= 0 && layoutIndex + 1 < e.Args.Length)
                horizontal = string.Equals(e.Args[layoutIndex + 1], "horizontal", StringComparison.OrdinalIgnoreCase);
            I18n.Initialize(langArg ?? I18n.LangAuto);
            try
            {
                TestShot.Run(outPath, horizontal);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(I18n.T("testshot_failed") + ex.Message, "AIQuotaMonitor",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            Shutdown();
            return;
        }

        // 设置窗口截图模式：AIQuotaMonitor.exe --test-settings-shot out.png [--lang en]
        int settingsShotIndex = Array.IndexOf(e.Args, "--test-settings-shot");
        if (settingsShotIndex >= 0 && settingsShotIndex + 1 < e.Args.Length)
        {
            string outPath = e.Args[settingsShotIndex + 1];
            I18n.Initialize(langArg ?? I18n.LangAuto);
            try
            {
                TestShot.RunSettings(outPath);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(I18n.T("testshot_failed") + ex.Message, "AIQuotaMonitor",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            Shutdown();
            return;
        }

        // 动画帧渲染模式：AIQuotaMonitor.exe --test-frames dir [--lang zh]（用于合成演示 GIF）
        int framesIndex = Array.IndexOf(e.Args, "--test-frames");
        if (framesIndex >= 0 && framesIndex + 1 < e.Args.Length)
        {
            string dir = e.Args[framesIndex + 1];
            I18n.Initialize(langArg ?? I18n.LangAuto);
            try
            {
                TestShot.RunFrames(dir);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(I18n.T("testshot_failed") + ex.Message, "AIQuotaMonitor",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            Shutdown();
            return;
        }

        var config = ConfigStore.Load();
        I18n.Initialize(langArg ?? config.Language);
        var window = new MainWindow(config);
        window.Show();
    }
}

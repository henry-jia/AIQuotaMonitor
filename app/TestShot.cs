using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AIQuotaMonitor;

/// <summary>
/// 测试截图模式：不初始化 WebView2，用内置示例数据渲染主窗口并截图。
/// 用法：AIQuotaMonitor.exe --test-shot out.png [--layout horizontal] [--lang en]
///       AIQuotaMonitor.exe --test-settings-shot out.png [--lang en]（输出 out_services.png / out_global.png 两张）
/// </summary>
public static class TestShot
{
    public static void Run(string outPath, bool horizontal)
    {
        var (config, results) = BuildSample(horizontal);
        var window = new MainWindow(config, testMode: true);
        window.LoadSnapshot(config, results);

        // 不显示窗口，直接对根元素做完整布局后渲染
        var content = (FrameworkElement)window.Content;
        Render(content, outPath);
    }

    /// <summary>设置窗口截图：示例配置实例化 SettingsWindow，分别渲染「服务」与「全局设置」两个 Tab。</summary>
    public static void RunSettings(string outPath)
    {
        var config = BuildSettingsSample();
        // 不点「测试抓取」就不会用到 engine；正常用法（MainWindow.OpenSettings）不受影响
        var window = new SettingsWindow(config, () => new ScrapeEngine());
        var content = (FrameworkElement)window.Content;

        window.MainTabs.SelectedIndex = 0;
        Render(content, InsertSuffix(outPath, "_services"), window.Width, window.Height);
        window.MainTabs.SelectedIndex = 1;
        Render(content, InsertSuffix(outPath, "_global"), window.Width, window.Height);
    }

    private static string InsertSuffix(string path, string suffix)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
        string name = Path.GetFileNameWithoutExtension(path) + suffix + Path.GetExtension(path);
        return Path.Combine(dir, name);
    }

    private static void Render(FrameworkElement content, string outPath, double? width = null, double? height = null)
    {
        // 不显示窗口，直接对根元素做完整布局后渲染
        content.Measure(new System.Windows.Size(width ?? double.PositiveInfinity, height ?? double.PositiveInfinity));
        var size = content.DesiredSize;
        content.Arrange(new Rect(0, 0, size.Width, size.Height));
        content.UpdateLayout();

        const double scale = 2.0; // 2x 输出，截图更清晰
        var bmp = new RenderTargetBitmap(
            (int)Math.Ceiling(size.Width * scale),
            (int)Math.Ceiling(size.Height * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bmp.Render(content);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        string fullPath = Path.GetFullPath(outPath);
        using (var fs = File.Create(fullPath))
            encoder.Save(fs);
    }

    /// <summary>动画帧渲染：数值随帧变化（越过基准线变色）、中段演示暂停置灰，用于合成演示 GIF。</summary>
    public static void RunFrames(string dir, int frameCount = 24)
    {
        Directory.CreateDirectory(dir);
        var (config, results) = BuildFramesSample();
        var resA = results[config.Services[0]];
        var window = new MainWindow(config, testMode: true);
        var content = (FrameworkElement)window.Content;

        for (int i = 0; i < frameCount; i++)
        {
            // 前 16 帧播放增长动画（缓出），之后定格
            double t = Math.Min(1, i / 15.0);
            double ease = 1 - Math.Pow(1 - t, 2);
            resA.Rules[0].Percent = 15 + 65 * ease;  // 5h 窗口（基准 40%）：15% → 80%，越过基准变色
            resA.Rules[1].Percent = 60 + 29 * ease;  // 7d 窗口（基准 ~88%）：60% → 89%，末尾超基准 + 倒计时变黄
            window.LoadSnapshot(config, results);

            // 中段帧演示「单服务暂停」：第二张卡置灰 + 已暂停徽标
            bool paused = i >= 14 && i <= 18;
            if (window.CardsPanel.Children.Count > 1 &&
                window.CardsPanel.Children[1] is ServiceCard cardB)
            {
                cardB.SetPaused(paused);
            }
            Render(content, Path.Combine(dir, $"frame_{i:D2}.png"));
        }
    }

    private static (AppConfig, Dictionary<ServiceConfig, ServiceScrapeResult>) BuildFramesSample()
    {
        var a = new ServiceConfig { Name = "星言 Pro", Url = "https://example.com/usage" };
        var b = new ServiceConfig { Name = "云悟 Team", Url = "https://example.com/panel" };
        var c = new ServiceConfig { Name = "智海 Max", Url = "https://example.com/console" };
        var config = new AppConfig { Services = new List<ServiceConfig> { a, b, c } };

        var results = new Dictionary<ServiceConfig, ServiceScrapeResult>
        {
            [a] = new ServiceScrapeResult
            {
                Service = a,
                Status = ScrapeStatus.Ok,
                Rules = new List<RuleResult>
                {
                    new() { Label = "5 小时用量", Percent = 15, Detail = "0.8 / 5 小时", ResetText = "3 小时后重置", ResetAt = DateTime.Now.AddHours(3) },
                    new() { Label = "7 天用量", Percent = 60, Detail = "4.2 / 7 天", ResetText = "20 小时后重置", ResetAt = DateTime.Now.AddHours(20) },
                },
                Subscription = new SubscriptionInfo { ExpireAt = DateTime.Now.AddDays(26), AutoRenew = true },
                SubscriptionFetchedAt = DateTimeOffset.Now,
            },
            [b] = new ServiceScrapeResult
            {
                Service = b,
                Status = ScrapeStatus.Ok,
                Rules = new List<RuleResult>
                {
                    new() { Label = "5 小时用量", Percent = 35, Detail = "1.8 / 5 小时", ResetText = "2 小时后重置", ResetAt = DateTime.Now.AddHours(2.5) },
                },
                Subscription = new SubscriptionInfo { ExpireAt = DateTime.Now.AddDays(9), AutoRenew = false },
                SubscriptionFetchedAt = DateTimeOffset.Now,
            },
            [c] = new ServiceScrapeResult
            {
                Service = c,
                Status = ScrapeStatus.Error,
                SuggestLogin = true,
                ErrorMessage = I18n.T("test_sample_error"),
            },
        };
        return (config, results);
    }

    private static AppConfig BuildSettingsSample()
    {
        var a = new ServiceConfig
        {
            Name = "星言 Pro",
            Url = "https://example.com/usage",
            ExtraWaitSeconds = 3,
            Rules = new List<QuotaRule>
            {
                new() { Label = "5 小时用量", Pattern = QuotaRule.DefaultPercentPattern },
                new() { Label = "7 天用量", Pattern = QuotaRule.DefaultPercentPattern },
            },
        };
        var b = new ServiceConfig { Name = "云悟 Team", Url = "https://example.com/panel" };
        return new AppConfig { Services = new List<ServiceConfig> { a, b } };
    }

    private static (AppConfig, Dictionary<ServiceConfig, ServiceScrapeResult>) BuildSample(bool horizontal)
    {
        var a = new ServiceConfig { Name = "星言 Pro", Url = "https://example.com/usage" };
        var b = new ServiceConfig { Name = "云悟 Team", Url = "https://example.com/panel" };
        var c = new ServiceConfig { Name = "智海 Max", Url = "https://example.com/console" };

        var config = new AppConfig
        {
            Layout = horizontal ? "horizontal" : "vertical",
            Services = new List<ServiceConfig> { a, b, c },
        };

        var results = new Dictionary<ServiceConfig, ServiceScrapeResult>
        {
            // 正常：三条配额覆盖三种状态色——42% 落后基准 = Normal，80% 超出基准 = Ahead，93% = Critical
            [a] = new ServiceScrapeResult
            {
                Service = a,
                Status = ScrapeStatus.Ok,
                Rules = new List<RuleResult>
                {
                    new() { Label = "5 小时用量", Percent = 42, Detail = "2.1 / 5 小时", ResetText = "2 小时后重置", ResetAt = DateTime.Now.AddHours(2) },
                    new() { Label = "7 天用量", Percent = 80, Detail = "5.6 / 7 天", ResetText = "20 小时后重置", ResetAt = DateTime.Now.AddHours(20) },
                    new() { Label = "30 天用量", Percent = 93, Detail = "27.9 / 30 天", ResetText = "20 天后重置", ResetAt = DateTime.Now.AddDays(20) },
                },
                Subscription = new SubscriptionInfo { ExpireAt = DateTime.Now.AddDays(31), AutoRenew = true },
                SubscriptionFetchedAt = DateTimeOffset.Now,
            },
            // 需要登录
            [b] = new ServiceScrapeResult { Service = b, Status = ScrapeStatus.NeedLogin },
            // 抓取失败
            [c] = new ServiceScrapeResult
            {
                Service = c,
                Status = ScrapeStatus.Error,
                SuggestLogin = true,
                ErrorMessage = I18n.T("test_sample_error"),
            },
        };
        return (config, results);
    }
}

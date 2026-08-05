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

    /// <summary>历史窗口截图：合成锯齿样本（24h 内两次重置 + 7 天单调爬升），纯离屏渲染不碰真实历史文件。</summary>
    public static void RunHistory(string outPath, string ruleLabel = "5 小时用量")
    {
        var svc = new ServiceConfig { Id = "sample-a", Name = "星言 Pro", Url = "https://example.com/usage" };
        var config = new AppConfig { Services = new List<ServiceConfig> { svc } };
        var now = DateTimeOffset.Now;
        var samples = new List<HistorySample>();

        // 5 小时用量：24h 每 15 分钟一点，每 5h 一周期（重置锯齿）
        for (int i = 0; i <= 96; i++)
        {
            var t = now.AddHours(-24 + i * 0.25);
            double phase = i * 0.25 % 5;
            double pct = phase / 5.0 * 78;
            samples.Add(new HistorySample
            {
                T = t,
                Svc = svc.Id!,
                Rule = "5 小时用量",
                RuleId = "sample-r5",
                Pct = pct,
                Detail = $"{pct / 100 * 5:0.#} / 5 小时",
                ResetAt = t.AddHours(5 - phase),
            });
        }
        // 7 天用量：单调爬升
        for (int i = 0; i <= 84; i++)
        {
            var t = now.AddHours(-7 * 24 + i * 2.0);
            double pct = 18 + i * 0.8;
            samples.Add(new HistorySample
            {
                T = t,
                Svc = svc.Id!,
                Rule = "7 天用量",
                RuleId = "sample-r7",
                Pct = pct,
                Detail = $"{pct / 100 * 7:0.#} / 7 天",
                ResetAt = now.AddHours(3 * 24),
            });
        }

        var window = new HistoryWindow(svc, ruleLabel, config, samples);
        var content = (FrameworkElement)window.Content;
        Render(content, outPath, window.Width, window.Height);
    }

    private static string InsertSuffix(string path, string suffix)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
        string name = Path.GetFileNameWithoutExtension(path) + suffix + Path.GetExtension(path);
        return Path.Combine(dir, name);
    }

    private static void Render(FrameworkElement content, string outPath, double? width = null, double? height = null)
    {
        // 不显示窗口，直接对根元素做完整布局后渲染。
        // 给了显式尺寸就按它 Arrange（Grid 的 * 行无固有高度，DesiredSize 会塌成只剩 Auto 行）
        content.Measure(new System.Windows.Size(width ?? double.PositiveInfinity, height ?? double.PositiveInfinity));
        var size = new System.Windows.Size(width ?? content.DesiredSize.Width, height ?? content.DesiredSize.Height);
        content.Arrange(new Rect(0, 0, size.Width, size.Height));
        content.UpdateLayout();

        const double scale = 2.0; // 2x 输出，截图更清晰
        var bmp = new RenderTargetBitmap(
            (int)Math.Ceiling(size.Width * scale),
            (int)Math.Ceiling(size.Height * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        // 先铺深色底再渲染内容：模拟运行时 Acrylic/Mica 之上的合成效果，避免 PNG 透明通道在看图器里发灰
        var baseLayer = new DrawingVisual();
        using (var dc = baseLayer.RenderOpen())
            dc.DrawRectangle(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#20202A")),
                null, new Rect(0, 0, size.Width, size.Height));
        bmp.Render(baseLayer);
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
        var a = new ServiceConfig { Id = "sample-a", Name = "星言 Pro", Url = "https://example.com/usage" };
        var b = new ServiceConfig { Id = "sample-b", Name = "云悟 Team", Url = "https://example.com/panel" };
        var c = new ServiceConfig { Id = "sample-c", Name = "智海 Max", Url = "https://example.com/console" };
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
            Id = "sample-a",
            Name = "星言 Pro",
            Url = "https://example.com/usage",
            ExtraWaitSeconds = 3,
            Rules = new List<QuotaRule>
            {
                new() { Label = "5 小时用量", Pattern = QuotaRule.DefaultPercentPattern },
                new() { Label = "7 天用量", Pattern = QuotaRule.DefaultPercentPattern },
            },
        };
        var b = new ServiceConfig { Id = "sample-b", Name = "云悟 Team", Url = "https://example.com/panel" };
        return new AppConfig { Services = new List<ServiceConfig> { a, b } };
    }

    private static (AppConfig, Dictionary<ServiceConfig, ServiceScrapeResult>) BuildSample(bool horizontal)
    {
        var a = new ServiceConfig { Id = "sample-a", Name = "星言 Pro", Url = "https://example.com/usage" };
        var b = new ServiceConfig { Id = "sample-b", Name = "云悟 Team", Url = "https://example.com/panel" };
        var c = new ServiceConfig { Id = "sample-c", Name = "智海 Max", Url = "https://example.com/console" };
        var d = new ServiceConfig { Id = "sample-d", Name = "澹月 Lite", Url = "https://example.com/lite" };
        var e = new ServiceConfig { Id = "sample-e", Name = "智海 Air", Url = "https://example.com/air" };

        var config = new AppConfig
        {
            Layout = horizontal ? "horizontal" : "vertical",
            Services = new List<ServiceConfig> { a, b, c, d, e },
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
                    new() { Label = "5 小时用量", Percent = 42, Detail = "2.1 / 5 小时", ResetText = "25 分钟后重置", ResetAt = DateTime.Now.AddMinutes(25) },
                    new() { Label = "7 天用量", Percent = 80, Detail = "5.6 / 7 天", ResetText = "20 小时后重置", ResetAt = DateTime.Now.AddHours(20) },
                    new() { Label = "30 天用量", Percent = 93, Detail = "27.9 / 30 天", ResetText = "20 天后重置", ResetAt = DateTime.Now.AddDays(20) },
                },
                Subscription = new SubscriptionInfo { ExpireAt = DateTime.Now.AddDays(4), AutoRenew = true },
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
            // 陈旧数据回退：刷新失败 → 琥珀警告行 + 上次数据
            [d] = new ServiceScrapeResult
            {
                Service = d,
                Status = ScrapeStatus.Ok,
                StaleError = I18n.T("test_sample_error"),
                Time = DateTimeOffset.Now.AddMinutes(-65),
                Rules = new List<RuleResult>
                {
                    new() { Label = "5 小时用量", Percent = 55, Detail = "2.7 / 5 小时", ResetText = "2 小时后重置", ResetAt = DateTime.Now.AddHours(2) },
                },
            },
            // 陈旧 + 疑似未登录 → 警告行 + 「去登录」按钮
            [e] = new ServiceScrapeResult
            {
                Service = e,
                Status = ScrapeStatus.Ok,
                StaleError = I18n.T("test_sample_error"),
                SuggestLogin = true,
                Time = DateTimeOffset.Now.AddMinutes(-65),
                Rules = new List<RuleResult>
                {
                    new() { Label = "7 天用量", Percent = 71, Detail = "5.0 / 7 天", ResetText = "4 天后重置", ResetAt = DateTime.Now.AddDays(4) },
                },
            },
        };
        return (config, results);
    }
}

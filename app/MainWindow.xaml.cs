using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace AIQuotaMonitor;

/// <summary>
/// 主窗口（桌面小部件）：无边框半透明圆角，可拖动、置顶可开关，
/// 定时顺序刷新各服务配额，托盘常驻。
/// </summary>
public partial class MainWindow : Window
{
    private readonly bool _testMode;
    private AppConfig _config;
    private ScrapeEngine? _engine;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly Dictionary<ServiceConfig, ServiceScrapeResult> _results = new();
    private readonly Dictionary<ServiceConfig, DateTime> _nextDue = new();
    private DispatcherTimer? _timer;
    private DispatcherTimer? _uiTimer;
    private WinForms.NotifyIcon? _tray;
    private WinForms.ToolStripMenuItem? _trayRefreshItem;
    private WinForms.ToolStripMenuItem? _trayPauseItem;
    private WinForms.ToolStripMenuItem? _trayTopmostItem;
    private WinForms.ToolStripMenuItem? _trayLanguageItem;
    private WinForms.ToolStripMenuItem? _trayLangAutoItem;
    private WinForms.ToolStripMenuItem? _trayLangZhItem;
    private WinForms.ToolStripMenuItem? _trayLangEnItem;
    private WinForms.ToolStripMenuItem? _traySettingsItem;
    private WinForms.ToolStripMenuItem? _trayToggleItem;
    private WinForms.ToolStripMenuItem? _trayExitItem;
    private bool _exiting;

    public MainWindow(AppConfig config, bool testMode = false)
    {
        _config = config;
        _testMode = testMode;
        InitializeComponent();
        I18n.Changed += OnI18nChanged;
        // 按住 Ctrl 时卡片服务名变链接样式（Ctrl+点击打开官方用量页面）
        PreviewKeyDown += (s, e) => UpdateCtrlHint();
        PreviewKeyUp += (s, e) => UpdateCtrlHint();
        ApplyTexts();
        UpdateLanguageChecks();

        if (!testMode)
        {
            if (!double.IsNaN(config.WindowLeft) && !double.IsNaN(config.WindowTop))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = config.WindowLeft;
                Top = config.WindowTop;
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            InitTray();
            Loaded += MainWindow_Loaded;
        }
        ApplyConfig();
    }

    private ScrapeEngine Engine => _engine ??= new ScrapeEngine();

    /// <summary>测试模式：直接注入配置与抓取结果快照。</summary>
    public void LoadSnapshot(AppConfig config, Dictionary<ServiceConfig, ServiceScrapeResult> results)
    {
        _config = config;
        _results.Clear();
        foreach (var kv in results) _results[kv.Key] = kv.Value;
        ApplyConfig();
    }

    private void ApplyConfig()
    {
        Topmost = _config.Topmost;
        Root.Background = new SolidColorBrush(Ui.ParseColor(_config.BackgroundColor,
            (Color)ColorConverter.ConvertFromString("#E814141B")));
        Root.Opacity = _config.Opacity;
        ApplyScale();
        MenuTopmost.IsChecked = _config.Topmost;
        if (_trayTopmostItem != null) _trayTopmostItem.Checked = _config.Topmost;
        MenuLayout.Header = _config.IsHorizontal ? I18n.T("layout_to_vertical") : I18n.T("layout_to_horizontal");
        CardsPanel.Orientation = _config.IsHorizontal ? Orientation.Horizontal : Orientation.Vertical;
        RebuildCards();
        RestartTimer();
    }

    /// <summary>界面缩放：LayoutTransform 整体缩放（含文字），窗口随内容自适应大小。</summary>
    private void ApplyScale()
    {
        double s = _config.UiScale;
        Root.LayoutTransform = Math.Abs(s - 1.0) < 0.001
            ? Transform.Identity
            : new ScaleTransform(s, s);
        ZoomResetButton.Content = $"{s:P0}";
    }

    private void Root_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        double step = e.Delta > 0 ? 0.05 : -0.05;
        double s = Math.Clamp(Math.Round((_config.UiScale + step) * 20) / 20, 0.6, 2.0);
        if (Math.Abs(s - _config.UiScale) < 0.001) return;
        _config.UiScale = s;
        ConfigStore.Save(_config);
        ApplyScale();
    }

    private void ZoomReset_Click(object sender, RoutedEventArgs e)
    {
        _config.UiScale = 1.0;
        ConfigStore.Save(_config);
        ApplyScale();
    }

    private void RebuildCards()
    {
        CardsPanel.Children.Clear();
        var services = _config.Services.Where(s => s.Enabled).ToList();
        if (services.Count == 0)
        {
            CardsPanel.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = Ui.Brush("#0DFFFFFF"),
                Padding = new Thickness(14, 12, 14, 12),
                Child = new TextBlock
                {
                    Text = I18n.T("no_services_hint"),
                    Foreground = Ui.Brush("#9A9AA5"),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                },
            });
            return;
        }
        foreach (var svc in services)
        {
            var card = new ServiceCard { Tag = svc };
            card.RequestLogin += OnRequestLogin;
            card.RequestRefresh += OnRequestRefresh;
            card.RequestPauseToggle += OnCardPauseToggle;
            card.SetPaused(svc.Paused);
            card.Margin = _config.IsHorizontal ? new Thickness(0, 0, 10, 0) : new Thickness(0, 0, 0, 10);
            if (_config.IsHorizontal) card.Width = 240;
            else card.MinWidth = 250;
            if (_results.TryGetValue(svc, out var res)) card.Bind(res, _config);
            else card.BindLoading(svc);
            CardsPanel.Children.Add(card);
        }
    }

    // ---------- 刷新 ----------

    private void RestartTimer()
    {
        if (_testMode) return;
        _timer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _timer.Tick -= Timer_Tick;
        _timer.Tick += Timer_Tick;
        _timer.Start();
        // 界面刷新：基准竖线与剩余时间文字只依赖「重置时间+当前时间」，
        // 每分钟用缓存结果重绘一次卡片，不触发任何网页抓取
        _uiTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _uiTimer.Tick -= UiTimer_Tick;
        _uiTimer.Tick += UiTimer_Tick;
        _uiTimer.Start();
    }

    private void UiTimer_Tick(object? sender, EventArgs e)
    {
        foreach (var child in CardsPanel.Children)
        {
            if (child is ServiceCard card && card.Tag is ServiceConfig svc &&
                _results.TryGetValue(svc, out var res))
            {
                card.Bind(res, _config);
            }
        }
    }

    /// <summary>按住/松开 Ctrl 时切换卡片服务名的链接样式。</summary>
    private void UpdateCtrlHint()
    {
        bool ctrl = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0;
        foreach (var child in CardsPanel.Children)
            if (child is ServiceCard card) card.SetCtrlHint(ctrl);
    }

    private async void Timer_Tick(object? sender, EventArgs e) => await RefreshDueAsync();

    private async Task RefreshAllAsync()
    {
        foreach (var svc in _config.Services.Where(s => s.Enabled))
            _nextDue[svc] = DateTime.MinValue;
        // 用户主动点的刷新必须生效：等待刷新锁，不允许被静默丢弃
        await RefreshDueAsync(waitForLock: true);
    }

    /// <summary>后台顺序刷新到期服务，全程不阻塞 UI（WebView2 调用本就在 UI 线程上排队）。
    /// waitForLock=true 时等待刷新锁（登录窗口关闭等场景不允许丢失本次刷新）。</summary>
    private async Task RefreshDueAsync(bool waitForLock = false)
    {
        if (_testMode) return;
        if (waitForLock) await _refreshLock.WaitAsync();
        else if (!await _refreshLock.WaitAsync(0)) return;
        SetBusy(true);
        try
        {
            // 全局暂停：不访问任何供应商（倒计时/基准线由 UiTimer 照常更新）
            if (_config.ScrapingPaused) return;
            foreach (var svc in _config.Services.Where(s => s.Enabled).ToList())
            {
                if (svc.Paused) continue; // 单服务暂停：跳过抓取
                if (_nextDue.TryGetValue(svc, out var due) && DateTime.Now < due) continue;
                _results.TryGetValue(svc, out var prev);
                // 订阅信息变化慢：SubscriptionUrl 为空时在用量页顺带扫（零成本，每次都取）；
                // 需跳转订阅页时按 6 小时缓存，避免每次刷新多一次导航
                bool fetchSub = string.IsNullOrWhiteSpace(svc.SubscriptionUrl)
                    || prev?.Subscription == null
                    || prev.SubscriptionFetchedAt < DateTimeOffset.Now.AddHours(-6);
                ServiceScrapeResult res;
                try
                {
                    res = await Engine.ScrapeAsync(svc, fetchSub);
                }
                catch (Exception ex)
                {
                    res = new ServiceScrapeResult
                    {
                        Service = svc,
                        Status = ScrapeStatus.Error,
                        ErrorMessage = ex.Message,
                    };
                }
                if (!fetchSub && prev?.Subscription != null)
                {
                    res.Subscription = prev.Subscription;
                    res.SubscriptionFetchedAt = prev.SubscriptionFetchedAt;
                }
                _results[svc] = res;
                _nextDue[svc] = DateTime.Now.AddMinutes(Math.Max(1, svc.RefreshIntervalMinutes ?? _config.RefreshIntervalMinutes));
                NotifyIfLoginNeeded(svc, prev, res);
                UpdateCard(svc, res);
            }
        }
        finally
        {
            SetBusy(false);
            _refreshLock.Release();
        }
    }

    /// <summary>刷新进行中：禁用各刷新按钮并显示「刷新中…」，给用户明确反馈。</summary>
    private bool _busy;

    private void SetBusy(bool busy)
    {
        _busy = busy;
        UpdateButtonStates();
        foreach (var child in CardsPanel.Children)
            if (child is ServiceCard c) c.SetBusy(busy);
    }

    /// <summary>刷新按钮与状态文字：暂停时显示提示并禁用刷新；刷新中显示「刷新中…」。</summary>
    private void UpdateButtonStates()
    {
        RefreshButton.IsEnabled = !_busy && !_config.ScrapingPaused;
        if (_config.ScrapingPaused)
        {
            BusyText.Text = I18n.T("scraping_paused_hint");
            BusyText.Visibility = Visibility.Visible;
        }
        else if (_busy)
        {
            BusyText.Text = I18n.T("refreshing");
            BusyText.Visibility = Visibility.Visible;
        }
        else
        {
            BusyText.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>全局暂停/继续切换：持久化，恢复时立即全量刷新。</summary>
    private void ToggleGlobalPause()
    {
        _config.ScrapingPaused = !_config.ScrapingPaused;
        ConfigStore.Save(_config);
        UpdatePauseVisuals();
        UpdateButtonStates();
        if (!_config.ScrapingPaused) _ = RefreshAllAsync();
    }

    /// <summary>暂停按钮图标与菜单勾选项随状态同步。</summary>
    private void UpdatePauseVisuals()
    {
        bool paused = _config.ScrapingPaused;
        PauseButton.Content = paused ? "▶" : "⏸";
        PauseButton.ToolTip = I18n.T(paused ? "resume_scraping" : "pause_scraping");
        MenuPause.IsChecked = paused;
        if (_trayPauseItem != null) _trayPauseItem.Checked = paused;
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e) => ToggleGlobalPause();

    private void MenuPause_Click(object sender, RoutedEventArgs e) => ToggleGlobalPause();

    /// <summary>卡片级暂停切换：持久化，恢复时立即刷新该服务。</summary>
    private void OnCardPauseToggle(ServiceConfig svc)
    {
        svc.Paused = !svc.Paused;
        ConfigStore.Save(_config);
        foreach (var child in CardsPanel.Children)
            if (child is ServiceCard card && ReferenceEquals(card.Tag, svc))
                card.SetPaused(svc.Paused);
        if (!svc.Paused)
        {
            _nextDue[svc] = DateTime.MinValue;
            _ = RefreshDueAsync(waitForLock: true);
        }
    }

    /// <summary>服务从正常变为「需要登录」时弹托盘气泡提醒（如阿里云会话级票据失效）。</summary>
    private void NotifyIfLoginNeeded(ServiceConfig svc, ServiceScrapeResult? prev, ServiceScrapeResult res)
    {
        static bool NeedsLogin(ServiceScrapeResult r) =>
            r.Status == ScrapeStatus.NeedLogin || (r.Status == ScrapeStatus.Error && r.SuggestLogin);
        if (!NeedsLogin(res) || (prev != null && NeedsLogin(prev)) || _tray == null) return;
        try
        {
            _tray.ShowBalloonTip(6000, I18n.T("app_title"),
                I18n.T("login_expired_balloon", svc.Name),
                WinForms.ToolTipIcon.Warning);
        }
        catch { /* 气泡失败不影响主流程 */ }
    }

    private void UpdateCard(ServiceConfig svc, ServiceScrapeResult res)
    {
        foreach (var child in CardsPanel.Children)
        {
            if (child is ServiceCard card && ReferenceEquals(card.Tag, svc))
            {
                card.Bind(res, _config);
                break;
            }
        }
    }

    // ---------- 登录 ----------

    /// <summary>卡片级单独刷新：立即到期并入队，等待刷新锁执行（点击必生效）。</summary>
    private void OnRequestRefresh(ServiceConfig svc)
    {
        _nextDue[svc] = DateTime.MinValue;
        _ = RefreshDueAsync(waitForLock: true);
    }

    private async void OnRequestLogin(ServiceConfig svc)
    {
        try
        {
            await Engine.EnsureInitializedAsync();
            var login = new LoginWindow(Engine.Env!, svc.Url, svc.Name) { Owner = this };
            login.Closed += async (s, e) =>
            {
                _nextDue[svc] = DateTime.MinValue;
                await RefreshDueAsync(waitForLock: true);
            };
            login.Show();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, I18n.T("open_login_failed") + ex.Message, "AIQuotaMonitor",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---------- 窗口行为 ----------

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 按住任意空白处拖动（按钮除外），拖动结束自动保存位置
        if (e.LeftButton == MouseButtonState.Pressed && !IsOnButton(e.OriginalSource as DependencyObject))
        {
            DragMove();
            SavePosition();
        }
    }

    private static bool IsOnButton(DependencyObject? d)
    {
        while (d != null)
        {
            if (d is System.Windows.Controls.Primitives.ButtonBase) return true;
            d = VisualTreeHelper.GetParent(d) ?? (d as FrameworkElement)?.Parent;
        }
        return false;
    }

    private void SavePosition()
    {
        if (_testMode || !IsLoaded || double.IsNaN(Left) || double.IsNaN(Top)) return;
        _config.WindowLeft = Left;
        _config.WindowTop = Top;
        ConfigStore.Save(_config);
    }

    private void ToggleVisibility()
    {
        if (IsVisible)
        {
            Hide();
        }
        else
        {
            Show();
            Activate();
            // 重新应用置顶，把窗口带到最前
            bool top = _config.Topmost;
            Topmost = false;
            Topmost = top;
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_testMode && !_exiting)
        {
            // 关闭即隐藏，程序留在托盘
            e.Cancel = true;
            SavePosition();
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    private void ExitApp()
    {
        _exiting = true;
        SavePosition();
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        System.Windows.Application.Current.Shutdown();
    }

    // ---------- 启动 ----------

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            ScrapeEngine.CheckRuntime();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, I18n.T("missing_runtime_title"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        await RefreshAllAsync();
    }

    // ---------- 菜单与标题按钮 ----------

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => _ = RefreshAllAsync();

    private void HideButton_Click(object sender, RoutedEventArgs e)
    {
        SavePosition();
        Hide();
    }

    private void MenuRefresh_Click(object sender, RoutedEventArgs e) => _ = RefreshAllAsync();

    private void MenuTopmost_Click(object sender, RoutedEventArgs e)
    {
        _config.Topmost = MenuTopmost.IsChecked;
        Topmost = _config.Topmost;
        if (_trayTopmostItem != null) _trayTopmostItem.Checked = _config.Topmost;
        ConfigStore.Save(_config);
    }

    private void MenuLayout_Click(object sender, RoutedEventArgs e)
    {
        _config.Layout = _config.IsHorizontal ? "vertical" : "horizontal";
        ConfigStore.Save(_config);
        ApplyConfig();
    }

    private void MenuSettings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void MenuHide_Click(object sender, RoutedEventArgs e)
    {
        SavePosition();
        Hide();
    }

    private void MenuExit_Click(object sender, RoutedEventArgs e) => ExitApp();

    // ---------- 语言切换 ----------

    private void MenuLangAuto_Click(object sender, RoutedEventArgs e) => SetLanguage(I18n.LangAuto);
    private void MenuLangZh_Click(object sender, RoutedEventArgs e) => SetLanguage(I18n.LangZh);
    private void MenuLangEn_Click(object sender, RoutedEventArgs e) => SetLanguage(I18n.LangEn);

    /// <summary>切换 UI 语言：持久化到 config.json 并立即生效（无需重启）。</summary>
    public void SetLanguage(string cfgLang)
    {
        _config.Language = cfgLang;
        ConfigStore.Save(_config);
        I18n.Set(cfgLang); // 生效语言变化时触发 Changed → OnI18nChanged
        UpdateLanguageChecks();
    }

    /// <summary>I18n.Changed 处理：刷新窗口文案、重建卡片与托盘文本。</summary>
    private void OnI18nChanged()
    {
        ApplyTexts();
        RebuildCards();
    }

    /// <summary>把当前语言的所有窗口/菜单/托盘文案刷一遍（构造时与语言切换时调用）。</summary>
    private void ApplyTexts()
    {
        TitleText.Text = I18n.T("app_title");
        MenuRefresh.Header = I18n.T("refresh_now");
        MenuPause.Header = I18n.T("pause_scraping");
        MenuTopmost.Header = I18n.T("topmost");
        MenuLayout.Header = _config.IsHorizontal ? I18n.T("layout_to_vertical") : I18n.T("layout_to_horizontal");
        MenuLanguage.Header = I18n.T("language_menu");
        MenuLangAuto.Header = I18n.T("language_auto");
        MenuLangZh.Header = I18n.T("language_zh");
        MenuLangEn.Header = I18n.T("language_en");
        MenuSettings.Header = I18n.T("settings");
        MenuHide.Header = I18n.T("hide_window");
        MenuExit.Header = I18n.T("exit");
        RefreshButton.ToolTip = I18n.T("refresh_now");
        ZoomResetButton.ToolTip = I18n.T("zoom_reset_tip");
        HideButton.ToolTip = I18n.T("hide_window_tray");
        UpdatePauseVisuals();
        UpdateButtonStates();

        if (_tray != null) _tray.Text = I18n.T("app_title");
        if (_trayRefreshItem != null) _trayRefreshItem.Text = I18n.T("refresh_now");
        if (_trayPauseItem != null) _trayPauseItem.Text = I18n.T("pause_scraping");
        if (_trayTopmostItem != null) _trayTopmostItem.Text = I18n.T("topmost");
        if (_trayLanguageItem != null) _trayLanguageItem.Text = I18n.T("language_menu");
        if (_trayLangAutoItem != null) _trayLangAutoItem.Text = I18n.T("language_auto");
        if (_trayLangZhItem != null) _trayLangZhItem.Text = I18n.T("language_zh");
        if (_trayLangEnItem != null) _trayLangEnItem.Text = I18n.T("language_en");
        if (_traySettingsItem != null) _traySettingsItem.Text = I18n.T("settings");
        if (_trayToggleItem != null) _trayToggleItem.Text = I18n.T("show_hide_window");
        if (_trayExitItem != null) _trayExitItem.Text = I18n.T("exit");
    }

    /// <summary>同步右键菜单与托盘菜单中语言单选项的勾选状态。</summary>
    private void UpdateLanguageChecks()
    {
        var lang = _config.Language;
        MenuLangAuto.IsChecked = lang == I18n.LangAuto;
        MenuLangZh.IsChecked = lang == I18n.LangZh;
        MenuLangEn.IsChecked = lang == I18n.LangEn;
        if (_trayLangAutoItem != null) _trayLangAutoItem.Checked = lang == I18n.LangAuto;
        if (_trayLangZhItem != null) _trayLangZhItem.Checked = lang == I18n.LangZh;
        if (_trayLangEnItem != null) _trayLangEnItem.Checked = lang == I18n.LangEn;
    }

    private void OpenSettings()
    {
        var win = new SettingsWindow(_config, () => Engine) { Owner = this };
        if (win.ShowDialog() == true && win.ResultConfig != null)
        {
            // 按指纹比对：只有新增或配置有变化的服务才重新抓取，
            // 未变化的服务保留现有卡片数据和刷新计时
            var oldByFingerprint = new Dictionary<string, ServiceConfig>();
            foreach (var s in _config.Services) oldByFingerprint[ConfigStore.Fingerprint(s)] = s;

            var newConfig = win.ResultConfig;
            var carriedResults = new Dictionary<ServiceConfig, ServiceScrapeResult>();
            var carriedDue = new Dictionary<ServiceConfig, DateTime>();
            foreach (var s in newConfig.Services)
            {
                if (!oldByFingerprint.TryGetValue(ConfigStore.Fingerprint(s), out var old)) continue;
                if (_results.TryGetValue(old, out var r)) carriedResults[s] = r;
                if (_nextDue.TryGetValue(old, out var d)) carriedDue[s] = d;
            }

            _config = newConfig;
            ConfigStore.Save(_config);
            I18n.Set(_config.Language); // 设置面板里改了语言时立即生效
            UpdateLanguageChecks();
            _results.Clear();
            foreach (var kv in carriedResults) _results[kv.Key] = kv.Value;
            _nextDue.Clear();
            foreach (var kv in carriedDue) _nextDue[kv.Key] = kv.Value;
            ApplyConfig();
            _ = RefreshDueAsync(); // 无到期时间的服务（新增/变更）会立即刷新，其余跳过
        }
    }

    // ---------- 托盘 ----------

    private void InitTray()
    {
        var menu = new WinForms.ContextMenuStrip();
        _trayRefreshItem = new WinForms.ToolStripMenuItem(I18n.T("refresh_now"));
        _trayRefreshItem.Click += (s, e) => Dispatcher.Invoke(() => _ = RefreshAllAsync());
        menu.Items.Add(_trayRefreshItem);

        _trayPauseItem = new WinForms.ToolStripMenuItem(I18n.T("pause_scraping"))
        {
            CheckOnClick = true,
            Checked = _config.ScrapingPaused,
        };
        _trayPauseItem.CheckedChanged += (s, e) =>
        {
            if (_trayPauseItem.Checked != _config.ScrapingPaused)
                Dispatcher.Invoke(ToggleGlobalPause);
        };
        menu.Items.Add(_trayPauseItem);

        _trayTopmostItem = new WinForms.ToolStripMenuItem(I18n.T("topmost"))
        {
            CheckOnClick = true,
            Checked = _config.Topmost,
        };
        _trayTopmostItem.CheckedChanged += (s, e) =>
        {
            _config.Topmost = _trayTopmostItem.Checked;
            MenuTopmost.IsChecked = _config.Topmost;
            Topmost = _config.Topmost;
            ConfigStore.Save(_config);
        };
        menu.Items.Add(_trayTopmostItem);

        // 语言子菜单：跟随系统 / 中文 / English（单选，切换立即生效并持久化）
        _trayLangAutoItem = new WinForms.ToolStripMenuItem(I18n.T("language_auto"));
        _trayLangAutoItem.Click += (s, e) => Dispatcher.Invoke(() => SetLanguage(I18n.LangAuto));
        _trayLangZhItem = new WinForms.ToolStripMenuItem(I18n.T("language_zh"));
        _trayLangZhItem.Click += (s, e) => Dispatcher.Invoke(() => SetLanguage(I18n.LangZh));
        _trayLangEnItem = new WinForms.ToolStripMenuItem(I18n.T("language_en"));
        _trayLangEnItem.Click += (s, e) => Dispatcher.Invoke(() => SetLanguage(I18n.LangEn));
        _trayLanguageItem = new WinForms.ToolStripMenuItem(I18n.T("language_menu"));
        _trayLanguageItem.DropDownItems.Add(_trayLangAutoItem);
        _trayLanguageItem.DropDownItems.Add(_trayLangZhItem);
        _trayLanguageItem.DropDownItems.Add(_trayLangEnItem);
        menu.Items.Add(_trayLanguageItem);
        UpdateLanguageChecks();

        menu.Items.Add(new WinForms.ToolStripSeparator());
        _traySettingsItem = new WinForms.ToolStripMenuItem(I18n.T("settings"));
        _traySettingsItem.Click += (s, e) => Dispatcher.Invoke(OpenSettings);
        menu.Items.Add(_traySettingsItem);
        _trayToggleItem = new WinForms.ToolStripMenuItem(I18n.T("show_hide_window"));
        _trayToggleItem.Click += (s, e) => Dispatcher.Invoke(ToggleVisibility);
        menu.Items.Add(_trayToggleItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        _trayExitItem = new WinForms.ToolStripMenuItem(I18n.T("exit"));
        _trayExitItem.Click += (s, e) => Dispatcher.Invoke(ExitApp);
        menu.Items.Add(_trayExitItem);

        _tray = new WinForms.NotifyIcon
        {
            Icon = IconHelper.CreateIcon(),
            Text = I18n.T("app_title"),
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (s, e) => Dispatcher.Invoke(ToggleVisibility);
    }
}

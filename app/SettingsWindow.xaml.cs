using System;
using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace AIQuotaMonitor;

/// <summary>
/// 设置窗口：编辑配置的深拷贝副本，「保存」后才生效，「取消」放弃全部修改。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppConfig _working;
    private readonly Func<ScrapeEngine> _engineFactory;
    private bool _suppressInterval;
    private bool _suppressType;
    private bool _suppressTheme;

    public AppConfig? ResultConfig { get; private set; }

    public SettingsWindow(AppConfig config, Func<ScrapeEngine> engineFactory)
    {
        InitializeComponent();
        Backdrop.Apply(this, Backdrop.Kind.Mica);
        _engineFactory = engineFactory;

        // 深拷贝副本；换成 ObservableCollection 以便列表增删即时刷新
        _working = ConfigStore.Clone(config);
        _working.Services = new ObservableCollection<ServiceConfig>(_working.Services);
        foreach (var s in _working.Services)
            s.Rules = new ObservableCollection<QuotaRule>(s.Rules);

        ServiceList.ItemsSource = _working.Services;

        // 全局设置区
        LayoutVertical.IsChecked = !_working.IsHorizontal;
        LayoutHorizontal.IsChecked = _working.IsHorizontal;
        TopmostCheck.IsChecked = _working.Topmost;
        OpacitySlider.Value = _working.Opacity;
        OpacityLabel.Text = _working.Opacity.ToString("P0");
        GlobalIntervalBox.Text = _working.RefreshIntervalMinutes.ToString();
        ScrapeTimeoutBox.Text = _working.ScrapeTimeoutSeconds.ToString();
        NotifyQuotaCheck.IsChecked = _working.NotifyQuotaEnabled;
        NotifyQuotaBox.Text = _working.NotifyQuotaPercent.ToString();
        AutoStartCheck.IsChecked = _working.AutoStart;
        ShowRemainingCheck.IsChecked = _working.ShowRemainingTime;
        ShowPaceCheck.IsChecked = _working.ShowPaceBaseline;
        RecordHistoryCheck.IsChecked = _working.RecordHistory;
        RemainingThresholdBox.Text = _working.RemainingThresholdDays.ToString();
        UnifiedFormatCheck.IsChecked = _working.UnifiedDateFormat;
        DateFormatCombo.Text = _working.DateFormat;
        LanguageCombo.SelectedIndex = _working.Language switch
        {
            I18n.LangZh => 1,
            I18n.LangEn => 2,
            _ => 0,
        };

        // 颜色主题：选中当前主题并刷色板（事件由 _suppressTheme 抑制）
        _working.Theme ??= new ThemeConfig();
        _suppressTheme = true;
        ThemeCombo.SelectedIndex = ThemeIndexOf(_working.Theme.Name);
        _suppressTheme = false;

        ApplyTexts();
        RefreshThemeSwatches();
        RefreshBgSwatch();

        if (_working.Services.Count > 0) ServiceList.SelectedIndex = 0;
    }

    private static int ThemeIndexOf(string? key) => key switch
    {
        "emerald" => 1,
        "violet" => 2,
        "sunset" => 3,
        "graphite" => 4,
        ColorTheme.CustomKey => 5,
        _ => 0,
    };

    /// <summary>按当前语言刷新设置窗口全部文案（构造时调用；语言改动在保存后由主窗口统一应用）。</summary>
    private void ApplyTexts()
    {
        Title = I18n.T("settings_title");
        TabServices.Header = I18n.T("tab_services");
        TabGlobal.Header = I18n.T("tab_global");
        ServiceListLabel.Text = I18n.T("service_list");
        BtnAddService.Content = I18n.T("add");
        BtnRemoveService.Content = I18n.T("remove");
        BtnMoveServiceUp.Content = I18n.T("move_up");
        BtnMoveServiceDown.Content = I18n.T("move_down");

        GroupService.Header = I18n.T("group_service");
        EnableServiceCheck.Content = I18n.T("enable_service");
        PauseServiceCheck.Content = I18n.T("pause_service_setting");
        LblName.Text = I18n.T("field_name");
        LblUrl.Text = I18n.T("field_url");
        LblExtraWait.Text = I18n.T("field_extra_wait");
        LblExtraWait.ToolTip = I18n.T("tip_extra_wait");
        ExtraWaitBox.ToolTip = I18n.T("tip_extra_wait");
        LblLoginIndicator.Text = I18n.T("field_login_indicator");
        LblLoginIndicator.ToolTip = I18n.T("tip_login_indicator");
        LoginIndicatorBox.ToolTip = I18n.T("tip_login_indicator");
        LblSvcInterval.Text = I18n.T("field_svc_interval");
        LblSvcInterval.ToolTip = I18n.T("tip_svc_interval");
        SvcIntervalBox.ToolTip = I18n.T("tip_svc_interval");
        LblSubUrl.Text = I18n.T("field_sub_url");
        LblSubUrl.ToolTip = I18n.T("tip_sub_url");
        SubUrlBox.ToolTip = I18n.T("tip_sub_url");

        GroupRules.Header = I18n.T("group_rules");
        BtnAddRule.Content = I18n.T("add");
        BtnRemoveRule.Content = I18n.T("remove");
        BtnMoveRuleUp.Content = I18n.T("move_up");
        BtnMoveRuleDown.Content = I18n.T("move_down");
        LblRuleLabel.Text = I18n.T("field_label");
        LblRuleLabel.ToolTip = I18n.T("tip_label");
        RuleLabelBox.ToolTip = I18n.T("tip_label");
        LblMatchText.Text = I18n.T("field_match_text");
        LblMatchText.ToolTip = I18n.T("tip_match_text");
        MatchTextBox.ToolTip = I18n.T("tip_match_text");
        LblType.Text = I18n.T("field_type");
        RuleTypeItemPercent.Content = I18n.T("rule_type_percent");
        RuleTypeItemFraction.Content = I18n.T("rule_type_fraction");
        LblSelector.Text = I18n.T("field_selector");
        LblSelector.ToolTip = I18n.T("tip_selector");
        SelectorBox.ToolTip = I18n.T("tip_selector");
        LblPattern.Text = I18n.T("field_pattern");
        LblResetSelector.Text = I18n.T("field_reset_selector");
        LblResetSelector.ToolTip = I18n.T("tip_reset_selector");
        ResetSelectorBox.ToolTip = I18n.T("tip_reset_selector");
        LblResetPattern.Text = I18n.T("field_reset_pattern");
        LblResetPattern.ToolTip = I18n.T("tip_reset_pattern");
        ResetPatternBox.ToolTip = I18n.T("tip_reset_pattern");
        PresetsLabel.Text = I18n.T("presets");
        BtnPresetPercent.Content = I18n.T("preset_percent");
        BtnPresetFraction.Content = I18n.T("preset_fraction");
        InvertCheck.Content = I18n.T("invert_checkbox");
        InvertCheck.ToolTip = I18n.T("tip_invert");

        GroupTest.Header = I18n.T("group_test");
        TestButton.Content = I18n.T("test_button");
        TestHintText.Text = I18n.T("test_hint");

        GroupGlobal.Header = I18n.T("group_global");
        ThemeLabel.Text = I18n.T("theme_color");
        ThemeItemAzure.Content = I18n.T("theme_azure");
        ThemeItemEmerald.Content = I18n.T("theme_emerald");
        ThemeItemViolet.Content = I18n.T("theme_violet");
        ThemeItemSunset.Content = I18n.T("theme_sunset");
        ThemeItemGraphite.Content = I18n.T("theme_graphite");
        ThemeItemCustom.Content = I18n.T("theme_custom");
        SwatchLblAccent.Text = I18n.T("color_accent");
        SwatchLblNormal.Text = I18n.T("color_normal");
        SwatchLblNear.Text = I18n.T("color_near");
        SwatchLblAhead.Text = I18n.T("color_ahead");
        SwatchLblCritical.Text = I18n.T("color_critical");
        LanguageLabel.Text = I18n.T("language_label");
        LangItemAuto.Content = I18n.T("language_auto");
        LangItemZh.Content = I18n.T("language_zh");
        LangItemEn.Content = I18n.T("language_en");
        LblLayout.Text = I18n.T("layout_direction");
        LayoutVertical.Content = I18n.T("layout_vertical");
        LayoutHorizontal.Content = I18n.T("layout_horizontal");
        TopmostCheck.Content = I18n.T("topmost");
        LblOpacity.Text = I18n.T("opacity");
        LblBackground.Text = I18n.T("background_color");
        LblGlobalInterval.Text = I18n.T("global_interval");
        LblScrapeTimeout.Text = I18n.T("scrape_timeout");
        LblScrapeTimeout.ToolTip = I18n.T("tip_scrape_timeout");
        ScrapeTimeoutBox.ToolTip = I18n.T("tip_scrape_timeout");
        ShowRemainingCheck.Content = I18n.T("show_remaining");
        ShowPaceCheck.Content = I18n.T("show_pace");
        RecordHistoryCheck.Content = I18n.T("record_history");
        RecordHistoryCheck.ToolTip = I18n.T("tip_record_history");
        NotifyQuotaCheck.Content = I18n.T("notify_quota");
        NotifyQuotaCheck.ToolTip = I18n.T("tip_notify_quota");
        NotifyQuotaBox.ToolTip = I18n.T("tip_notify_quota");
        AutoStartCheck.Content = I18n.T("auto_start");
        AutoStartCheck.ToolTip = I18n.T("tip_auto_start");
        UnifiedFormatCheck.Content = I18n.T("unified_format");
        UnifiedFormatCheck.ToolTip = I18n.T("tip_unified_format");
        DateFormatCombo.ToolTip = I18n.T("tip_date_format");
        DateFmtItemCn.Content = I18n.T("datefmt_preset_cn");

        BtnSave.Content = I18n.T("save");
        BtnCancel.Content = I18n.T("cancel");
    }

    // ---------- 服务列表 ----------

    private void ServiceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var svc = ServiceList.SelectedItem as ServiceConfig;
        ServiceEditor.IsEnabled = svc != null;
        RuleList.ItemsSource = svc?.Rules;
        _suppressInterval = true;
        SvcIntervalBox.Text = svc?.RefreshIntervalMinutes?.ToString() ?? "";
        _suppressInterval = false;
        RuleList.SelectedIndex = svc != null && svc.Rules.Count > 0 ? 0 : -1;
    }

    private void AddService_Click(object sender, RoutedEventArgs e)
    {
        var svc = new ServiceConfig
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = I18n.T("new_service"),
            Rules = new ObservableCollection<QuotaRule> { new QuotaRule { Label = I18n.T("default_rule_label") } },
        };
        _working.Services.Add(svc);
        ServiceList.SelectedItem = svc;
    }

    private void RemoveService_Click(object sender, RoutedEventArgs e)
    {
        if (ServiceList.SelectedItem is not ServiceConfig svc) return;
        if (System.Windows.MessageBox.Show(this, I18n.T("confirm_delete_service", svc.Name), I18n.T("settings_box_title"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        int idx = ServiceList.SelectedIndex;
        _working.Services.Remove(svc);
        if (_working.Services.Count > 0)
            ServiceList.SelectedIndex = Math.Min(idx, _working.Services.Count - 1);
    }

    private void MoveServiceUp_Click(object sender, RoutedEventArgs e) => MoveService(-1);
    private void MoveServiceDown_Click(object sender, RoutedEventArgs e) => MoveService(1);

    private void MoveService(int delta)
    {
        if (ServiceList.SelectedItem is not ServiceConfig svc) return;
        var list = (ObservableCollection<ServiceConfig>)_working.Services;
        int idx = list.IndexOf(svc);
        int target = idx + delta;
        if (idx < 0 || target < 0 || target >= list.Count) return;
        list.Move(idx, target);
        ServiceList.SelectedItem = svc;
    }

    // ---------- 服务字段 ----------

    private void SvcIntervalBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressInterval || ServiceList.SelectedItem is not ServiceConfig svc) return;
        if (string.IsNullOrWhiteSpace(SvcIntervalBox.Text)) svc.RefreshIntervalMinutes = null;
        else if (int.TryParse(SvcIntervalBox.Text, out int m) && m >= 1) svc.RefreshIntervalMinutes = m;
    }

    // ---------- 规则列表 ----------

    private void RuleList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var rule = RuleList.SelectedItem as QuotaRule;
        RuleEditor.IsEnabled = rule != null;
        _suppressType = true;
        RuleTypeCombo.SelectedIndex = rule?.Type == QuotaRule.TypeFraction ? 1 : 0;
        _suppressType = false;
    }

    private void RuleTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressType || RuleList.SelectedItem is not QuotaRule rule) return;
        rule.Type = RuleTypeCombo.SelectedIndex == 1 ? QuotaRule.TypeFraction : QuotaRule.TypePercent;
    }

    private void AddRule_Click(object sender, RoutedEventArgs e)
    {
        if (ServiceList.SelectedItem is not ServiceConfig svc) return;
        var rule = new QuotaRule { Id = Guid.NewGuid().ToString("N"), Label = I18n.T("new_rule") };
        svc.Rules.Add(rule);
        RuleList.SelectedItem = rule;
    }

    private void RemoveRule_Click(object sender, RoutedEventArgs e)
    {
        if (ServiceList.SelectedItem is not ServiceConfig svc) return;
        if (RuleList.SelectedItem is not QuotaRule rule) return;
        int idx = RuleList.SelectedIndex;
        svc.Rules.Remove(rule);
        if (svc.Rules.Count > 0)
            RuleList.SelectedIndex = Math.Min(idx, svc.Rules.Count - 1);
    }

    private void MoveRuleUp_Click(object sender, RoutedEventArgs e) => MoveRule(-1);
    private void MoveRuleDown_Click(object sender, RoutedEventArgs e) => MoveRule(1);

    private void MoveRule(int delta)
    {
        if (ServiceList.SelectedItem is not ServiceConfig svc) return;
        if (RuleList.SelectedItem is not QuotaRule rule) return;
        var list = (ObservableCollection<QuotaRule>)svc.Rules;
        int idx = list.IndexOf(rule);
        int target = idx + delta;
        if (idx < 0 || target < 0 || target >= list.Count) return;
        list.Move(idx, target);
        RuleList.SelectedItem = rule;
    }

    private void PresetPercent_Click(object sender, RoutedEventArgs e)
    {
        RulePatternBox.Text = QuotaRule.DefaultPercentPattern;
        RuleTypeCombo.SelectedIndex = 0;
    }

    private void PresetFraction_Click(object sender, RoutedEventArgs e)
    {
        RulePatternBox.Text = QuotaRule.DefaultFractionPattern;
        RuleTypeCombo.SelectedIndex = 1;
    }

    // ---------- 测试抓取 ----------

    private async void TestScrape_Click(object sender, RoutedEventArgs e)
    {
        if (ServiceList.SelectedItem is not ServiceConfig svc) return;
        if (string.IsNullOrWhiteSpace(svc.Url))
        {
            TestOutput.Text = I18n.T("test_need_url");
            return;
        }
        TestButton.IsEnabled = false;
        TestOutput.Text = I18n.T("test_running");
        try
        {
            var snapshot = ConfigStore.Clone(svc);
            var res = await _engineFactory().ScrapeAsync(snapshot, timeoutSeconds: _working.ScrapeTimeoutSeconds);
            TestOutput.Text = FormatResult(res);
        }
        catch (Exception ex)
        {
            TestOutput.Text = I18n.T("test_error") + ex.Message;
        }
        finally
        {
            TestButton.IsEnabled = true;
        }
    }

    private static string FormatResult(ServiceScrapeResult res)
    {
        var sb = new StringBuilder();
        sb.AppendLine(res.Status switch
        {
            ScrapeStatus.Ok => I18n.T("status_ok"),
            ScrapeStatus.NeedLogin => I18n.T("status_need_login"),
            _ => I18n.T("status_failed"),
        });
        if (!string.IsNullOrEmpty(res.ErrorMessage)) sb.AppendLine(I18n.T("error_prefix") + res.ErrorMessage);
        if (res.Subscription != null)
        {
            sb.AppendLine(I18n.T("sub_info"));
            if (res.Subscription.ExpireAt is { } exp) sb.AppendLine(I18n.T("sub_expire", $"{exp:yyyy-MM-dd HH:mm}"));
            if (res.Subscription.AutoRenew is { } ar) sb.AppendLine(I18n.T("sub_auto_renew", I18n.T(ar ? "on" : "off")));
            if (res.Subscription.ExpireAt == null && res.Subscription.AutoRenew == null)
                sb.AppendLine(I18n.T("sub_not_detected"));
        }
        foreach (var r in res.Rules)
        {
            sb.AppendLine();
            sb.AppendLine(I18n.T("rule_header", r.Label));
            if (r.Error != null)
            {
                sb.AppendLine(I18n.T("rule_failed") + r.Error);
            }
            else
            {
                sb.AppendLine(I18n.T("rule_percent") + (r.Percent.HasValue ? r.Percent.Value.ToString("0.##") + "%" : "—"));
                if (r.Detail != null) sb.AppendLine(I18n.T("rule_detail") + r.Detail);
                if (r.ResetText != null) sb.AppendLine(I18n.T("rule_reset") + r.ResetText);
            }
            if (!string.IsNullOrEmpty(r.RawText))
            {
                string raw = r.RawText.Replace("\r", " ").Replace("\n", " ⏎ ");
                if (raw.Length > 280) raw = raw[..280] + "…";
                sb.AppendLine(I18n.T("rule_raw_text") + raw);
            }
        }
        if (res.Rules.Count == 0 && res.Status != ScrapeStatus.NeedLogin)
            sb.AppendLine(I18n.T("no_rules_in_service"));
        return sb.ToString();
    }

    // ---------- 全局设置 / 保存 ----------

    // ---------- 颜色主题 ----------

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressTheme || ThemeCombo.SelectedItem is not ComboBoxItem item) return;
        _working.Theme.Name = item.Tag as string ?? "azure";
        RefreshThemeSwatches();
    }

    /// <summary>按当前主题（预设或自定义）刷新五行色板：预设只读展示，自定义可点击修改。</summary>
    private void RefreshThemeSwatches()
    {
        var t = ColorTheme.Resolve(_working);
        bool custom = _working.Theme.Name == ColorTheme.CustomKey;
        SetSwatch(SwatchAccent, HexAccent, t.Accent, custom);
        SetSwatch(SwatchNormal, HexNormal, t.Normal, custom);
        SetSwatch(SwatchNear, HexNear, t.Near, custom);
        SetSwatch(SwatchAhead, HexAhead, t.Ahead, custom);
        SetSwatch(SwatchCritical, HexCritical, t.Critical, custom);
    }

    private static void SetSwatch(System.Windows.Controls.Primitives.ButtonBase btn, TextBlock hex,
        System.Windows.Media.Color color, bool enabled)
    {
        btn.Background = new System.Windows.Media.SolidColorBrush(color);
        btn.IsEnabled = enabled;
        hex.Text = ColorTheme.Hex(color);
    }

    private void Swatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn || btn.Tag is not string field) return;
        var current = ColorTheme.Resolve(_working);
        var color = field switch
        {
            "Accent" => current.Accent,
            "Normal" => current.Normal,
            "Near" => current.Near,
            "Ahead" => current.Ahead,
            _ => current.Critical,
        };
        var dlg = new ColorPickerDialog(color) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedColor is not { } picked) return;
        string hex = ColorTheme.Hex(picked);
        switch (field)
        {
            case "Accent": _working.Theme.Accent = hex; break;
            case "Normal": _working.Theme.Normal = hex; break;
            case "Near": _working.Theme.Near = hex; break;
            case "Ahead": _working.Theme.Ahead = hex; break;
            default: _working.Theme.Critical = hex; break;
        }
        RefreshThemeSwatches();
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityLabel != null) OpacityLabel.Text = OpacitySlider.Value.ToString("P0");
    }

    /// <summary>背景色：点击色板打开取色对话框（含屏幕取色器），即时预览。</summary>
    private void BgSwatch_Click(object sender, RoutedEventArgs e)
    {
        var current = Ui.ParseColor(_working.BackgroundColor,
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#9914141B"));
        var dlg = new ColorPickerDialog(current) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedColor is not { } picked) return;
        _working.BackgroundColor = ColorTheme.Hex(picked);
        RefreshBgSwatch();
    }

    private void RefreshBgSwatch()
    {
        var c = Ui.ParseColor(_working.BackgroundColor,
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#9914141B"));
        BgSwatch.Background = new System.Windows.Media.SolidColorBrush(c);
        BgHexText.Text = _working.BackgroundColor;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(GlobalIntervalBox.Text, out int mins) || mins < 1)
        {
            System.Windows.MessageBox.Show(this, I18n.T("invalid_interval"), I18n.T("settings_box_title"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _working.RefreshIntervalMinutes = mins;
        if (int.TryParse(ScrapeTimeoutBox.Text, out int secs)) _working.ScrapeTimeoutSeconds = secs; // 属性内 clamp 10–300
        _working.Layout = LayoutHorizontal.IsChecked == true ? "horizontal" : "vertical";
        _working.Topmost = TopmostCheck.IsChecked == true;
        _working.Opacity = OpacitySlider.Value;
        _working.ShowRemainingTime = ShowRemainingCheck.IsChecked == true;
        _working.ShowPaceBaseline = ShowPaceCheck.IsChecked == true;
        _working.RecordHistory = RecordHistoryCheck.IsChecked == true;
        _working.NotifyQuotaEnabled = NotifyQuotaCheck.IsChecked == true;
        if (int.TryParse(NotifyQuotaBox.Text, out int qp)) _working.NotifyQuotaPercent = qp; // 属性内 clamp 50–100
        _working.AutoStart = AutoStartCheck.IsChecked == true;
        _working.Language = (LanguageCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? I18n.LangAuto;
        if (int.TryParse(RemainingThresholdBox.Text, out int threshold) && threshold >= 1)
            _working.RemainingThresholdDays = threshold;
        _working.UnifiedDateFormat = UnifiedFormatCheck.IsChecked == true;
        if (!string.IsNullOrWhiteSpace(DateFormatCombo.Text))
            _working.DateFormat = DateFormatCombo.Text.Trim();
        ResultConfig = _working;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

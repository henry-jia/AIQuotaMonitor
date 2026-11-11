using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using AIQuotaMonitor;

internal static class SettingsCreditsTests
{
    internal static (int Checks, List<string> Failures) Run()
    {
        int checks = 0;
        var failures = new List<string>();
        var thread = new Thread(() =>
        {
            try
            {
                I18n.Initialize(I18n.LangEn);
                var codex = new ServiceConfig { Name = "Codex", Url = "https://chatgpt.com/#settings/Usage" };
                var other = new ServiceConfig { Name = "Other", Url = "https://example.com/usage" };
                var config = new AppConfig { Services = new List<ServiceConfig> { codex, other } };
                var window = new SettingsWindow(config, () => new ScrapeEngine());
                var list = (ListBox)window.FindName("ServiceList");
                var box = (TextBox)window.FindName("CreditsLimitBox");
                var label = (TextBlock)window.FindName("LblCreditsLimit");
                var url = (TextBox)window.FindName("UrlBox");
                var save = (Button)window.FindName("BtnSave");

                Check(list.SelectedIndex == 0, "Settings test must select the Codex service first.");
                Check(box.Text == "62500" && box.Visibility == Visibility.Visible && label.Visibility == Visibility.Visible,
                    "Codex settings must show a Credits limit of 62500 by default.");
                Check(label.Text == "Credits limit" && Equals(box.ToolTip, I18n.T("tip_credits_limit")),
                    "Credits limit label and explanation must be localized.");

                list.SelectedIndex = 1;
                Check(box.Visibility == Visibility.Collapsed && label.Visibility == Visibility.Collapsed,
                    "Credits limit must be hidden for non-Codex services.");
                url.Text = "https://chatgpt.com/settings/usage?tab=overview";
                Check(box.Visibility == Visibility.Visible && label.Visibility == Visibility.Visible,
                    "Changing a service URL to Codex must reveal the Credits limit immediately.");

                list.SelectedIndex = 0;
                box.Text = "75000";
                box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                try
                {
                    save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("DialogResult", StringComparison.Ordinal))
                {
                    // The shared test Application is already shut down; ResultConfig is assigned before this host-only error.
                }
                Check(window.ResultConfig?.Services[0].CreditsLimit == 75000,
                    $"Saving settings must persist the custom Credits limit (box={box.Text}, result={(window.ResultConfig == null ? "null" : window.ResultConfig.Services[0].CreditsLimit.ToString())}).");
            }
            catch (Exception ex)
            {
                failures.Add($"Settings credits UI test failed: {ex.GetType()}: {ex.Message}");
            }
            finally
            {
                I18n.Initialize(I18n.LangEn);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return (checks, failures);

        void Check(bool passed, string message)
        {
            checks++;
            if (!passed) failures.Add(message);
        }
    }
}

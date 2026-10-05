using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using AIQuotaMonitor;

internal static class ServiceCardLoginTests
{
    internal static (int Checks, List<string> Failures) Run()
    {
        int checks = 0;
        var failures = new List<string>();
        var thread = new Thread(() =>
        {
            var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            try
            {
                app.InitializeComponent();
                var shared = new ServiceConfig { Id = "shared", Name = "Shared account" };
                var isolated = new ServiceConfig { Id = "isolated", Name = "Isolated account", IsolatedSession = true };
                var card = new ServiceCard();
                ServiceConfig? requested = null;
                card.RequestLogin += service => requested = service;
                var button = card.FindName("CardLoginButton") as Button;
                Check(button != null, "Every card must have a permanent Sign in button.");
                if (button == null) return;

                foreach (var language in new[] { I18n.LangEn, I18n.LangZh })
                {
                    I18n.Initialize(language);
                    card.ApplyTexts();
                    Check(Equals(button.ToolTip, I18n.T("go_login")), "Sign in tooltip must follow the UI language.");
                    Check(AutomationProperties.GetName(button) == I18n.T("go_login"), "Sign in must have a localized accessible name.");
                }

                foreach (var service in new[] { shared, isolated })
                {
                    card.BindLoading(service);
                    CheckLogin("loading", service);
                    foreach (var result in new[]
                    {
                        new ServiceScrapeResult { Service = service, Status = ScrapeStatus.Ok },
                        new ServiceScrapeResult { Service = service, Status = ScrapeStatus.NeedLogin },
                        new ServiceScrapeResult { Service = service, Status = ScrapeStatus.Error, SuggestLogin = false },
                        new ServiceScrapeResult { Service = service, Status = ScrapeStatus.Error, SuggestLogin = true },
                        new ServiceScrapeResult { Service = service, StaleError = "Refresh failed", SuggestLogin = false },
                        new ServiceScrapeResult { Service = service, StaleFromDisk = true },
                    })
                    {
                        card.Bind(result, new AppConfig());
                        CheckLogin($"{result.Status}, stale={result.StaleError != null || result.StaleFromDisk}, suggest={result.SuggestLogin}", service);
                    }
                    card.SetPaused(true);
                    card.SetBusy(true);
                    CheckLogin("paused and busy", service);
                    card.SetPaused(false);
                    card.SetBusy(false);
                }

                void CheckLogin(string state, ServiceConfig service)
                {
                    Check(button.Visibility == Visibility.Visible && button.IsEnabled,
                        $"Sign in must remain available for {service.Id}: {state}.");
                    requested = null;
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(ReferenceEquals(requested, service),
                        $"Sign in must request the bound service for {service.Id}: {state}.");
                }
            }
            catch (Exception ex)
            {
                failures.Add($"Card login UI test failed: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                I18n.Initialize(I18n.LangEn);
                app.Shutdown();
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

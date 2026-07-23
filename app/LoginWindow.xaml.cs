using System;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace AIQuotaMonitor;

/// <summary>
/// 内置浏览器登录窗口：与抓取引擎共享用户数据目录（同一域名只需登录一次）。
/// 关闭窗口后由主窗口触发该服务的重新抓取。
/// </summary>
public partial class LoginWindow : Window
{
    private readonly CoreWebView2Environment _env;
    private readonly string _url;

    public LoginWindow(CoreWebView2Environment env, string url, string serviceName)
    {
        InitializeComponent();
        Title = I18n.T("login_window_title", serviceName);
        _env = env;
        _url = url;
        Loaded += LoginWindow_Loaded;
    }

    private async void LoginWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await WebView.EnsureCoreWebView2Async(_env);
            WebView.CoreWebView2.Navigate(_url);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, I18n.T("init_browser_failed") + ex.Message, "AIQuotaMonitor",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }
}

using System;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace AIQuotaMonitor;

/// <summary>
/// 内置浏览器窗口：使用与抓取引擎一致的会话环境（默认共享 profile，同一域名只需登录一次；
/// 服务开启「独立会话」时为该服务独立的 profile）。
/// 两种用途：登录（viewOnly=false）与查看配置的页面核对内容（viewOnly=true）。
/// 关闭窗口后由主窗口触发该服务的重新抓取。
/// </summary>
public partial class LoginWindow : Window
{
    private readonly CoreWebView2Environment _env;
    private readonly string _url;

    public LoginWindow(CoreWebView2Environment env, string url, string serviceName, bool viewOnly = false)
    {
        InitializeComponent();
        Backdrop.Apply(this, Backdrop.Kind.None); // 深色标题栏 + 圆角即可，WebView 内容区不透明
        Title = I18n.T(viewOnly ? "view_window_title" : "login_window_title", serviceName);
        _env = env;
        _url = GetStartUrl(url, viewOnly);
        Loaded += LoginWindow_Loaded;
    }

    internal static string GetStartUrl(string url, bool viewOnly)
    {
        // Kimi 未登录时从订阅页跳回首页，跳转后的登录按钮可能无响应。
        if (!viewOnly && Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && uri.UserInfo.Length == 0
            && uri.Host is "www.kimi.com" or "kimi.com"
            && uri.AbsolutePath is "/membership/subscription" or "/membership/subscription/")
        {
            return new Uri(uri, "/").AbsoluteUri;
        }

        return url;
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

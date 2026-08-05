using System;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace AIQuotaMonitor;

/// <summary>
/// 内置浏览器窗口：与抓取引擎共享用户数据目录（同一域名只需登录一次）。
/// 两种用途：登录（viewOnly=false）与查看页面核对内容（viewOnly=true，仅标题不同）。
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

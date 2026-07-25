using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace AIQuotaMonitor;

/// <summary>
/// 拖拽虚影窗：无边框透明置顶窗口，显示被拖卡片的位图快照（带浮起阴影），
/// 跟随鼠标移动——与本体无关，所以永不抖动，也可以拖出主窗口范围。
/// 本体留在原槽位，只有「提交换位」时槽位才变化。
/// </summary>
public sealed class GhostWindow : Window
{
    private readonly double _grabX;
    private readonly double _grabY;

    public GhostWindow(BitmapSource image, double grabX, double grabY)
    {
        _grabX = grabX;
        _grabY = grabY;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        Content = new Border
        {
            CornerRadius = new CornerRadius(10),
            Effect = new DropShadowEffect
            {
                BlurRadius = 18,
                Opacity = 0.5,
                ShadowDepth = 4,
                Color = Colors.Black,
            },
            Child = new Image { Source = image, Stretch = Stretch.None },
        };
    }

    /// <summary>移动到指定的屏幕坐标（DIP，光标的抓取点对准按下时的相对位置）。</summary>
    public void MoveTo(Point screenDip)
    {
        Left = screenDip.X - _grabX;
        Top = screenDip.Y - _grabY;
    }
}

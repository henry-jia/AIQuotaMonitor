using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AIQuotaMonitor;

/// <summary>
/// 颜色选择对话框（深色主题）：大色块预览 + hex 输入 + RGB 滑块 + 屏幕取色器。
/// ShowDialog 返回 true 时 SelectedColor 有效。
/// </summary>
public partial class ColorPickerDialog : Window
{
    private Color _color;
    private bool _suppress;
    private bool _closed; // 窗口关闭后禁止任何 Show/Visibility 操作（取色异步恢复路径）

    public Color? SelectedColor { get; private set; }

    public ColorPickerDialog(Color initial)
    {
        InitializeComponent();
        Backdrop.Apply(this, Backdrop.Kind.Mica);
        Closed += (_, _) => _closed = true;
        Title = I18n.T("color_picker_title");
        BtnScreenPick.Content = I18n.T("screen_pick");
        BtnOk.Content = I18n.T("ok");
        BtnCancel.Content = I18n.T("cancel");
        SetColor(initial);
    }

    /// <summary>统一入口：改色后同步预览 / hex / 滑块 / 数值。</summary>
    private void SetColor(Color c)
    {
        _color = c;
        _suppress = true;
        PreviewBox.Background = new SolidColorBrush(c);
        HexBox.Text = $"{c.R:X2}{c.G:X2}{c.B:X2}";
        HexBox.ClearValue(Control.BackgroundProperty);
        SliderR.Value = c.R;
        SliderG.Value = c.G;
        SliderB.Value = c.B;
        ValR.Text = c.R.ToString();
        ValG.Text = c.G.ToString();
        ValB.Text = c.B.ToString();
        _suppress = false;
    }

    private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppress) return;
        SetColor(Color.FromRgb((byte)SliderR.Value, (byte)SliderG.Value, (byte)SliderB.Value));
    }

    private void HexBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppress) return;
        // 只接受 6 位十六进制（输入框前的 # 为固定装饰）；非法输入标红，不改动当前颜色
        if (TryParseHex(HexBox.Text, out var c))
        {
            HexBox.ClearValue(Control.BackgroundProperty);
            SetColor(c);
        }
        else
        {
            HexBox.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#40222A"));
        }
    }

    private static bool TryParseHex(string? text, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim().TrimStart('#');
        if (text.Length != 6) return false;
        if (!uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;
        color = Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        SelectedColor = _color;
        DialogResult = true;
    }

    /// <summary>屏幕取色：先隐藏对话框，整屏截图后用全屏覆盖窗口取色，结束后恢复对话框。</summary>
    private async void ScreenPick_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        await Task.Delay(180); // 等窗口真正从屏幕消失再截图
        if (_closed) return; // 隐藏期间对话框被关闭（如设置窗口连带关闭/程序退出）——不得再 Show
        // PerMonitorV2 下 VirtualScreen 返回的是物理像素坐标，与 CopyFromScreen 一致
        var vs = System.Windows.Forms.SystemInformation.VirtualScreen;
        using var bmp = new System.Drawing.Bitmap(vs.Width, vs.Height);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
            g.CopyFromScreen(vs.Left, vs.Top, 0, 0, bmp.Size, System.Drawing.CopyPixelOperation.SourceCopy);
        if (_closed) return; // 截图耗时（多屏大图），期间可能已关闭

        // Owner 设为本对话框：若取色期间对话框被关闭，覆盖层随之关闭，ShowDialog 返回后不再恢复
        var overlay = new EyedropperWindow(bmp, vs) { Owner = this };
        bool? picked = overlay.ShowDialog();
        if (_closed) return; // 对话框在取色期间已关闭——直接放弃，绝不 Show()
        Show();
        Activate();
        if (picked == true && overlay.Picked is { } c)
            SetColor(c);
    }

    /// <summary>
    /// 取色覆盖窗口：无边框全屏显示冻结截图，光标旁 7x7 像素放大镜 + 当前 hex，
    /// 左键单击取色，Esc 或右键取消。
    /// DPI 换算：截图为物理像素，WPF 窗口用 DIP——窗口尺寸按所在显示器 DPI 由像素换算，
    /// 鼠标位置再按「Image 实际显示尺寸 → 截图像素」的比例反推取色点，多屏不同 DPI 下仍指向正确像素。
    /// </summary>
    private sealed class EyedropperWindow : Window
    {
        private const int LoupeSize = 7;    // 放大 7x7 像素
        private const int LoupeCell = 18;   // 每像素放大到 18 DIP

        private readonly System.Drawing.Bitmap _bmp;   // 全屏截图（物理像素）
        private readonly System.Drawing.Rectangle _vs; // 虚拟屏幕（物理像素）
        private readonly BitmapSource _source;
        private readonly Image _image;
        private readonly Canvas _canvas;
        private readonly Border _loupe;
        private readonly Image _loupeImage;
        private readonly TextBlock _loupeHex;
        private Color _current;

        public Color? Picked { get; private set; }

        public EyedropperWindow(System.Drawing.Bitmap bmp, System.Drawing.Rectangle vs)
        {
            _bmp = bmp;
            _vs = vs;
            _source = ToBitmapSource(bmp);

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            Cursor = Cursors.Cross;
            Background = Brushes.Black;
            Focusable = true;

            _canvas = new Canvas();
            _image = new Image { Source = _source, Stretch = Stretch.Fill };
            _canvas.Children.Add(_image);

            // 放大镜面板：放大图（含中心准星）+ hex + 操作提示
            _loupeImage = new Image { Width = LoupeSize * LoupeCell, Height = LoupeSize * LoupeCell };
            RenderOptions.SetBitmapScalingMode(_loupeImage, BitmapScalingMode.NearestNeighbor);
            var loupeGrid = new Grid();
            loupeGrid.Children.Add(_loupeImage);
            loupeGrid.Children.Add(new Border
            {
                Width = LoupeCell,
                Height = LoupeCell,
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1.5),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
            _loupeHex = new TextBlock
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 5, 0, 0),
            };
            var hint = new TextBlock
            {
                Text = I18n.T("eyedropper_hint"),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C9C9D1")),
                FontSize = 10.5,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = LoupeSize * LoupeCell + 16,
                Margin = new Thickness(0, 3, 0, 0),
            };
            var panel = new StackPanel();
            panel.Children.Add(loupeGrid);
            panel.Children.Add(_loupeHex);
            panel.Children.Add(hint);
            _loupe = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x14, 0x14, 0x1B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8),
                Child = panel,
                IsHitTestVisible = false,
            };
            _canvas.Children.Add(_loupe);

            Content = _canvas;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            // 物理像素 → DIP：窗口铺满整个虚拟屏幕
            var dpi = VisualTreeHelper.GetDpi(this);
            Left = _vs.Left / dpi.DpiScaleX;
            Top = _vs.Top / dpi.DpiScaleY;
            Width = _vs.Width / dpi.DpiScaleX;
            Height = _vs.Height / dpi.DpiScaleY;
            Keyboard.Focus(this);
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            _image.Width = ActualWidth;
            _image.Height = ActualHeight;
        }

        /// <summary>窗口 DIP 坐标 → 截图物理像素坐标。</summary>
        private System.Drawing.Point ToPixel(System.Windows.Point pos)
        {
            double sx = _bmp.Width / Math.Max(_image.ActualWidth, 1);
            double sy = _bmp.Height / Math.Max(_image.ActualHeight, 1);
            int x = Math.Clamp((int)(pos.X * sx), 0, _bmp.Width - 1);
            int y = Math.Clamp((int)(pos.Y * sy), 0, _bmp.Height - 1);
            return new System.Drawing.Point(x, y);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var pos = e.GetPosition(_canvas);
            var px = ToPixel(pos);
            var dc = _bmp.GetPixel(px.X, px.Y);
            _current = Color.FromRgb(dc.R, dc.G, dc.B);

            // 7x7 区域裁剪放大（贴近屏幕边缘时平移裁剪框，保证放大镜始终满幅）
            int cx = Math.Clamp(px.X - LoupeSize / 2, 0, _bmp.Width - LoupeSize);
            int cy = Math.Clamp(px.Y - LoupeSize / 2, 0, _bmp.Height - LoupeSize);
            _loupeImage.Source = new CroppedBitmap(_source, new Int32Rect(cx, cy, LoupeSize, LoupeSize));
            _loupeHex.Text = $"#{_current.R:X2}{_current.G:X2}{_current.B:X2}";

            // 放大镜跟随光标，靠近右/下边缘时翻转到另一侧
            _loupe.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            double lw = _loupe.DesiredSize.Width, lh = _loupe.DesiredSize.Height;
            double lx = pos.X + 24, ly = pos.Y + 24;
            if (lx + lw > _canvas.ActualWidth) lx = pos.X - 24 - lw;
            if (ly + lh > _canvas.ActualHeight) ly = pos.Y - 24 - lh;
            Canvas.SetLeft(_loupe, Math.Max(0, lx));
            Canvas.SetTop(_loupe, Math.Max(0, ly));
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            Picked = _current;
            DialogResult = true;
        }

        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonDown(e);
            DialogResult = false;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Escape) DialogResult = false;
        }

        private static BitmapSource ToBitmapSource(System.Drawing.Bitmap bmp)
        {
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            ms.Position = 0;
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = ms;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
    }
}

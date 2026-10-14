using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using SoundPartyCompanion.Core.Devices;
using Forms = System.Windows.Forms;

namespace SoundPartyCompanion.App.Infrastructure;

public sealed record TrayMenuState(
    bool IsWindowVisible,
    bool IsTopmost,
    string DeviceName,
    string ConnectionStatus,
    string BatteryText,
    string BatteryDetailText,
    bool HasBattery,
    bool IsConnected,
    bool IsCurrentOutput,
    bool CanSetOutput);

public sealed class TrayIconService : IDisposable
{
    private static readonly Color MenuBackground = Color.FromArgb(40, 42, 40);
    private static readonly Color MenuText = Color.FromArgb(238, 234, 228);
    private static readonly Color MenuMuted = Color.FromArgb(174, 178, 171);

    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly RoundedContextMenuStrip _menu;
    private readonly Forms.ToolStripLabel _deviceLabel;
    private readonly Forms.ToolStripLabel _statusLabel;
    private readonly Forms.ToolStripMenuItem _showItem;
    private readonly Forms.ToolStripMenuItem _outputItem;
    private readonly Forms.ToolStripMenuItem _topmostItem;
    private readonly Forms.ToolStripMenuItem _diagnosticItem;
    private readonly Icon _icon;
    private readonly Bitmap _headerImage;
    private readonly Action _showOrHide;
    private readonly Action<bool> _setTopmost;
    private bool _updating;
    private bool _disposed;

    public TrayIconService(
        Action showOrHide,
        Action<bool> setTopmost,
        Action setAsCurrentOutput,
        Action openSettings,
        Action openBluetoothSettings,
        Action exportDiagnostics,
        Action exitApplication)
    {
        _showOrHide = showOrHide ?? throw new ArgumentNullException(nameof(showOrHide));
        _setTopmost = setTopmost ?? throw new ArgumentNullException(nameof(setTopmost));
        ArgumentNullException.ThrowIfNull(setAsCurrentOutput);
        ArgumentNullException.ThrowIfNull(openSettings);
        ArgumentNullException.ThrowIfNull(openBluetoothSettings);
        ArgumentNullException.ThrowIfNull(exportDiagnostics);
        ArgumentNullException.ThrowIfNull(exitApplication);

        _icon = CreateTrayIcon();
        _headerImage = CreateHeaderImage();

        _menu = new RoundedContextMenuStrip
        {
            BackColor = MenuBackground,
            ForeColor = MenuText,
            Font = new Font("Microsoft YaHei UI", 9f),
            MinimumSize = new Size(276, 0),
            Padding = new Forms.Padding(8, 8, 14, 8),
            ImageScalingSize = new Size(32, 32),
            // ContextMenuStrip's native shadow keeps a rectangular footprint even
            // when the window is clipped to a rounded Region, leaving dark corner
            // blocks on light desktops. The custom border provides the separation.
            DropShadowEnabled = false,
            ShowImageMargin = false,
            ShowCheckMargin = false,
            Renderer = new TrayMenuRenderer()
        };

        _deviceLabel = new Forms.ToolStripLabel("Xiaomi蓝牙音箱Pro")
        {
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
            ForeColor = MenuText,
            Image = _headerImage,
            ImageScaling = Forms.ToolStripItemImageScaling.SizeToFit,
            TextImageRelation = Forms.TextImageRelation.ImageBeforeText,
            Padding = new Forms.Padding(27, 8, 10, 0),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _statusLabel = new Forms.ToolStripLabel("正在检测连接")
        {
            Font = new Font("Microsoft YaHei UI", 8f),
            ForeColor = MenuMuted,
            Margin = new Forms.Padding(45, 1, 0, 2),
            Padding = new Forms.Padding(0, 0, 10, 7),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _menu.Items.Add(_deviceLabel);
        _menu.Items.Add(_statusLabel);
        _menu.Items.Add(new Forms.ToolStripSeparator());

        _showItem = AddItem("▣  隐藏组件", () => InvokeOnUi(_showOrHide), "primary");
        _outputItem = AddItem("◖  设为当前音频输出", () => InvokeOnUi(setAsCurrentOutput));
        _menu.Items.Add(new Forms.ToolStripSeparator());

        _topmostItem = AddItem("▤  始终置顶", () => { }, "toggle");
        _topmostItem.CheckOnClick = true;
        _topmostItem.CheckedChanged += (_, _) =>
        {
            if (!_updating)
            {
                InvokeOnUi(() => _setTopmost(_topmostItem.Checked));
            }
        };
        AddItem("☷  打开设置", () => InvokeOnUi(openSettings));
        AddItem("ᛒ  打开蓝牙设置", () => InvokeOnUi(openBluetoothSettings));
        _diagnosticItem = AddItem("↓  导出诊断", () => InvokeOnUi(exportDiagnostics));
        _menu.Items.Add(new Forms.ToolStripSeparator());
        AddItem("↪  退出", () => InvokeOnUi(exitApplication));
        _menu.FixItemWidths();

        _notifyIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = _menu,
            Icon = _icon,
            Text = "Sound Party Companion",
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => InvokeOnUi(_showOrHide);
    }

    public void Update(TrayMenuState state)
    {
        _deviceLabel.Text = string.IsNullOrWhiteSpace(state.DeviceName)
            ? "Xiaomi蓝牙音箱Pro"
            : state.DeviceName;
        var battery = state.BatteryDetailText.Contains("已过期", StringComparison.Ordinal)
            ? $"{state.BatteryText}（已过期）"
            : state.BatteryText;
        _statusLabel.Text = state.IsConnected && state.HasBattery
            ? $"{state.ConnectionStatus} · {battery}"
            : state.ConnectionStatus;
        _showItem.Text = state.IsWindowVisible ? "▣  隐藏组件" : "▣  显示组件";
        _outputItem.Text = state.IsCurrentOutput ? "✓  当前音频输出" : "◖  设为当前音频输出";
        _outputItem.Enabled = state.CanSetOutput;

        if (_topmostItem.Checked != state.IsTopmost)
        {
            _updating = true;
            try
            {
                _topmostItem.Checked = state.IsTopmost;
            }
            finally
            {
                _updating = false;
            }
        }

        var tooltip = $"Sound Party Companion · {state.ConnectionStatus}";
        _notifyIcon.Text = tooltip.Length > 63 ? tooltip[..63] : tooltip;
    }

    public void ShowLowBatteryReminder(string deviceName, int percent, int threshold)
    {
        var displayName = string.IsNullOrWhiteSpace(deviceName)
            ? "Xiaomi蓝牙音箱Pro"
            : deviceName;
        _notifyIcon.BalloonTipTitle = threshold <= LowBatteryReminderRules.CriticalThreshold
            ? "音箱电量很低"
            : "音箱电量较低";
        _notifyIcon.BalloonTipText = $"{displayName} 当前剩余 {percent}%，建议及时充电。";
        _notifyIcon.BalloonTipIcon = Forms.ToolTipIcon.Warning;
        _notifyIcon.ShowBalloonTip(6000);
    }

    public void SetDiagnosticExportEnabled(bool enabled)
    {
        if (!_disposed) _diagnosticItem.Enabled = enabled;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _headerImage.Dispose();
        _icon.Dispose();
        _disposed = true;
    }

    private Forms.ToolStripMenuItem AddItem(string text, Action action, string? tag = null)
    {
        var item = new Forms.ToolStripMenuItem(text)
        {
            ForeColor = MenuText,
            // Keep the glyph inside the selected surface, not on its 9-DIP edge.
            Padding = new Forms.Padding(22, 7, 9, 7),
            Tag = tag
        };
        item.Click += (_, _) => action();
        _menu.Items.Add(item);
        return item;
    }

    private static Icon CreateTrayIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.Clear(Color.Transparent);
            DrawSpeakerGlyph(graphics, new Rectangle(8, 3, 16, 26), 6, 1.5f);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var fromHandle = Icon.FromHandle(handle);
            return (Icon)fromHandle.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static Bitmap CreateHeaderImage()
    {
        var bitmap = new Bitmap(64, 64);
        using var graphics = Graphics.FromImage(bitmap);
        using var tile = new SolidBrush(Color.FromArgb(57, 59, 56));
        using var background = RoundedRectangle(new Rectangle(0, 0, 64, 64), 20);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.Clear(Color.Transparent);
        graphics.FillPath(tile, background);
        DrawSpeakerGlyph(graphics, new Rectangle(20, 9, 24, 46), 8, 2f);
        return bitmap;
    }

    private static void DrawSpeakerGlyph(
        Graphics graphics,
        Rectangle bounds,
        int cornerRadius,
        float outlineWidth)
    {
        using var body = new SolidBrush(Color.FromArgb(31, 36, 37));
        using var outline = new Pen(Color.FromArgb(238, 234, 228), outlineWidth);
        using var accent = new SolidBrush(Color.FromArgb(196, 105, 70));
        using var bodyPath = RoundedRectangle(bounds, cornerRadius);

        graphics.FillPath(body, bodyPath);
        graphics.DrawPath(outline, bodyPath);

        var accentWidth = Math.Max(2, bounds.Width / 6);
        var accentInset = Math.Max(2, bounds.Width / 7);
        var accentBounds = new Rectangle(
            bounds.Right - accentInset - accentWidth,
            bounds.Top + bounds.Height / 5,
            accentWidth,
            bounds.Height * 3 / 5);
        using var accentPath = RoundedRectangle(accentBounds, Math.Max(1, accentWidth / 2));
        graphics.FillPath(accent, accentPath);
    }

    private static GraphicsPath RoundedRectangle(Rectangle rectangle, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void InvokeOnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.Invoke(action);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    private sealed class RoundedContextMenuStrip : Forms.ContextMenuStrip
    {
        private const int ClassStyleDropShadow = 0x00020000;
        private bool _fixedLayout;

        protected override Forms.CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.ClassStyle &= ~ClassStyleDropShadow;
                return parameters;
            }
        }

        public void FixItemWidths()
        {
            var preferredSize = GetPreferredSize(Size.Empty);
            AutoSize = false;
            Size = preferredSize;
            foreach (Forms.ToolStripItem item in Items)
            {
                if (item is not Forms.ToolStripMenuItem) continue;
                var height = item.GetPreferredSize(Size.Empty).Height;
                item.AutoSize = false;
                item.Size = new Size(Width, height);
            }
            _fixedLayout = true;
            PerformLayout();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateRoundedRegion();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (_fixedLayout)
            {
                foreach (Forms.ToolStripItem item in Items)
                {
                    if (item is Forms.ToolStripMenuItem) item.Width = Width;
                }
            }
            UpdateRoundedRegion();
        }

        private void UpdateRoundedRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            var radius = (int)Math.Round(16d * DeviceDpi / 96d);
            using var outline = RoundedRectangle(new Rectangle(0, 0, Width, Height), radius);
            var oldRegion = Region;
            Region = new Region(outline);
            oldRegion?.Dispose();
        }
    }

    private sealed class TrayMenuRenderer : Forms.ToolStripProfessionalRenderer
    {
        public TrayMenuRenderer() : base(new TrayColorTable()) { }

        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
        {
            if (e.Item is Forms.ToolStripMenuItem)
            {
                // ToolStripMenuItem's internal layout ignores Padding for its text.
                // Position it explicitly inside the rounded selection surface.
                var scale = e.Graphics.DpiX / 96f;
                var left = (int)Math.Round(22 * scale);
                var right = (int)Math.Round((e.Item.Tag as string == "toggle" ? 57 : 22) * scale);
                var verticalInset = Math.Max(1, (int)Math.Round(4 * scale));
                e.TextRectangle = new Rectangle(left, verticalInset,
                    Math.Max(0, e.Item.Width - left - right), Math.Max(0, e.Item.Height - 2 * verticalInset));
                // The old internal text rectangle was only 18px high at the top
                // of a 34px row. Center in the same surface used by the background.
                e.TextFormat = (e.TextFormat & ~(Forms.TextFormatFlags.Bottom | Forms.TextFormatFlags.WordBreak))
                    | Forms.TextFormatFlags.VerticalCenter | Forms.TextFormatFlags.SingleLine;
            }
            else if (e.Item is Forms.ToolStripLabel { Image: not null })
            {
                // Preserve the label's image/text layout and align the title to status.
                var shift = (int)Math.Round(4 * e.Graphics.DpiX / 96f);
                e.TextRectangle = new Rectangle(e.TextRectangle.Left + shift, e.TextRectangle.Top,
                    Math.Max(0, e.TextRectangle.Width - shift), e.TextRectangle.Height);
            }
            base.OnRenderItemText(e);
        }

        protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
        {
            var item = e.Item;
            var isPrimary = item.Tag as string == "primary";
            var color = item.Selected
                ? Color.FromArgb(91, 70, 59)
                : isPrimary ? Color.FromArgb(76, 58, 49) : MenuBackground;
            var scale = e.Graphics.DpiX / 96f;
            var horizontalInset = (int)Math.Round(9 * scale);
            var verticalInset = Math.Max(1, (int)Math.Round(4 * scale));
            var radius = (int)Math.Round(7 * scale);
            using var brush = new SolidBrush(color);
            using var shape = RoundedRectangle(
                new Rectangle(
                    horizontalInset,
                    verticalInset,
                    item.Width - horizontalInset * 2,
                    item.Height - verticalInset * 2),
                radius);
            e.Graphics.FillPath(brush, shape);

            if (item.Tag as string == "toggle" && item is Forms.ToolStripMenuItem toggle)
            {
                var trackWidth = (int)Math.Round(28 * scale);
                var trackHeight = (int)Math.Round(17 * scale);
                var trackInset = (int)Math.Round(17 * scale);
                var knobSize = (int)Math.Round(11 * scale);
                var knobInset = (int)Math.Round(3 * scale);
                var track = new Rectangle(
                    item.Width - trackInset - trackWidth,
                    (item.Height - trackHeight) / 2,
                    trackWidth,
                    trackHeight);
                using var trackBrush = new SolidBrush(toggle.Checked
                    ? Color.FromArgb(177, 107, 80) : Color.FromArgb(91, 94, 90));
                using var trackPath = RoundedRectangle(track, trackHeight / 2);
                e.Graphics.FillPath(trackBrush, trackPath);
                using var knob = new SolidBrush(Color.FromArgb(245, 240, 234));
                var knobX = toggle.Checked
                    ? track.Right - knobSize - knobInset
                    : track.Left + knobInset;
                e.Graphics.FillEllipse(knob, knobX, track.Top + knobInset, knobSize, knobSize);
            }
        }

        protected override void OnRenderItemCheck(Forms.ToolStripItemImageRenderEventArgs e)
        {
            // The topmost state is rendered as a switch at the right edge.
        }

        protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip is not RoundedContextMenuStrip) return;
            var bounds = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            var radius = (int)Math.Round(16d * e.Graphics.DpiX / 96d);
            using var outline = RoundedRectangle(bounds, radius);
            using var border = new Pen(Color.FromArgb(69, 72, 68));
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.DrawPath(border, outline);
        }
    }

    private sealed class TrayColorTable : Forms.ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => MenuBackground;
        public override Color MenuBorder => Color.FromArgb(69, 72, 68);
        public override Color MenuItemBorder => MenuBackground;
        public override Color MenuItemSelected => Color.FromArgb(91, 70, 59);
        public override Color SeparatorDark => Color.FromArgb(59, 62, 58);
        public override Color SeparatorLight => MenuBackground;
    }
}

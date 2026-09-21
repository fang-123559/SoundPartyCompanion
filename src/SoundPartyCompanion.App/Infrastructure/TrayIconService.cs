using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace SoundPartyCompanion.App.Infrastructure;

public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ToolStripMenuItem _showItem;
    private readonly Forms.ToolStripMenuItem _topmostItem;
    private readonly Action _showOrHide;
    private readonly Action<bool> _setTopmost;
    private bool _updating;
    private bool _disposed;

    public TrayIconService(
        Action showOrHide,
        Action<bool> setTopmost,
        Action openSettings,
        Action openBluetoothSettings,
        Action exitApplication)
    {
        _showOrHide = showOrHide ?? throw new ArgumentNullException(nameof(showOrHide));
        _setTopmost = setTopmost ?? throw new ArgumentNullException(nameof(setTopmost));
        ArgumentNullException.ThrowIfNull(openSettings);
        ArgumentNullException.ThrowIfNull(openBluetoothSettings);
        ArgumentNullException.ThrowIfNull(exitApplication);

        _showItem = new Forms.ToolStripMenuItem("隐藏组件");
        _showItem.Click += (_, _) => InvokeOnUi(_showOrHide);
        _topmostItem = new Forms.ToolStripMenuItem("始终置顶")
        {
            CheckOnClick = true
        };
        _topmostItem.CheckedChanged += (_, _) =>
        {
            if (!_updating)
            {
                InvokeOnUi(() => _setTopmost(_topmostItem.Checked));
            }
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(_showItem);
        menu.Items.Add(_topmostItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("打开设置", null, (_, _) => InvokeOnUi(openSettings));
        menu.Items.Add("打开蓝牙设置", null, (_, _) => InvokeOnUi(openBluetoothSettings));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => InvokeOnUi(exitApplication));

        _notifyIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = SystemIcons.Application,
            Text = "Sound Party Companion",
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => InvokeOnUi(_showOrHide);
    }

    public void Update(bool isWindowVisible, bool isTopmost)
    {
        _showItem.Text = isWindowVisible ? "隐藏组件" : "显示组件";

        if (_topmostItem.Checked != isTopmost)
        {
            _updating = true;
            try
            {
                _topmostItem.Checked = isTopmost;
            }
            finally
            {
                _updating = false;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _disposed = true;
    }

    private static void InvokeOnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
    }
}

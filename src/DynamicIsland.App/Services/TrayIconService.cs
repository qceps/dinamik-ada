using System.IO;
using System.Windows.Forms;
using DynamicIsland.Core.Diagnostics;

namespace DynamicIsland.App.Services;

/// <summary>Sistem tepsisi simgesi ve Türkçe bağlam menüsü (görev çubuğunda pencere yok).</summary>
public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _visibilityItem;
    private readonly ToolStripMenuItem _gameModeItem;
    private readonly ToolStripMenuItem _interactionItem;
    private readonly System.Drawing.Icon? _iconImage;

    public TrayIconService(bool isDemo)
    {
        _iconImage = LoadIcon();
        var menu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true };
        var settingsItem = new ToolStripMenuItem("Ayarlar…", null, (_, _) => SettingsRequested?.Invoke());
        settingsItem.Font = new System.Drawing.Font(settingsItem.Font, System.Drawing.FontStyle.Bold);
        _visibilityItem = new ToolStripMenuItem("Adayı göster", null, (_, _) => ToggleVisibilityRequested?.Invoke());
        _gameModeItem = new ToolStripMenuItem("Oyun modu (şu an etkin değil)") { Enabled = false };
        _interactionItem = new ToolStripMenuItem("Etkileşim modu", null, (_, _) => ToggleInteractionRequested?.Invoke());
        menu.Items.Add(settingsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_visibilityItem);
        menu.Items.Add(_interactionItem);
        menu.Items.Add(_gameModeItem);
        menu.Items.Add(new ToolStripSeparator());
        _updateItem = new ToolStripMenuItem("Güncelle", null, (_, _) => UpdateRequested?.Invoke()) { Visible = false };
        menu.Items.Add(_updateItem);
        menu.Items.Add(new ToolStripMenuItem("Çıkış", null, (_, _) => ExitRequested?.Invoke()));

        _icon = new NotifyIcon
        {
            Icon = _iconImage ?? System.Drawing.SystemIcons.Application,
            Text = isDemo ? "Dinamik Ada (Demo modu)" : "Dinamik Ada — Spotify",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                SettingsRequested?.Invoke();
            }
        };
        _icon.BalloonTipClicked += (_, _) =>
        {
            var action = _balloonAction;
            _balloonAction = null;
            action?.Invoke();
        };
        _icon.BalloonTipClosed += (_, _) => _balloonAction = null;
    }

    private readonly ToolStripMenuItem _updateItem;
    private Action? _balloonAction;

    /// <summary>Tepsi menüsündeki "Güncelle" öğesi (null: gizle).</summary>
    public void SetUpdateAvailable(string? version)
    {
        _updateItem.Visible = version is not null;
        _updateItem.Text = version is null ? "Güncelle" : $"Güncelle ({version})";
    }

    /// <summary>Bilgi bildirimi; tıklanırsa <paramref name="onClick"/> çalışır.</summary>
    public void ShowInfo(string title, string text, Action? onClick = null)
    {
        try
        {
            _balloonAction = onClick;
            _icon.ShowBalloonTip(6000, title, text, ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            Log.Warn("Tepsi bildirimi gösterilemedi", ex);
        }
    }

    public event Action? UpdateRequested;

    public event Action? SettingsRequested;
    public event Action? ToggleVisibilityRequested;
    public event Action? ToggleInteractionRequested;
    public event Action? ExitRequested;

    public void UpdateState(bool userHidden, bool gameModeActive, bool interactionMode)
    {
        _visibilityItem.Checked = !userHidden;
        _gameModeItem.Text = gameModeActive ? "Oyun modu: etkin (tıklamalar geçer)" : "Oyun modu: etkin değil";
        _interactionItem.Checked = interactionMode;
    }

    public void ShowNotice(string title, string text)
    {
        try
        {
            _icon.ShowBalloonTip(4000, title, text, ToolTipIcon.Warning);
        }
        catch (Exception ex)
        {
            Log.Warn("Tepsi bildirimi gösterilemedi", ex);
        }
    }

    private static System.Drawing.Icon? LoadIcon()
    {
        try
        {
            var info = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
            if (info is null)
            {
                return null;
            }

            using var stream = info.Stream;
            var size = SystemInformation.SmallIconSize;
            return new System.Drawing.Icon(stream, size);
        }
        catch (Exception ex)
        {
            Log.Warn("Tepsi simgesi yüklenemedi", ex);
            return null;
        }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _iconImage?.Dispose();
    }
}

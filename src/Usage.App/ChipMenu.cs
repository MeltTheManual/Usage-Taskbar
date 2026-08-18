using System.Windows.Forms;
using Usage.Core;

namespace Usage.App;

/// <summary>
/// The right-click menu for the chip. This stayed a WinForms ContextMenuStrip on purpose: it owns its own
/// focus and dismissal, which a WPF menu on a never-activating window does not handle nearly as cleanly.
/// </summary>
internal sealed class ChipMenu : IDisposable
{
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _claudeHeader;
    private readonly ToolStripMenuItem _codexHeader;
    private readonly ToolStripMenuItem _noLogins;
    private readonly ToolStripMenuItem _startWithWindows;

    public ChipMenu(Action quit, Action refresh)
    {
        _claudeHeader = new ToolStripMenuItem("Claude --") { Enabled = false };
        _codexHeader = new ToolStripMenuItem("Codex --") { Enabled = false };
        _noLogins = new ToolStripMenuItem("No Claude Code or Codex login found")
        {
            Enabled = false,
            Visible = false
        };

        _startWithWindows = new ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
            Checked = WindowsStartup.IsRegistered()
        };
        // Subscribed after Checked is seeded so reading the current state does not look like a user click.
        _startWithWindows.CheckedChanged += (_, _) => ApplyStartupChoice();

        _menu = new ContextMenuStrip();
        _menu.Items.Add(_claudeHeader);
        _menu.Items.Add(_codexHeader);
        _menu.Items.Add(_noLogins);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Refresh now", null, (_, _) => refresh()));
        _menu.Items.Add(_startWithWindows);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Quit Usage", null, (_, _) => quit()));

        // The close reason is the evidence for whether dismissal actually works. Clicking away should log
        // AppFocusChange. If that line never appears, the menu is not being told it lost focus.
        _menu.Opened += (_, _) => AppLog.Write("menu opened");
        _menu.Closed += (_, e) => AppLog.Write("menu closed: " + e.CloseReason);
    }

    public void Update(RemainingSnapshot snapshot)
    {
        // A provider that is not on this machine loses its header row entirely, rather than sitting there as a
        // blank line. If neither is present the menu says so once, so it is never just a bare Refresh button.
        _claudeHeader.Visible = !snapshot.Claude.IsHidden;
        _codexHeader.Visible = !snapshot.Codex.IsHidden;

        _claudeHeader.Text = ChipText.MenuText("Claude", snapshot.Claude);
        _codexHeader.Text = ChipText.MenuText("Codex", snapshot.Codex);

        var noneFound = snapshot.Claude.IsHidden && snapshot.Codex.IsHidden;
        _noLogins.Visible = noneFound;
    }

    /// <summary>True while the menu is on screen, so the chip does not fight its own menu for z-order.</summary>
    public bool IsOpen => _menu.Visible;

    public void ShowAtCursor()
    {
        try
        {
            _menu.Show(Cursor.Position);

            // A ContextMenuStrip only dismisses itself on an outside click while it owns the foreground.
            // The chip carries WS_EX_NOACTIVATE and never activates, so right-clicking it leaves the
            // foreground with whatever the user was already in, nothing tells the menu it lost focus, and it
            // sits there until something inside it is clicked. Claiming the foreground for the dropdown
            // itself restores normal behaviour. This is allowed here because the click that opened the menu
            // is the last input event, which is exactly the case SetForegroundWindow permits.
            NativeMethods.SetForegroundWindow(_menu.Handle);
        }
        catch (Exception ex)
        {
            AppLog.Write("menu " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    public void Dispose() => _menu.Dispose();

    private void ApplyStartupChoice()
    {
        try
        {
            if (_startWithWindows.Checked)
            {
                WindowsStartup.Register(InstallLocation.Exe);
            }
            else
            {
                WindowsStartup.Unregister();
            }

            AppLog.Write("startup " + (_startWithWindows.Checked ? "on" : "off"));
        }
        catch (Exception ex)
        {
            AppLog.Write("startup toggle " + ex.GetType().Name + ": " + ex.Message);
        }
    }
}

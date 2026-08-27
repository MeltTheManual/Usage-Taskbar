using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Usage.Core;

namespace Usage.App;

public partial class App : System.Windows.Application
{
    private const string UiMutexName = @"Local\MeltTheManual.Usage.UI";
    private const string WatchMutexName = @"Local\MeltTheManual.Usage.Watch";

    /// <summary>Set by the UI when the user picks Quit, so the watcher stands down instead of restarting it.</summary>
    private const string QuitEventName = @"Local\MeltTheManual.Usage.Quit";

    private static readonly TimeSpan NormalWatchInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan BackoffWatchInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RestartWindow = TimeSpan.FromMinutes(2);
    private const int MaxRestartsPerWindow = 5;

    private Mutex? _mutex;
    private EventWaitHandle? _quitSignal;
    private ChipWindow? _chip;
    private ChipMenu? _menu;
    private RemainingClient? _client;
    private DisplaySettings? _settings;
    private RemainingSnapshot? _lastRaw;
    private RemainingSnapshot? _lastGood;
    private DispatcherTimer? _refreshTimer;
    private DispatcherTimer? _watchTimer;
    private DispatcherTimer? _uiQuitTimer;
    private int _restarts;
    private DateTimeOffset _windowStart = DateTimeOffset.MinValue;
    private bool _quitting;
    private bool _isUi;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        HookCrashLogs();

        try
        {
            // Developer switch: render the hover card to a PNG and exit. Checked before anything else so it
            // never registers startup and never disturbs a copy that is already running.
            var previewIndex = Array.FindIndex(e.Args, a =>
                string.Equals(a.Trim(), "--card-preview", StringComparison.OrdinalIgnoreCase));
            if (previewIndex >= 0 && previewIndex + 1 < e.Args.Length)
            {
                var sample = Array.Exists(e.Args, a =>
                    string.Equals(a.Trim(), "--sample", StringComparison.OrdinalIgnoreCase));
                RunCardPreview(e.Args[previewIndex + 1], sample);
                Shutdown();
                return;
            }

            // Stops a running copy from outside, which the installer and uninstaller both need so they are
            // never fighting a locked exe. Signals the same event the Quit menu item uses and exits at once.
            if (Array.Exists(e.Args, a => string.Equals(a.Trim(), "--quit", StringComparison.OrdinalIgnoreCase)))
            {
                RequestQuitFromOutside();
                Shutdown();
                return;
            }

            _quitSignal = new EventWaitHandle(false, EventResetMode.ManualReset, QuitEventName, out _);

            var args = e.Args.Select(a => a.Trim()).ToArray();
            var watch = args.Contains("--watch", StringComparer.OrdinalIgnoreCase);
            var ui = args.Contains("--ui", StringComparer.OrdinalIgnoreCase);

            if (!watch && !ui)
            {
                EnsureRole("--watch");
                EnsureRole("--ui");
                Shutdown();
                return;
            }

            if (watch)
            {
                StartWatcher();
                return;
            }

            StartUi();
        }
        catch (Exception ex)
        {
            AppLog.Write("startup " + ex.GetType().Name + ": " + ex.Message);
            throw;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _quitting = true;
        _refreshTimer?.Stop();
        _watchTimer?.Stop();
        _uiQuitTimer?.Stop();
        _chip?.StopPlacing();
        _chip?.Close();
        _menu?.Dispose();
        _client?.Dispose();
        _quitSignal?.Dispose();
        TryReleaseMutex();
        AppLog.Write(_isUi ? "ui exit" : "watch exit");
        base.OnExit(e);
    }

    private void StartWatcher()
    {
        _mutex = new Mutex(true, WatchMutexName, out var created);
        if (!created)
        {
            AppLog.Write("watch already running");
            Shutdown();
            return;
        }

        WindowsStartup.EnsureFirstRunRegistration(InstallLocation.Exe);
        AppLog.Write("watch started pid=" + Environment.ProcessId);
        EnsureRole("--ui");

        _watchTimer = new DispatcherTimer { Interval = NormalWatchInterval };
        _watchTimer.Tick += (_, _) => WatchTick();
        _watchTimer.Start();
    }

    private void WatchTick()
    {
        try
        {
            if (_quitSignal?.WaitOne(0) == true)
            {
                AppLog.Write("watch stopping because quit was requested");
                Shutdown();
                return;
            }

            if (MutexExists(UiMutexName))
            {
                // The UI is healthy. Once it has been up for a full window, forget the earlier failures.
                if (_restarts > 0 && DateTimeOffset.UtcNow - _windowStart > RestartWindow)
                {
                    _restarts = 0;
                    _watchTimer!.Interval = NormalWatchInterval;
                    AppLog.Write("ui is stable again, back to the normal watch interval");
                }

                return;
            }

            var now = DateTimeOffset.UtcNow;
            if (now - _windowStart > RestartWindow)
            {
                _windowStart = now;
                _restarts = 0;
            }

            _restarts++;
            if (_restarts > MaxRestartsPerWindow)
            {
                // Something is genuinely broken. Keep trying, but stop spawning a process every 3 seconds.
                if (_watchTimer!.Interval != BackoffWatchInterval)
                {
                    _watchTimer.Interval = BackoffWatchInterval;
                    AppLog.Write("ui keeps dying, backing off to a 5 minute retry");
                }
            }

            AppLog.Write("watch restarting ui attempt=" + _restarts);
            EnsureRole("--ui");
        }
        catch (Exception ex)
        {
            AppLog.Write("watch tick " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private void StartUi()
    {
        _isUi = true;
        _mutex = new Mutex(true, UiMutexName, out var created);
        if (!created)
        {
            AppLog.Write("ui already running");
            Shutdown();
            return;
        }

        System.Windows.Forms.Application.EnableVisualStyles();

        _client = new RemainingClient(log: AppLog.Write);
        _settings = DisplaySettings.Load();
        _menu = new ChipMenu(QuitEverything, OnDisplayChanged, _settings);
        _chip = new ChipWindow(_menu);
        _chip.Show();
        AppLog.Write("ui started pid=" + Environment.ProcessId);
        _ = RefreshSafeAsync();

        _refreshTimer = new DispatcherTimer { Interval = RemainingClient.RefreshInterval };
        _refreshTimer.Tick += async (_, _) => await RefreshSafeAsync();
        _refreshTimer.Start();

        // The UI watches the same quit event the watcher does. Without this, a quit raised from anywhere other
        // than this process stopped the watcher and left the chip sitting on the taskbar, which is exactly what
        // happened the first time a running copy was replaced by hand.
        _uiQuitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _uiQuitTimer.Tick += (_, _) =>
        {
            if (_quitSignal?.WaitOne(0) == true && !_quitting)
            {
                AppLog.Write("ui stopping because quit was requested");
                _quitting = true;
                Shutdown();
            }
        };
        _uiQuitTimer.Start();
    }

    /// <summary>
    /// Sets the shared quit event without starting any part of the app, behind <c>--quit</c>. The installer and
    /// uninstaller both use it so they never have to fight a locked exe or kill anything. If no copy is running
    /// there is no event to open, and that is a success rather than a failure.
    /// </summary>
    private static void RequestQuitFromOutside()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(QuitEventName, out var running))
            {
                using (running)
                {
                    running.Set();
                }

                AppLog.Write("quit requested from outside");
            }
            else
            {
                AppLog.Write("quit requested from outside, nothing was running");
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("outside quit " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static void RunCardPreview(string path, bool sample)
    {
        try
        {
            RemainingSnapshot snapshot;
            if (sample)
            {
                snapshot = SampleSnapshot();
            }
            else
            {
                using var client = new RemainingClient();
                var settings = DisplaySettings.Load();
                snapshot = client.FetchAsync(settings.ShowClaude, settings.ShowCodex).GetAwaiter().GetResult();
                snapshot = settings.Apply(snapshot);
            }

            CardPreview.Save(snapshot, path);
            Console.WriteLine("card preview written to " + path);
        }
        catch (Exception ex)
        {
            Console.WriteLine("card preview failed: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    /// <summary>
    /// Invented but realistic numbers for documentation screenshots, behind <c>--sample</c>.
    /// A real reading is whatever the account happens to be at, which is right for checking a layout and
    /// wrong for a README, where an empty meter reads as a broken app rather than a used-up week. Reset
    /// times are relative to now so a regenerated screenshot never shows a date in the past. Codex gets no
    /// five-hour window here because it does not report one, and the picture should match the real card.
    /// </summary>
    private static RemainingSnapshot SampleSnapshot()
    {
        var midnight = new DateTimeOffset(DateTime.Today, DateTimeOffset.Now.Offset);
        return new RemainingSnapshot(
            new ProviderReading(ReadingStatus.Ok, 62, midnight.AddDays(3).AddHours(3), 88, DateTimeOffset.Now.AddHours(3)),
            new ProviderReading(ReadingStatus.Ok, 45, midnight.AddDays(5).AddHours(15), null, null));
    }

    private void QuitEverything()
    {
        AppLog.Write("quit requested");
        _quitting = true;

        try
        {
            _quitSignal?.Set();
        }
        catch (Exception ex)
        {
            AppLog.Write("quit signal " + ex.GetType().Name + ": " + ex.Message);
        }

        Shutdown();
    }

    private async Task RefreshSafeAsync()
    {
        if (_client is null || _quitting)
        {
            return;
        }

        RemainingSnapshot snapshot;
        try
        {
            snapshot = await _client.FetchAsync(
                includeClaude: _settings?.ShowClaude ?? true,
                includeCodex: _settings?.ShowCodex ?? true).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AppLog.Write("refresh " + ex.GetType().Name + ": " + ex.Message);
            snapshot = new RemainingSnapshot(ProviderReading.Unavailable(), ProviderReading.Unavailable());
        }

        try
        {
            snapshot = MarkStaleIfNeeded(snapshot, _lastGood);
            _lastRaw = snapshot;
            if (snapshot.Claude.Status == ReadingStatus.Ok || snapshot.Codex.Status == ReadingStatus.Ok)
            {
                _lastGood = snapshot;
            }

            ShowVisible(snapshot);
        }
        catch (Exception ex)
        {
            AppLog.Write("apply " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    /// <summary>
    /// Instant chip update when Show Claude or Show Codex is toggled, then a refresh so a newly shown
    /// provider gets a real number instead of waiting for the three-minute timer.
    /// </summary>
    private void OnDisplayChanged()
    {
        ShowVisible(_lastRaw);
        _ = RefreshSafeAsync();
    }

    private void ShowVisible(RemainingSnapshot? raw)
    {
        var snapshot = _settings?.Apply(raw ?? new RemainingSnapshot(
            ProviderReading.Hidden(),
            ProviderReading.Hidden())) ?? raw;
        if (snapshot is null)
        {
            return;
        }

        _menu?.Update(snapshot);
        _chip?.Apply(snapshot);
    }

    private static RemainingSnapshot MarkStaleIfNeeded(RemainingSnapshot current, RemainingSnapshot? previous)
    {
        return new RemainingSnapshot(
            Soften(current.Claude, previous?.Claude),
            Soften(current.Codex, previous?.Codex));
    }

    private static ProviderReading Soften(ProviderReading current, ProviderReading? previous)
    {
        if (current.Status != ReadingStatus.Unavailable)
        {
            return current;
        }

        return previous?.Status == ReadingStatus.Ok ? ProviderReading.Stale() : current;
    }

    private static void EnsureRole(string role)
    {
        var exe = InstallLocation.Exe;
        if (!File.Exists(exe))
        {
            exe = Environment.ProcessPath ?? "Usage.exe";
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            Arguments = role,
            WorkingDirectory = Path.GetDirectoryName(exe) ?? InstallLocation.Folder,
            UseShellExecute = false
        });
    }

    private static bool MutexExists(string name)
    {
        try
        {
            using var existing = Mutex.OpenExisting(name);
            return true;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private void TryReleaseMutex()
    {
        try
        {
            _mutex?.ReleaseMutex();
        }
        catch (Exception)
        {
            // Abandoned mutex is fine on exit.
        }

        _mutex?.Dispose();
    }

    private static void HookCrashLogs()
    {
        Current.DispatcherUnhandledException += (_, args) =>
        {
            AppLog.Write("dispatcher " + args.Exception.GetType().Name + ": " + args.Exception.Message);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                AppLog.Write("domain " + ex.GetType().Name + ": " + ex.Message);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog.Write("task " + args.Exception.GetType().Name + ": " + args.Exception.Message);
            args.SetObserved();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => AppLog.Write("process exit");
    }
}

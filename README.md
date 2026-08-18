# Usage

A small always-on readout of how much Claude Code and Codex you have left, parked on your Windows taskbar just
left of the clock.

```
Claude 45% · Codex 14%
```

Those are percentages **remaining**, not spent. Hover the text for a card with a meter per limit window and the
exact time each one resets. Right-click it for a manual refresh, a start-with-Windows toggle, and quit.

![The hover card](docs/hover-card.png)

## Why

Both tools tell you where you stand only if you go and ask them. If you work in them all day you end up either
checking constantly or getting surprised by a wall. This puts the number somewhere you already look, and it
never guesses: if a reading cannot be trusted it says so in words instead of showing a stale percent.

## Install

1. Download `Usage-Setup-1.0.0.exe` from the
   [latest release](https://github.com/MeltTheManual/usage-taskbar/releases/latest).
2. Run it and pick where to install.

A normal installer. It asks for a location, adds a Start Menu entry so you can find it by typing "Usage", and
registers properly in Add or Remove Programs. Nothing else is needed: .NET is built into the app, so there is
no runtime to install separately.

It installs for the current user by default, so there is no admin prompt. You can choose an all-users install
from the wizard if you would rather have it in Program Files.

It starts with Windows from then on. You can turn that off from the right-click menu and it will stay off, and
you can always start it again from the Start Menu.

**To remove it:** Add or Remove Programs, or the uninstaller in the install folder. It stops the app, deletes
its files, removes the Start Menu shortcut, clears the startup entry, and removes its small settings folder at
`%LOCALAPPDATA%\Usage`. Nothing is left behind.

**There is nothing to configure.** It finds whatever is already signed in on your account and shows that. If you
only use one of the two tools, the other one simply does not appear: no empty row, no "sign in" nag telling you
to install something you never asked for. If neither is present it says so once instead of pretending.

You need to already be signed in to Claude Code or Codex, or both. Usage does not have a login of its own and
will never ask you for one. It reads whatever the current Windows user has, so on a shared PC each person sees
only their own numbers.

## What it reads, and what it will never do

This app looks at the login files the official CLIs keep on your machine, so it is worth being precise about
what that means.

It reads exactly two files, and only reads them:

- `%USERPROFILE%\.claude\.credentials.json`
- `%USERPROFILE%\.codex\auth.json`

`%USERPROFILE%` is resolved at runtime from whoever is running the app, so nothing about any other machine or
account is baked into the build. There is no account id, licence key, or install fingerprint anywhere in it.

It sends the access token it finds to exactly two places, which are the same endpoints the official tools use:

- `https://api.anthropic.com/api/oauth/usage`
- `https://chatgpt.com/backend-api/wham/usage`

It never writes to those files, never refreshes or rotates your tokens, and never sends them anywhere else.
There is no telemetry, no analytics, and no server belonging to this project. Nothing leaves your machine
except those two requests.

That read-only rule is enforced by a test, not just by good intentions. `Never_writes_to_the_login_files` fails
the build if any outbound write is attempted, and it compares the bytes and modification times of both login
files across a full fetch. Earlier versions did try to refresh expiring tokens. That was removed on purpose:
Anthropic rotates refresh tokens as single use, so a successful refresh here would have invalidated the copy
Claude Code was holding and could have signed you out of the tool you were working in. An observer does not get
to touch another program's credentials.

The log it writes to `%TEMP%\Usage.log` records status words and exception types only. No tokens, ever.

If you would rather check all of that yourself than take a README's word for it, that is the correct instinct
and the whole thing is about 1,500 lines. Start at `src/Usage.Core/RemainingClient.cs`.

## Requirements

- Windows. Built and used daily on Windows 11.
- Claude Code or Codex already signed in.

Honest limits: this was written for one machine and then made public, so the tested surface is narrow. It has
only been run at 100% display scaling, and on multi-monitor setups it follows the primary taskbar. Windows 10
is untested. If it misbehaves on your setup, an issue with your Windows version, scaling, and taskbar position
is genuinely useful.

## Build from source

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
dotnet test Usage.sln -c Release             # 26 tests
powershell -File scripts\Publish-Usage.ps1   # fast local build, needs .NET installed to run
powershell -File scripts\Publish-Release.ps1 # the self-contained single file
powershell -File scripts\Build-Installer.ps1 # the installer that ships
```

`Publish-Usage.ps1` writes a small framework-dependent build to `dist\`. `Publish-Release.ps1` writes the
single self-contained `Usage.exe` to `out\release\` and prints its SHA256. `Build-Installer.ps1` republishes
that exe and wraps it with [Inno Setup](https://jrsoftware.org/isinfo.php), which you will need installed
(`winget install --id JRSoftware.InnoSetup -e`).

The app does not care where it is installed. It works out its own location from the running exe, and anything
it writes at runtime goes to `%LOCALAPPDATA%\Usage` instead of next to the exe, so an install into Program
Files behaves the same as a per-user one.

There is also a probe that prints the readings once and exits, which is the fastest way to tell a bad reading
apart from a bad display:

```powershell
dotnet run --project src\Usage.Probe\Usage.Probe.csproj -c Release
```

And a switch that renders the hover card to an image with live numbers, so you can work on its design without
holding a mouse still over a tooltip:

```powershell
out\release\Usage.exe --card-preview card.png           # live numbers
out\release\Usage.exe --card-preview card.png --sample  # invented numbers, for screenshots
```

`Usage.exe --quit` stops a running copy from outside, which is what the installer and uninstaller use so they
never have to kill anything.

## How it works, and the one trap worth knowing

Two processes. `--ui` draws the chip, `--watch` does nothing but restart the chip if it dies. Seeing two
`Usage` entries in Task Manager is correct.

The chip is a borderless always-on-top window positioned next to the clock, not a notification-area icon.
Notification icons were tried first and rejected, because Windows hides them behind the overflow arrow and an
icon cannot show two changing numbers.

**The trap, if you are going to touch the window code:** the taskbar is topmost too. `WS_EX_TOPMOST` is
membership of a band, not rank within it, and Explorer re-promotes the taskbar above everything in that band
whenever a full-screen app appears or leaves. So the chip does not stop painting, it gets buried, and it looks
identical to a rendering bug. `RaiseAboveTaskbar` re-asserts rank on every 250ms tick to heal it. Also, never
set that style with `SetWindowLongPtr` and believe the result: it reports the style back without applying it,
so the window reads as `TOPMOST=True` while sitting underneath the taskbar. That one cost four wrong diagnoses.

## Support, honestly

This was built for one machine and then shared because it might be useful to someone else. It is a side
project, not a product with a support team, so replies may be slow and some things may never get fixed.

Bug reports are genuinely welcome all the same, and the most useful ones are about setups I could not test:
display scaling other than 100%, multiple monitors, a taskbar that is not at the bottom, Windows 10, or shell
replacements like ExplorerPatcher. If the chip lands in the wrong place or does not show up, please say which
of those applies to you.

## Contributing

Issues and pull requests are welcome. Two things will make a change much easier to accept:

- Keep the app read-only with respect to the login files. That rule is not up for discussion, and there is a
  test that will stop you anyway.
- Never show a number that might not be true. If a reading fails, the honest status word is the feature.

`dotnet test Usage.sln -c Release` should pass before and after your change.

## License

MIT. See [LICENSE](LICENSE).

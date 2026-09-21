# Claude Usage for Command Palette

A PowerToys Command Palette extension that puts your Claude subscription usage in the **Dock**.

> Unofficial community project. Not affiliated with or endorsed by Anthropic or Microsoft.

- **In the Dock:** the 5-hour session limit, e.g. `🟢 30%  5h · 2h 14m` (🟡 from 70%, 🔴 from 90%).
- **Click it:** all limits (session, weekly, model-scoped such as Fable weekly) with reset countdowns, your plan, and, at the bottom, local token stats.

## Where the numbers come from

| Data | Source | Scope |
|---|---|---|
| Session / weekly / model limits, reset times | `GET https://api.anthropic.com/api/oauth/usage`, the same endpoint Claude Code's `/usage` uses | **Your account**, across claude.ai, desktop, and Claude Code |
| Plan label | `~/.claude/.credentials.json` | Local |
| Tokens by day / model, prompts, sessions | `~/.claude/projects/**/*.jsonl` transcripts | **This PC, Claude Code only** |

The extension reuses the OAuth token Claude Code already stores. The token goes only into the `Authorization` header of that one request. It is never logged, written, or refreshed. When it expires the band shows `⚠️ auth`, and starting Claude Code once refreshes it.

> The usage endpoint is undocumented and may change. It also rate-limits aggressive polling, so the default refresh interval is 5 minutes and 429 responses back off 3 → 6 → 12 → 15 min.

`CLAUDE_CONFIG_DIR` is honoured if your Claude Code config lives somewhere other than `~/.claude`.

## Requirements

- Windows 10 19041+ / Windows 11, PowerToys with Command Palette **0.9 or later** (Dock support)
- Signed in to Claude Code on this machine (a Pro/Max subscription login)

## Installation

### 1. Turn on the Dock

1. Install or update [PowerToys](https://github.com/microsoft/PowerToys/releases) and make sure **Command Palette** is enabled in PowerToys Settings.
2. Open Command Palette (default <kbd>Win</kbd>+<kbd>Alt</kbd>+<kbd>Space</kbd>) → **Settings** → **Dock (Preview)** → turn on **Enable Dock**.

### 2. Download

From the [latest release](https://github.com/xmsadik/claude-usage-cmdpal/releases/latest) download:
- `ClaudeUsageDev.cer`
- the package for your CPU: `ClaudeUsage_<version>_x64.msix` (Intel/AMD) or `ClaudeUsage_<version>_arm64.msix` (Arm, e.g. Snapdragon). Not sure? Run `$env:PROCESSOR_ARCHITECTURE` in PowerShell: `AMD64` → x64, `ARM64` → arm64.

### 3. Trust the certificate (once per machine)

The package is signed with a self-signed certificate, so Windows has to be told to trust it. In **PowerShell as Administrator**, in the download folder:

```powershell
Import-Certificate .\ClaudeUsageDev.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
```

### 4. Install

In a normal PowerShell window (or double-click the `.msix` and choose *Install*):

```powershell
Add-AppxPackage .\ClaudeUsage_0.1.0.0_x64.msix
```

### 5. Show it in the Dock

1. Open Command Palette and run **Reload** so it picks up the new extension.
2. The *Claude Usage* band usually appears in the Dock by itself. If it doesn't, search for **Claude Usage** in Command Palette, open its context menu and run **Pin to Dock** (choose the *Right* side to sit next to the system info).
3. Click the band for the detail view. Settings: search **Claude Usage** → *Settings*.

### Update

Download the newer `.msix` and run `Add-AppxPackage` again; the certificate step isn't needed again. Then **Reload** Command Palette.

### Uninstall

```powershell
Get-AppxPackage ClaudeUsage | Remove-AppxPackage
# optional, as Administrator: remove the trusted certificate
Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Subject -eq 'CN=ClaudeUsageDev' | Remove-Item
```

### Troubleshooting

| Symptom | Fix |
|---|---|
| `0x800B0109` / "the root certificate … is not trusted" on install | Step 3 was skipped or not run as Administrator. |
| `0x80073CFB` / "a package with the same identity is already installed" | A development build is registered: `Get-AppxPackage ClaudeUsage \| Remove-AppxPackage`, then install again. |
| Band doesn't appear | Check **Enable Dock** is on, run **Reload**, then use **Pin to Dock** as in step 5. |
| Band shows `⚠️ auth` | The Claude Code sign-in expired. Start Claude Code once (or run `claude auth login`); the band recovers on the next refresh. |
| Band shows `--%` for a long time | No usage data yet: Claude Code must be signed in on this machine with a Pro/Max account. |
| Clicking the band opens the palette instead of the flyout, or it stops updating | Command Palette host issues, see [Known host issues](#known-host-issues). **Reload** fixes both. |

## Settings

Command Palette → *Claude Usage* → *Settings*: **Refresh interval** (1, 2, 5, 10, 15, 30 min; default 5).

## Build from source

Needs the .NET 10 SDK. A full Windows SDK / Visual Studio is **not** required.

```powershell
dotnet test tests\ClaudeUsage.Tests -p:Platform=x64      # unit tests
.\scripts\dev-deploy.ps1                                 # build + register (Developer Mode on)
.\scripts\dev-deploy.ps1 -Remove                         # unregister
```

After deploying, run **Reload** in Command Palette. If the band does not appear by itself, use **Pin to Dock** (see [Installation](#5-show-it-in-the-dock)).

### MSIX package

```powershell
.\scripts\pack.ps1 -Sign        # dist\...\ClaudeUsage_<ver>_x64.msix + dist\ClaudeUsageDev.cer
```

`-Platform ARM64` builds the Arm package. The first `-Sign` run creates a self-signed `CN=ClaudeUsageDev` code-signing certificate in `Cert:\CurrentUser\My` and reuses it afterwards. Install the result as described in [Installation](#installation).

## Layout

```
src/ClaudeUsage/        Command Palette extension (Dock band, detail page, settings, UsageStore)
src/ClaudeUsage.Core/   Plain .NET library: credentials, API client, parser, transcript scanner, markdown
tests/ClaudeUsage.Tests xUnit tests for Core
scripts/                dev-deploy.ps1, pack.ps1
```

## Known host issues

These are Command Palette bugs, not bugs in this extension:
- [#50367](https://github.com/microsoft/PowerToys/issues/50367): after the host releases an idle extension, clicking a band opens the palette instead of the flyout.
- [#49688](https://github.com/microsoft/PowerToys/issues/49688): bands stop repainting after roughly 41 hours of uptime.

Running **Reload** in Command Palette works around both.

## License

[MIT](LICENSE). Parts derived from the PowerToys extension template are © Microsoft, MIT; see [NOTICE](NOTICE).

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

## Settings

Command Palette → *Claude Usage* → *Settings*: **Refresh interval** (1, 2, 5, 10, 15, 30 min; default 5).

## Build from source

Needs the .NET 10 SDK. A full Windows SDK / Visual Studio is **not** required.

```powershell
dotnet test tests\ClaudeUsage.Tests -p:Platform=x64      # unit tests
.\scripts\dev-deploy.ps1                                 # build + register (Developer Mode on)
.\scripts\dev-deploy.ps1 -Remove                         # unregister
```

After deploying, run **Reload** in Command Palette. If the band does not appear by itself, add it from the Dock's edit mode.

### MSIX package

```powershell
.\scripts\pack.ps1 -Sign        # dist\...\ClaudeUsage_<ver>_x64.msix + dist\ClaudeUsageDev.cer
```

The package is signed with a self-signed certificate (`CN=ClaudeUsageDev`). On each target machine, trust it once from an elevated prompt, then install:

```powershell
Import-Certificate .\ClaudeUsageDev.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
Add-AppxPackage .\ClaudeUsage_0.1.0.0_x64.msix
```

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

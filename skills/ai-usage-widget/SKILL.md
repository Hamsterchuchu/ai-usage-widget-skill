---
name: ai-usage-widget
description: Build, install, customize, and troubleshoot floating account usage widgets for Codex and Claude/Cowork on Windows. Use for desktop quota cards and their appearance settings; not per-message token meters or browser extensions.
---

# AI Usage Widget

Deliver a working companion card above the selected desktop app. This is a separate Windows WPF program; it does not modify the app or inject a browser extension. Read [references/setup.md](references/setup.md) for build/start commands and the experimental Claude bridge requirements.

## Install or update

- Supports Windows x64 with .NET Framework 4.x. Use the included C# source and `scripts/Build-Widget.ps1`; no package downloads or AI calls are needed to build.
- Build in the user's requested location, or a workspace `artifacts/` folder. Preserve existing widgets and settings when updating. Do not close the desktop app or its conversations.
- Start with `scripts/Start-Widget.ps1 -Provider codex` or `-Provider claude`. State files default to `%LOCALAPPDATA%\AIUsageWidget\<provider>`; use `-DataDirectory` for a workspace or portable installation.
- Codex uses the bundled CLI's stdio app-server and existing login. It only initializes a connection and calls `account/rateLimits/read`, once every two minutes. Prefer the `codex` bucket in `rateLimitsByLimitId`. Missing windows mean unknown, never zero. Do not read credential files, consume resets, or start a thread/turn.
- Claude/Cowork needs a compatible **desktop Mods SDK** and an open Code session. The included bridge uses engine-held authorization, not credential-file access. Its `hooks.modules` format is not a classic CLI hook. Generate it with `scripts/New-ClaudeBridge.ps1`; confirm that the installed runtime supports these APIs before installing. When unsupported, explain the limitation and complete any independent Codex work. Never patch app archives, integrity checks, or Windows access controls.

## Customize

The card's gear button opens live preview with Save, Cancel, and Reset. Settings are independent for each provider. The UI supports three themes, a hex accent color, text/display scaling, card width, opacity, used/remaining percentage, visible quota windows, and footer visibility.

For requests such as “make it larger and dark,” use the UI or edit only the requested keys in that provider's `settings.json`. Preserve unrelated keys and position. If editing the file, restart only the companion process after verifying its executable path; the file is loaded at startup. Never stop all processes named `codex` or `claude`.

Settings fields (case-sensitive):

| Field | Accepted values |
| --- | --- |
| `Theme` | `warm`, `mint`, `night` |
| `Accent` | `#RRGGBB` |
| `TextScale` | 0.9–1.5, scales text and layout together |
| `CardWidth` | 260–420 logical pixels, before scaling |
| `Opacity` | 0.65–1 |
| `Display` | `used`, `remaining` |
| `Windows` | `all`, `session` (shorter than a week), `weekly` |
| `ShowFooter` | boolean |

Percentages refer to account-wide quota. `remaining = 100 - used`; keep unknown values unknown, hide unavailable periods, and wait for fresh data after a reset. The appearance dialog must not trigger usage refreshes or AI calls.

## Verify and report

- Run the executable with `--self-test --data-dir <scratch-directory>` after changing code or calculation behavior. For bridge changes run the bundled `node --test scripts/tests/bridge.test.mjs` from this skill folder.
- Use `--render-preview --demo --data-dir <scratch-directory>` to export `preview.png` without starting a backend. `--settings-preview` also renders the settings panel. Demo values are synthetic and remain in memory.
- For an authorized real installation, verify the sanitized `usage.json` timestamp advances and the card's `widget-status.json` reports visibility with the selected app. Do not publish those runtime files.
- Report installed location, verified behavior, and any actual connection limitation. Do not claim Claude integration works on untested SDK versions.

The skill does not authorize publishing, startup registration, credential changes, or unrelated app modifications. Honor the user's requested scope for those operations.

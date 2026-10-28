# Setup reference

## Windows build

Run from the repository root in PowerShell:

```powershell
./skills/ai-usage-widget/scripts/Build-Widget.ps1
./skills/ai-usage-widget/scripts/Start-Widget.ps1 -Provider codex
```

Only Windows' built-in .NET Framework compiler and WPF libraries are used. The generated executable is `artifacts/AIUsageWidget.exe`. Both scripts accept explicit paths; no developer-specific directories are embedded. `Build-Widget.ps1 -OutputDirectory <folder>` chooses the executable directory. Runtime state is stored separately in `%LOCALAPPDATA%\AIUsageWidget\codex` unless `-DataDirectory` is provided.

The companion follows the foreground Codex app, including the packaged executable named `ChatGPT.exe` under an `OpenAI.Codex_...` package. It hides when another app is selected. It does not target the standalone ChatGPT package or CLI terminal. Claude targets `claude.exe`.

Drag the title to move it; minus folds it; close stops only the companion and its own read-only backend. No Windows startup or task scheduler settings are changed. After reboot, start the companion again.

## Codex usage connection

The CLI is discovered from `%LOCALAPPDATA%\OpenAI\Codex\bin\<version>\codex.exe`, newest directory first. It must already be installed and signed in. The app-server is started with `--listen stdio://`; it exposes no listening network port. It sends `initialize`, `initialized`, then `account/rateLimits/read`, and no thread or model requests. Refresh is two minutes; errors back off five minutes. Closing the widget kills only the child process it created.

Actual quota periods vary by account. The renderer supports one or two windows and uses returned durations, rather than inventing a five-hour limit. Credits, account IDs, email addresses, and raw responses are not exported.

For a one-time live connection check without displaying a card, run `AIUsageWidget.exe --check-connection --data-dir <scratch-directory>` and wait for its exit code. It writes a sanitized usage snapshot plus `connection-verification.json` and closes its own backend. It does not change the account or start an AI turn.

Official API reference: [Codex app-server](https://learn.chatgpt.com/docs/app-server).

## Claude / Cowork: experimental bridge

The working prototype was verified with Claude desktop's Mods SDK runtime reporting Claude Code **2.1.308**. This bridge requires `$.session.authorize`, `$.http.fetch`, `$.fs.write`, `$.clock.every`, and event registration. Availability can differ by desktop build. The ordinary CLI hook format uses event arrays and cannot load this bridge's `hooks.modules` directly.

Generate a fresh local plugin folder:

```powershell
./skills/ai-usage-widget/scripts/New-ClaudeBridge.ps1
./skills/ai-usage-widget/scripts/Start-Widget.ps1 -Provider claude
```

The generator writes to `artifacts/claude-usage-bridge` and refuses to overwrite an existing directory. Use `-OutputDirectory` and `-UsageFile` when choosing a portable location. The widget's `-DataDirectory` must be the directory containing that `usage.json`.

Install the generated folder through the compatible runtime's supported local plugin/Mods installation flow. Inspect that installed version's plugin help or desktop interface for the exact command; do not assume classic CLI hooks support it. Keep one desktop Code session open to power the connection while using Cowork. If the runtime does not expose this SDK, stop the bridge setup and report that Claude live usage is unavailable there. The widget can still preview or consume an explicitly supplied sanitized snapshot.

The bridge requests `https://api.anthropic.com/api/oauth/usage` with the engine-held authorization handle and the `anthropic-beta: oauth-2025-04-20` header. This beta endpoint and SDK are compatibility-sensitive. The module never accesses tokens, cookies, environment variables, conversations, or model calls. It exports only `five_hour`/`seven_day` percentage and reset fields plus timestamp/status. HTTP 429 honors Retry-After; authentication failures back off ten minutes; connection errors back off five minutes.

Do not install multiple bridge copies into simultaneously active sessions for the same output file. The per-module timer suppresses duplicate reads within that module, not across separate processes.

## Preview and testing

```powershell
./artifacts/AIUsageWidget.exe --preview --demo --data-dir ./artifacts/demo
./artifacts/AIUsageWidget.exe --render-preview --demo --data-dir ./artifacts/demo
./artifacts/AIUsageWidget.exe --render-preview --settings-preview --demo --data-dir ./artifacts/demo
./artifacts/AIUsageWidget.exe --self-test --data-dir ./artifacts/tests
node --test tests/bridge.test.mjs
```

GUI executables return asynchronously from interactive PowerShell; use `Start-Process -Wait -PassThru` when a check needs their exit code. Preview mode starts no usage backend. Demo values are generated in memory, never saved over live account data. Normal mode uses a single instance per provider, so another launch does not create another poller.

## Publishing

Publish only source, scripts, instructions, tests, and synthetic preview images. Exclude generated executables, shortcuts with machine paths, `usage.json`, `position.json`, `settings.json`, error logs, and verification files from source commits. The repository `.gitignore` already excludes them. Build and run checks before creating a release.

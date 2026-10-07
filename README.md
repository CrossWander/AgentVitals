# AgentVitals

A Windows tray app (Avalonia, .NET 10) that keeps an eye on your AI coding agents: how much of
each quota window is used and when it resets, what your Claude Code sessions are doing right now,
and whether Anthropic's services are healthy. Inspired by [ClaudeBar](https://github.com/tddworks/ClaudeBar)
and [claude-status-bar](https://github.com/m1ckc3s/claude-status-bar). Interface in English,
Ukrainian and Russian, light and dark.

## What it shows

- **Quotas.** The session, the week and per-model weekly limits, each as a card with the
  percentage used, a bar and the time left until it resets. Extra usage, if enabled.
- **The tray icon** is a pulse line coloured by the window that limits you most: green under 50%,
  amber to 80%, red above, grey when there is no data. An exhausted week turns it red even with an
  empty session. A toast comes once when the session or the week reaches 90%, and again only
  after that window resets.
- **Live sessions.** With the hooks connected, the pulse runs while Claude is working and a blue
  dot blinks while it waits for your permission.
- **Anthropic status** from status.claude.com, with a toast when it changes.

Hover the icon for a quick card, click it for the full popover, right-click for the menu.

## Where the data comes from

| Agent | Source | Status |
|---|---|---|
| Claude | `GET api.anthropic.com/api/oauth/usage` with the Claude Code token from `~/.claude/.credentials.json` | working |
| Codex | `GET chatgpt.com/backend-api/wham/usage` with `~/.codex/auth.json` | not verified |
| Gemini | `cloudcode-pa.googleapis.com` quota calls with `~/.gemini/oauth_creds.json` | not verified |

The token is only ever read. AgentVitals never refreshes it: Claude Code does that itself, and a
refresh token spent by someone else would sign Claude Code out. If the token has expired, the card
says so — open `claude` and the data comes back within a minute. Quotas are checked every
minute by default (30 seconds, 1, 2, 5 or 10 minutes in settings); `Retry-After` is respected.

Agents whose credential file is missing are simply not shown.

## Live sessions

Settings → *Live sessions* → **Connect** adds AgentVitals hooks to `~/.claude/settings.json`
(a backup is kept as `settings.json.agentvitals.bak`). Each hook runs `AgentVitals.exe hook`
in the background, which writes one small state file per session into `sessions\` next to the exe.
**Disconnect** removes only AgentVitals' entries. If you move the exe, settings say so — reconnect.

## Files

Everything lives next to the exe: `config.json` (settings), `log.txt` (two days of errors and
changes), `sessions\` (session states). If that folder is read-only (say, under `Program Files`),
they go to `%LOCALAPPDATA%\AgentVitals` instead. Start with Windows is a value in
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

Windows 10 may put a new tray icon into the hidden area — drag it onto the taskbar once.

## Build

Requires the .NET 10 SDK.

    dotnet test AgentVitals.slnx
    powershell -File tools\publish.ps1            # release\AgentVitals.exe, needs .NET 10 Runtime
    powershell -File tools\publish.ps1 -SelfContained
    powershell -File tools\make-icons.ps1         # redraw app.ico and app.png

## License

Apache-2.0

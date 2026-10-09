# <img src="app.png" width="36" height="36" valign="middle" alt=""> AgentVitals

**Claude Code quotas, live sessions and spend — in the Windows tray.**

[![Release](https://github.com/CrossWander/AgentVitals/actions/workflows/release.yml/badge.svg)](https://github.com/CrossWander/AgentVitals/actions/workflows/release.yml)
[![Latest release](https://img.shields.io/github/v/release/CrossWander/AgentVitals)](https://github.com/CrossWander/AgentVitals/releases/latest)
[![.NET 10](https://img.shields.io/badge/.NET-10-blue.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![Platform](https://img.shields.io/badge/Platform-Windows-0078D4.svg)](https://www.microsoft.com/windows)
[![License](https://img.shields.io/badge/License-Apache%202.0-green.svg)](LICENSE)

A Windows tray app that shows how much of your Claude quota is left and when it resets, what each
Claude Code session is doing right now, what today's work would cost at API prices, and whether
Anthropic is up. A toast warns you before you run out, when Claude finishes a long turn and when it
asks for permission. Inspired by [ClaudeBar](https://github.com/tddworks/ClaudeBar) and
[claude-status-bar](https://github.com/m1ckc3s/claude-status-bar), which do this on macOS.

| Light | Dark |
|---|---|
| <img src="docs/popover-light.png" width="360" alt="AgentVitals window, light theme"> | <img src="docs/popover-dark.png" width="360" alt="AgentVitals window, dark theme"> |

## Quick start

1. Download `AgentVitals.exe` from the [latest release](https://github.com/CrossWander/AgentVitals/releases/latest).
   It needs the [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0). There is no
   installer: the exe runs from any folder.
2. Start it. A pulse icon appears in the tray. Windows 10 may put it into the hidden area — drag it
   onto the taskbar once.
3. If you are signed in to Claude Code, the quotas show up right away. To see live sessions, open
   Settings → *Live sessions* → **Connect**.

Hover the icon for a quick card, click it for the full window, right-click for the menu. The window
stays open while you work in other windows: close it with ✕, another click on the icon or Esc.

## What it covers

| Area | What you get |
|---|---|
| Quotas | Session, week and per-model weekly limits with a reset countdown; a mark on the bar shows where even spending would be by now → [more](#quotas-and-alerts) |
| Tray icon | A pulse line coloured by the limit closest to running out; it beats while Claude works, and a blue dot blinks while Claude waits for you |
| Alerts | Toasts at 75% and 90% of a limit (adjustable from 50% to 95%), when a long turn ends, when Claude asks for permission and when Anthropic's status changes |
| Live sessions | Every open Claude Code session with its folder, git branch, state and turn timer → [more](#live-sessions) |
| Usage statistics | Today's cost, tokens and working time next to yesterday's; a 30-day chart of cost, tokens, cache or models → [more](#usage-statistics) |
| Anthropic status | The status.claude.com indicator, with a toast when it changes |
| After sleep | Asks again as soon as the PC wakes; a window whose reset time has passed shows 0% instead of stale numbers |
| Languages and themes | English, Ukrainian and Russian; light, dark or as in Windows |
| Keyboard | Esc closes the window, F5 or Ctrl+R refreshes, Ctrl+1…3 switch the agent tabs |

## Providers

| Provider | Source | Status |
|---|---|---|
| Claude | `GET api.anthropic.com/api/oauth/usage` with the Claude Code token from `~/.claude/.credentials.json` | working |
| Codex | `GET chatgpt.com/backend-api/wham/usage` with `~/.codex/auth.json` | not verified |
| Gemini | `cloudcode-pa.googleapis.com` quota calls with `~/.gemini/oauth_creds.json` | not verified |

An agent whose credential file is missing is simply not shown.

## Quotas and alerts

- **Cards.** Each quota window is a card with the percentage used, a bar and the time left until it
  resets. A window at 100% says *used up*. Extra usage is shown if it is enabled.
- **Pace.** The session and week bars carry a mark where even spending would be by now, and the
  card says *ahead of pace*, *on pace* or *room to spare* (within 5 points is on pace). Hovering the
  card explains the mark with both numbers.
- **Colours.** The pulse line in the tray takes the colour of the window that limits you most:
  green below the *important* level, amber from it, red from *critical*, grey when there is no
  data. These are the same levels the toasts use, so what turns red is what you were warned
  about; cards, bars and the header follow them too. An exhausted week turns the icon red even
  with an empty session.
- **Toasts.** When the session or the week reaches 75% (*important*, a quarter left) and 90%
  (*critical*, almost used up), with the time until it resets. Both levels move in steps of 5
  between 50% and 95% in settings. Each toast comes once, and again only after that window resets;
  a window that is already past a level when AgentVitals starts gets its toast too.
- **How a toast looks.** It slides up in the bottom-right corner above the clock and stays ten
  seconds for a quota and four for anything else, for as long as the mouse is on it. It comes up
  over the AgentVitals window if that is open, and a click opens the window. Every toast is also
  written to `log.txt`.

Quotas are checked every minute by default (2, 5 or 10 minutes in settings; more often the server
answers 429), and `Retry-After` is respected. After a night's sleep the Claude Code token has
usually expired: the card keeps the last numbers and waits, except a window whose reset time has
passed, which shows 0% and *reset* (use from other devices since then is not visible until fresh
data arrives). Claude Code renews the sign-in on its first request, and AgentVitals notices the
new file and asks at once.

## Live sessions

Settings → *Live sessions* → **Connect** adds AgentVitals hooks to `~/.claude/settings.json`.
Each hook runs `AgentVitals.exe hook` in the background, which writes one small state file per
session into `sessions\` next to the exe. **Disconnect** removes only AgentVitals' entries.

- The pulse runs while Claude is working, and a blue dot blinks while it waits for your permission.
- A running turn shows how long it has been going (*working · 3 min*). A toast says when Claude
  finishes a turn longer than 5 minutes or asks for permission; both can be tuned or switched off
  in settings.
- A turn stopped with Esc fires no hook. The session's transcript says so, and the session goes
  idle at once instead of after 15 minutes.
- A terminal closed without `/exit` fires no hook either. The hook records the Claude Code process,
  and the session leaves the list as soon as that process is gone.
- Claude Code reads hooks when a session starts: sessions that were already open show up after
  you restart them.
- AgentVitals rewrites `settings.json` as plain JSON, so comments in it are not kept. Before every
  change the file is copied to `settings.json.agentvitals.bak`, comments included.
- If you move the exe, settings say so — reconnect. Before deleting the exe, press **Disconnect**:
  otherwise Claude Code keeps calling a program that is no longer there.

## Usage statistics

Under the limits, the Claude tab adds today's numbers and a 30-day chart. They come from Claude
Code's own logs in `~/.claude/projects` (subagents included), read on this machine only:
AgentVitals takes the token counts of model answers and keeps nothing else. Each answer counts
once — Claude Code writes the same answer several times while it streams.

- **Cost** is what the tokens would cost at Anthropic's API list prices, on a subscription too.
  Prices come from the [official pricing page](https://platform.claude.com/docs/en/about-claude/pricing)
  once a day and are kept in `prices.md` next to the settings, so cost works offline. A model the
  page doesn't list yet gets no price rather than a guess. Fast mode is counted at standard prices.
- **Working time** adds up stretches of activity; a pause longer than 30 minutes starts a new one.
- **The chart** shows cost, tokens (input and output), cache (written and read) or cost by model
  family (Opus, Sonnet, Haiku, Fable, other). In the cache view the corner says what the cache saved
  over 30 days: what the cached tokens would have cost as plain input, minus what they did cost.
- Codex and Gemini have no statistics yet: the slot is there (`IProvider.Usage`), the reader isn't.

## Privacy

- **The token is only ever read.** AgentVitals never refreshes it: Claude Code does that itself, and
  a refresh token spent by someone else would sign Claude Code out.
- **No tokens are spent.** The quota endpoint is not a model call, so checking it costs nothing
  from your limits.
- **Where it connects:** the quota endpoints in [Providers](#providers), status.claude.com, and
  Anthropic's pricing page once a day. Nothing else — no account of its own, no telemetry.
- **Logs stay on this machine.** The statistics keep only token counts. To notice a turn stopped
  with Esc, the end of a live session's transcript is checked for Claude Code's interruption mark.
  Nothing from the logs leaves the computer.

## Files

Everything lives next to the exe: `config.json` (settings), `log.txt` (two days of errors and
changes), `sessions\` (session states), `prices.md` (Anthropic's price list). If that folder is
read-only (say, under `Program Files`), they go to `%LOCALAPPDATA%\AgentVitals` instead. Start with
Windows is a value in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

## Build

Requires the .NET 10 SDK.

    dotnet test AgentVitals.slnx
    powershell -File tools\publish.ps1            # release\AgentVitals.exe, needs .NET 10 Runtime
    powershell -File tools\publish.ps1 -SelfContained
    powershell -File tools\make-icons.ps1         # redraw app.ico and app.png

`release\` is a build folder and stays out of git. Publishing a release on GitHub runs
[`release.yml`](.github/workflows/release.yml): it tests the tagged sources, builds the exe with
`tools\publish.ps1` and attaches it to that release.

## License

Apache License 2.0 — see [LICENSE](LICENSE) and [NOTICE](NOTICE).

Copyright 2026 Dykalo Pavlo. Forks and derivative works must keep the contents of `NOTICE`
and mark the files they changed (sections 4b and 4d of the license).

Claude and Anthropic are trademarks of Anthropic PBC. This project is independent, not
affiliated with or supported by Anthropic, OpenAI or Google; the names identify the services
the software reads.

## Author

Developed by Dykalo Pavlo, 2026.

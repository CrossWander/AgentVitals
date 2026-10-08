# AgentVitals

A Windows tray app (Avalonia, .NET 10) that keeps an eye on your AI coding agents: how much of
each quota window is used and when it resets, what your Claude Code sessions are doing right now,
and whether Anthropic's services are healthy. Inspired by [ClaudeBar](https://github.com/tddworks/ClaudeBar)
and [claude-status-bar](https://github.com/m1ckc3s/claude-status-bar). Interface in English,
Ukrainian and Russian, light and dark.

## What it shows

- **Quotas.** The session, the week and per-model weekly limits, each as a card with the
  percentage used, a bar and the time left until it resets. A window at 100% says *used up*.
  Extra usage, if enabled. The session and week bars carry a mark where even spending would be by
  now, and the card says *ahead of pace*, *on pace* or *room to spare* (within 5 points is on pace).
- **The tray icon** is a pulse line coloured by the window that limits you most: green under 50%,
  amber to 80%, red above, grey when there is no data. An exhausted week turns it red even with an
  empty session.
- **Toasts** when the session or the week reaches 75% (*important*, a quarter left) and 90%
  (*critical*, almost used up), with the time until it resets. Each comes once and again only
  after that window resets; a window that is already past a level when AgentVitals starts gets
  its toast too. A toast slides up in the bottom-right corner above the clock, stays four seconds
  (as long as the mouse is on it) and opens the window on click. Every toast is also written to
  `log.txt`.
- **Live sessions.** With the hooks connected, the pulse runs while Claude is working and a blue
  dot blinks while it waits for your permission. A turn stopped with Esc fires no hook; the
  session's transcript says so, and the session goes idle at once instead of after 15 minutes.
  A running turn shows how long it has been going (*working · 3 min*). A toast says when Claude
  finishes a turn longer than 5 minutes or asks for permission; both can be tuned or switched off
  in settings.
- **Anthropic status** from status.claude.com, with a toast when it changes.
- **Usage statistics** (Claude). Today's cost, tokens and working time, each with yesterday's
  number, and a 30-day chart of cost, tokens or cache — see [Usage statistics](#usage-statistics).

Hover the icon for a quick card, click it for the full popover, right-click for the menu. The popover stays open while you work in other windows: close it with ✕, another click on the icon or Esc. F5 or Ctrl+R refreshes, Ctrl+1…3 switch the agent tabs.

## Where the data comes from

| Agent | Source | Status |
|---|---|---|
| Claude | `GET api.anthropic.com/api/oauth/usage` with the Claude Code token from `~/.claude/.credentials.json` | working |
| Codex | `GET chatgpt.com/backend-api/wham/usage` with `~/.codex/auth.json` | not verified |
| Gemini | `cloudcode-pa.googleapis.com` quota calls with `~/.gemini/oauth_creds.json` | not verified |

The token is only ever read. AgentVitals never refreshes it: Claude Code does that itself, and a
refresh token spent by someone else would sign Claude Code out. After a night's sleep the token
has usually expired: the card keeps the last numbers and waits, except a window whose reset time
has passed, which shows 0% and "reset" (use from other devices since then is not visible until fresh
data arrives). Claude Code renews the sign-in on
its first request, and AgentVitals notices the new file and fetches at once. Quotas are checked every
minute by default (2, 5 or 10 minutes in settings; more often the server answers 429);
`Retry-After` is respected.

Agents whose credential file is missing are simply not shown.

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
- Codex and Gemini have no statistics yet: the slot is there (`IProvider.Usage`), the reader isn't.

## Live sessions

Settings → *Live sessions* → **Connect** adds AgentVitals hooks to `~/.claude/settings.json`.
Each hook runs `AgentVitals.exe hook` in the background, which writes one small state file per
session into `sessions\` next to the exe. **Disconnect** removes only AgentVitals' entries.

- Claude Code reads hooks when a session starts: sessions that were already open show up after
  you restart them.
- AgentVitals rewrites `settings.json` as plain JSON, so comments in it are not kept. Before every
  change the file is copied to `settings.json.agentvitals.bak`, comments included.
- If you move the exe, settings say so — reconnect. Before deleting the exe, press **Disconnect**:
  otherwise Claude Code keeps calling a program that is no longer there.

## Files

Everything lives next to the exe: `config.json` (settings), `log.txt` (two days of errors and
changes), `sessions\` (session states), `prices.md` (Anthropic's price list). If that folder is read-only (say, under `Program Files`),
they go to `%LOCALAPPDATA%\AgentVitals` instead. Start with Windows is a value in
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

Windows 10 may put a new tray icon into the hidden area — drag it onto the taskbar once.

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

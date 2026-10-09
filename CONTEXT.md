# AgentVitals

A tray companion for people who code with AI agents: how much of each agent's quota is left, whether Anthropic is healthy, what the live Claude Code sessions are doing, and what the work cost.

## Language

### Agents and quotas

**Agent**:
A coding assistant with its own CLI and sign-in whose quotas are watched: Claude Code, Codex or Gemini CLI.
_Avoid_: tool, client, account

**Installed**:
An agent is installed when its sign-in file is on the computer; only installed agents are shown and asked.
_Avoid_: available, enabled

**Plan**:
What the person pays the agent's maker for, such as Pro or Max.
_Avoid_: tier, subscription

**Quota window**:
An allowance that fills up with use and empties at its reset; it has a used share and a reset time.
_Avoid_: limit, bucket

**Session window**:
The five-hour quota window.

**Week window**:
The seven-day quota window, either for all models or for one model family.

**Model window**:
A quota window of one Gemini model family whose length is not told.

**Limiting window**:
A quota window that stops all work when it runs out, such as the session window; a week window for one model family is not.
_Avoid_: blocking window

**Reset**:
The moment a quota window empties; once it has passed, the window counts as empty even before fresh data arrives.

**Bottleneck**:
The limiting window with the most used, and so the one that ends the work first; the icon and the header speak for it.
_Avoid_: worst window

**Exhausted**:
A quota window with nothing left.
_Avoid_: maxed out, over limit

**Thresholds**:
The two used shares the person chooses, Important and Critical; they colour every meter and trigger quota toasts.
_Avoid_: limits

**Health**:
Good, Warning, Critical or Unknown: a used share measured against the thresholds, or the state of the Anthropic status page.
_Avoid_: tone, indicator

**Pace**:
Whether a window is used faster than time runs: Spare, Even or Ahead of the elapsed share of the window.
_Avoid_: rate, speed

**Extra usage**:
Paid use past the plan, with its spent amount and monthly limit.
_Avoid_: spend, overage

**Sign-in expired**:
The agent's sign-in no longer works; data comes back once the agent's own CLI renews it.
_Avoid_: logged out, unauthorized

### Live sessions

**Live session**:
A running Claude Code conversation in one folder, reported by the hooks.
_Avoid_: chat, conversation

**Session state**:
Idle, Working or Waiting; Waiting means the session asks for permission and stands still until answered.

**Turn**:
One stretch of Claude working on a prompt, from the prompt to the stop.
_Avoid_: run, request

**Hooks**:
AgentVitals' entries in Claude Code's settings through which live sessions report their state; they are On, Off or Moved (pointing at an old copy of the program).

### Alerts

**Quota toast**:
A toast when a limiting window crosses a threshold; told once per threshold per window cycle, and again only after the window drops below Important.
_Avoid_: quota notification

**Session toast**:
A toast when a long turn finishes or a live session waits for permission.

### Usage statistics

**Usage day**:
One local day of an agent's work from its own logs: tokens, cost, cache savings and work time.

**Cost**:
What a day's tokens would cost at API prices; a subscription does not bill it, it shows what the plan is worth.
_Avoid_: spend, price

**Work time**:
How long a day's logs show the agent busy; pauses longer than half an hour are left out.
_Avoid_: working, active time, uptime

**Model family**:
A group of models priced and counted together: Opus, Sonnet, Haiku, Fable; for Gemini, Pro, Flash and Flash Lite.
_Avoid_: tier, model class

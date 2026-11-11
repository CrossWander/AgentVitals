# Security policy

## Supported versions

Only the [latest release](https://github.com/CrossWander/AgentVitals/releases/latest) gets fixes. Settings →
*Startup and updates* → **Update** brings you to it.

## Reporting a problem

Please don't open a public issue. Send a
[private report](https://github.com/CrossWander/AgentVitals/security/advisories/new) instead: only the
maintainer sees it, and the fix and the advisory are published together once the release is out.

Include what you can:

- the AgentVitals version (Settings → *About*) and your Windows version;
- what happens and how to make it happen;
- what an attacker gets out of it.

Don't send real tokens, cookies or emails: describe them or replace them with fakes.

## What counts

AgentVitals reads the sign-ins of Claude Code, Codex, Gemini CLI, GitHub Copilot and Cursor, so the
promises in the [Privacy](README.md#privacy) section are what matters most. Please report anything that
breaks them, for example:

- a token, cookie or sign-in that is written to disk, logged, shown or sent anywhere but its own quota
  endpoint;
- a request to a host the README doesn't list;
- a refresh token being spent, which would sign the agent out;
- an update that could install an exe that isn't the one attached to the GitHub release;
- the hooks AgentVitals adds to Claude Code and Codex running something they shouldn't.

AgentVitals is a one-person project, so there is no bug bounty, but every report gets an answer and credit in
the advisory if you want it.

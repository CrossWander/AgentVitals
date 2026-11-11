# Contributing to AgentVitals

Thanks for taking the time. Bug reports, ideas and pull requests are all welcome.

## Issues

- **A bug or an idea:** open an [issue](https://github.com/CrossWander/AgentVitals/issues/new/choose) and pick
  the form that fits. English, Ukrainian and Russian are all fine.
- **A security problem** (a token or a sign-in leaking, a request going where it shouldn't): don't open an
  issue — see [SECURITY.md](SECURITY.md).
- **Screenshots:** turn on the eye at the end of the plan line first, so emails and session prompts stay hidden.

## Pull requests

For anything bigger than a small fix, open an issue first, so we agree on the idea before you spend time on
the code.

1. Install the .NET 10 SDK and run `dotnet test AgentVitals.slnx`: it should be green before you start.
2. Make the change, with a test that fails without it.
3. Run `dotnet test AgentVitals.slnx` again and open the pull request against `master`.

## How the code is laid out

Four projects in `src\`, and each one knows only those before it (`ArchitectureTests` checks this):

- `AgentVitals.Platform` — Windows calls, files, the log and sounds;
- `AgentVitals.Core` — quotas, usage, status pages and live sessions, with no UI;
- `AgentVitals.Agents` — a folder per agent with what is particular to it;
- `AgentVitals` — the tray app: windows, strings, polling, settings and updates.

The words the code and the README use are in [CONTEXT.md](CONTEXT.md).

## Rules the tests and reviews hold to

- **Three languages.** Every string the app shows lives in `src\AgentVitals\Lang\en.axaml`, `ru.axaml` and
  `uk.axaml`; a key missing from one of them fails `LangTests`. If you don't speak Russian or Ukrainian, put the
  English text in all three and say so in the pull request.
- **Three READMEs.** `README.md` is the original, `README.ru.md` and `README.uk.md` follow it. A change to one
  goes into all three; `ReadmeTests` checks they keep the same headings, tables and images.
- **Tests don't depend on the machine.** The GitHub runner has no agents signed in, so tests use fake agents
  and temporary folders and never touch the real Credential Manager, `~/.claude` or `~/.codex`.
- **Sign-ins are only read.** Never refresh, write, log or show a token, and never put an email in a log
  line. The [Privacy](README.md#privacy) section of the README is a promise; a change that breaks it won't be
  merged.
- **Keep the non-Windows paths.** The app ships for Windows only, but branches for other systems stay in place;
  guard Windows calls with a check of the OS instead of deleting the rest.

## Style

- Identifiers, commit messages and docs are in English. Existing code comments are in Russian; yours can be
  in English.
- Tabs in C#, as `.editorconfig` says.
- A commit message is a plain sentence about what changes for the user, with no `feat:`-style prefix, e.g.
  *Keep the tray window open while the mouse is near it*.

By contributing, you agree that your work is licensed under the [Apache License 2.0](LICENSE), as the rest
of the project is.

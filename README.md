<p align="center">
  <img src="assets/icon-preview.png" width="104" alt="Codex Legion icon">
</p>

<h1 align="center">Codex Legion</h1>

<p align="center">
  A small native Windows home for Codex.<br>
  Private profile, real project tools, less desktop overhead.
</p>

<p align="center">
  Built by Sorin, curiosity, and a suspicious number of “quick” side projects.<br>
  <a href="https://buymeacoffee.com/Pafeliepixel">☕ Keep the Legion caffeinated</a>
</p>

![Codex Legion interface](assets/readme-preview.svg)

## Why this exists

Codex is excellent at the actual work: understanding a repository, changing files, running commands, reviewing diffs, and staying with a task until it is finished. The official desktop experience is polished, but it also carries the weight of a browser-based application.

Codex Legion started with a simple question: **how much of that experience can live inside a genuinely native Windows shell?**

The result is a WPF client that feels at home on Windows and leaves the agent work to a private Codex app-server process. There is no always-running Chromium window, Node.js runtime, or WebView in the interface. The client and server talk directly over JSONL through standard input/output.

It is useful when you want:

- a lighter Codex window beside an editor or game engine;
- a separate login and chat history from the official app;
- clear control over model, thinking effort, approvals, and file access;
- an install that works even when the official Codex app is not present.

## What it can do

Codex Legion covers the everyday coding loop: persistent chats, streaming replies, model selection, a snap-to-level thinking slider, approval modes, commands, file changes, plans, reasoning summaries, web searches, token usage, and native diff cards.

For bigger jobs, **Parallel work** can stay in one lane, call in backup only when you ask, or smartly split independent work across a small crew. Legion shows who joined and when they wrap up, then brings everything back into one answer—tiny teamwork, fewer tiny meetings.

You can stop or steer an active turn, attach images, use local skills, inspect Git status, create managed worktrees, and reopen or organize previous chats. Approval, permission, question, and MCP prompts stay inside the same themed interface instead of falling back to generic Windows dialogs.

When a task needs a real webpage, Legion can launch a visible managed Edge or Chrome window. It uses a separate browser profile, starts only on demand, and disappears when you close it or exit Legion. Reading and scrolling stay quiet; clicking and typing follow your approval mode. Your everyday browser tabs, cookies, and extensions are left alone.

Prefer to keep the model close too? Open **Local models** and Legion checks the PC's RAM, GPU, VRAM, and free space, then ranks a set of downloadable models. Pick one and Legion brings in a pinned Strata control bundle, the matching hardware engine, and the model itself. Strata never needs to be installed: there is no extra app, service, Start menu entry, or PATH change. The engine runs as Legion's child on loopback port `18347` and stops with the app.

The regular installers stay small because local AI is entirely optional. The 73 MB control bundle, hardware engine, and model files are downloaded only after a size-and-license prompt, verified by the embedded engine, and kept in Legion's private data plus the model folder you choose. Model downloads range from roughly 24 GB to well over 250 GB.

Tap the microphone for a proper hands-free conversation—not push-to-talk. Legion listens for natural turns, speaks back, and lets you interrupt. The first tap opens a themed setup with a direct link to create an OpenAI API key. Once it connects, that key is protected for the current Windows user with DPAPI, so the next conversation is one click away. Voice usage comes from your OpenAI API credits.

Official Codex history is never pulled in silently. After the first login and PIN setup, the app asks whether you want to copy it. You can say no and keep a completely clean slate, or import it later from the sidebar.

## Pick your installer

| Download | Best for | Size |
| --- | --- | ---: |
| **CodexLegionSetup.exe** | Normal installation. Downloads the verified private runtime when needed. | ~0.5 MB |
| **CodexLegionSetup-offline.exe** | A complete install for offline or repeat setups. | ~164 MB |

Both are per-user installers and do not need administrator access. Codex Legion installs under `%LOCALAPPDATA%\Programs\Codex Legion`, can add Start menu or desktop shortcuts, and includes an uninstaller.

## Your account stays separate

Codex Legion does not borrow the official app's saved credentials. It gives its app-server a dedicated profile, removes ambient OpenAI credential variables from the child process, and protects remembered authentication with Windows DPAPI while the server is stopped.

After signing in once, you set a 4–12 digit PIN. The login is remembered, while the PIN is requested again every seven days—useful on a computer that other people may occasionally touch without making you sign in every launch.

## What it is built on

```text
Codex Legion — native WPF / .NET Framework UI
    │
    └── JSON-RPC over stdin/stdout
          │
          └── private OpenAI Codex app-server
                ├── isolated login and chat history
                ├── model and approval capabilities
                ├── agent loop and streamed events
                └── sandboxed local tools
```

The UI keeps only lightweight rendered state. The Codex runtime lives in a separate child process, so its memory is visible independently and is released when the app closes. Cloud use opens no permanent server port; managed browsing uses a random loopback debugging port only while its isolated browser is running. If you start a local model, its private API uses Legion's dedicated loopback port `18347` for that session.

Codex itself is the open-source [OpenAI Codex runtime](https://github.com/openai/codex). Codex Legion is an independent community client and is not an official OpenAI application.

Optional local inference is powered by the open-source [Strata engine](https://github.com/Niko1221/Strata), downloaded into Legion on demand under Strata's MIT license. Model weights and hardware components keep their own upstream license terms, which Legion shows before downloading them.

## Curiosity encouraged

No mystery meat here. Every line of Codex Legion’s own code is in this repository, ready to be read, questioned, poked, or given a very suspicious eyebrow. Build it yourself, follow the network calls, and check how the keys are tucked away—the hood is open on purpose.

## Updates without surprises

**Tools → Check for updates** reads the latest public GitHub release. If a newer version exists, Codex Legion downloads the small installer, verifies its SHA-256 digest, and asks before launching it.

The web installer fetches a runtime only when it is missing or its pinned fingerprint changed. The larger offline installer already carries the same verified runtime.

## Building it yourself

Windows already includes most of what the project needs:

```powershell
./build.ps1
./run.ps1
./test.ps1
```

To create the installers, point the packager at a Codex Windows runtime directory:

```powershell
./package.ps1 -RuntimeSource C:\path\to\codex-runtime -Version 0.7.0 -Zip
```

The source stays simple: code-only WPF, the inbox .NET Framework compiler, PowerShell packaging, and no JavaScript dependency tree.

## Still growing

Codex Legion is happily a work in progress: the core coding loop is real, while rich Markdown, full connector installation, and a few ambitious ideas are still wandering toward the party.

Found a sharp corner, a sleepy button, or something that simply feels odd? Open an issue with a screenshot and a few words about what happened. Those reports are excellent fuel for the next build.

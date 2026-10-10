# SideMon Milestones

Ideas and planned work for SideMon. Nothing here is committed to a release until it is built, tested and listed in `CHANGELOG.md`.

Current release: **4.4.0**

Status key: `[ ]` not started, `[~]` in progress, `[x]` done.

## Already covered

CPU, GPU, RAM, drives, network, battery, motherboard sensors, AI CLI usage (Claude Code, Antigravity/Gemini, Codex, Copilot), FPS overlay, graph window.

## Next up (suggested priority)

1. [ ] **Top processes**: top 3-5 by CPU, RAM or GPU ("what is eating my PC").
2. [ ] **VRAM and GPU power**: VRAM used (useful for ComfyUI/Qwen work), GPU watts, hotspot and fan.
3. [ ] **Ping and jitter**: to a host the user chooses (e.g. 1.1.1.1 or their own server).
4. [ ] **Threshold alerts**: tray toast when a value crosses a limit (CPU/GPU temperature, drive nearly full, AI usage limit nearly hit). Applies to every monitor, so probably worth more than any single new monitor.

## System monitors

- [ ] **Per-core CPU and clock speed**: mini bar per core, boost clocks.
- [ ] **Network extras**: Wi-Fi signal and link speed, public IP.
- [ ] **Disk health**: SMART health, NVMe temperature and remaining life, read/write speed.
- [ ] **Uptime and pending-restart flag**: Windows "restart needed" indicator.
- [ ] **Power draw and cost**: CPU/GPU watts and an estimated cost from a user-entered tariff.
- [ ] **Bluetooth device battery**: mouse, headset, controller.
- [ ] **UPS status**: if one is attached.
- [ ] **Audio level / now playing**, **clipboard** and **download-folder size**.

## Personal / workflow monitors

- [ ] **Hermes and agent status**: is Hermes running, active sessions and background jobs, whether the usage server at `127.0.0.1:8765` is up.
- [ ] **Service health checks**: green/red dots for the user's own sites and APIs (e.g. pishonstudio.com, the Colexio backend) via ping or HTTP check.
- [ ] **GitHub**: open PRs, failing CI and unread notifications for `konguspng`.
- [ ] **Weather** and a calendar **next event**.
- [ ] **Pomodoro / focus timer**.

## Display and UX

- [ ] **Sparklines**: tiny inline history graph on any metric (the graph window already exists; this shows trends at a glance).
- [ ] Every new monitor is an on/off item in Settings > Monitors, matches the existing monitor style, and is off by default unless it costs nothing.

## Carried over from earlier work

- [ ] **Claude long-lived token button**: a Settings action that launches `claude setup-token` for the user. Open design question: if `CLAUDE_CODE_OAUTH_TOKEN` is set, the Claude CLI stops refreshing `~/.claude/.credentials.json`, which SideMon reads for usage, so the usage bar could show "expired" more often. A token from `setup-token` may also lack the scope the usage endpoint needs (untested). Alternative: a "Refresh Claude login" button that runs a harmless `claude` command to renew `.credentials.json`. SideMon must never read, write or log tokens itself.
- [ ] **Startup task permission**: when SideMon is not elevated, creating the `SideMonStartup` scheduled task fails with "Access is denied"; handle that case with a clear message instead of a logged error.
- [ ] **Settings window**: click-through test of every control (dependent greying, save/cancel, Monitors provider list) and a re-render after the opacity label change.
- [ ] **Glass**: test on more than one PC and on Windows 10; confirm the acrylic tint and opacity feel right.

## Rules for all milestones

- Gemini via `agy` does the building and testing; results are verified before release.
- Nothing is committed, tagged or published until the owner says so.
- Real behaviour is tested (on-screen render or measured output), and anything unverified is stated in the release notes.

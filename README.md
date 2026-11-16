<h1><img src="icon.png" width="48" height="48" align="top" /> SideMon</h1>

**Free Windows 11 / 10 sidebar that shows your AI coding quota (Claude Code, OpenAI Codex, Gemini / Antigravity, GitHub Copilot, Cursor, Grok, OpenCode Go) next to your CPU, GPU, RAM, network and drive stats.**

SideMon docks to the edge of your screen like a desktop widget. One glance tells you how much of your Claude Code 5-hour limit is left, when your weekly Codex quota resets, and how hot your GPU is, without opening a terminal, a browser tab or Task Manager. It is free and open source, with no account, no ads and no telemetry.

[**Download the latest installer**](https://github.com/konguspng/sidemon/releases/latest), run it, done. The .NET runtime is bundled.

## AI usage monitor

Stop guessing when you will hit your limit. SideMon uses the login your AI CLI or app already has on this PC and shows each quota as a bar with its reset countdown.

| Provider | What you see | Sign-in used |
| --- | --- | --- |
| **Claude Code** (Pro / Max) | 5-hour session and weekly limits, plan name | Claude Code CLI login |
| **Gemini via agy / Antigravity** | Gemini and Claude/GPT quota groups | agy login |
| **OpenAI Codex CLI** (ChatGPT Plus / Pro) | 5-hour and weekly limits, plan name | Codex CLI login |
| **GitHub Copilot** | Premium requests, chat and completions quota | Copilot CLI, `gh` or `GH_TOKEN` |
| **Cursor** | Auto + Composer and API usage for the billing cycle | Cursor app sign-in |
| **Grok Build** | Credit pool and reset | `grok login` |
| **OpenCode Go** | 5-hour, weekly and monthly usage | workspace ID + console cookie |

* **Stays signed in.** When Claude Code's or Grok's token expires, SideMon lets that CLI renew its own login in the background. Re-logins and account switches show up within seconds.
* **Never shows false numbers.** If a refresh fails, the last good values stay on screen, dimmed, with their age. A quota whose reset time has passed says so instead of showing an old percentage.
* **Read-only and private.** SideMon never writes, uploads or displays your tokens. Requests go straight from your PC to each provider; there is no SideMon server in between.
* Provider logos, per-provider on/off, an alert colour when a quota runs low, and a one-click **Sign in again** button when a login has expired.

## Hardware monitor

* CPU clocks, temperature, load and power; GPU load, temperature, VRAM and fan; RAM; network up/down; drive usage and activity.
* **Real frosted glass**: live Windows acrylic blur behind the sidebar, with card panels and accent bars.
* **Fit to screen**: one click sizes the sidebar to your screen height; auto-fit keeps it fitting when you add or remove monitors.
* **Game friendly**: polling pauses while a fullscreen app runs, or while the PC is locked or asleep. An optional FPS overlay reads RivaTuner (RTSS).
* Graphs, alerts with blink, global hotkeys, clock and date, multi-monitor and per-monitor DPI support, bundled fonts and 10+ languages.
* **One-click updates**: click the update notice and SideMon downloads, verifies (SHA-256) and installs the new version.

## Built to be light

* Every monitor is off until you tick it, and an unticked monitor runs no timer or network call.
* AI quotas are polled at most every 5 minutes, with back-off on errors and rate limits.
* No background services and no telemetry. Crash logs are only sent if you press **Send** after previewing them.

## Requirements

* Windows 10 or Windows 11 (x64)
* Administrator rights (needed for CPU/GPU sensors). On first run SideMon offers to install the [PawnIO](https://pawnio.eu) driver for CPU/GPU clock, temperature and voltage readings.
* For AI usage: the CLI or app of each provider you want to track, signed in once.

## Credits

* Based on [Sidebar Diagnostics](https://github.com/ArcadeRenegade/SidebarDiagnostics) by ArcadeRenegade. Thank you for the original project!
* Hardware data from [Libre Hardware Monitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor); kernel driver by [PawnIO](https://pawnio.eu).
* AI provider request shapes informed by [quota-usage-monitor](https://github.com/rozsazoltan/quota-usage-monitor); provider logos from [Lobe Icons](https://github.com/lobehub/lobe-icons) (MIT). Provider names and logos belong to their owners; SideMon is not affiliated with or endorsed by them.
* Bundled fonts are licensed under the SIL Open Font License (see `SidebarDiagnostics/Fonts/FONT-LICENSES.txt`).

## License

GNU General Public License. Please link back to this repository if you redistribute.

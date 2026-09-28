# ThermalScope

A Windows app for watching PC temperatures from a phone on your home Wi-Fi, recording named sessions, and comparing multiple runs. The dashboard identifies your CPU/GPU from discovered sensors; available readings depend on your hardware and drivers.

## Run

### Setup wizard

Run `ThermalScope-Setup-0.1.0-win-x64.exe` from the `dist` folder (or a GitHub release when one is published). The wizard installs into `C:\Program Files\ThermalScope` by default, lets you change the destination, adds Start menu shortcuts and an optional desktop shortcut, and registers the app in Windows Installed apps. It offers optional Private-LAN firewall access and installation of the signed PawnIO driver if missing. Approve UAC; the driver task needs Internet access and opens PawnIO's own installer. If you skip those tasks, rerun setup to add them later.

Installed recordings and sensor assignments live in `%LOCALAPPDATA%\ThermalScope\data`, not Program Files. Upgrading or uninstalling preserves that folder. Uninstall removes only the installed program files, shortcuts, and this installation's firewall rule; it leaves the shared sensor driver in place. Close the control window before upgrading or uninstalling so recordings finish safely. The current development/portable build continues using its existing `data` folder; setup does not silently copy or modify those recordings. To migrate them, close both copies, back up both data folders, and copy the old folder into the installed recording location only if it has no existing recordings. Run only one real collector at a time (port 8088).

This build is not code-signed, so Windows may display an unknown-publisher/SmartScreen warning. Only run installers obtained from a source you trust; the accompanying `.sha256` file records the build checksum, not publisher identity.

### Portable / development build

1. Install the sensor driver once with **Install Sensor Driver.cmd**. It downloads the signed PawnIO installer bundled in the official Libre Hardware Monitor release and checks its signature. Windows may show a UAC prompt. No MSI Remote Server is used.
2. Run **Enable LAN access.cmd** once. It adds a firewall rule only for the app, TCP 8088, the Private network profile and local subnet.
3. Search **ThermalScope** in the Windows Start menu (or double-click **Start ThermalScope.cmd**). Approve Windows UAC for sensor access. The native control window starts the server automatically and shows the address for your phone. Use **Open dashboard** to open it locally, **Copy** for the phone address, and **Stop server / Start server** to control monitoring.
4. On your iPhone, connect to the same Wi-Fi and open that LAN address in Safari. Use Share → Add to Home Screen for a standalone dashboard. The corner button toggles focus mode: only the temperature chart on the left and cooling response on the right, filling the landscape view. Portrait phones present the same dashboard rotated horizontally; turning the phone into landscape removes the rotation. Use the small corner button again to exit (or Escape on a keyboard). LAN HTTP may not support automatic screen wake locks; use your phone's Auto-Lock setting when needed.

All runtime files and recordings are on Windows. You can shut down WSL before gaming. Minimizing the window keeps monitoring active. Stopping the server or closing the window saves an active recording and exits the server; closing also exits the launcher. There is no background tray process. A Wi-Fi interruption or locked phone does not stop recording. Opening ThermalScope again brings its existing control window forward. Sensor access runs under the account used for administrator elevation; if you enter another administrator's credentials, its user-data folder is used. `/api/info` reports the actual data path.

## Sensor setup

Open **Sensor setup** and check the discovered readings. Recognized CPU/GPU names are assigned automatically. Generic motherboard fan numbers are intentionally not assumed to mean CPU fan. Assign the correct tachometer after verifying the motherboard header or BIOS label. A sensor with no reading is shown as unavailable, never zero. Zero RPM is a valid stopped fan.

An AIO cooler's available RPM readings depend on how its cables are connected. A combined connection may expose just one tachometer signal; separate connections can expose more. Do not interpret a pump speed as radiator fan speed. RPM is not a sound-level measurement, and RPM between different fan models does not directly compare loudness.

LibreHardwareMonitorLib reads CPU, GPU, RAM and motherboard sensors. Afterburner's documented monitoring shared memory is also read, when Afterburner is running, for FPS/frame time and recognized fallback readings. FPS availability depends on RTSS/game support and configured Afterburner monitoring sources. No overclocking or fan-control APIs are used.

The cooling-response chart's **System fans** line is the arithmetic mean of available system/chassis fan RPM readings. CPU, GPU and pump readings (including their assigned sensor IDs) are excluded, as are duplicate sensor IDs and missing readings. Valid zero RPM readings are included; an unused header reporting zero cannot be distinguished automatically from a stopped fan. Generic fan headers are included unless assigned to CPU/pump, so check those assignments first. This calculated metric is also saved in new recordings and available in Compare/CSV; older recordings without it show unavailable values.

## Record and compare

Give each run any session name (up to 120 characters), or leave it blank for an automatic date/time name. Cooling solution and game/workload are optional free-text labels. Choose any duration from 1–240 minutes (default 60) and optional room temperature/notes, then start. Recording stops automatically, or use Stop & save sooner. Saved sessions include timestamped samples, raw sensor readings and these metadata. Existing recordings remain compatible.

**Sessions** is the recording library with CSV exports. **Compare** is a separate view: search by name, cooler, workload or notes, then check any number of saved sessions. Active recordings become selectable after they stop. Each selected run gets a colored elapsed-time overlay and its own average, peak and final 15-minute summary. The metric selector includes temperatures, fans/pump, power, utilization, memory, FPS and frame time. Missing readings are excluded, not shown as zero; valid sample counts are shown. A short session uses its available interval for the final average. Comparisons load the selected recordings into the browser, so select fewer sessions on memory-constrained phones if needed.

Use the same game workload, resolution, settings, FPS cap and fan policy for both coolers. Note room temperature and any change to PBO/power limits. The last 15 minutes help compare the warmed-up systems. CSV exports contain the dashboard metrics; raw readings are retained in the local SQLite database.

## Files

- `app/`: self-contained Windows executable, its dependencies and web assets.
- `app/desktop/ThermalScope.Desktop.exe`: native WPF control window; the Start menu shortcut points here.
- `data/sessions.sqlite`: real recordings and sensor assignments. Back up the whole `data` folder while the app is stopped (SQLite uses WAL sidecar files while running).
- `data-demo/`: separate simulated recordings when `app\ThermalScope.exe --demo` is used (port 8099).
- `Build.ps1`: builds with a .NET 10 SDK. SDK is needed only for development.
- `tests/Smoke.ps1`: integration checks with simulated data and an isolated temporary database.
- `tests/Desktop.ps1`: launcher lifecycle, graceful saving and crash-cleanup checks; uses isolated demo recordings.
- `tests/Installer.ps1`: elevated install, running-app guard, version upgrade, uninstall, firewall and data-preservation checks using a separate test product (`Build-Installer.ps1 -TestMode` first).
- `tests/InstallerUI.ps1`: elevated interactive wizard inspection with screenshots; exits before installation.
- `Install Start Menu.ps1`: creates or updates the current user's ThermalScope shortcut.
- `Build-Installer.ps1`: publishes a separate staging build, gathers dependency notices, and compiles the Windows installer.
- `setup/ThermalScope.iss`: Inno Setup wizard, optional setup tasks, shortcuts, upgrade and uninstall behavior.
- `dist/`: generated installers and SHA-256 checksums; excluded from source control.

## Build and share the source

On Windows with a .NET 10 SDK installed, run `Build.ps1`, then `Install Start Menu.ps1` to add your own shortcut. Runtime does not require Node, Python, WSL or an installed .NET runtime. See `tests/Browser.mjs` for optional Playwright browser checks.

To build a setup wizard, use `Build-Installer.ps1 -InstallCompiler`. This downloads and verifies the signed Inno Setup 7.1.0 build tool from its official release if needed. Alternatively install Inno Setup 7 (or 6.7+) yourself and pass `-CompilerPath`. The first build also downloads upstream license documents; users only need Internet during setup if installing a missing sensor driver. Set `-Version 0.1.1` when building a new release. Inno Setup may require a commercial license for commercial use; consult its terms if you change the project's distribution model.

ThermalScope's own source is licensed under MIT; see `LICENSE`. Third-party components retain their own licenses. Publish source files, not local recordings, installed drivers, downloaded tools or generated binaries. `.gitignore` excludes those folders and SQLite files. Review the files you stage before committing; don't include personal screenshots or logs. Retain `THIRD-PARTY-NOTICES.md` and the generated `licenses` folder in binary distributions. Upload the normal installer and its checksum as release assets, not as source files. GitHub publishing is not performed automatically by the app.

There are no remote fonts, CDNs, analytics or cloud storage. Phone access uses HTTP on your trusted LAN. Do not forward port 8088 through your router. If access fails, check that the network is Private, both devices are on the same LAN, and Wi-Fi client isolation is disabled. The app window may list VPN adapters as well as your Wi-Fi/Ethernet address; use the home LAN address.

## Recovery

Samples are committed to SQLite every second during recording. A forced exit leaves the saved samples intact; the next launch marks an unfinished session `interrupted`. Normal stop finalizes the session. Storage errors are reported on the dashboard and stop recording while keeping live monitoring available.

## Dependencies

.NET / ASP.NET Core / WPF, LibreHardwareMonitorLib 0.9.6 (MPL-2.0 and its documented third-party licenses), Microsoft.Data.Sqlite and SQLitePCLRaw. See `THIRD-PARTY-NOTICES.md` for upstream sources. The web dashboard is vanilla JavaScript and canvas charts. The native icon is derived from the existing vector logo and is included in the source, so no Node or frontend build process is required to run or rebuild the app.

## How this project was made

This app was 100% vibe-coded using Codex with GPT-6-Sol at Medium reasoning effort. I have zero knowledge of the chosen stack (C#, ASP.NET Core, WPF, SQLite, and vanilla JavaScript); I described what I wanted, tested the app, and iterated with Codex.

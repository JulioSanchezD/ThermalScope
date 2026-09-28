# Third-party components

- .NET / ASP.NET Core: https://github.com/dotnet/runtime and https://github.com/dotnet/aspnetcore (MIT and their third-party notices).
- WPF: https://github.com/dotnet/wpf (MIT).
- LibreHardwareMonitorLib 0.9.6: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/v0.9.6 (MPL-2.0; additional notices: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/v0.9.6/THIRD-PARTY-NOTICES.txt).
- Microsoft.Data.Sqlite: https://github.com/dotnet/efcore (MIT).
- SQLitePCLRaw: https://github.com/ericsink/SQLitePCL.raw (Apache-2.0); SQLite: https://sqlite.org/copyright.html (public domain).
- PawnIO is an optional separately installed driver: https://github.com/namazso/PawnIO (GPL-2.0-or-later with documented exception). Its signed installer is obtained from the official Libre Hardware Monitor release source when requested.
- LHM embeds PawnIO.Modules 0.1.6 (LGPL-2.1): https://github.com/namazso/PawnIO.Modules/tree/0.1.6 . Installer builds include its upstream source archive and the upstream license notices.
- Transitive libraries BlackSharp.Core, DiskInfoToolkit and RAMSPDToolkit-NDD retain their MPL-2.0 licenses and their versioned source references in the bundled package metadata. HidSharp and Mono.Posix.NETStandard retain their respective upstream licenses/notices, also included in `licenses/`.

Afterburner is not redistributed. The adapter implements its documented monitoring shared-memory format from the SDK installed on this PC.

Installer builds include full license/notice documents and dependency package metadata in `licenses/`. LibreHardwareMonitorLib 0.9.6 is used unmodified; its corresponding source is available at the versioned upstream link above and https://codeload.github.com/LibreHardwareMonitor/LibreHardwareMonitor/zip/refs/tags/v0.9.6 . Its MPL-2.0 terms remain applicable; ThermalScope's MIT license does not replace third-party licenses.

The setup wizard is generated with Inno Setup: https://jrsoftware.org/ . The compiler is a development tool, not a bundled monitoring dependency. PawnIO is downloaded separately, verified, and installed only when the user selects that task; it is not embedded in the ThermalScope installer.

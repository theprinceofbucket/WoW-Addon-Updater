# WoW Addon Updater

[English](README.md) | [Deutsch](README_DE.md)

**Version 0.5.0 · Windows · World of Warcraft WotLK 3.3.5a**

A portable updater with a dark interface in English and German. It checks a built-in selection of 21 addon packages and can install or update selected packages. Addons are downloaded from GitHub, GitLab, and selected ZIP and website sources. Intended for the original 3.3.5a client, not current Retail or Classic clients.

## Download and launch

1. Download **WoW-Addon-Updater.exe** from **Releases** on the GitHub project page. GitHub's **Code → Download ZIP** button downloads the source code, not the ready-to-use executable.
2. Copy the EXE into your WoW folder, directly next to **Wow.exe**.
3. Launch the updater. Close WoW before installing or updating addons.

No manual path configuration is needed. Windows PowerShell 5.1 and .NET Framework 4.8 are required. Protected installations, such as those in Program Files (x86), may require running the updater as administrator. The EXE is not digitally signed, so Windows may display a warning.

## Using the updater

Open **⋯** at the top left to switch between **English and German**. The language preference is saved for each Windows user. The menu also contains **Check for updates**: on request, the updater checks this project's latest public GitHub release and asks for confirmation before installing it. Nothing is installed if no release is available. See the detailed guide for checksum verification, the temporary helper, and recovery.

Future self-updates require a regular release with a tag such as `v0.5.1` and exactly one asset named `WoW-Addon-Updater.exe`. Its product version must match the tag, and GitHub must provide a SHA-256 digest for the asset. Prereleases and downgrades are not installed. The application remains a single portable EXE; a temporary helper used to replace the running executable is created in the WoW folder and removed after a successful restart.

**Self-update testing:** Local tests cover executable replacement, restart, cancellation, and cleanup. An end-to-end online update from 0.5.0 to a newer public release has not yet been tested.

- **Find addons / check updates:** Detects supported installed addons, selects their checkboxes, and compares them with downloaded packages. This step does not change installed addon files.
- **Install / update selection:** Installs selected missing or unchecked packages and detected updates. Packages already checked and found up to date are skipped. Downloads from the check are reused within the session.
- **Cancel:** Requests a safe cancellation. A file replacement already in progress is completed or rolled back.
- **Start WoW:** Removes the updater's download cache, hands off the game launch to the normal Windows desktop when the updater is elevated, and closes the window after a successful launch request. Windows Explorer must be running with normal user permissions. If an error occurs, the updater stays open. This function does not delete WoW's own Cache or WTF folders.
- **More:** Restore backups, view their disk usage, delete all backups after confirmation, and save the log to Documents/WoWUpdaterLogs.

The list is sorted alphabetically. Checkboxes are locked while an operation is running, but scrolling remains available. Red highlights the available Cancel button, blue highlights selected detected updates, and green highlights Start WoW after successful completion with no pending updates. Displayed versions come from TOC files; the actual comparison uses file checksums.

## Supported addons

AllStats, AtlasLoot, Bagnon (optional), Cheese, ClassicAPI, CompactRaidFrame, CompactRaidFrame HealEx, DBM Warmane, Details WotLK, Leatrix Maps, Leatrix Plus, MinimapButtonButton, NotPlater, NPCScan, PallyPower, Postal, Questie 3.3.5a, TomTom, WeakAuras WotLK, WhatsTraining WotLK, and WorldMapTooltipScale.

APIDocumentation is included in the WeakAuras package. Questie is installed as **Questie-335**, and NotPlater as **NotPlater-3.3.5**. Select the checkbox manually to install an additional addon. Sources, branches, and folder mappings are defined in `addons.example.json` and embedded in the EXE. An external `addons.json` does not change the EXE's catalog.

## Backups and limitations

Replaced addon folders are backed up under `Interface/AddOns_UpdaterBackups`. WTF settings are neither modified nor backed up. An additional personal backup of Interface and WTF is recommended. For packages containing several folders, a complete restore may require selecting multiple folders from the same backup.

Warperia may limit downloads. The updater does not bypass these limits. A validated Warperia download is reused locally for up to 24 hours, so it may represent an older source version. **Start WoW** clears this cache, requiring new downloads on the next check. Checking other sources downloads archives rather than querying version numbers alone. Addon packages are processed sequentially.

Version 0.5.0 is an early release. Download providers and addon authors may change their file structures. A successful file comparison does not guarantee that an addon works correctly in-game. When reporting an issue, include the updater version, addon name, and log; remove personal paths before publishing the log.

## Building from source

Run this command in the source folder on Windows:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Gui\Build.ps1
```

The script uses the C# compiler included with .NET Framework and creates `WoW-Addon-Updater.exe`. The backend and catalog are embedded, so only the EXE is needed to use the application. Version information is defined in `Gui/VersionInfo.cs` and must be updated consistently for each new release.

See [EXE-Anleitung.md](EXE-Anleitung.md) for the detailed user guide (German), and [CHANGELOG.md](CHANGELOG.md) for release changes (German).

## Credits

This project was created by **theprinceofbucket with assistance from OpenAI ChatGPT/Codex**. AI assisted with planning, programming, debugging, testing, and documentation. The project owner provided decisions, practical testing, and feedback. This is an independent community project, not an official OpenAI or Blizzard product.

Thank you to the authors of the supported addons. Their projects remain independent and are subject to their respective licenses.

## License

The source code of this updater is released under the [MIT License](LICENSE). This license does not automatically apply to downloaded addons. No addon archives are included in this repository.

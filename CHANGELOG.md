# Changelog

Alle nennenswerten Änderungen an MorniLAN. Format nach [Keep a Changelog](https://keepachangelog.com/de/1.1.0/), Versionierung nach [SemVer](https://semver.org/lang/de/).
Ab Meilenstein 9 wird dieser Changelog automatisch aus den Conventional Commits erzeugt.

## [Unreleased]

### Added
- Projektmappe mit Agent (Worker Service), Launcher und Admin (Avalonia 12), Shared und Tests.
- Zentrale Version in `Directory.Build.props`, zentrale Paketversionen in `Directory.Packages.props`.
- Shared-Modelle: `AppEntry`, `AppId`, `ApprovalSet` (Standard: gesperrt), `DeviceInfo`/`DeviceStatus`, `WindowsEditionInfo`, `AdminCommand` (polymorph per JSON).
- Agent: Windows-Dienst-Hosting, Serilog mit täglicher Log-Rotation (14 Tage, max. 10 MB je Datei), Erkennung der Windows-Edition.
- Build-Skript `build.ps1` und GitHub-Actions-CI (Build und Tests).

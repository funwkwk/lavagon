# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [1.1.0] - 2026-11-27

### Added

- Automatic setup: on the first start (and whenever `bin\` has no usable service) Lavagon downloads Apache 2.4, PHP (Thread Safe x64), MySQL 8.4 LTS and Mailpit from their official websites into `bin\`, and installs or updates the Visual C++ runtime when needed. Progress is shown per component in the main window, with *Retry* on failure. No manual downloads any more.

### Changed

- The welcome panel with download links is replaced by the setup progress panel.

## [1.0.1] - 2026-11-26

### Added

- Installer `Lavagon-Setup.exe` (per-user, no admin): install folder of your choice, Start menu / desktop shortcuts, *Settings > Apps* entry, silent mode (`--silent --dir`), upgrade that keeps user files and an uninstaller that keeps projects and databases. `build.ps1` and `release.ps1` build and publish it.

### Changed

- First start: instead of "No services found", a welcome panel lists Apache, PHP, MySQL and Mailpit with their status, official download links, the target folder in `bin\` and links to open the bin folder and the guide.
- Components extracted into `bin\` are detected while Lavagon runs (no restart needed).
- Releases are published from a local machine with the new `release.ps1` (build, zip + SHA256, tag, push, GitHub release); GitHub Actions workflows only run when started manually.
- README: release badge instead of the CI badge, release instructions.
- Branches: `main` is the default, stable branch; development happens on `dev`, which is merged into `main` by hand. `release.ps1` only releases from `main`.

## [1.0.0] - 2026-11-26

### Added

- First public version of Lavagon.
- Main window with per-service switches, *Start All / Stop*, Web, Database, Terminal and Root buttons; tray icon; native menu.
- Services: Apache 2.4 (mod_php), Nginx (php-cgi), MySQL / MariaDB, PostgreSQL, MongoDB, Redis, Memcached, Mailpit.
- PHP 5.6 – 8.6 side by side: per-version `php.ini`, extension toggles, quick settings, DLL preloading, Visual C++ runtime / architecture / NTS checks, Windows time zone.
- Auto virtual hosts (`{name}.test`), hosts file sync through a one-off UAC prompt, local CA with per-site HTTPS.
- MySQL: automatic data directory initialization, graceful shutdown limited to the server Lavagon started, create database, change / copy root password, backup, legacy authentication for PHP < 7.4.
- Quick app recipes (Blank, WordPress, Laravel, Symfony, CodeIgniter, Slim, CakePHP).
- Preferences: run at startup, run minimized, start all automatically, document root, data directory, hostname format, ports.
- GitHub Actions build and release workflows.

[Unreleased]: https://github.com/funwkwk/lavagon/compare/v1.1.0...HEAD
[1.1.0]: https://github.com/funwkwk/lavagon/compare/v1.0.1...v1.1.0
[1.0.1]: https://github.com/funwkwk/lavagon/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/funwkwk/lavagon/releases/tag/v1.0.0

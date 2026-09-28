# Changelog

All notable changes to WdiCheck are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

WdiCheck consumes the WinDriverIndex data schema, which is versioned separately
(`schema_version` in every file). Each release notes the schema versions it supports.

## [Unreleased]

## [0.2.1] - 2026-09-28

Supports WinDriverIndex data schema 2.0.x, and still reads 1.x mirrors.

### Added
- Data 2.0.x `infs.json` `driver_ver_aliases`: an installed build under an alternative DriverVer
  spelling (Realtek's `10.080.0407.2026` for `10.080.50.0407`) matches that build instead of
  showing as "newer than index".
- GitHub Actions release workflow: pushing a `vX.Y.Z` tag checks the tag against the project
  version, runs the tests, builds x64 and ARM64 zips and MSIX packages, and publishes a GitHub
  release with the version's changelog section as notes (`scripts/release-notes.ps1`).

### Changed
- INFs renamed per release (AMD display `u*.inf`) are compared across the directly following
  version line within the installed INF's series (name without digits). A Raphael iGPU on
  `31.0.24002.92` is now shown behind `32.0.21043.5001`, the newest build that lists the device,
  rather than current on 31.x or behind a family water level it can't install.
- The parallel-line note is omitted when the update itself is on the water level's line.
- Build artifacts carry the version: `WdiCheck-<version>-<rid>` (folder and `.zip`) and
  `WdiCheck-<version>-<rid>[-unsigned].msix`, read from `Directory.Build.props` through MSBuild.

## [0.2.0] - 2026-09-28

Supports WinDriverIndex data schema 2.0.0, and still reads 1.x mirrors.

### Added
- **INF-level comparison** using `infs.json`. Each device's installed INF is resolved to
  its original name (`oem114.inf` → `rt640x64.inf`) through SetupAPI, without elevation.
  Its DriverVer is then compared with the newest release of the same INF. Vendors that
  rename the INF per release (AMD display drivers) are matched through the device's
  hardware IDs, keeping to releases on the same version line and device class. Releases of
  the same INF name in another numbering scheme (for example MediaTek's vendor 3.x vs Windows
  Update's year-based 25.x) aren't compared; the package-level fallback handles those.
- **Board matching by SMBIOS keys** (`boards.json` `smbios`): Dell SKU, HP platform ID,
  Lenovo MTM prefix, and MSI board code. Where MSI boards share a code, the name decides.
  Name matching remains for Gigabyte, ASUS and ASRock.
- **Manifest-based refresh.** `manifest.json` is checked first, and cached files whose
  SHA-256 matches are used without a request. An unchanged index costs one small download.
- **Download hints** (`families.json` `download_hint`), such as NVIDIA App, AMD Adrenalin
  and Intel DSA, in the detail pane and the export.
- **Packages that carry an INF update** are labelled "package … contains rt640x64.inf 10.080…".
  Listings of the same build under different DriverVer spellings are pooled.
- **Export columns:** `original_inf`, `matched_inf`, `download_hint`. `newest` and
  `newest_date` replace `newest_on_line` and `newest_on_line_date`.
- **Data notice** on the Overview when the source has no INF data, or publishes a schema
  newer than this build understands.
- `CHANGELOG.md`.

### Changed
- Version-scheme translations (NVIDIA INF → marketing version, Realtek UAD builds) come from
  `families.json` `version_equiv`. The built-in rules are only a fallback for 1.x data.
- Package-level comparison, now the fallback when no INF matches, respects the index's
  `water-level.json` `lines`. A line marked `parallel_to_water` is never treated as
  superseded by the water level, so AMD 31.x is shown as current on its own line, with a
  note about 32.x. The note about another line appears only when a driver on that line
  actually binds the device.
- Listings that name several versions (`6.0.9464.1/12.1.6003.5923`) are no longer used as
  comparison targets.
- Lag for INF matches is measured between INF DriverVer dates.

### Fixed
- False "behind" results for components versioned separately from their package. For
  example, NVIDIA Platform Controllers `32.0.16.1534` was compared with the GPU driver
  (616.92), and Realtek Audio Universal Service with an unrelated `1.4.2.55`.
- "Not comparable" results for per-component INFs (chipset, audio components) that the
  index now covers.

## [0.1.0] - 2026-09-28

First release, against WinDriverIndex data schema 1.0.0.

### Added
- WinUI 3 app with Overview, Drivers, This PC's board and Settings pages. Light and dark
  themes follow the system.
- Device inventory through Windows.Devices.Enumeration: hardware and compatible IDs, and
  the driver version, date, provider and INF.
- Matching against `families.json` by hardware ID. Class-level IDs are ignored, and
  families that share an ID are ranked.
- Package-level comparison within a version line, with NVIDIA and Realtek scheme
  translation, successor-line handling and a sibling-component rule.
- Machine identification from SMBIOS, the vendor's `by-board` report, and BIOS/AGESA
  currency. The board can be overridden.
- Cached API client with ETag/Last-Modified revalidation and an offline fallback.
- CSV and JSON export, and download links (the board vendor's first; https only).
- Build script for unpackaged (self-contained) and MSIX output, dev-certificate script,
  and an asset generator.
- `doc/upstream-requests.md`: problems found in WinDriverIndex and requested changes.

[Unreleased]: https://github.com/dtucny/wdi-check/compare/v0.2.1...HEAD
[0.2.1]: https://github.com/dtucny/wdi-check/compare/v0.2.0...v0.2.1
[0.2.0]: https://github.com/dtucny/wdi-check/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/dtucny/wdi-check/releases/tag/v0.1.0

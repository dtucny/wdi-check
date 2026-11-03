# Changelog

All notable changes to WdiCheck are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

WdiCheck consumes the WinDriverIndex data schema, which is versioned separately
(`schema_version` in every file). Each release notes the schema versions it supports.

## [Unreleased]

### Fixed
- **"Installed here" says why there's no device** on the board page, instead of "No device found" for
  all of them:
  - "Not fitted: this PC has MediaTek Wi-Fi 6E" when the listing covers other configurations of the
    model (Lenovo lists Realtek Wi-Fi beside the MediaTek card some units have) and this PC has
    another listed family of the same kind.
  - "Can't tell: no hardware IDs" for a family the index has nothing to match devices by (Lenovo's
    "Notebook WLAN (multi-silicon)", "Notebook Chipset (AMD)").
- **Drivers that bind only a class ID are matched to their INF.** AMD's HD Audio bus driver
  (`amdafd.inf`) binds only `PCI\VEN_1002&CC_0403`, which the app ignores for matching, so it showed
  as not indexed although the index lists its releases. When the installed INF's own name matches,
  class IDs now count, and a same-named Extension INF isn't taken as the device's driver.

## [0.4.0] - 2026-11-01

### Added
- **Links to the WinDriverIndex site.** The board page links its model's report on the site
  ("WinDriverIndex page", beside "Vendor support page"), from the configured data source, so a full
  mirror links its own site.
- **Graphics card page links.**
  - The GPU maker's driver page ("Get from NVIDIA"), whichever card maker built the card.
  - Each model in "Pick your model" links its vendor page and WinDriverIndex report, to help tell
    which one you have.
  - At the top: the chosen model's WinDriverIndex report and vendor page or, before you've picked one,
    "Search WinDriverIndex", the site's catalogue search.

### Changed
- **The chip maker's own release** is the one on your driver's numbering line when the maker lists one.
  Realtek's page now lists NDIS 10.080.50.0407 beside NetAdapterCx 1125.031.50.0603
  ([WinDriverIndex#15](https://github.com/dtucny/WinDriverIndex/issues/15)). So an NDIS driver is
  shown Realtek's 10.080.50.0407, with Realtek's own date, instead of a release "numbered differently".
- When the newest build of an INF is one the chip maker also lists, its own download is among the
  update's links, beside the board vendors' packages. The chip maker is named (Realtek) instead of
  "Silicon vendor".
- **Drivers: the family's newest on another numbering line no longer reads as an update.** The row is
  labelled for what it is:
  - "Parallel driver": a newer driver model this device can also run, such as Realtek NetAdapterCx
    1125.x beside NDIS 10.x. Switching to it is a choice, not an update.
  - "Other line in family": a package version or another component, such as AMD's chipset package
    against its INFs.
  
  That row is now the only place the pane says this. The match explanation and the separate
  "Different version line" box no longer repeat it.

## [0.3.0] - 2026-11-01

### Added
- **A page per add-in graphics card.**
  - With an add-in card, "This PC" in the navigation expands into the board (Motherboard, Laptop or
    Desktop) and one page per card ("MSI RTX 4070 Ti"). The Overview's graphics row opens the card's page.
  - The card is identified from its PCI IDs against WinDriverIndex's 1,353 ASUS, MSI and Gigabyte cards.
    Those IDs are the GPU, its revision and the card maker, which a maker's models on one GPU share
    (20 MSI cards for an RTX 4070 Ti).
  - The page asks which model you have only when the answer would change it, meaning one of the
    candidates lists a VBIOS. Otherwise it shows the maker's listing, which the models share, or both
    ends of it where it differs, and says why it didn't ask. You can always pick from the list or
    search, and the choice is remembered.
  - Makers that only link to the GPU maker's driver (MSI) get that driver's newest release instead.
  - Subsystem device IDs, once WinDriverIndex publishes them
    ([WinDriverIndex#12](https://github.com/dtucny/WinDriverIndex/issues/12)), will narrow the match
    to the exact model.

### Changed
- **Behind the vendor, or the vendor behind the newest.** Each row of a vendor's listing now reads left
  to right:
  - your driver against the vendor's ("Current with MSI", "234 d behind MSI", "Ahead of MSI");
  - the vendor's version against the newest anywhere ("299 d behind newest").

  Where WinDriverIndex has the INFs inside the vendor's package, the comparison is INF to INF; otherwise
  it uses the listed version, and only where that is a build of the same driver.
- "Installed here" says "Windows driver" or "No device found" instead of a blank.
- The Drivers page says the same for each driver. On the Overview, "This machine" and "Needs
  attention" show the vendor's latest version, and a second label for your driver against it next to
  the lag behind the newest ("Current with MSI" · "186 d behind newest"). "Needs attention" lists the
  updates your vendor offers first.
- "This PC's board" is now "This PC". The board page is titled by what the machine is.

### Fixed
- The Overview's processor row showed one chipset component's version (3.0.5.0) and opened only that
  component (the GPIO controller) in Drivers. A row for a package of drivers now says how many there
  are and how they stand ("AMD Chipset · 7 drivers, all current") and opens all of them.
- Drivers: long download link names wrap instead of running off the pane. The section is titled
  "Where to get it" for a driver that's already current. "Family newest" says when it's a package
  version numbered differently from the driver (AMD's chipset package against its INFs).
- Drivers: a family without a download hint links the chip maker's own page, saying when its newest
  is on another numbering line. Realtek's page carries both the NDIS 10.x and the NetAdapter 1125.x
  drivers.

## [0.2.8] - 2026-11-01

### Added
- **Licence.** WdiCheck is free software under the GNU GPL version 3 or later, with additional
  terms in `NOTICE`: forks keep the "Based on WdiCheck by D. Tucny" attribution and must say
  they're modified. Settings › About shows the credit and links the licence and source, and
  releases include `LICENSE` and `NOTICE`. `CONTRIBUTING.md` explains how to report problems and
  the terms contributions are accepted under.

### Dependencies
- .NET SDK 10.0.100 → 10.0.403, so the app bundles the .NET 10.0.14 runtime.
- Windows App SDK 2.3.9 → 2.6.4 (`Microsoft.WindowsAppSDK.WinUI`). It now brings a matching
  `Microsoft.WindowsAppSDK.InteractiveExperiences` itself, so the separate pin to 2.1.9 is gone.
- Tests run on xUnit v3 4.0 through Microsoft.Testing.Platform (`dotnet test --project …`).

### Removed
- `doc/upstream-requests.md`. Open requests to WinDriverIndex are now
  [issues on its repository](https://github.com/dtucny/WinDriverIndex/issues): currently
  [9](https://github.com/dtucny/WinDriverIndex/issues/9), [10](https://github.com/dtucny/WinDriverIndex/issues/10)
  and [11](https://github.com/dtucny/WinDriverIndex/issues/11).

## [0.2.7] - 2026-11-01

### Added
- **"This machine" covers more than the board.** Below the BIOS, the Overview lists the processor
  (with its chipset driver) and each GPU, with the driver's status. A GPU is labelled built-in or by
  card maker ("MSI card") from its PCI subsystem ID. Where WinDriverIndex lists the chip vendor's own
  release (AMD, NVIDIA, Intel, Realtek), the row shows it with a download link, and says when it's
  numbered differently from the installed driver: AMD's chipset package 8.09.28.1124 holds drivers
  numbered 3.0.5.0. For graphics, the release shown is the newest of the chip vendor's own INFs
  that binds the GPU: AMD's 26.10.2 is 32.0.32015.12012 for RDNA3/4 but 32.0.21045.20006 for
  RDNA2 and Ryzen 7000 iGPUs. "All components" expands to every other driver family on the PC.
  Clicking a row opens the device in Drivers.
- The Overview header names the CPU and any add-in GPU after the board.
- Uses the INF rows WinDriverIndex now extracts from NVIDIA's, AMD's and Intel's own graphics
  installers (data 2.0.x, 2026-11-01), so a driver installed from the chip vendor compares like any
  other: a PC on NVIDIA 621.01 is current, not "newer than index".

### Changed
- **The Windows Update search no longer starts PowerShell.** WdiCheck now calls the Windows Update
  Agent's COM API directly, through CsWin32's marshalling-free bindings, which work in the trimmed build.
  Machines that block or flag `powershell.exe -EncodedCommand` (AppLocker, WDAC, Constrained Language
  Mode, Defender attack-surface rules) can now run the search, and it no longer looks like a script
  launcher to security tools. Cancelling or timing out now asks the agent to abort, where the old code
  killed the PowerShell process. Common failures are explained: the service is disabled, policy blocks
  Windows Update, or there's no network.

## [0.2.6] - 2026-09-29

### Added
- **Windows Update offers for devices the index doesn't know about** now appear under "Needs attention"
  and in the Drivers list, even with in-box and unmatched devices hidden. On a Lenovo Legion Slim 5, Windows Update offers
  Lenovo's Universal Device Client driver 26.8.0.29, which is bound to a `ROOT\` ID the index doesn't collect.
- **Catalog-only updates are labelled.** When the only listing of the newer build is the Windows Update
  Catalog, the detail pane says "a newer build exists (another OEM's, via Windows Update)": Microsoft
  offers it to some OEM's machines, which doesn't mean this machine will be offered it.
- The JSON export lists which devices each Windows Update offer applies to (`applies_to`). Both exports
  add a note per offer (`windows_update_note`: optional, installs automatically, or matches installed BIOS).

### Fixed
- **The installed BIOS counted as a Windows Update driver update.** Windows Update offers an OEM firmware
  package when the firmware device only has Microsoft's generic driver, even if it holds the BIOS that's
  already installed (Legion Slim 5: Lenovo firmware 1.50.0.0, dated the same day as BIOS M3CN50WW). A
  system-firmware offer dated the same day as the installed BIOS is now shown as "most likely the BIOS you
  already have" and not counted. The ESRT (`HKLM\HARDWARE\UEFI\ESRT`) identifies which firmware resource is
  the BIOS.
- **Wrong Windows Update page.** Offers that aren't optional install automatically and are listed on the
  main Windows Update page, not under Optional updates. The button now opens the page that lists them.

## [0.2.5] - 2026-09-29

### Added
- **What Windows Update offers this PC.** After each check, WdiCheck asks the Windows Update
  Agent which drivers it offers this exact machine (`Microsoft.Update.Session`, searching
  `IsInstalled=0 and Type='Driver'` against Windows Update). Unlike a Catalog search, this applies
  Microsoft's machine-specific targeting. It's shown on the Overview, with a link to Optional
  updates, and per device in the detail pane. Where WdiCheck finds a newer build but Windows Update
  offers nothing, the detail pane says the build isn't targeted at this machine. Offers are also
  in the CSV/JSON export.
  - The search runs in the background (typically 10–60 s), needs no elevation and installs nothing.
  - It uses a separate Windows PowerShell process, so a stuck search can be abandoned; it gives up
    after 3 minutes.
  - It can be turned off in Settings.

## [0.2.4] - 2026-09-29

### Added
- **BIOS updates shown like driver updates.** When the vendor lists a newer BIOS, the Overview shows
  installed → newest (`1.L2 → 7D73v1L3`), mentions it in the headline, and puts it at the top of
  "Needs attention". The board page shows the newest version with its date, and a "Get the update"
  link to the vendor's support page. Uses the index's new `last_bios_version`.

### Fixed
- **BIOS shown as "behind" when current.** Windows reports a BIOS's build date and the index lists
  the vendor's publish date, and vendors publish months after building: the Legion Slim 5's M3CN50WW was built
  2026-04-23 and published 2026-08-07. It is Lenovo's newest, but showed as 106 days behind. With
  dates only, a vendor BIOS published within 180 days of the installed build date now shows
  "Probably current", with an explanation. When the index provides the vendor's BIOS version
  (`last_bios_version`), it's compared directly. The comparison tolerates vendor formats: MSI lists
  `7D73v1L2` for SMBIOS `1.L2`, and HP SMBIOS says `S70 Ver. 01.29.00` for `01.29.00`. Formats
  that can't be compared fall back to the date rule rather than a false "behind".
- **Another OEM's INF used as the update target.** Lenovo's Realtek audio (`hdxacplv.inf`) was
  compared with MSI's `hdxacpmsi.inf`, because both bind the same chip. INFs reached through a
  hardware ID now count only in the installed INF's series.
- **Later build shown as current.** A newer build of the same release (Bluetooth `26.40.3.65` vs
  installed `26.40.2.228`) showed as current because one vendor listed the older build later. The
  "sibling component" rule now applies only when the first two version numbers differ.
- **Board report older than the index.** Board reports aren't covered by the manifest, so a cached
  report could predate the index data it was shown with. It is now re-checked when that happens.
- **Board page table scrolled sideways by a few pixels.** Its fixed column widths were slightly
  wider than the card. The columns now share the card's width and wrap, and the table scrolls only
  when the window is narrower than 880 px.
- **Drivers list squeezed the device column to nothing** at window widths from about 1,100 to
  1,500 px. The column layout switched on window width, but the list's real width also depends on
  the navigation and detail panes. Rows now switch between columns and the compact layout
  (installed → newest under the name) on the list's own width.
- **Garbled board search placeholder** (`â€œB650 Tomahawkâ€`) and the `·` separator in its
  suggestions, both broken by an encoding slip in 0.2.3.

## [0.2.3] - 2026-09-29

### Changed
- Release packages are about a third of the size: zip and MSIX 91 MB → 32 MB (231 MB → 82 MB unpacked).
  - The app references the `Microsoft.WindowsAppSDK.WinUI` component instead of the
    `Microsoft.WindowsAppSDK` metapackage, which also bundled AI/ML (ONNX Runtime, DirectML),
    Search and Widgets.
  - Release publishes are trimmed, and the MSIX is now built with `dotnet publish` so it is
    trimmed too.

## [0.2.2] - 2026-09-28

The first published build of the 0.2.1 changes: INF driver-version aliases, AMD per-release INF
series matching, versioned artifacts and the release workflow (see 0.2.1 below). Also:

### Added
- Screenshots in the README.

### Fixed
- The release build failed on GitHub's runner: PowerShell 7 splits unquoted `-p:Name=Value`
  arguments, so MSBuild saw a second project. All MSBuild property arguments are now quoted.
- The board page read "Matched from SMBIOS SMBIOS board code …".

## [0.2.1] - 2026-09-28 [not published]

Tagged, but the release build failed (see 0.2.2), so no packages were published. 0.2.2 includes everything below.

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

[Unreleased]: https://github.com/dtucny/wdi-check/compare/v0.4.0...HEAD
[0.4.0]: https://github.com/dtucny/wdi-check/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/dtucny/wdi-check/compare/v0.2.8...v0.3.0
[0.2.8]: https://github.com/dtucny/wdi-check/compare/v0.2.7...v0.2.8
[0.2.7]: https://github.com/dtucny/wdi-check/compare/v0.2.6...v0.2.7
[0.2.6]: https://github.com/dtucny/wdi-check/compare/v0.2.5...v0.2.6
[0.2.5]: https://github.com/dtucny/wdi-check/compare/v0.2.4...v0.2.5
[0.2.4]: https://github.com/dtucny/wdi-check/compare/v0.2.3...v0.2.4
[0.2.3]: https://github.com/dtucny/wdi-check/compare/v0.2.2...v0.2.3
[0.2.2]: https://github.com/dtucny/wdi-check/compare/v0.2.1...v0.2.2
[0.2.1]: https://github.com/dtucny/wdi-check/compare/v0.2.0...v0.2.1
[0.2.0]: https://github.com/dtucny/wdi-check/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/dtucny/wdi-check/releases/tag/v0.1.0

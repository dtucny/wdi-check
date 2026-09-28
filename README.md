# WdiCheck

A Windows desktop app (WinUI 3) that checks the drivers installed on this PC against
[WinDriverIndex](https://windriverindex.tucny.com/) and shows which ones have a newer release.

It reads the hardware IDs and driver versions Windows reports for every present device,
matches them against the index's driver families, and compares each installed version with the
newest release anywhere: across MSI, Gigabyte, ASRock, ASUS, Lenovo, Dell, HP, Windows Update
and the silicon vendors. It also identifies the machine from SMBIOS and shows the vendor's own
support listing for it, including BIOS currency.

WdiCheck only reads. It never downloads or installs drivers; links go to the listing's own page.

## What you see

- **Overview** – counts by status, the identified machine with its BIOS status, and the drivers
  that need attention (sorted by how far behind they are). You can export CSV or JSON.
- **Drivers** – every matched device, grouped by component, with installed version, the newest
  comparable release and a status. The detail pane explains the match: which hardware ID hit,
  which families share it and why one was chosen, the releases on the same version line, and
  download links (your board vendor's first).
- **This PC's board** – the vendor listing for your motherboard or laptop (the site's
  `by-board` report), with an *Installed here* column, BIOS installed vs newest listed, and AGESA
  status on AMD boards. If automatic matching is wrong or ambiguous, search the catalogue and pick
  your model (remembered).
- **Settings** – theme, data source (any mirror of `/v1/latest/`), offline cache, attribution.

### Statuses

| Status | Meaning |
|---|---|
| Current | At or above the newest release on its version line. *Newer than index* if above anything listed. |
| Behind (≤ 1 yr / > 1 yr) | A newer release exists. Lag = days between your version's first listing (or its driver date) and the newer release. |
| ≈ | Your exact version isn't in the index; compared with the nearest listed versions on the same line. Treat as a hint. |
| Not comparable | The device matched a family, but its version is on a line no listing uses — typically a per-component INF version inside a larger package. |
| Windows driver | The device uses a Windows in-box driver; a vendor driver family exists for the hardware. |
| Not in index | No hardware ID of the device is in the index (most Microsoft/virtual/peripheral devices). |

### How matching works

1. Hardware IDs, then compatible IDs, most specific first. Class-level IDs (`PCI\CC_*`,
   `USB\Class_*`) are ignored, because they pair in-box drivers with unrelated vendor families.
2. The first ID found in `families.json` gives the candidate families. When several families share
   it (about a quarter of the index's IDs do), the family that lists your exact version wins, then
   one that lists your version line, then one with a water level, then a silicon-vendor match.
3. Versions are compared within a **version line** (leading number + number of parts), because
   vendors publish the same driver in incompatible schemes (package vs INF versions, NDIS 10.x vs
   NetAdapter 1125.x). Two-part marketing versions (NVIDIA `616.92`) form a single line. The family
   newest also counts when it sits on the directly following line (AMD INF 31.x → 32.x); other
   lines are shown as a note.
4. NVIDIA INF versions (`32.0.16.1692`) are translated to the marketing scheme (`616.92`), and
   ASRock's Realtek UAD versions (`9360.1_UAD`) to `6.0.9360.1`, mirroring the site's rules.
5. A numerically higher version released no later than yours is treated as a sibling component or
   branch, not an update.

Known limitation: the index publishes package versions, while Windows reports each INF's own
version, so multi-INF packages (AMD chipset, Intel chipset/Serial IO) can only be compared
approximately. See [doc/upstream-requests.md](doc/upstream-requests.md).

## Data

All data comes from the WinDriverIndex static API (`https://windriverindex.tucny.com/v1/latest/`):
`families.json`, `water-level.json`, `artefacts.json`, `boards.json` and `by-board/{id}.json`.
Responses are cached in `%LOCALAPPDATA%\WdiCheck\cache` and revalidated with ETags, so a refresh is
usually a few "not modified" responses, and the app still works offline from the last copy.

Index data is © WinDriverIndex contributors, licensed [CC-BY-4.0](https://creativecommons.org/licenses/by/4.0/).
"Newest" means the newest version any source has published, not a judgement that it is good.

## Building

Requirements: Windows 10 1809+ (Windows 11 recommended) and the .NET 10 SDK
(`winget install Microsoft.DotNet.SDK.10`). Visual Studio is not required.

```powershell
dotnet test tests/WdiCheck.Core.Tests          # unit tests
dotnet build WdiCheck.slnx                     # x64 debug build
src\WdiCheck.App\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\WdiCheck.exe

./scripts/build.ps1                            # tests + unpackaged zip + unsigned MSIX → artifacts/
./scripts/build.ps1 -Platform ARM64 -Unpackaged
```

**Unpackaged** (`artifacts/WdiCheck-win-x64/`, plus a zip) is self-contained: .NET and the
Windows App SDK are bundled, so it runs by double-clicking `WdiCheck.exe`, with nothing to install.

**MSIX**: Windows only installs signed packages whose signer it trusts. For local testing:

```powershell
./scripts/new-dev-cert.ps1 -Trust          # elevated; creates CN=WdiCheck Dev and trusts it
./scripts/build.ps1 -Msix -CertificateThumbprint <thumbprint printed above>
Add-AppxPackage artifacts\msix\WdiCheck.App_0.1.0.0_x64_Test\WdiCheck.App_0.1.0.0_x64.msix
./scripts/new-dev-cert.ps1 -Untrust        # when done
```

The warning `Path to mspdbcmf.exe could not be found` on MSIX builds is expected without Visual
Studio; it only means no symbols package is produced.

`scripts/generate-assets.ps1` regenerates the icon and logo PNGs.

## Layout

```
src/WdiCheck.Core/         API client + cache, version parsing, index, device & board matching, export
src/WdiCheck.App/          WinUI 3 app (MVVM, CommunityToolkit.Mvvm)
tests/WdiCheck.Core.Tests/ xUnit tests (fixtures built from real index rows)
scripts/                   build, dev certificate, asset generation
doc/upstream-requests.md   problems found in WinDriverIndex and suggested API changes
```

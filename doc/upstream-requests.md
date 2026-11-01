# WinDriverIndex: requests from WdiCheck

Problems found in the [WinDriverIndex](https://windriverindex.tucny.com/) data while building
WdiCheck, and the changes requested. WdiCheck works around each open item until the index changes.

Last reviewed 2026-11-01, against data schema 2.0.x (publish of 2026-11-01).

## Status

**Open**: needs a change in the index. **Done**: changed in the index, and WdiCheck uses it.
**Declined**: the maintainer decided against it; the reason is in the history below.

| # | Request | Index | WdiCheck |
|---|---|---|---|
| 9 | [Families with no HWIDs](#9-families-with-no-hwids-and-17-lenovo-inf-contents) | **Open** (crawler job) | Shows those families on the board page only |
| 17 | [Lenovo INF contents](#9-families-with-no-hwids-and-17-lenovo-inf-contents) (e.g. `hdxacplv.inf`) | **Open** (same crawler job as #9) | Falls back to package-level comparison |
| 14 | [Year-numbered `mtkwl6ex.inf` INF rows](#14-year-numbered-mediatek-inf-rows) | **Open** (payloads needed) | Year-numbered listings are routed and compared within their own line; INF-level comparison waits for rows |
| 19 | INF rows for silicon-vendor releases | Done (2026-11-01: NVIDIA, AMD, Intel Arc/Iris graphics; rows have `sources: ["silicon"]`) | Used: a GPU's chip-vendor release is the newest silicon INF row binding it, never the family's newest |
| 18 | PCI match keys on graphics cards | Partly done (2026-11-01: `pci.device`, `pci.subsys_vendor`); card model not available from any source | Not used: a match would be "one of MSI's 20 RTX 4070 Ti cards", and they all get the chip vendor's driver. Names the card maker from the PCI subsystem vendor |
| 16 | BIOS version in `by-board` | Done (2026-09-29: `last_bios_version`, `last_bios_version_date`) | Used: compared with SMBIOS `BIOSVersion` (tolerating vendor formats such as MSI `7D73v1L3` for `1.L3`); shown as installed → newest like a driver update |
| 5 | JSON body and short TTL for 404s | Done (Cloudflare rule: `{"error":"not_found"}`, `max-age=300`) | Treats any 404 as "not indexed"; never parses the body |
| 1 | `by-hwid` kept only the last family written | Done (2.0.0, breaking) | Not affected; WdiCheck doesn't use `by-hwid` |
| 2 | Family HWID sets too broad | Done (2.0.0) | Fewer ambiguous matches; ranking kept for genuine sharing |
| 3 | Junk HWIDs from INF string keys | Done (2.0.0) | Local filter kept as a safeguard |
| 4 | Generic class-code IDs in family sets | Done (2.0.0) | Local filter kept as a safeguard |
| 6 | INF-level versions (`infs.json`) | Done (2.0.0) | Used: installed INF compared with the newest release of the same INF |
| 7 | Scheme rules and version lines | Done (2.0.0: `version_equiv`, `lines`) | Used; built-in rules only for 1.x data |
| 8 | SMBIOS match keys on boards | Done (2.0.0: Dell, HP, Lenovo, MSI) | Used; names only for Gigabyte, ASUS and ASRock |
| 10 | `version_normalised` type; undocumented files | Done (2.0.0) | Nothing needed |
| 11 | Manifest file | Done (2.0.0: `manifest.json`) | Used: unchanged files skip the download |
| 11 | Compact client bundle | Declined | Not needed with the manifest |
| 12 | Download hints | Done (2.0.0: NVIDIA, AMD, Intel) | Shown under "Get the update" and in the export |
| 13 | Two DriverVer spellings of one build | Done (2.0.x: `driver_ver_aliases`) | Used: an alias counts as the same build |
| 15 | AMD 31.x wrongly marked parallel to 32.x | Done (2.0.x: line spans fixed) | Follows the installed INF's `u*.inf` series to the newest build listing the device |

## Open requests

### 9. Families with no HWIDs, and 17. Lenovo INF contents

21 families have empty `hwids`, so they can't be matched to devices. Six are `(preinstall)` variants
that pool their HWIDs with the full family by design, so 15 are really empty; two of those
(`USB-C Connector Manager`, `Intel WAPI`) are Dell-only, and Dell's installers yield no INFs. Lenovo's customised INFs,
such as `hdxacplv.inf` for Realtek audio, have no `infs.json` rows. Both happen because HP and
Lenovo packages are indexed from catalogue metadata only, never downloaded. The maintainer
plans to fetch those payloads in a later crawler run. Sizing (2026-11-01): HP is 382 softpaqs
(14.8 GB) for the newest package per family plus unassigned ones; Lenovo is 1,108 packages, whose
payload URLs sit in descriptor XML the crawler doesn't parse yet.

Related: Intel's Wi-Fi, Bluetooth and chipset installers are WiX Burn bundles that the INF extractor
can't unpack yet, so those families have only board vendors' INF rows (#19, round 4 in the history).

### 14. Year-numbered MediaTek INF rows

Lenovo's year-numbered MediaTek builds (25.x/26.x) are now routed to the right families as a
parallel line (done in 2.0.x). `infs.json` still has only MediaTek's vendor-numbered
`mtkwl6ex.inf` (3.x), so a machine on the Windows Update build (a Legion Slim 5: `25.40.2.579`) can't be
compared INF-to-INF. The fix needs the Lenovo or Windows Update payloads (same crawler job as #9).

---

## History

<details>
<summary>Round 4 (2026-11-01): the "This machine" summary (#18 partly done, #19 done)</summary>

- **INF rows for silicon-vendor releases (#19).** Releases from the `silicon` source had no
  `infs.json` rows. NVIDIA's `621.01` (INF DriverVer `32.0.16.2101`) was listed, but the newest
  `nv_disp*.inf` row was 616.92, so a PC on 621.01 was "newer than index". For AMD, WdiCheck
  couldn't tell whether the newest release, `32.0.32015.12012`, covered this PC's Raphael iGPU
  (`DEV_164E`). *Resolution:* each silicon page's installer is downloaded and its INFs extracted,
  as rows with `sources: ["silicon"]`, a `driver_date`, `first_published` = the release date, and
  no `artefact_ids`. Covered: NVIDIA, AMD and Intel Arc/Iris graphics; Intel's Wi-Fi, Bluetooth and
  chipset installers are WiX Burn bundles that aren't unpacked yet. AMD 26.10.2 is two branches in
  two installers: `…-win11-b.exe` is `32.0.32015.12012` (`u0205499.inf`: RDNA3/4, e.g. the 780M's
  `DEV_15BF`), and `…-win11-a.exe` is `32.0.21045.20006` (`u0205498.inf`: RDNA2 and Raphael). So the
  family's newest doesn't cover `DEV_164E`. For AMD graphics, compare against the newest `u*.inf`
  row binding the device, never the family water. WdiCheck had counted an installed build the
  family lists as current; that workaround is removed, because it would be wrong for AMD `-a`
  devices.
- **PCI match keys on graphics cards (#18).** The 1,353 `graphics-card` entries had no match keys.
  This PC's card reports `PCI\VEN_10DE&DEV_2782&SUBSYS_51301462`: chip RTX 4070 Ti, maker MSI
  (`1462`), model `5130`, which GPU-Z resolves to the Ventus 3X OC. Requested: `pci` with device,
  subsystem and boost-clock keys. *Resolution:* every card now has
  `"pci": {"device": ["10DE:2782"], "subsys_vendor": "1462"}`, from the display INFs' own device
  names. Where one AMD device ID is several chips, the revision is appended (`1002:744C:C8` is the
  RX 7900 XTX). The card model (`subsys`) isn't published: pci.ids has no subsystem entries for
  `10DE:2782`, NVIDIA's INFs name every partner ID "RTX 4070 Ti", and VBIOS downloads cover only
  about 330 of 1,353 cards. Without it a boost clock separates nothing, so it isn't added either.
- **Also fixed:** AMD's own chipset release was stuck at 8.08.12.551 since late September, because
  AMD renamed the installer to lower case. It's now 8.09.28.1124.

</details>

<details>
<summary>Round 3 (2026-09-29): from a Lenovo Legion Slim 5, checked against Lenovo Vantage (#16 done, #17 open)</summary>

- **BIOS version, not just date (#16).** `by-board` `bios` had only `last_bios`, the date the vendor
  published its newest BIOS. Windows reports the BIOS's *build* date, and vendors publish weeks to
  months later: the Legion Slim 5's M3CN50WW was built 2026-04-23 and published 2026-08-07. Vantage
  confirms it is the newest, but a date comparison said 106 days behind. Requested:
  `last_bios_version` (Lenovo `M3CN50WW`, MSI `1.L2`), so an exact match with the SMBIOS
  `BIOSVersion` is a reliable "current". *Resolution:* published 2026-09-29 as `last_bios_version`
  and `last_bios_version_date`. Vendor formats differ from SMBIOS (MSI lists `7D73v1L3` for SMBIOS
  `1.L3`), so WdiCheck compares the same-length tail of the two.
- **Lenovo INFs (#17).** WdiCheck used to compare Lenovo's `hdxacplv.inf` with MSI's
  `hdxacpmsi.inf`, because both bind the same Realtek chip. WdiCheck now counts only INFs in the
  installed INF's series, and otherwise falls back to package-level comparison.

</details>

<details>
<summary>Round 2 (2026-09-28): observations on the 2.0.0 data (#13–#15), all addressed in 2.0.x</summary>

- **Two DriverVer spellings for one build (#13).** `rt640x64.inf` had rows `10.080.0407.2026` and
  `10.080.50.0407`, both dated 2026-04-07, so the first always sorted as "newest".
  *Resolution:* Realtek's two patterns are folded into one row, with the other spelling in
  `driver_ver_aliases`.
- **Same INF name, different numbering schemes (#14).** `mtkwl6ex.inf` was indexed only in vendor
  3.x numbering, while Windows Update ships 25.x/26.x. *Resolution:* year-numbered listings are
  routed by chip and form a parallel line that never sets the water level. INF rows are still
  open.
- **Parallel AMD display lines (#15).** 31.x was marked parallel to 32.x, although 32.x display
  INFs bind a Raphael iGPU (`PCI\VEN_1002&DEV_164E`). *Resolution:* 31.x wasn't parallel; it was
  flagged because a vendor re-listed a 31.x package in 2026. Line spans now use first appearances
  and need 90 days of overlap. The per-device answer is the newest `u*.inf` row binding the device
  (32.0.21043.5001), not the family water level.

</details>

<details>
<summary>Round 1 (2026-09-28): original report against schema 1.0.0 (#1–#12)</summary>

Numbers are from the `2026-09-22T15:22:43+00:00` publish.

#### 1. `by-hwid/{hwid}.json` keeps only the last family written

`publish.py` → `_emit_by_hwid` wrote `by-hwid/<hwid>.json` once per (family, HWID) pair, so a
shared HWID's file described whichever family came last. 15,836 of 59,138 HWIDs (27%) were
shared; `by-hwid/PCI_VEN_10EC+DEV_8125.json` (an RTL8125) returned "Realtek 8126 LAN".
*Resolution:* each file lists every family, with `match_share`; `known_versions` moved to
`by-family/{id}.json` (by-hwid went from 9.1 GB to about 40 MB).

#### 2. Family HWID sets are over-broad

"Realtek Wi-Fi" contained `PCI\VEN_10EC&DEV_8125` (a LAN chip), and the Realtek LAN families
contained each other's IDs. *Resolution:* chip-specific IDs go to their own family, an INF counts
only for families that ship it consistently, and preinstall variants pool with their full family.
Shared HWIDs fell from 15,836 to about 5,200.

#### 3. Malformed HWIDs from INF parsing

`USB\CLASS_01.DEVICEDESC`, `PCI\AMDPCIE.DEVICEDESC`, `ACPI\PARAMETERS` and similar were string-table
keys or section names. *Resolution:* filtered at extraction and when reading stored INF rows.

#### 4. Class-code IDs match in-box drivers

`PCI\CC_010802` matched Windows' in-box NVMe driver to "AMD RAID". *Resolution:* IDs with no device
part are left out (18 were truly generic; IDs such as `PCI\VEN_8086&DEV_4D28&CC_0401` stay).

#### 5. 404s return HTML

Missing files returned Cloudflare's 27 KB HTML 404 page, cached for 4 hours, so a lookup that
missed could stay a cached 404 for hours after a deploy added the file. *Resolution:* a Cloudflare
rule on `/v1/*` now returns `{"error":"not_found"}` as `application/json` with `max-age=300`.

#### 6. INF-level versions

Windows reports each device's INF `DriverVer`, but listings carried package versions, so AMD GPIO
`2.2.0.136` was compared with the AMD Chipset package `7.12.30.211`. *Resolution:* `infs.json`, one
row per (INF name, DriverVer), with HWIDs stripped of SUBSYS/REV.

#### 7. Version-scheme rules as data

`SCHEME_EQUIV` and the parallel-line logic existed only in `publish.py`. *Resolution:*
`families.json` `version_equiv` (`{pattern, replace, flags, note}`), and `water-level.json` `lines`
with `parallel_to_water`.

#### 8. SMBIOS identifiers on boards

*Resolution:* `boards.json` `smbios` with `system_sku` (Dell), `baseboard_product` (HP),
`system_product_prefix` (Lenovo) and `baseboard_product_contains` (MSI). Gigabyte, ASUS and ASRock
publish no identifier beyond the model name.

#### 9. Families with no HWIDs

See Open requests. The maintainer treats an empty `hwids` list as the "board only" signal, so no
separate flag was added.

#### 10. Consistency and documentation

`artefacts.json` `version_normalised` was a JSON-encoded string, and several files were
undocumented. *Resolution:* now an array; `artefacts.json`, `changes.json`, `infs.json`,
`manifest.json` and `by-family/` are listed on the homepage and in the README.

#### 11. Client bundle and manifest

*Resolution:* `manifest.json` added, with `{bytes, sha256}` per file. The `client-index.json`
bundle was declined: it would repeat about 7 MB of existing files and could drift out of sync, and
compression already addresses size.

#### 12. Download hints

*Resolution:* `families.json` `download_hint` for NVIDIA, AMD and Intel families. OEM-customised
drivers (Realtek audio and LAN, MediaTek, several Intel components) have none; clients should
point to the board's `support_url`.

</details>

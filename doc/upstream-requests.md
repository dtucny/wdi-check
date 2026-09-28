# WinDriverIndex: problems found and suggested changes

Building WdiCheck, a local checker that matches a PC's installed drivers against the
[WinDriverIndex](https://windriverindex.tucny.com/) static API (spec §11, roadmap §7's "check my actual PC"),
turned up the issues below. WdiCheck works around every one of them, and each workaround can be
removed once the site changes. Numbers are from the `2026-09-22T15:22:43+00:00` publish.

Items are ordered by how much they would improve a client's accuracy.

## Status (schema 2.0.0, live 2026-09-28; adopted in WdiCheck 0.2.0)

| # | Request | Status | WdiCheck |
|---|---|---|---|
| 1 | by-hwid last-writer-wins | Fixed (breaking; `known_versions` moved to `by-family/{id}.json`) | Not affected; WdiCheck doesn't use by-hwid |
| 2 | Over-broad HWID sets | Fixed (chip IDs owned by one family; ~5,200 shared HWIDs remain) | Fewer ambiguous matches; ranking stays for genuine sharing |
| 3 | Junk HWIDs | Fixed | Local filter can stay as a safeguard |
| 4 | Class-code IDs | Fixed (18 truly generic IDs removed) | Local filter can stay |
| 5 | JSON 404s | Needs a Cloudflare rule; also shorten the 404 TTL | WdiCheck already treats any 404 as "not indexed" |
| 6 | INF-level versions | Added `infs.json` (per `inf_name` + DriverVer; HWIDs without SUBSYS/REV) | **Adopted.** Original INF name via SetupAPI; DriverVer compared with the newest release of that INF; package-level matching is the fallback |
| 7 | Scheme rules / lines | Added `families.version_equiv`, `water-level.lines` with `parallel_to_water` | **Adopted.** Built-in rules kept only for 1.x data; `parallel_to_water` vetoes the successor rule |
| 8 | SMBIOS keys | Added for Dell, HP, Lenovo, MSI | **Adopted.** Keys first; MSI shared codes resolved by name |
| 9 | Families without HWIDs | Empty `hwids` is the board-only signal | Show those only on the board page (already the case) |
| 10 | Consistency | Fixed | Nothing to change (array field isn't bound) |
| 11 | Manifest / bundle | `manifest.json` added; bundle declined | **Adopted.** Cached files whose SHA-256 matches skip the request |
| 12 | Download hints | Added for NVIDIA, AMD, Intel families | **Adopted.** Shown first under "Get the update" and in the export |

### New observations from the 2.0.0 data (maintainer's second round: all three addressed in data 2.0.x)

> Status: the Realtek spellings are folded into one row with `driver_ver_aliases` (WdiCheck reads
> them). Lenovo's year-numbered MediaTek builds are routed to Wi-Fi 6E/7 as a parallel 25.x line
> (WdiCheck compares within that line through the package-level fallback; year-numbered
> `mtkwl6ex.inf` INF rows await payload crawling). AMD 31.x is no longer marked parallel, and the
> per-device answer comes from `infs.json`: WdiCheck follows the installed INF's `u*.inf` series
> to the newest build listing the device.


- **Two DriverVer spellings for one build.** `rt640x64.inf` has rows `10.080.0407.2026` and
  `10.080.50.0407`, both dated 2026-04-07. The normalised arrays differ (`[10,80,407,2026]` vs
  `[10,80,50,407]`), so the first always sorts as "newest". WdiCheck pools their download links,
  but if both are the same build, one canonical row (or an alias field) would be cleaner.
- **Same INF name, different numbering schemes.** `mtkwl6ex.inf` is indexed only in MediaTek's
  vendor 3.x numbering, while Windows Update ships it as 25.x/26.x. A machine on the WU build
  (WILLOW: `25.40.2.579`) can't be ordered against the index. Indexing the WU builds of the INF,
  or marking rows with the source scheme, would let clients compare them.
- **Parallel AMD display lines.** The index marks AMD 31.x as `parallel_to_water` with 32.x, yet
  32.x display INFs (for example `u0403049.inf`) bind `PCI\VEN_1002&DEV_164E`, a Raphael iGPU on 31.x.
  WdiCheck follows the index (current on 31.x, with a note about 32.x). If 32.x is the recommended
  driver for such devices, a per-device flag would make that explicit.

The original report follows.

---

## Bugs

### 1. `by-hwid/{hwid}.json` keeps only the last family written

`publish.py` → `_emit_by_hwid` loops over families and writes `by-hwid/<hwid>.json` once per
(family, HWID) pair. When an HWID belongs to several families, each write overwrites the
previous one, so the file describes whichever family came last.

- 15,836 of 59,138 distinct HWIDs (27%) are in more than one family.
- `by-hwid/PCI_VEN_10EC+DEV_8125.json` (RTL8125, a 2.5GbE NIC) returns **Realtek 8126 LAN**. The
  HWID is also in Realtek Wi-Fi, Realtek 8168 LAN and Realtek 8125 LAN.

This is the endpoint the spec calls the primary query shape, and for shared HWIDs it is
currently wrong. WdiCheck doesn't use it; it downloads `families.json` and disambiguates locally.

**Fix:** aggregate by HWID first and emit one file per HWID with a list:

```json
{ "hwid": "PCI\\VEN_10EC&DEV_8125",
  "families": [ { "family_id": 88, "family": "Realtek 8125 LAN", "water_level": {…}, "known_versions": […] }, … ] }
```

The filename encoding (`\` → `_`, `&` → `+`) is also undocumented; worth a line in the README.

### 2. Family HWID sets are over-broad

- "Realtek Wi-Fi" contains `PCI\VEN_10EC&DEV_8125` (a LAN controller).
- Realtek 8168 / 8125 / 8126 LAN all contain each other's device IDs.
- AMD Graphics contains AMD HD Audio and `SWC\` software-component IDs; NVIDIA Graphics contains
  HD Audio, virtual audio and USB-C IDs.

This looks like every INF in a combined package contributing all its HWIDs to the package's
family. It is the root cause of item 1, and it makes the family a poor unit for per-device checks.

**Fix:** assign HWIDs per INF (each INF's `[Models]` section), and publish that mapping (see item 6).

### 3. Malformed HWIDs from INF parsing

Entries such as `USB\CLASS_01.DEVICEDESC`, `PCI\AMDPCIE.DEVICEDESC`,
`PCI\VEN_8086&DEV_09AB.DEVICEDESC` and `ACPI\PARAMETERS` are string-table keys or section names,
not hardware IDs. **Fix:** skip `%…%` tokens, `.DeviceDesc` keys and non-model sections during extraction.

### 4. Class-code IDs match in-box drivers

207 entries are class-level IDs, for example `PCI\CC_010802` (any NVMe controller) and
`PCI\VEN_8086&DEV_4D28&CC_0401`. A naive client matches Windows' in-box *Standard NVM Express
Controller* to "AMD RAID" through `PCI\CC_010802`.

**Fix:** drop class-only IDs (`PCI\CC_*`, `PCI\VEN_xxxx&CC_*`, `USB\Class_*`), or flag them `"generic": true`.

### 5. 404s return HTML

Missing `by-hwid` / `by-board` files return the 27 KB site 404 page with status 404. A small JSON
body such as `{"error":"not_found"}` would be friendlier to API clients (WdiCheck only checks the status).

---

## Data that would make local checks accurate

### 6. INF-level versions (the most important request)

Windows reports each device's **INF `DriverVer`**, but listings carry **package** versions. A local
checker compares two different things:

| Device on a test PC | Installed (INF) | Family water (package) |
|---|---|---|
| AMD GPIO Controller | 2.2.0.136 | AMD Chipset 7.12.30.211 |
| AMD PSP 11.0 Device | 5.43.0.0 | AMD Chipset 7.12.30.211 |
| AMD SMBus | 5.12.0.44 | AMD Chipset 7.12.30.211 |
| Realtek Audio Universal Service (SWC) | 1.0.1026.0 | Realtek Audio 6.4.0.2443 |
| NVIDIA High Definition Audio | 1.4.6.3 | NVIDIA Graphics 616.92 |

WdiCheck handles these with "version line" heuristics, but that is guesswork: SMBus `5.12.0.44`
lands on the same `5.x` line as unrelated chipset listings, and gets compared against `5.40.0.0`.

The pipeline already has what's needed: the `inf` table holds `driver_ver` per `payload_sha256`.
**Publish it**, for example as `infs.json`:

```json
{ "inf_id": 123, "family_id": 2, "inf_name": "amdgpio2.inf", "driver_ver": "2.2.0.136",
  "driver_date": "2025-10-11", "hwids": ["ACPI\\AMDI0030", …],
  "artefact_ids": [4411, 5120], "first_published": "2025-11-10" }
```

With this, a client matches HWID → INF → newest `driver_ver` for that INF and compares like with
like. That fixes the chipset, audio-component and NVIDIA-audio cases above, as well as items 1 and 2.

### 7. Publish the version-scheme rules as data

`SCHEME_EQUIV` (NVIDIA INF → marketing, Realtek UAD → `6.0.x`) and the "parallel line" logic
behind `same_line_newest` exist only in `publish.py`, so every client has to re-implement them.
Suggested additions to `water-level.json` (or `families.json`):

```json
{ "family_id": 88, "version_scheme": "inf",
  "lines": [ { "major": 10, "newest": "10.080.0525.2026", "newest_date": "2026-09-16" },
             { "major": 1125, "newest": "1125.29.50.202", "newest_date": "2026-02-01", "upstream_only": true } ],
  "equivalence": [ { "pattern": "^3\\d\\.0\\.1(\\d)\\.(\\d{4})$", "replace": "$1$2[0:2].$2[2:]" } ] }
```

Even just `lines` would let clients drop their line heuristics.

Related oddity: for **AMD Chipset** the water level is `7.12.30.211` (2026-02-05), but board 1682
(MSI MPG B650I EDGE WIFI) lists `8.03.25.247` (2026-09-18) with `same_line_newest` `8.08.12.551`
and `lag_days` 229. The water "version" is lower than versions vendors list, presumably because
of the effective-INF mapping. A short note in the schema on how `version` relates to the listed
versions would prevent clients from misreading it.

### 8. SMBIOS identifiers on boards

Board matching currently relies on names. That works for MSI (`MPG B650I EDGE WIFI (MS-7D73)` →
`MPG B650I EDGE WIFI`), Lenovo (`vendor_product_id` is the machine type) and HP (the platform ID
before `/`), but not everywhere. Please add explicit keys to `boards.json`:

```json
"smbios": { "baseboard_product": ["MPG B650I EDGE WIFI (MS-7D73)"], "product_code": "MS-7D73",
            "system_sku": [], "mtm_prefix": ["10SV"], "platform_id": ["81C5"] }
```

Gigabyte revisions (`B550 X GAMING (rev. 1.x)` vs `(rev. 2.x)`) especially need the baseboard
version so clients can pick the right listing.

### 9. Families with no HWIDs

16 families have empty `hwids`: Intel Platform Performance, USB Audio Firmware Tool,
ASUS Bluetooth (unspecified), LHDC Audio, MediaTek Bluetooth, WWAN (module vendors), Card Reader,
Intel ISH, Notebook Bluetooth/WLAN (multi-silicon), GPU VBIOS, Intel WWAN, Notebook Chipset
(Intel/AMD), USB-C Connector Manager, Intel WAPI. These can never be matched locally. Either
populate them from INF extraction, or mark them `"board_only": true` so clients know to surface
them only through `by-board`.

### 10. Consistency and documentation

- `artefacts.json` → `version_normalised` is a JSON-encoded **string** (`"[6, 0, 9235, 1]"`), while
  `water-level.json` uses a real array.
- `artefacts.json` and `changes.json` are published but missing from the homepage and README
  endpoint lists.
- `wucatalog` artefact URLs are Update Catalog *search* links. That's fine, but a
  `"url_kind": "search"` field would let clients label them.

### 11. Client bundle and manifest

A desktop client needs `families.json` + `water-level.json` + `artefacts.json` + `boards.json`
(about 1.4 MB compressed, 15 MB raw) to check one PC. Two optional additions would help:

- `manifest.json` with `generated` and a per-file ETag/size, so a client can check freshness in
  one small request.
- `client-index.json`: HWID → INF/family ids, per-line newest, and scheme rules only, without the
  per-artefact detail.

### 12. Actionable download hints

For upstream-only water levels, the only link is a Windows Update Catalog search. A per-family
`download_hint` (for example "NVIDIA App", "AMD Software: Adrenalin Edition", or the silicon
vendor's landing URL) would give users something actionable, matching what `board.html` already
says for GPUs.

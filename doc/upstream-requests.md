# WinDriverIndex: problems found and suggested changes

Building WdiCheck, a local checker that matches a PC's installed drivers against the
[WinDriverIndex](https://windriverindex.tucny.com/) static API (spec §11, roadmap §7's "check my actual PC"),
turned up the issues below. WdiCheck works around every one of them, and each workaround can be
removed once the site changes. Numbers are from the `2026-09-22T15:22:43+00:00` publish.

Items are ordered by how much they would improve a client's accuracy.

## Status (maintainer reply, schema 2.0.0; not yet deployed as of 2026-09-28)

| # | Request | Status | WdiCheck follow-up once 2.0.0 is live |
|---|---|---|---|
| 1 | by-hwid last-writer-wins | Fixed (breaking; `known_versions` moved to `by-family/{id}.json`) | None needed; WdiCheck doesn't use by-hwid |
| 2 | Over-broad HWID sets | Fixed (chip IDs owned by one family; ~5,200 shared HWIDs remain) | Fewer ambiguous matches; ranking logic stays for real sharing |
| 3 | Junk HWIDs | Fixed | Local filter can stay as a safeguard |
| 4 | Class-code IDs | Fixed (18 truly generic IDs removed) | Local filter can stay |
| 5 | JSON 404s | Needs a Cloudflare rule; also shorten the 404 TTL | WdiCheck already treats any 404 as "not indexed" |
| 6 | INF-level versions | Added `infs.json` (per `inf_name` + DriverVer; HWIDs without SUBSYS/REV) | **Main follow-up:** match by installed `InfPath` → original INF name / HWID and compare `driver_ver_normalised`; keep line heuristics as a fallback |
| 7 | Scheme rules / lines | Added `families.version_equiv`, `water-level.lines` with `parallel_to_water` | Replace hard-coded `SchemeEquivalence` and the successor-line heuristic |
| 8 | SMBIOS keys | Added for Dell, HP, Lenovo, MSI | Match by `smbios` keys first; names only for Gigabyte/ASUS/ASRock |
| 9 | Families without HWIDs | Empty `hwids` is the board-only signal | Show those only on the board page (already the case) |
| 10 | Consistency | Fixed | Nothing to change (array field isn't bound) |
| 11 | Manifest / bundle | `manifest.json` added; bundle declined | Revalidate the manifest, re-download files whose sha256 changed |
| 12 | Download hints | Added for NVIDIA, AMD, Intel families | Show the hint; otherwise the board's `support_url` |

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

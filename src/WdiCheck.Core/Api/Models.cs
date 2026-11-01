namespace WdiCheck.Core.Api;

// Wire models for the WinDriverIndex static JSON API. Written against schema 2.0.0; every
// field added in 2.0.0 is optional so a 1.x mirror still loads.
// Property names map to snake_case via WdiJsonContext.

/// <summary>Envelope every aggregate file is wrapped in.</summary>
public sealed class Envelope<T>
{
    public string SchemaVersion { get; init; } = "";
    public string? License { get; init; }
    public string? Generated { get; init; }
    public string? Caveat { get; init; }
    public T Data { get; init; } = default!;
}

public sealed class Family
{
    public int FamilyId { get; init; }
    public string Name { get; init; } = "";
    public string? SiliconVendor { get; init; }
    public string? Component { get; init; }
    public List<string> Hwids { get; init; } = [];

    /// <summary>2.0.0: rule translating listing/INF versions to the family's canonical scheme.</summary>
    public VersionEquivRule? VersionEquiv { get; init; }

    /// <summary>2.0.0: generic driver source for end users, when one exists.</summary>
    public DownloadHint? DownloadHint { get; init; }
}

/// <summary><c>replace</c> uses $1-style group references (valid for .NET Match.Result).</summary>
public sealed class VersionEquivRule
{
    public string Pattern { get; init; } = "";
    public string Replace { get; init; } = "";
    public string? Flags { get; init; }
    public string? Note { get; init; }
}

public sealed class DownloadHint
{
    public string Label { get; init; } = "";
    public string? Url { get; init; }
}

public sealed class WaterLevel
{
    public int FamilyId { get; init; }
    public string Family { get; init; } = "";
    public string Version { get; init; } = "";
    public List<long>? VersionNormalised { get; init; }
    public string? FirstPublished { get; init; }
    public List<string>? PublishedBy { get; init; }
    public bool UpstreamOnly { get; init; }
    public string? BestVendorVersion { get; init; }

    /// <summary>2.0.0: per major-version line, with the site's parallel-line verdict.</summary>
    public List<VersionLineInfo>? Lines { get; init; }
}

public sealed class VersionLineInfo
{
    public long Major { get; init; }
    public string? Newest { get; init; }
    public List<long>? NewestNormalised { get; init; }
    public string? NewestDate { get; init; }
    public string? FirstDate { get; init; }
    public string? LastDate { get; init; }
    public bool WaterLine { get; init; }

    /// <summary>
    /// True when the line was maintained during the same period as the water level's line
    /// (a genuine parallel branch); false for an older version of the same scheme.
    /// </summary>
    public bool ParallelToWater { get; init; }
}

/// <summary>2.0.0 infs.json: one row per (INF file name, DriverVer).</summary>
public sealed class InfRow
{
    /// <summary>Null for ~250 MSI INFs whose file name was lost on extraction; match those by HWID.</summary>
    public string? InfName { get; init; }
    public string DriverVer { get; init; } = "";

    /// <summary>2.0.x: other DriverVer spellings of the same build; match installed versions against these too.</summary>
    public List<string>? DriverVerAliases { get; init; }
    public List<long>? DriverVerNormalised { get; init; }
    public string? DriverDate { get; init; }
    public string? Class { get; init; }

    /// <summary>Every family whose packages carry the INF (not the ownership set).</summary>
    public List<int> FamilyIds { get; init; } = [];
    public List<int> ArtefactIds { get; init; } = [];

    /// <summary>HWIDs with SUBSYS/REV removed; devices always report that shorter form too.</summary>
    public List<string> Hwids { get; init; } = [];
    public string? FirstPublished { get; init; }

    /// <summary>2.0.x: "silicon" for INFs extracted from the chip vendor's own installer (those rows have no artefacts).</summary>
    public List<string>? Sources { get; init; }
}

/// <summary>2.0.0 manifest.json: size and hash of every aggregate file.</summary>
public sealed class Manifest
{
    public Dictionary<string, ManifestFile> Files { get; init; } = [];
    public Dictionary<string, int>? Directories { get; init; }
}

public sealed class ManifestFile
{
    public long Bytes { get; init; }
    public string? Sha256 { get; init; }
}

public sealed class Artefact
{
    public int ArtefactId { get; init; }
    public string Vendor { get; init; } = "";
    public string? Kind { get; init; }
    public int? FamilyId { get; init; }
    public string? VersionRaw { get; init; }
    public string? ReleaseDate { get; init; }
    public long? FileSize { get; init; }
    public string? Url { get; init; }
    public int IsBeta { get; init; }
}

public sealed class Board
{
    public int BoardId { get; init; }
    public string Vendor { get; init; } = "";
    public string? VendorProductId { get; init; }
    public string Name { get; init; } = "";
    public string? Slug { get; init; }
    public string? Revision { get; init; }
    public string? Chipset { get; init; }
    public string? Socket { get; init; }
    public string? ProductType { get; init; }
    public string? ReleaseDate { get; init; }
    public string? SupportUrl { get; init; }

    /// <summary>2.0.0: SMBIOS match keys (Dell, HP, Lenovo, MSI); null where the vendor publishes none.</summary>
    public BoardSmbios? Smbios { get; init; }
}

public sealed class BoardSmbios
{
    /// <summary>Dell: SMBIOS SKU Number, exact.</summary>
    public List<string>? SystemSku { get; init; }

    /// <summary>HP: baseboard product (platform ID), exact.</summary>
    public List<string>? BaseboardProduct { get; init; }

    /// <summary>Lenovo: system product name (MTM) starts with one of these.</summary>
    public List<string>? SystemProductPrefix { get; init; }

    /// <summary>MSI: baseboard product contains one of these (MS-xxxx). Codes can be shared; check the name too.</summary>
    public List<string>? BaseboardProductContains { get; init; }
}

/// <summary>/v1/latest/by-board/{id}.json — not wrapped in a data envelope.</summary>
public sealed class BoardReport
{
    public string SchemaVersion { get; init; } = "";
    public string? Generated { get; init; }
    public Board Board { get; init; } = new();
    public BoardBios? Bios { get; init; }
    public GpuReference? GpuReference { get; init; }
    public List<BoardFamilyRow> Families { get; init; } = [];
}

public sealed class BoardBios
{
    public string? LastBios { get; init; }

    /// <summary>
    /// The vendor's newest BIOS version as listed, e.g. "M3CN50WW" (addressed upstream; data update in flight).
    /// Formats can differ from SMBIOS (MSI "7D73v1L2" vs "1.L2"); see BoardAssessment.CompareBiosVersions.
    /// </summary>
    public string? LastBiosVersion { get; init; }

    /// <summary>Publish date of <see cref="LastBiosVersion"/>.</summary>
    public string? LastBiosVersionDate { get; init; }
    public string? Agesa { get; init; }
    public string? AgesaLine { get; init; }
    public bool? AgesaAtWater { get; init; }
}

public sealed class GpuReference
{
    public string? Family { get; init; }
    public string? WaterVersion { get; init; }
    public string? WaterFirstPublished { get; init; }
}

public sealed class BoardFamilyRow
{
    public string Family { get; init; } = "";
    public string? Component { get; init; }
    public string? ListedVersion { get; init; }
    public string? ListedEquiv { get; init; }
    public string? ListedDate { get; init; }
    public string? WaterVersion { get; init; }
    public string? WaterFirstPublished { get; init; }
    public string? SameLineNewest { get; init; }
    public string? SameLineDate { get; init; }
    public bool UpstreamOnly { get; init; }
    public int? LagDays { get; init; }
}

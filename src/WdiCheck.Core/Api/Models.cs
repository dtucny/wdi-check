namespace WdiCheck.Core.Api;

// Wire models for the WinDriverIndex static JSON API (schema 1.0.0).
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

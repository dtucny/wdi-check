using WdiCheck.Core.Api;
using WdiCheck.Core.Matching;

namespace WdiCheck.Core.Reporting;

/// <param name="Version">The listing's own (package) version.</param>
/// <param name="Contains">For INF-level targets: "rt640x64.inf 10.080.0407.2026", the driver inside the package.</param>
public sealed record DownloadLink(string Vendor, string Version, string? ReleaseDate, Uri Url, bool IsBoardVendor, bool IsUpstream,
    string? Contains = null);

public static class DownloadLinks
{
    /// <summary>Upstream sources (not a hardware vendor's support page).</summary>
    private static readonly HashSet<string> UpstreamSources = new(StringComparer.OrdinalIgnoreCase) { "wucatalog", "silicon" };

    /// <summary>Only https links from the index are ever opened.</summary>
    public static bool IsSafe(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps;

    public static bool IsUpstream(string vendor) => UpstreamSources.Contains(vendor);

    /// <summary>Links for a version: this PC's board vendor first, then upstream, then other vendors; newest listing first.</summary>
    public static IReadOnlyList<DownloadLink> For(VersionRef? version, string? boardVendor)
    {
        if (version is null)
            return [];
        return version.Artefacts
            .Where(a => IsSafe(a.Url))
            .OrderByDescending(a => boardVendor is not null && a.Vendor.Equals(boardVendor, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(a => IsUpstream(a.Vendor))
            .ThenByDescending(a => a.ReleaseDate)
            .DistinctBy(a => a.Url)
            .Select(a => new DownloadLink(a.Vendor, a.VersionRaw ?? version.Raw, a.ReleaseDate, new Uri(a.Url!),
                boardVendor is not null && a.Vendor.Equals(boardVendor, StringComparison.OrdinalIgnoreCase), IsUpstream(a.Vendor),
                version.InfName is not null && !string.Equals(a.VersionRaw, version.Raw, StringComparison.OrdinalIgnoreCase)
                    ? $"{version.InfName} {version.Raw}" : null))
            .ToList();
    }

    /// <summary>The family's generic driver source (schema 2.0.0 download_hint), when it has a safe URL.</summary>
    public static DownloadHint? Hint(DeviceResult r) =>
        r.Family?.Family.DownloadHint is { } h && IsSafe(h.Url) ? h : null;
}

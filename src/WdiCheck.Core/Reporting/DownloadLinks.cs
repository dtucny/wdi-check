using WdiCheck.Core.Api;
using WdiCheck.Core.Index;

namespace WdiCheck.Core.Reporting;

public sealed record DownloadLink(string Vendor, string Version, string? ReleaseDate, Uri Url, bool IsBoardVendor, bool IsUpstream);

public static class DownloadLinks
{
    /// <summary>Upstream sources (not a hardware vendor's support page).</summary>
    private static readonly HashSet<string> UpstreamSources = new(StringComparer.OrdinalIgnoreCase) { "wucatalog", "silicon" };

    /// <summary>Only https links from the index are ever opened.</summary>
    public static bool IsSafe(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps;

    public static bool IsUpstream(string vendor) => UpstreamSources.Contains(vendor);

    /// <summary>Links for a version: this PC's board vendor first, then upstream, then other vendors; newest listing first.</summary>
    public static IReadOnlyList<DownloadLink> For(KnownVersion? version, string? boardVendor)
    {
        if (version is null)
            return [];
        return version.Artefacts
            .Where(a => IsSafe(a.Url))
            .OrderByDescending(a => boardVendor is not null && a.Vendor.Equals(boardVendor, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(a => IsUpstream(a.Vendor))
            .ThenByDescending(a => a.ReleaseDate)
            .DistinctBy(a => a.Url)
            .Select(a => new DownloadLink(a.Vendor, a.VersionRaw ?? version.Version.Raw, a.ReleaseDate, new Uri(a.Url!),
                boardVendor is not null && a.Vendor.Equals(boardVendor, StringComparison.OrdinalIgnoreCase), IsUpstream(a.Vendor)))
            .ToList();
    }
}

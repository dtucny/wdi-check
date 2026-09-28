using System.Text;
using System.Text.RegularExpressions;
using WdiCheck.Core.Api;
using WdiCheck.Core.Inventory;

namespace WdiCheck.Core.Matching;

public sealed record BoardCandidate(Board Board, double Score, string Reason);

/// <summary>
/// Identifies this machine in boards.json from SMBIOS. Schema 2.0.0 publishes match keys for
/// Dell (SKU), HP (platform ID), Lenovo (MTM prefix) and MSI (MS-xxxx code); those are tried first.
/// Name heuristics cover Gigabyte, ASUS and ASRock (whose SMBIOS product is roughly the model name)
/// and 1.x data: DIY boards by normalised baseboard product, Lenovo by machine type, HP by platform
/// ID, Dell by system product name.
/// </summary>
public static partial class BoardMatcher
{
    /// <summary>A top candidate at or above this score is selected automatically.</summary>
    public const double AutoSelectScore = 0.9;

    public static string? MapVendor(string? manufacturer)
    {
        if (string.IsNullOrWhiteSpace(manufacturer))
            return null;
        var m = manufacturer.ToUpperInvariant();
        if (m.Contains("MICRO-STAR") || m.StartsWith("MSI")) return "msi";
        if (m.Contains("GIGABYTE")) return "gigabyte";
        if (m.Contains("ASUSTEK") || m.StartsWith("ASUS")) return "asus";
        if (m.Contains("ASROCK")) return "asrock";
        if (m.Contains("LENOVO")) return "lenovo";
        if (m.Contains("DELL")) return "dell";
        if (m.Contains("HEWLETT") || m == "HP" || m.StartsWith("HP ") || m.Contains("HP INC")) return "hp";
        return null;
    }

    public static IReadOnlyList<BoardCandidate> Rank(SystemIdentity id, IEnumerable<Board> boards, int max = 10)
    {
        var systemVendor = MapVendor(id.SystemManufacturer);
        var boardVendor = MapVendor(id.BaseBoardManufacturer);
        var machines = boards.Where(b => !string.Equals(b.ProductType, "graphics-card", StringComparison.OrdinalIgnoreCase)).ToList();

        var results = new List<BoardCandidate>(MatchSmbiosKeys(id, machines));
        // OEM systems first: their identity is the system, not the OEM-specific baseboard.
        switch (systemVendor)
        {
            case "lenovo":
                results.AddRange(MatchLenovo(id, machines));
                break;
            case "hp":
                results.AddRange(MatchHp(id, machines));
                break;
            case "dell":
                results.AddRange(MatchByName(machines.Where(b => b.Vendor == "dell"), id.SystemProductName, "system product name"));
                break;
        }

        if (boardVendor is "msi" or "gigabyte" or "asus" or "asrock" && !string.IsNullOrEmpty(id.BaseBoardProduct))
        {
            var sameVendor = machines.Where(b => b.Vendor == boardVendor);
            // Laptops/desktops from these vendors identify by system product name; DIY boards by baseboard product.
            results.AddRange(MatchByName(sameVendor, id.BaseBoardProduct, "baseboard product", id.BaseBoardVersion));
            if (!SystemIdentity.IsPlaceholder(id.SystemProductName) && id.SystemProductName != id.BaseBoardProduct)
                results.AddRange(MatchByName(sameVendor, id.SystemProductName, "system product name"));
        }

        return results
            .GroupBy(r => r.Board.BoardId)
            .Select(g => g.MaxBy(r => r.Score)!)
            .Where(r => r.Score >= 0.5)
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Board.Name.Length)
            .Take(max)
            .ToList();
    }

    /// <summary>The candidate to select without asking, if the evidence is unambiguous.</summary>
    public static BoardCandidate? AutoSelect(IReadOnlyList<BoardCandidate> ranked) =>
        ranked.Count > 0 && ranked[0].Score >= AutoSelectScore && (ranked.Count == 1 || ranked[1].Score < ranked[0].Score)
            ? ranked[0]
            : null;

    private static IEnumerable<BoardCandidate> MatchSmbiosKeys(SystemIdentity id, List<Board> machines)
    {
        foreach (var b in machines)
        {
            if (b.Smbios is not { } keys)
                continue;
            if (Any(keys.SystemSku, k => Eq(id.SystemSku, k)))
                yield return new BoardCandidate(b, 1.0, $"SMBIOS SKU {id.SystemSku}");
            else if (Any(keys.BaseboardProduct, k => Eq(id.BaseBoardProduct, k)))
                yield return new BoardCandidate(b, 1.0, $"SMBIOS baseboard {id.BaseBoardProduct}");
            else if (Any(keys.SystemProductPrefix, k => id.SystemProductName?.Trim().StartsWith(k, StringComparison.OrdinalIgnoreCase) == true))
                yield return new BoardCandidate(b, 1.0, $"SMBIOS machine type {id.SystemProductName}");
            else if (Any(keys.BaseboardProductContains, k => id.BaseBoardProduct?.Contains(k, StringComparison.OrdinalIgnoreCase) == true))
            {
                // MSI reuses codes across siblings (MEG X570 ACE and UNIFY are both MS-7C35): let the name decide between them.
                var name = MatchByName([b], id.BaseBoardProduct, "baseboard product").FirstOrDefault()?.Score ?? 0;
                yield return new BoardCandidate(b, 0.9 + 0.1 * name,
                    $"SMBIOS board code {keys.BaseboardProductContains!.First(k => id.BaseBoardProduct!.Contains(k, StringComparison.OrdinalIgnoreCase))}" +
                    (name >= 0.98 ? " and name" : ""));
            }
        }

        static bool Any(List<string>? keys, Func<string, bool> test) => keys is { Count: > 0 } && keys.Any(k => !string.IsNullOrWhiteSpace(k) && test(k.Trim()));
        static bool Eq(string? a, string b) => a is not null && a.Trim().Equals(b, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<BoardCandidate> MatchLenovo(SystemIdentity id, List<Board> machines)
    {
        // Lenovo's vendor_product_id is the 4-character machine type, SystemProductName the full MTM (e.g. 10SV0012US).
        var mtm = id.SystemProductName?.Trim();
        if (mtm is { Length: >= 4 })
        {
            var mt = mtm[..4];
            foreach (var b in machines.Where(b => b.Vendor == "lenovo" && string.Equals(b.VendorProductId, mt, StringComparison.OrdinalIgnoreCase)))
                yield return new BoardCandidate(b, 1.0, $"Lenovo machine type {mt.ToUpperInvariant()}");
        }

        // SystemVersion carries the friendly name (e.g. "ThinkPad X1 Carbon Gen 11").
        foreach (var c in MatchByName(machines.Where(b => b.Vendor == "lenovo"), id.SystemVersion, "system version"))
            yield return c with { Score = c.Score * 0.95 };
    }

    private static IEnumerable<BoardCandidate> MatchHp(SystemIdentity id, List<Board> machines)
    {
        // HP's vendor_product_id is "<platform id>/<slug>"; the platform ID is the baseboard product (e.g. 81C5).
        var platform = id.BaseBoardProduct?.Trim();
        if (!string.IsNullOrEmpty(platform))
        {
            foreach (var b in machines.Where(b => b.Vendor == "hp" && b.VendorProductId is not null))
            {
                var pid = b.VendorProductId!.Split('/')[0];
                if (pid.Equals(platform, StringComparison.OrdinalIgnoreCase))
                    yield return new BoardCandidate(b, 1.0, $"HP platform ID {platform.ToUpperInvariant()}");
            }
        }

        foreach (var c in MatchByName(machines.Where(b => b.Vendor == "hp"), id.SystemProductName, "system product name"))
            yield return c with { Score = c.Score * 0.95 };
    }

    private static IEnumerable<BoardCandidate> MatchByName(IEnumerable<Board> boards, string? product, string field, string? boardVersion = null)
    {
        if (SystemIdentity.IsPlaceholder(product))
            yield break;
        var target = Normalise(product!);
        if (target.Length == 0)
            yield break;
        var targetCompact = target.Replace(" ", "");
        var targetTokens = target.Split(' ');

        foreach (var b in boards)
        {
            var name = Normalise(b.Name);
            if (name.Length == 0)
                continue;
            double score;
            string how;
            if (name == target)
            {
                score = 1.0;
                how = "exact";
            }
            else if (name.Replace(" ", "") == targetCompact)
            {
                score = 0.98;
                how = "exact ignoring punctuation";
            }
            else if (EndsWithTokens(name, target) || EndsWithTokens(target, name))
            {
                // Dell/HP names often repeat the series: "Precision Precision 5470" vs "Precision 5470".
                score = 0.93;
                how = "name contains model";
            }
            else
            {
                var nameTokens = name.Split(' ');
                var common = nameTokens.Intersect(targetTokens).Count();
                score = 2.0 * common / (nameTokens.Length + targetTokens.Length) * 0.85;
                how = "similar name";
            }

            // Prefer the listed revision that matches the baseboard version (Gigabyte "(rev. 1.x)").
            if (score >= 0.9 && RevisionMatches(b.Name, boardVersion))
                score = Math.Min(1.0, score + 0.01);
            else if (score >= 0.9 && RevisionOf(b.Name) is not null)
                score -= 0.01;

            if (score >= 0.5)
                yield return new BoardCandidate(b, score, $"{field} \"{product}\" ({how})");
        }
    }

    private static bool EndsWithTokens(string longer, string shorter) =>
        longer.Length > shorter.Length && longer.EndsWith(" " + shorter, StringComparison.Ordinal);

    private static string? RevisionOf(string name)
    {
        var m = RevisionRegex().Match(name);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static bool RevisionMatches(string boardName, string? boardVersion)
    {
        var rev = RevisionOf(boardName);
        if (rev is null || string.IsNullOrWhiteSpace(boardVersion))
            return false;
        // "(rev. 1.x)" matches baseboard versions 1.0, 1.1, 1.x …
        var major = rev.Split('.')[0];
        return boardVersion.Trim().StartsWith(major + ".", StringComparison.OrdinalIgnoreCase) || boardVersion.Trim() == major;
    }

    /// <summary>Uppercase alphanumeric tokens; parentheticals like "(MS-7D73)" and "(rev. 1.x)" removed.</summary>
    internal static string Normalise(string s)
    {
        s = ParenRegex().Replace(s, " ");
        s = s.Replace("™", "").Replace("®", "");
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.ToUpperInvariant())
            sb.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
        return SpaceRegex().Replace(sb.ToString(), " ").Trim();
    }

    [GeneratedRegex(@"\([^)]*\)")]
    private static partial Regex ParenRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaceRegex();

    [GeneratedRegex(@"\(rev\.?\s*([0-9x.]+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex RevisionRegex();
}

using WdiCheck.Core.Api;
using WdiCheck.Core.Inventory;
using WdiCheck.Core.Versions;

namespace WdiCheck.Core.Index;

/// <summary>One infs.json row: a specific INF file at a specific DriverVer.</summary>
public sealed class InfRelease
{
    internal InfRelease(InfRow row, IReadOnlyList<Artefact> artefacts)
    {
        Row = row;
        Version = DriverVersion.FromParts(row.DriverVerNormalised, row.DriverVer);
        DriverDate = FamilyInfo.ParseDate(row.DriverDate);
        FirstPublished = FamilyInfo.ParseDate(row.FirstPublished);
        Artefacts = artefacts;
        Hwids = new HashSet<string>(row.Hwids.Select(Hwid.Normalise), StringComparer.Ordinal);
        Aliases = (row.DriverVerAliases ?? []).Select(DriverVersion.Parse).Where(v => v.IsParsed).ToList();
    }

    public InfRow Row { get; }
    public string? InfName => Row.InfName;
    public DriverVersion Version { get; }

    /// <summary>Other DriverVer spellings of the same build (Realtek stamps 10.080.0407.2026 and 10.080.50.0407).</summary>
    public IReadOnlyList<DriverVersion> Aliases { get; }

    /// <summary>True when <paramref name="v"/> is this build under its canonical DriverVer or any alias.</summary>
    public bool IsBuild(DriverVersion v) => Version.Equals(v) || Aliases.Any(a => a.Equals(v));

    /// <summary>The INF's own DriverVer date — comparable with the date Windows reports for the installed driver.</summary>
    public DateOnly? DriverDate { get; }

    public DateOnly? FirstPublished { get; }
    public IReadOnlyList<Artefact> Artefacts { get; }
    public IReadOnlySet<string> Hwids { get; }
}

/// <summary>How an installed driver was matched to infs.json.</summary>
public sealed record InfMatch(string? InfName, bool ByName, IReadOnlyList<InfRelease> Releases, InfRelease Newest, InfRelease? Installed);

/// <summary>
/// INF-level index (schema 2.0.0 infs.json). Windows reports each device's INF DriverVer, so
/// comparing it with other releases of the same INF is like-for-like — unlike package versions,
/// which is all the 1.x index offered.
/// </summary>
public sealed class InfIndex
{
    private readonly Dictionary<string, List<InfRelease>> _byName;
    private readonly Dictionary<string, List<InfRelease>> _byHwid;

    private InfIndex(Dictionary<string, List<InfRelease>> byName, Dictionary<string, List<InfRelease>> byHwid, int count)
    {
        _byName = byName;
        _byHwid = byHwid;
        Count = count;
    }

    public int Count { get; }

    public static InfIndex Build(IEnumerable<InfRow> rows, IEnumerable<Artefact> artefacts)
    {
        var artefactById = artefacts.GroupBy(a => a.ArtefactId).ToDictionary(g => g.Key, g => g.First());
        var byName = new Dictionary<string, List<InfRelease>>(StringComparer.OrdinalIgnoreCase);
        var byHwid = new Dictionary<string, List<InfRelease>>(StringComparer.Ordinal);
        var count = 0;
        foreach (var row in rows)
        {
            var release = new InfRelease(row,
                row.ArtefactIds.Select(id => artefactById.GetValueOrDefault(id)).OfType<Artefact>().ToList());
            if (!release.Version.IsParsed)
                continue;
            count++;
            if (!string.IsNullOrEmpty(row.InfName))
                Add(byName, row.InfName, release);
            foreach (var h in release.Hwids)
            {
                if (!Hwid.IsGeneric(h) && !Hwid.IsMalformed(h))
                    Add(byHwid, h, release);
            }
        }

        return new InfIndex(byName, byHwid, count);
    }

    public IReadOnlyList<InfRelease> ByName(string infName) => _byName.GetValueOrDefault(infName) ?? [];

    public IReadOnlyList<InfRelease> ByHwid(string hwid) => _byHwid.GetValueOrDefault(Hwid.Normalise(hwid)) ?? [];

    /// <summary>
    /// The releases this device's driver should be compared with:
    /// every release of its original INF (restricted to those binding one of its IDs, in the installed version's
    /// numbering scheme), plus releases
    /// of other INFs that bind its most specific indexed ID, are on the same version line (or, within the installed
    /// INF's series, the next line) and the same
    /// device class (vendors that rename the INF per release, e.g. AMD display drivers).
    /// </summary>
    public InfMatch? Match(LocalDevice device)
    {
        var installed = DriverVersion.Parse(device.DriverVersion);
        if (installed.Line is not { } line)
            return null;

        var ids = Hwid.DeviceIds(device);
        var idSet = new HashSet<string>(ids, StringComparer.Ordinal);
        var pool = new List<InfRelease>();
        var name = device.OriginalInfName;

        var byName = false;
        if (!string.IsNullOrEmpty(name))
        {
            // Same INF name under another numbering scheme (MediaTek's vendor 3.x vs Windows Update's
            // year-based 25.x for mtkwl6ex.inf) can't be ordered against the installed version; leave it out.
            var named = ByName(name)
                .Where(r => (r.Hwids.Count == 0 || r.Hwids.Overlaps(idSet)) && r.Version.Line is { } l && l.IsSameScheme(line))
                .ToList();
            pool.AddRange(named);
            byName = named.Count > 0;
        }

        foreach (var id in ids)
        {
            var hits = ByHwid(id);
            if (hits.Count == 0)
                continue;
            // With the installed INF's series known (AMD renames display INFs every release: u0401611.inf,
            // u0199286.inf → "u.inf"), the directly following line counts too — that is how a 31.x install learns
            // which 32.x build its device is listed in. Without it, only the same line is safe.
            var series = Series(name);
            pool.AddRange(hits.Where(r =>
                r.Version.Line is { } l
                && (l == line || (series is not null && series == Series(r.InfName) && line.IsFollowedBy(l)))
                && ClassMatches(r.Row.Class, device.Class)
                && !(byName && string.Equals(r.InfName, name, StringComparison.OrdinalIgnoreCase))));
            break;
        }

        if (pool.Count == 0)
            return null;

        var releases = pool.Distinct().OrderByDescending(r => r.Version).ThenByDescending(r => r.DriverDate).ToList();
        var newest = releases[0];
        var installedRelease = releases.FirstOrDefault(r => r.IsBuild(installed));
        return new InfMatch(byName ? name : newest.InfName, byName, releases, newest, installedRelease);
    }

    /// <summary>An INF name's series: lower-case, digits removed (u0401611.inf → u.inf); null when unknown.</summary>
    internal static string? Series(string? infName) =>
        string.IsNullOrEmpty(infName) ? null : new string(infName.ToLowerInvariant().Where(c => !char.IsAsciiDigit(c)).ToArray());

    /// <summary>True when some INF binding one of the device's IDs has a release on <paramref name="line"/>.</summary>
    public bool HasLineFor(LocalDevice device, VersionLine line) =>
        Hwid.DeviceIds(device).Any(id => ByHwid(id).Any(r => r.Version.Line == line));

    private static bool ClassMatches(string? a, string? b) =>
        string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b) || a.Equals(b, StringComparison.OrdinalIgnoreCase);

    private static void Add(Dictionary<string, List<InfRelease>> map, string key, InfRelease r)
    {
        if (!map.TryGetValue(key, out var list))
            map[key] = list = [];
        list.Add(r);
    }
}

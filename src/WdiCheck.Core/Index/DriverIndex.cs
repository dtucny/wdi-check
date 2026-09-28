using System.Globalization;
using WdiCheck.Core.Api;
using WdiCheck.Core.Versions;

namespace WdiCheck.Core.Index;

/// <summary>One distinct version of a family, with every listing that published it.</summary>
public sealed class KnownVersion
{
    internal KnownVersion(DriverVersion version) => Version = version;

    public DriverVersion Version { get; }

    /// <summary>Earliest release date any source listed this version under.</summary>
    public DateOnly? FirstReleased { get; internal set; }

    /// <summary>True only when every listing of this version is flagged beta.</summary>
    public bool IsBeta { get; internal set; } = true;

    public List<Artefact> Artefacts { get; } = [];

    public IEnumerable<string> Vendors => Artefacts.Select(a => a.Vendor).Distinct(StringComparer.OrdinalIgnoreCase);
}

public sealed class FamilyInfo
{
    private readonly Dictionary<DriverVersion, KnownVersion> _byVersion;
    private readonly Dictionary<VersionLine, KnownVersion> _newestPerLine;

    internal FamilyInfo(Family family, WaterLevel? water, Dictionary<DriverVersion, KnownVersion> versions)
    {
        Family = family;
        Water = water;
        WaterVersion = water is null ? null : SchemeEquivalence.Effective(family.Name, water.Version);
        WaterDate = ParseDate(water?.FirstPublished);
        _byVersion = versions;
        Versions = versions.Values.OrderByDescending(v => v.Version).ThenByDescending(v => v.FirstReleased).ToList();
        _newestPerLine = new();
        foreach (var kv in Versions)
        {
            if (kv.IsBeta || kv.Version.Line is not { } line)
                continue;
            if (!_newestPerLine.TryGetValue(line, out var cur) || kv.Version > cur.Version)
                _newestPerLine[line] = kv;
        }
    }

    public Family Family { get; }

    public int Id => Family.FamilyId;

    public string Name => Family.Name;

    public string Component => string.IsNullOrEmpty(Family.Component) ? "other" : Family.Component!;

    /// <summary>Null for "(preinstall)" families and families with no parseable listings.</summary>
    public WaterLevel? Water { get; }

    public DriverVersion? WaterVersion { get; }

    public DateOnly? WaterDate { get; }

    public bool IsPreinstall => Name.Contains("(preinstall)", StringComparison.OrdinalIgnoreCase);

    /// <summary>All distinct known versions, newest first.</summary>
    public IReadOnlyList<KnownVersion> Versions { get; }

    public IReadOnlyCollection<VersionLine> Lines => _newestPerLine.Keys;

    public KnownVersion? Find(DriverVersion version) =>
        version.IsParsed && _byVersion.TryGetValue(version, out var kv) ? kv : null;

    public bool HasLine(VersionLine line) => _newestPerLine.ContainsKey(line);

    public KnownVersion? NewestOnLine(VersionLine line) => _newestPerLine.GetValueOrDefault(line);

    internal static DateOnly? ParseDate(string? s) =>
        DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}

/// <summary>
/// Local, queryable form of the index: HWID → candidate families, and per family the
/// water level plus every known version grouped by numbering line.
/// Built from families.json + water-level.json + artefacts.json.
/// </summary>
public sealed class DriverIndex
{
    private readonly Dictionary<string, List<FamilyInfo>> _byHwid;

    private DriverIndex(Dictionary<int, FamilyInfo> families, Dictionary<string, List<FamilyInfo>> byHwid, string? generated)
    {
        Families = families;
        _byHwid = byHwid;
        Generated = generated;
    }

    public IReadOnlyDictionary<int, FamilyInfo> Families { get; }

    public string? Generated { get; }

    public int HwidCount => _byHwid.Count;

    public static DriverIndex Build(IEnumerable<Family> families, IEnumerable<WaterLevel> water, IEnumerable<Artefact> artefacts, string? generated = null)
    {
        var familyList = families.ToList();
        var waterById = water.GroupBy(w => w.FamilyId).ToDictionary(g => g.Key, g => g.First());
        var nameById = familyList.ToDictionary(f => f.FamilyId, f => f.Name);

        var versionsById = new Dictionary<int, Dictionary<DriverVersion, KnownVersion>>();
        foreach (var a in artefacts)
        {
            if (a.FamilyId is not { } fid || !nameById.TryGetValue(fid, out var fname))
                continue;
            if (a.Kind is not null && !a.Kind.Equals("driver", StringComparison.OrdinalIgnoreCase))
                continue;
            var v = SchemeEquivalence.Effective(fname, a.VersionRaw);
            if (!v.IsParsed)
                continue;

            if (!versionsById.TryGetValue(fid, out var map))
                versionsById[fid] = map = new();
            if (!map.TryGetValue(v, out var kv))
                map[v] = kv = new KnownVersion(v);

            kv.Artefacts.Add(a);
            if (a.IsBeta == 0)
                kv.IsBeta = false;
            var date = FamilyInfo.ParseDate(a.ReleaseDate);
            if (date is not null && (kv.FirstReleased is null || date < kv.FirstReleased))
                kv.FirstReleased = date;
        }

        var infos = new Dictionary<int, FamilyInfo>();
        var byHwid = new Dictionary<string, List<FamilyInfo>>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in familyList)
        {
            var info = new FamilyInfo(f, waterById.GetValueOrDefault(f.FamilyId), versionsById.GetValueOrDefault(f.FamilyId) ?? new());
            infos[f.FamilyId] = info;
            foreach (var raw in f.Hwids)
            {
                var hwid = Hwid.Normalise(raw);
                if (hwid.Length == 0 || Hwid.IsGeneric(hwid) || Hwid.IsMalformed(hwid))
                    continue;
                if (!byHwid.TryGetValue(hwid, out var list))
                    byHwid[hwid] = list = [];
                if (!list.Contains(info))
                    list.Add(info);
            }
        }

        return new DriverIndex(infos, byHwid, generated);
    }

    public IReadOnlyList<FamilyInfo> Lookup(string hwid) =>
        _byHwid.TryGetValue(Hwid.Normalise(hwid), out var list) ? list : [];

    public FamilyInfo? ByName(string name) =>
        Families.Values.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}

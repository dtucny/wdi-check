using WdiCheck.Core.Api;
using WdiCheck.Core.Index;
using WdiCheck.Core.Inventory;
using WdiCheck.Core.Versions;

namespace WdiCheck.Core.Matching;

public enum DriverStatus
{
    /// <summary>At (or above) the newest comparable release.</summary>
    Current,
    /// <summary>A newer comparable release exists.</summary>
    Behind,
    /// <summary>Matched a family, but the installed version can't be compared with anything the index lists.</summary>
    NotComparable,
    /// <summary>Windows in-box driver on hardware a vendor family covers.</summary>
    Inbox,
    /// <summary>No HWID of this device appears in the index.</summary>
    NotIndexed,
}

public enum MatchConfidence
{
    None,
    /// <summary>Installed version shares a numbering line with indexed package versions (heuristic).</summary>
    SameLine,
    /// <summary>Installed version is exactly a package version some vendor/upstream listed.</summary>
    ExactVersion,
    /// <summary>The installed INF is in infs.json, though not at this exact DriverVer.</summary>
    Inf,
    /// <summary>The installed INF and its exact DriverVer are in infs.json.</summary>
    InfExact,
}

/// <summary>A version to compare with or update to, with the listings that published it.</summary>
public sealed record VersionRef(DriverVersion Version, DateOnly? Date, IReadOnlyList<Artefact> Artefacts)
{
    public string Raw => Version.Raw;

    /// <summary>Set for INF-level versions: the INF inside the listed packages that carries this version.</summary>
    public string? InfName { get; init; }

    internal static VersionRef From(KnownVersion kv) => new(kv.Version, kv.FirstReleased, kv.Artefacts);

    internal static VersionRef From(InfRelease r) => new(r.Version, r.DriverDate, r.Artefacts);
}

public sealed record WaterLevelNote(string Version, DateOnly? FirstPublished, VersionLine? Line);

public sealed class DeviceResult
{
    public required LocalDevice Device { get; init; }
    public DriverStatus Status { get; init; }
    public MatchConfidence Confidence { get; init; }

    /// <summary>The device ID that hit the family index.</summary>
    public string? MatchedId { get; init; }

    /// <summary>True when the hit came from a compatible ID rather than a hardware ID.</summary>
    public bool MatchedOnCompatibleId { get; init; }

    public FamilyInfo? Family { get; init; }

    /// <summary>Other families the matched ID also belongs to, best first.</summary>
    public IReadOnlyList<FamilyInfo> OtherCandidates { get; init; } = [];

    /// <summary>Installed version as compared (INF DriverVer, or the family's canonical scheme).</summary>
    public DriverVersion? Installed { get; init; }

    /// <summary>INF-level match (schema 2.0.0), when the installed INF is in infs.json.</summary>
    public InfMatch? Inf { get; init; }

    /// <summary>Package-level index entry for the exact installed version, when listed (fallback path).</summary>
    public KnownVersion? InstalledKnown { get; init; }

    /// <summary>Newest package version on the installed version's line (fallback path).</summary>
    public KnownVersion? LineNewest { get; init; }

    /// <summary>The newest release this driver is compared with.</summary>
    public VersionRef? Newest { get; init; }

    /// <summary>The release to update to, when behind.</summary>
    public VersionRef? Update { get; init; }

    public DateOnly? InstalledDate { get; init; }

    /// <summary>Days between the installed release and <see cref="Update"/>; 0 when current; null when unknown.</summary>
    public int? LagDays { get; init; }

    /// <summary>
    /// The family's newest release when it sits on a genuinely parallel line (e.g. Realtek NDIS 10.x vs
    /// NetAdapter 1125.x). Informational only.
    /// </summary>
    public WaterLevelNote? ParallelLine { get; init; }

    /// <summary>Installed version is newer than anything the index lists for it.</summary>
    public bool IsNewerThanIndex { get; init; }

    /// <summary>Human-readable explanation of how the match and status were decided.</summary>
    public string Reason { get; init; } = "";

    public string Component => Family?.Component ?? "other";

    /// <summary>The release to update to, when behind.</summary>
    public VersionRef? Target => Status == DriverStatus.Behind ? Update : null;

    /// <summary>Behind on the evidence of a same-line package version rather than an exact listing or INF.</summary>
    public bool IsApproximate => Confidence == MatchConfidence.SameLine;
}

/// <summary>
/// Matches local devices against the index.
/// 1. Walk hardware IDs then compatible IDs (most specific first), ignoring class-level IDs;
///    the first ID with a family hit gives the candidate families.
/// 2. In-box drivers get no currency verdict.
/// 3. INF level (infs.json): the installed INF's DriverVer is compared with the newest release of the
///    same INF — like-for-like, no heuristics.
/// 4. Otherwise, package level: candidates are ranked (exact version listed &gt; version line listed &gt;
///    has a water level &gt; silicon vendor matches), and the installed version is compared within its own
///    numbering line; the family newest also counts when it is on the directly following line and the
///    index doesn't mark the installed line as parallel.
/// </summary>
public sealed class DeviceMatcher(DriverIndex index, InfIndex? infs = null)
{
    public IReadOnlyList<DeviceResult> MatchAll(IEnumerable<LocalDevice> devices) => devices.Select(Match).ToList();

    public DeviceResult Match(LocalDevice device)
    {
        string? matchedId = null;
        IReadOnlyList<FamilyInfo> candidates = [];
        var onCompatible = false;

        foreach (var (id, compat) in device.HardwareIds.Select(h => (h, false)).Concat(device.CompatibleIds.Select(c => (c, true))))
        {
            var norm = Hwid.Normalise(id);
            if (norm.Length == 0 || Hwid.IsGeneric(norm))
                continue;
            var hit = index.Lookup(norm);
            if (hit.Count > 0)
            {
                matchedId = id;
                candidates = hit;
                onCompatible = compat;
                break;
            }
        }

        var inbox = device.IsInboxDriver || !device.HasDriver;
        var inf = !inbox ? infs?.Match(device) : null;

        if (matchedId is null && inf is null)
        {
            return new DeviceResult { Device = device, Status = DriverStatus.NotIndexed, Reason = "No hardware or compatible ID of this device is in the index." };
        }

        if (inbox)
        {
            var best = candidates.OrderByDescending(f => f.Water is not null).ThenBy(f => f.IsPreinstall).First();
            return new DeviceResult
            {
                Device = device,
                Status = DriverStatus.Inbox,
                MatchedId = matchedId,
                MatchedOnCompatibleId = onCompatible,
                Family = best,
                OtherCandidates = candidates.Where(c => c != best).ToList(),
                Reason = device.HasDriver
                    ? $"Using the Windows in-box driver ({device.InfPath ?? device.DriverProvider}); the {best.Name} vendor family also covers this hardware."
                    : $"No driver installed; the {best.Name} family covers this hardware.",
            };
        }

        var ranked = candidates
            .Select(f => new Candidate(f, f.Effective(device.DriverVersion), device.DriverProvider))
            .OrderByDescending(c => c.Score)
            .ToList();

        return inf is not null
            ? MatchByInf(device, inf, ranked, matchedId, onCompatible)
            : MatchByFamily(device, ranked, matchedId!, onCompatible);
    }

    private DeviceResult MatchByInf(LocalDevice device, InfMatch inf, List<Candidate> ranked, string? matchedId, bool onCompatible)
    {
        var installed = DriverVersion.Parse(device.DriverVersion);
        var newest = inf.Newest;
        var owners = newest.Row.FamilyIds;

        // Prefer the HWID-matched family the INF ships in; else the best-ranked candidate; else the INF's own families.
        var family = ranked.FirstOrDefault(c => owners.Contains(c.Family.Id))?.Family
                     ?? ranked.FirstOrDefault()?.Family
                     ?? owners.Select(id => index.Families.GetValueOrDefault(id)).OfType<FamilyInfo>().FirstOrDefault();
        var others = ranked.Select(c => c.Family).Where(f => f != family).ToList();
        var installedDate = device.DriverDate is { } dd ? DateOnly.FromDateTime(dd.UtcDateTime) : (DateOnly?)null;
        var confidence = inf.Installed is not null ? MatchConfidence.InfExact : MatchConfidence.Inf;
        var infName = inf.InfName ?? "(unnamed INF)";
        var how = inf.ByName
            ? $"Matched the installed INF {infName} in the index."
            : $"Matched this device's hardware ID to {infName} in the index.";
        // Only mention the water level when this device could actually run a driver on its line (Realtek
        // NetAdapter 1125.x for an RTL8125); for components of a package (chipset GPIO vs package 7.x) it's noise.
        var parallel = family is not null && installed.Line is { } line
                       && ParallelLineNote(family, line, null) is { Line: { } wl } note
                       && infs!.HasLineFor(device, wl)
            ? note
            : null;
        var parallelText = ParallelText(parallel);
        // Packages carrying the same INF release under another DriverVer spelling (Realtek publishes
        // 10.080.0407.2026 and 10.080.50.0407 for one 2026-04-07 build) are the same update: pool their links.
        var sameBuild = inf.Releases
            .Where(r => string.Equals(r.InfName, newest.InfName, StringComparison.OrdinalIgnoreCase)
                        && r.DriverDate == newest.DriverDate && r.Version.Major == newest.Version.Major)
            .SelectMany(r => r.Artefacts)
            .DistinctBy(a => a.ArtefactId)
            .ToList();
        var newestRef = VersionRef.From(newest) with { Artefacts = sameBuild, InfName = newest.InfName };

        if (installed < newest.Version)
        {
            var lag = DaysBetween(installedDate, newest.DriverDate);
            return new DeviceResult
            {
                Device = device,
                Status = DriverStatus.Behind,
                Confidence = confidence,
                MatchedId = matchedId,
                MatchedOnCompatibleId = onCompatible,
                Family = family,
                OtherCandidates = others,
                Installed = installed,
                Inf = inf,
                Newest = newestRef,
                Update = newestRef,
                InstalledDate = installedDate,
                LagDays = lag is > 0 ? lag : null,
                ParallelLine = parallel,
                Reason = $"{how} Its newest release is {newest.Row.DriverVer}" +
                         (newest.DriverDate is { } d ? $" (dated {d:yyyy-MM-dd})" : "") +
                         (!string.Equals(newest.InfName, inf.InfName, StringComparison.OrdinalIgnoreCase) && newest.InfName is not null
                             ? $", shipped as {newest.InfName}." : ".") + parallelText,
            };
        }

        var newer = installed > newest.Version;
        return new DeviceResult
        {
            Device = device,
            Status = DriverStatus.Current,
            Confidence = confidence,
            MatchedId = matchedId,
            MatchedOnCompatibleId = onCompatible,
            Family = family,
            OtherCandidates = others,
            Installed = installed,
            Inf = inf,
            Newest = newestRef,
            InstalledDate = installedDate,
            IsNewerThanIndex = newer,
            LagDays = 0,
            ParallelLine = parallel,
            Reason = how + (newer
                ? $" Your version is newer than any release of it the index has seen (newest indexed: {newest.Row.DriverVer})."
                : " You have its newest release.") + parallelText,
        };
    }

    private static DeviceResult MatchByFamily(LocalDevice device, List<Candidate> ranked, string matchedId, bool onCompatible)
    {
        var top = ranked[0];
        var family = top.Family;
        var installed = top.Installed;
        var others = ranked.Skip(1).Select(c => c.Family).ToList();
        var why = ranked.Count > 1
            ? $"{matchedId} is shared by {ranked.Count} families; chose {family.Name} ({top.Why}). "
            : "";

        if (installed.Line is not { } line || !family.HasLine(line))
        {
            return new DeviceResult
            {
                Device = device,
                Status = DriverStatus.NotComparable,
                Confidence = MatchConfidence.None,
                MatchedId = matchedId,
                MatchedOnCompatibleId = onCompatible,
                Family = family,
                OtherCandidates = others,
                Installed = installed,
                Reason = why + (installed.IsParsed
                    ? $"Installed version {installed.Raw} is on line {installed.Line}, which no {family.Name} listing uses — " +
                      "likely a per-component INF version inside a larger package."
                    : $"Installed version '{installed.Raw}' could not be parsed."),
            };
        }

        var known = family.Find(installed);
        var lineNewest = family.NewestOnLine(line)!;
        var confidence = known is not null ? MatchConfidence.ExactVersion : MatchConfidence.SameLine;
        var installedDate = known?.FirstReleased ?? (device.DriverDate is { } dd ? DateOnly.FromDateTime(dd.UtcDateTime) : (DateOnly?)null);

        // Update candidates: a newer release on the installed line, or the family's water level when it
        // sits on the next line of the same shape (INF 31.x -> 32.x). The newer-dated one wins.
        var onLine = installed < lineNewest.Version ? lineNewest : null;
        var successor = SuccessorWater(family, line, installed);
        var update = onLine;
        if (successor is not null && (update is null || (successor.FirstReleased ?? DateOnly.MinValue) >= (update.FirstReleased ?? DateOnly.MinValue)))
            update = successor;
        var lag = update is null ? 0 : DaysBetween(installedDate, update.FirstReleased);

        var parallel = successor is null ? ParallelLineNote(family, line, lineNewest) : null;
        var parallelText = ParallelText(parallel);

        // A higher version released no later than the installed one is almost always a sibling
        // component or parallel branch (AMD GPIO 2.2.x vs 2.9.x), not a successor.
        if (update is not null && lag is not 0)
        {
            var approx = confidence == MatchConfidence.SameLine
                ? " Your exact version isn't listed, so this comparison is approximate."
                : "";
            return new DeviceResult
            {
                Device = device,
                Status = DriverStatus.Behind,
                Confidence = confidence,
                MatchedId = matchedId,
                MatchedOnCompatibleId = onCompatible,
                Family = family,
                OtherCandidates = others,
                Installed = installed,
                InstalledKnown = known,
                LineNewest = lineNewest,
                Newest = VersionRef.From(update),
                Update = VersionRef.From(update),
                InstalledDate = installedDate,
                LagDays = lag,
                ParallelLine = parallel,
                Reason = why + (update == successor
                    ? $"{update.Version.Raw} is the newest {family.Name} release, on the version line after yours ({update.Version.Line})."
                    : $"{update.Version.Raw} is the newest {family.Name} release on the {line} line.") + approx + parallelText,
            };
        }

        var newerThanIndex = installed > lineNewest.Version && successor is null;
        return new DeviceResult
        {
            Device = device,
            Status = DriverStatus.Current,
            Confidence = confidence,
            MatchedId = matchedId,
            MatchedOnCompatibleId = onCompatible,
            Family = family,
            OtherCandidates = others,
            Installed = installed,
            InstalledKnown = known,
            LineNewest = lineNewest,
            Newest = VersionRef.From(lineNewest),
            InstalledDate = installedDate,
            IsNewerThanIndex = newerThanIndex,
            LagDays = 0,
            ParallelLine = parallel,
            Reason = why + (newerThanIndex
                ? $"Installed {installed.Raw} is newer than any {family.Name} release the index has seen on its line."
                : update is not null
                    ? $"{update.Version.Raw} is numbered higher but was released no later than your version, so it is " +
                      "treated as a sibling component or branch rather than an update."
                    : $"Installed {installed.Raw} is the newest {family.Name} release on its line.") + parallelText,
        };
    }

    /// <summary>
    /// The family's water level when its line directly succeeds <paramref name="line"/>: same segment count, a
    /// major within ~10% above (31 -> 32, 10 -> 11), and not marked by the index as a parallel line.
    /// </summary>
    private static KnownVersion? SuccessorWater(FamilyInfo family, VersionLine line, DriverVersion installed)
    {
        if (family.WaterVersion is not { Line: { } wl } water || !IsSuccessorLine(line, wl) || water <= installed)
            return null;
        if (family.LineInfo(line.Major) is { ParallelToWater: true })
            return null;
        return family.Find(water);
    }

    internal static bool IsSuccessorLine(VersionLine from, VersionLine to) => from.IsFollowedBy(to);

    /// <summary>
    /// A note about the family's water level on another line. With schema 2.0.0 line data, only lines the index
    /// marks parallel get one; otherwise the water must be newer than the newest release on the installed line.
    /// </summary>
    private static WaterLevelNote? ParallelLineNote(FamilyInfo family, VersionLine line, KnownVersion? lineNewest)
    {
        if (family.Water is not { } water || family.WaterVersion?.Line is not { } wl || wl == line)
            return null;
        if (family.Water.Lines is { Count: > 0 })
        {
            return family.LineInfo(line.Major) is { ParallelToWater: true }
                ? new WaterLevelNote(water.Version, family.WaterDate, wl)
                : null;
        }

        // INF path (lineNewest null): the caller confirms a driver on the water's line binds the device.
        if (lineNewest is not null && family.WaterDate is { } wd && lineNewest.FirstReleased is { } ld && wd <= ld)
            return null;
        return new WaterLevelNote(water.Version, family.WaterDate, wl);
    }

    private static string ParallelText(WaterLevelNote? parallel) => parallel is null ? "" :
        $" The family's newest release, {parallel.Version} ({parallel.FirstPublished:yyyy-MM-dd}), is on a parallel " +
        $"numbering line ({parallel.Line}); that can be a newer driver model or a different component — check the vendor's notes.";

    private static int? DaysBetween(DateOnly? from, DateOnly? to) =>
        from is { } f && to is { } t ? Math.Max(0, t.DayNumber - f.DayNumber) : null;

    private sealed class Candidate
    {
        public Candidate(FamilyInfo family, DriverVersion installed, string? provider)
        {
            Family = family;
            Installed = installed;
            var exact = family.Find(installed) is not null;
            var onLine = installed.Line is { } l && family.HasLine(l);
            var hasWater = family.Water is not null && !family.IsPreinstall;
            var vendor = SiliconVendorMatches(family.Family.SiliconVendor, provider);
            Score = (exact ? 8 : 0) + (onLine ? 4 : 0) + (hasWater ? 2 : 0) + (vendor ? 1 : 0);
            Why = exact ? "installed version is listed there"
                : onLine ? "installed version's line is listed there"
                : hasWater ? "only candidate with a water level"
                : "best remaining candidate";
        }

        public FamilyInfo Family { get; }
        public DriverVersion Installed { get; }
        public int Score { get; }
        public string Why { get; }
    }

    private static bool SiliconVendorMatches(string? siliconVendor, string? provider)
    {
        if (string.IsNullOrEmpty(siliconVendor) || string.IsNullOrEmpty(provider))
            return false;
        if (provider.Contains(siliconVendor, StringComparison.OrdinalIgnoreCase))
            return true;
        return siliconVendor.Equals("amd", StringComparison.OrdinalIgnoreCase)
               && (provider.Contains("Advanced Micro Devices", StringComparison.OrdinalIgnoreCase) || provider.Contains("ATI", StringComparison.Ordinal));
    }
}

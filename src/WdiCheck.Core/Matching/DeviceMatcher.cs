using WdiCheck.Core.Index;
using WdiCheck.Core.Inventory;
using WdiCheck.Core.Versions;

namespace WdiCheck.Core.Matching;

public enum DriverStatus
{
    /// <summary>At (or above) the newest version on its numbering line.</summary>
    Current,
    /// <summary>A newer version exists on the installed driver's own line.</summary>
    Behind,
    /// <summary>Matched a family, but the installed version isn't on any line the index knows (e.g. per-component INF versions).</summary>
    NotComparable,
    /// <summary>Windows in-box driver on hardware a vendor family covers.</summary>
    Inbox,
    /// <summary>No HWID of this device appears in the index.</summary>
    NotIndexed,
}

public enum MatchConfidence
{
    None,
    /// <summary>Installed version shares a numbering line with indexed versions.</summary>
    SameLine,
    /// <summary>Installed version is exactly a version some vendor/upstream listed.</summary>
    ExactVersion,
}

public sealed class DeviceResult
{
    public required LocalDevice Device { get; init; }
    public DriverStatus Status { get; init; }
    public MatchConfidence Confidence { get; init; }

    /// <summary>The device ID that hit the index.</summary>
    public string? MatchedId { get; init; }

    /// <summary>True when the hit came from a compatible ID rather than a hardware ID.</summary>
    public bool MatchedOnCompatibleId { get; init; }

    public FamilyInfo? Family { get; init; }

    /// <summary>Other families the matched ID also belongs to, best first.</summary>
    public IReadOnlyList<FamilyInfo> OtherCandidates { get; init; } = [];

    /// <summary>Installed version in the family's canonical scheme.</summary>
    public DriverVersion? Installed { get; init; }

    /// <summary>Index entry for the exact installed version, when listed.</summary>
    public KnownVersion? InstalledKnown { get; init; }

    /// <summary>Newest non-beta version on the installed version's line.</summary>
    public KnownVersion? LineNewest { get; init; }

    /// <summary>Days between the installed version's release and <see cref="Update"/>'s; 0 when current.</summary>
    public int? LagDays { get; init; }

    /// <summary>
    /// The family's newest release when it sits on a different numbering line that was published
    /// after the newest release on the installed line (e.g. Realtek NDIS 10.x vs NetAdapter 1125.x).
    /// Informational only: per-component INF lines inside a package look the same, so this never
    /// drives the status.
    /// </summary>
    public WaterLevelNote? ParallelLine { get; init; }

    /// <summary>Installed version is newer than anything the index has seen on its line.</summary>
    public bool IsNewerThanIndex { get; init; }

    /// <summary>Human-readable explanation of how the family and status were chosen.</summary>
    public string Reason { get; init; } = "";

    public string Component => Family?.Component ?? "other";

    public DateOnly? InstalledDate =>
        InstalledKnown?.FirstReleased ?? (Device.DriverDate is { } d ? DateOnly.FromDateTime(d.UtcDateTime) : null);

    /// <summary>
    /// The newer release this driver should move to: the newest on its own line, or the family's
    /// newest when that is on the directly following line (AMD INF 31.x -> 32.x).
    /// </summary>
    public KnownVersion? Update { get; init; }

    /// <summary>The version to update to, when behind.</summary>
    public KnownVersion? Target => Status == DriverStatus.Behind ? Update : null;

    /// <summary>Behind on the evidence of a same-line version rather than an exact listing; may be a sibling component.</summary>
    public bool IsApproximate => Confidence == MatchConfidence.SameLine;
}

public sealed record WaterLevelNote(string Version, DateOnly? FirstPublished, VersionLine? Line);

/// <summary>
/// Matches local devices against the index.
/// 1. Walk hardware IDs then compatible IDs (most specific first), ignoring class-level IDs;
///    the first ID with an index hit defines the candidate families.
/// 2. In-box drivers get no currency verdict.
/// 3. Candidates are ranked: exact installed version listed &gt; installed version's line known
///    &gt; family has a water level &gt; silicon vendor matches driver provider.
/// 4. The installed version is compared within its own numbering line, plus the family's newest when
///    that is on the directly following line; other lines (e.g. Realtek NDIS 10.x vs NetAdapter
///    1125.x) are reported as a note only.
/// </summary>
public sealed class DeviceMatcher(DriverIndex index)
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

        if (matchedId is null)
        {
            return new DeviceResult { Device = device, Status = DriverStatus.NotIndexed, Reason = "No hardware or compatible ID of this device is in the index." };
        }

        if (device.IsInboxDriver || !device.HasDriver)
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
            .Select(f => new Candidate(f, SchemeEquivalence.Effective(f.Name, device.DriverVersion), device.DriverProvider))
            .OrderByDescending(c => c.Score)
            .ToList();
        var top = ranked[0];
        var family = top.Family;
        var installed = top.Installed;
        var others = ranked.Skip(1).Select(c => c.Family).ToList();
        var why = candidates.Count > 1
            ? $"{matchedId} is shared by {candidates.Count} families; chose {family.Name} ({top.Why}). "
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
        // sits on the next line of the same shape (AMD INF 31.x -> 32.x). The newer-dated one wins.
        var onLine = installed < lineNewest.Version ? lineNewest : null;
        var successor = SuccessorWater(family, line, installed);
        var update = onLine;
        if (successor is not null && (update is null || (successor.FirstReleased ?? DateOnly.MinValue) >= (update.FirstReleased ?? DateOnly.MinValue)))
            update = successor;
        var lag = update is null ? 0 : DaysBetween(installedDate, update.FirstReleased);

        var parallel = successor is null ? ParallelLineNote(family, line, lineNewest) : null;
        var parallelText = parallel is null ? "" :
            $" The family's newest release, {parallel.Version} ({parallel.FirstPublished:yyyy-MM-dd}), is on a different " +
            $"numbering line ({parallel.Line}); that can be a newer driver model or a different component — check the vendor's notes.";

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
                Update = update,
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
    /// The family's water level when its line directly succeeds <paramref name="line"/>: same segment count and a
    /// major within ~10% above (31 -> 32, 10 -> 11). Per-component INF lines inside packages (GPIO 2.x vs chipset
    /// package 7.x, NDIS 10.x vs NetAdapter 1125.x) fail this and are only reported as a parallel line.
    /// </summary>
    private static KnownVersion? SuccessorWater(FamilyInfo family, VersionLine line, DriverVersion installed)
    {
        if (family.WaterVersion is not { Line: { } wl } water || !IsSuccessorLine(line, wl) || water <= installed)
            return null;
        return family.Find(water);
    }

    internal static bool IsSuccessorLine(VersionLine from, VersionLine to) =>
        to.Segments == from.Segments && to.Major > from.Major && to.Major <= from.Major + Math.Max(1, from.Major / 10);

    private static WaterLevelNote? ParallelLineNote(FamilyInfo family, VersionLine line, KnownVersion lineNewest)
    {
        if (family.Water is not { } water || family.WaterVersion?.Line is not { } wl || wl == line)
            return null;
        if (family.WaterDate is { } wd && lineNewest.FirstReleased is { } ld && wd <= ld)
            return null;
        return new WaterLevelNote(water.Version, family.WaterDate, wl);
    }

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

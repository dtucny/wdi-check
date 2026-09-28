using System.Globalization;
using System.Text;
using System.Text.Json;
using WdiCheck.Core.Inventory;
using WdiCheck.Core.Matching;

namespace WdiCheck.Core.Reporting;

public static class ReportExporter
{
    private static readonly string[] Columns =
    [
        "device", "class", "status", "confidence", "component", "family", "installed_version", "installed_date",
        "driver_provider", "inf", "original_inf", "matched_inf", "newest", "newest_date", "family_newest", "family_newest_date",
        "lag_days", "matched_id", "instance_id", "download_url", "download_hint",
    ];

    public static async Task WriteCsvAsync(Stream stream, IEnumerable<DeviceResult> results, CancellationToken ct = default)
    {
        await using var w = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true);
        await w.WriteLineAsync(string.Join(',', Columns)).ConfigureAwait(false);
        foreach (var r in results)
        {
            ct.ThrowIfCancellationRequested();
            await w.WriteLineAsync(string.Join(',', Row(r).Select(Csv))).ConfigureAwait(false);
        }
    }

    public static async Task WriteJsonAsync(Stream stream, IEnumerable<DeviceResult> results, SystemIdentity? identity,
        BoardAssessment? board, string? indexGenerated, CancellationToken ct = default)
    {
        await using var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        w.WriteStartObject();
        w.WriteString("generated", DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture));
        w.WriteString("index_generated", indexGenerated);
        w.WriteString("source", "WinDriverIndex (https://windriverindex.tucny.com/), CC-BY-4.0");
        if (identity is not null)
        {
            w.WriteStartObject("system");
            w.WriteString("baseboard_manufacturer", identity.BaseBoardManufacturer);
            w.WriteString("baseboard_product", identity.BaseBoardProduct);
            w.WriteString("system_manufacturer", identity.SystemManufacturer);
            w.WriteString("system_product", identity.SystemProductName);
            w.WriteString("bios_version", identity.BiosVersion);
            w.WriteString("bios_date", identity.BiosReleaseDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            w.WriteEndObject();
        }

        if (board is not null)
        {
            w.WriteStartObject("board");
            w.WriteNumber("board_id", board.Report.Board.BoardId);
            w.WriteString("vendor", board.Report.Board.Vendor);
            w.WriteString("name", board.Report.Board.Name);
            w.WriteString("bios_status", board.BiosStatus.ToString());
            w.WriteString("vendor_last_bios", board.Report.Bios?.LastBios);
            w.WriteEndObject();
        }

        w.WriteStartArray("devices");
        foreach (var r in results)
        {
            ct.ThrowIfCancellationRequested();
            w.WriteStartObject();
            var row = Row(r);
            for (var i = 0; i < Columns.Length; i++)
            {
                if (Columns[i] == "lag_days" && r.LagDays is { } lag)
                    w.WriteNumber(Columns[i], lag);
                else
                    w.WriteString(Columns[i], string.IsNullOrEmpty(row[i]) ? null : row[i]);
            }

            w.WriteEndObject();
        }

        w.WriteEndArray();
        w.WriteEndObject();
        await w.FlushAsync(ct).ConfigureAwait(false);
    }

    private static string?[] Row(DeviceResult r)
    {
        var family = r.Family;
        var target = r.Target;
        return
        [
            r.Device.Name,
            r.Device.Class,
            r.Status.ToString(),
            r.Confidence.ToString(),
            r.Component,
            family?.Name,
            r.Device.DriverVersion,
            Date(r.InstalledDate),
            r.Device.DriverProvider,
            r.Device.InfPath,
            r.Device.OriginalInfName,
            r.Inf?.InfName,
            r.Newest?.Raw,
            Date(r.Newest?.Date),
            family?.Water?.Version,
            Date(family?.WaterDate),
            r.LagDays?.ToString(CultureInfo.InvariantCulture),
            r.MatchedId,
            r.Device.InstanceId,
            target?.Artefacts.Select(a => a.Url).FirstOrDefault(DownloadLinks.IsSafe),
            r.Status == DriverStatus.Behind ? DownloadLinks.Hint(r)?.Url : null,
        ];
    }

    private static string? Date(DateOnly? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Csv(string? s)
    {
        if (string.IsNullOrEmpty(s))
            return "";
        // Neutralise spreadsheet formula injection from device/vendor strings.
        if (s[0] is '=' or '+' or '-' or '@')
            s = "'" + s;
        return s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }
}

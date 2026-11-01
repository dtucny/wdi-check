using WdiCheck.Core.Matching;

namespace WdiCheck.App.ViewModels;

/// <summary>One line of "This machine": a piece of hardware, its driver's status and the chip vendor's own release.</summary>
public sealed class HardwareRowViewModel
{
    public HardwareRowViewModel(HardwareItem item, DeviceRowViewModel? driver)
    {
        Label = item.Component == "processor" ? "Processor" : Display.Component(item.Component);
        Title = item.Detail is { } detail ? $"{item.Name} · {detail}" : item.Name;

        if (driver is not null)
        {
            var r = driver.Result;
            var others = item.Devices.Count > 1 ? $" · {item.Devices.Count} devices" : "";
            // NVIDIA's 32.0.16.2101 is 621.01 in the family's numbering, which the chip vendor's release uses.
            var installed = r.Family?.Effective(r.Device.DriverVersion) is { IsParsed: true } iv && iv.Raw != driver.Installed
                ? $"{driver.Installed} (= {iv.Raw})"
                : driver.Installed;
            DriverText = r.Status == DriverStatus.Behind
                ? $"{driver.Family} · {installed} → {driver.Newest}{others}"
                : $"{driver.Family} · {installed}{others}";
            (Pill, Severity) = driver.HasWindowsUpdateUpdate && r.Status != DriverStatus.Behind
                ? (driver.AttentionPill, driver.AttentionSeverity)
                : (driver.StatusText, driver.Severity);
            SearchName = driver.Name;
            Filter = r.Status is DriverStatus.Inbox or DriverStatus.NotIndexed ? Filters.Everything : Filters.AllMatched;
        }
        else
        {
            DriverText = "No driver the index compares";
            SearchName = item.Name;
            Filter = Filters.Everything;
        }

        var maker = Display.SiliconVendor(item.Driver?.Family?.Family.SiliconVendor);
        if (item.Silicon is { } s)
        {
            SiliconText = $"{maker ?? "The chip vendor"}'s newest release: {s.Version}" +
                          (s.Date is { } d ? $" ({Display.Date(d)})" : "") +
                          (s.SameNumbering ? "" : ", numbered differently from the installed driver");
            SiliconUrl = s.Url;
            SiliconLinkText = maker is null ? "Chip vendor's download" : $"Get from {maker}";
        }
    }

    /// <summary>"Processor", "Graphics", "Ethernet"…</summary>
    public string Label { get; }

    /// <summary>"NVIDIA GeForce RTX 4070 Ti · MSI card".</summary>
    public string Title { get; }

    /// <summary>"NVIDIA Graphics · 621.01", or installed → newest when behind.</summary>
    public string DriverText { get; }

    public string Pill { get; } = "";
    public Severity Severity { get; } = Severity.Neutral;
    public bool HasPill => Pill.Length > 0;

    /// <summary>What the Drivers list is searched and filtered for when the row is clicked.</summary>
    public string SearchName { get; }
    public string Filter { get; }

    public string SiliconText { get; } = "";
    public bool HasSilicon => SiliconText.Length > 0;
    public Uri? SiliconUrl { get; }
    public string SiliconLinkText { get; } = "";
    public bool HasSiliconUrl => SiliconUrl is not null;

    /// <summary>Accessible name of the row.</summary>
    public override string ToString() => $"{Label}: {Title}, {Pill}";
}

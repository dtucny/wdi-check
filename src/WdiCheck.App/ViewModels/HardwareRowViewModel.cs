using WdiCheck.Core.Matching;

namespace WdiCheck.App.ViewModels;

/// <summary>One line of "This machine": a piece of hardware, its driver's status and the chip vendor's own release.</summary>
public sealed class HardwareRowViewModel
{
    /// <param name="driver">The row's representative driver (<see cref="HardwareItem.Driver"/>).</param>
    /// <param name="devices">Every device of the row, for a row that stands for a package of drivers.</param>
    public HardwareRowViewModel(HardwareItem item, DeviceRowViewModel? driver, IReadOnlyList<DeviceRowViewModel> devices, string? cardKey = null)
    {
        CardKey = cardKey;
        var isPackage = false;
        Label = item.Component == "processor" ? "Processor" : Display.Component(item.Component);
        Title = item.Detail is { } detail ? $"{item.Name} · {detail}" : item.Name;

        if (driver is not null)
        {
            var r = driver.Result;
            // A GPU row is the GPU's own driver. Anything else with several devices is a package (the chipset's GPIO,
            // PSP, I2C… drivers): no single component's version speaks for it, so the row describes the family.
            isPackage = item.Component != "graphics" && devices.Count > 1;
            if (isPackage)
            {
                var behind = devices.Count(d => d.Result.Status == DriverStatus.Behind);
                var current = devices.Count(d => d.Result.Status == DriverStatus.Current);
                DriverText = $"{driver.Family} · {devices.Count} drivers" +
                             (behind > 0 ? $", {behind} behind" : current == devices.Count ? ", all current" : "");
                if (devices.Select(d => d.Vendor).OfType<VendorListing>().ToList() is [var first, ..] listings)
                {
                    (VendorPill, VendorSeverity) = Display.Standing(VendorComparison.Combine(listings.Select(l => l.Comparison)), first.Vendor);
                    VendorLine = driver.VendorLatest;
                }
                SearchName = driver.Family;
            }
            else
            {
                var others = item.Devices.Count > 1 ? $" · {item.Devices.Count} devices" : "";
                // NVIDIA's 32.0.16.2101 is 621.01 in the family's numbering, which the chip vendor's release uses.
                var installed = r.Family?.Effective(r.Device.DriverVersion) is { IsParsed: true } iv && iv.Raw != driver.Installed
                    ? $"{driver.Installed} (= {iv.Raw})"
                    : driver.Installed;
                DriverText = r.Status == DriverStatus.Behind
                    ? $"{driver.Family} · {installed} → {driver.Newest}{others}"
                    : $"{driver.Family} · {installed}{others}";
                (VendorLine, VendorPill, VendorSeverity) = (driver.VendorLine, driver.VendorPill, driver.VendorSeverity);
                SearchName = driver.Name;
            }
            (Pill, Severity) = driver.HasWindowsUpdateUpdate && r.Status != DriverStatus.Behind
                ? (driver.AttentionPill, driver.AttentionSeverity)
                : (VendorPill.Length > 0 && r.Status == DriverStatus.Behind ? $"{driver.StatusText} newest" : driver.StatusText, driver.Severity);
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
                          (s.SameNumbering ? "" : ", numbered differently from the installed driver" + (isPackage ? "s" : ""));
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

    /// <summary>The board's or card's vendor: its latest version, and the driver against it ("Current with MSI").</summary>
    public string VendorLine { get; } = "";
    public bool HasVendorLine => VendorLine.Length > 0;
    public string VendorPill { get; } = "";
    public Severity VendorSeverity { get; } = Severity.Neutral;
    public bool HasVendorPill => VendorPill.Length > 0;

    /// <summary>An add-in card with a page of its own, which the row opens instead of the Drivers list.</summary>
    public string? CardKey { get; }

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

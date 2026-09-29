using WdiCheck.Core.Inventory;
using WdiCheck.Core.WindowsUpdate;

namespace WdiCheck.App.ViewModels;

public enum WindowsUpdateStatus
{
    /// <summary>Turned off in Settings.</summary>
    Off,
    Searching,
    Done,
    Failed,
}

/// <summary>State of the Windows Update driver search that runs alongside each scan.</summary>
public sealed record WindowsUpdateInfo(WindowsUpdateStatus Status, WindowsUpdateResult? Result)
{
    public static readonly WindowsUpdateInfo Off = new(WindowsUpdateStatus.Off, null);
    public static readonly WindowsUpdateInfo Searching = new(WindowsUpdateStatus.Searching, null);

    public static WindowsUpdateInfo From(WindowsUpdateResult result) =>
        new(result.Succeeded ? WindowsUpdateStatus.Done : WindowsUpdateStatus.Failed, result);

    public IReadOnlyList<WindowsUpdateOffer> Offers => Result?.Offers ?? [];

    public WindowsUpdateOffer? OfferFor(LocalDevice device) => Offers.FirstOrDefault(o => o.AppliesTo(device));
}

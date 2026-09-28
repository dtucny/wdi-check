using WdiCheck.Core.Index;
using WdiCheck.Core.Versions;

namespace WdiCheck.Core.Tests;

public class DriverVersionTests
{
    [Fact]
    public void ZeroPaddedSegmentsAreEqual()
    {
        // Spec §6.3: MSI's 3.05.00.1380 and ASUS's 3.5.0.1380 are the same driver.
        Assert.Equal(DriverVersion.Parse("3.05.00.1380"), DriverVersion.Parse("3.5.0.1380"));
        Assert.Equal(0, DriverVersion.Parse("3.05.00.1380").CompareTo(DriverVersion.Parse("3.5.0.1380")));
        Assert.Equal(DriverVersion.Parse("3.05.00.1380").GetHashCode(), DriverVersion.Parse("3.5.0.1380").GetHashCode());
    }

    [Theory]
    [InlineData("XB560NF_v6001.16.175.0", new long[] { 6001, 16, 175, 0 })]
    [InlineData("6.0.9520.1_Nahimic", new long[] { 6, 0, 9520, 1 })]
    [InlineData("v1.2 build 10.79.50.1003", new long[] { 10, 79, 50, 1003 })]
    [InlineData("10.080.0525.2026", new long[] { 10, 80, 525, 2026 })]
    public void LongestDottedRunWins(string raw, long[] expected)
    {
        var v = DriverVersion.Parse(raw);
        Assert.Equal(expected, v.Parts);
        Assert.Equal(raw, v.Raw);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2443_UAD_WHQL")]
    [InlineData("latest")]
    public void UnparseableHasNoParts(string? raw)
    {
        var v = DriverVersion.Parse(raw);
        Assert.False(v.IsParsed);
        Assert.Null(v.Line);
        Assert.True(v < DriverVersion.Parse("0.1"));
    }

    [Fact]
    public void BetaIsFlagged()
    {
        Assert.True(DriverVersion.Parse("5.40.0.0 Beta").IsBeta);
        Assert.False(DriverVersion.Parse("5.40.0.0").IsBeta);
    }

    [Fact]
    public void MixedLengthsCompareAsZeroPadded()
    {
        Assert.Equal(DriverVersion.Parse("1.2"), DriverVersion.Parse("1.2.0.0"));
        Assert.True(DriverVersion.Parse("1.2.0.1") > DriverVersion.Parse("1.2"));
        Assert.True(DriverVersion.Parse("31.0.24028.1001") > DriverVersion.Parse("31.0.24002.92"));
    }

    [Fact]
    public void LineIsMajorAndSegmentCount()
    {
        Assert.Equal(new VersionLine(10, 4), DriverVersion.Parse("10.79.50.1003").Line);
        Assert.NotEqual(DriverVersion.Parse("1125.29.50.202").Line, DriverVersion.Parse("10.79.50.1003").Line);
        Assert.NotEqual(DriverVersion.Parse("616.92").Line, DriverVersion.Parse("616.92.0.1").Line);
        // Two-part marketing versions are a single line across branches.
        Assert.Equal(DriverVersion.Parse("591.86").Line, DriverVersion.Parse("616.92").Line);
    }

    [Fact]
    public void OversizedSegmentIsUnparseable() =>
        Assert.False(DriverVersion.Parse("1.99999999999999999999999").IsParsed);
}

public class SchemeEquivalenceTests
{
    [Theory]
    [InlineData("32.0.16.1692", "616.92")]
    [InlineData("32.0.15.9186", "591.86")]
    [InlineData("31.0.15.4630", "546.30")]
    public void NvidiaInfToMarketing(string inf, string marketing) =>
        Assert.Equal(marketing, SchemeEquivalence.NvidiaMarketing(inf));

    [Theory]
    [InlineData("32.0.101.6881")] // Intel graphics: three-digit third field
    [InlineData("1.4.6.3")]
    [InlineData("616.92")]
    public void NonNvidiaInfIsUntouched(string raw) => Assert.Null(SchemeEquivalence.NvidiaMarketing(raw));

    [Theory]
    [InlineData("9977.1_UAD_WHQL", "6.0.9977.1")]
    [InlineData("10007.1_UAD_WHQL", "6.0.10007.1")]
    [InlineData("9360.1_UAD_WHQL_Nahimic", "6.0.9360.1")]
    [InlineData("9231.1 UAD", "6.0.9231.1")]
    public void RealtekUadToCanonical(string raw, string canonical) =>
        Assert.Equal(canonical, SchemeEquivalence.RealtekUad(raw));

    [Fact]
    public void RulesApplyOnlyToTheirFamily()
    {
        Assert.Equal("616.92", SchemeEquivalence.Effective("NVIDIA Graphics", "32.0.16.1692").Normalised);
        Assert.Equal("32.0.16.1692", SchemeEquivalence.Effective("AMD Graphics", "32.0.16.1692").Normalised);
        Assert.Equal("6.0.9360.1", SchemeEquivalence.Effective("Realtek Audio", "9360.1_UAD_WHQL").Normalised);
        Assert.Equal("9360.1", SchemeEquivalence.Effective("Realtek LAN", "9360.1_UAD_WHQL").Normalised);
    }
}

public class HwidTests
{
    [Theory]
    [InlineData(@"PCI\CC_010802")]
    [InlineData(@"PCI\VEN_10EC&CC_020000")]
    [InlineData(@"USB\CLASS_01&SUBCLASS_01")]
    [InlineData(@"USB\COMPOSITE")]
    [InlineData(@"HDAUDIO\FUNC_01")]
    [InlineData(@"*PNP0A03")]
    public void ClassLevelIdsAreGeneric(string id) => Assert.True(Hwid.IsGeneric(Hwid.Normalise(id)));

    [Theory]
    [InlineData(@"PCI\VEN_10EC&DEV_8125")]
    [InlineData(@"PCI\VEN_1022&DEV_43BD&CC_0104")]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10EC&DEV_0897")]
    [InlineData(@"SWC\AMDOCL-23.40")]
    [InlineData(@"ACPI\AMDI0030")]
    public void DeviceIdsAreNotGeneric(string id) => Assert.False(Hwid.IsGeneric(Hwid.Normalise(id)));

    [Theory]
    [InlineData(@"PCI\AMDPCIE.DEVICEDESC")]
    [InlineData(@"ACPI\PARAMETERS")]
    [InlineData("NOBACKSLASH")]
    public void IndexJunkIsMalformed(string id) => Assert.True(Hwid.IsMalformed(Hwid.Normalise(id)));
}

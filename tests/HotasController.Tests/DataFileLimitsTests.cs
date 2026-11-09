using HotasController.Core.Storage;

namespace HotasController.Tests;

public sealed class DataFileLimitsTests
{
    [Fact]
    public void Limits_MatchTheAgreedValues()
    {
        Assert.Equal(100, DataFileLimits.MaxPresets);
        Assert.Equal(1000, DataFileLimits.MaxDevices);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("Saitek X-56", "Saitek X-56")]
    [InlineData("Line1\r\nLine2", "Line1  Line2")]
    [InlineData("Tab\there", "Tab here")]
    public void CleanName_ReplacesControlCharacters(string? input, string expected) =>
        Assert.Equal(expected, DataFileLimits.CleanName(input, 100));

    [Fact]
    public void CleanName_CutsToMaxLength() =>
        Assert.Equal("abcde", DataFileLimits.CleanName("abcdefghij", 5));

    [Theory]
    [InlineData(DataFolderPolicy.AdministratorsSid, true)]
    [InlineData(DataFolderPolicy.LocalSystemSid, true)]
    [InlineData("s-1-5-18", true)]
    [InlineData("S-1-5-21-1111-2222-3333-1001", false)] // an ordinary user
    [InlineData("S-1-5-32-545", false)]                  // the Users group
    [InlineData("S-1-1-0", false)]                       // Everyone
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsTrustedOwner_OnlyAdministratorsAndSystem(string? sid, bool expected) =>
        Assert.Equal(expected, DataFolderPolicy.IsTrustedOwner(sid));
}

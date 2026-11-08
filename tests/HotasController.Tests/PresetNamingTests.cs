using HotasController.Core.Presets;

namespace HotasController.Tests;

public sealed class PresetNamingTests
{
    [Theory]
    [InlineData("<Default>")]
    [InlineData("<default>")]
    [InlineData("  <DEFAULT>  ")]
    public void IsDefaultLabel_True(string name) => Assert.True(PresetNaming.IsDefaultLabel(name));

    [Theory]
    [InlineData("Default")]
    [InlineData("Racing")]
    [InlineData("<Default> 2")]
    [InlineData("")]
    [InlineData(null)]
    public void IsDefaultLabel_False(string? name) => Assert.False(PresetNaming.IsDefaultLabel(name));
}

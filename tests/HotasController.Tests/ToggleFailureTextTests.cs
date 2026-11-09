using HotasController.Core;
using Xunit;

namespace HotasController.Tests;

public class ToggleFailureTextTests
{
    [Fact]
    public void WithRestartHint_NoFlaggedNodes_MessageUnchanged()
    {
        Assert.Equal("Disabling 'X' failed: nope.", ToggleFailureText.WithRestartHint("Disabling 'X' failed: nope.", []));
    }

    [Fact]
    public void WithRestartHint_FlaggedNodes_NamedInAdvice()
    {
        var text = ToggleFailureText.WithRestartHint("Disabling 'X' failed: nope.", ["USB Input Device"]);
        Assert.StartsWith("Disabling 'X' failed: nope.", text);
        Assert.Contains("'USB Input Device'", text);
        Assert.Contains("restart", text);
    }

    [Fact]
    public void WithRestartHint_DuplicateNames_ListedOnce()
    {
        var text = ToggleFailureText.WithRestartHint("failed.", ["USB Input Device", "USB Input Device"]);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(text, "'USB Input Device'"));
    }
}

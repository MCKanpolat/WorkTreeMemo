using WorkTreeMemo.Core.Localization;

namespace WorkTreeMemo.Core.Tests;

public sealed class LocalizerTests
{
    [Fact]
    public void Localizer_uses_selected_resource_culture()
    {
        var localizer = new Localizer();
        Localizer.UseCulture("tr-TR");
        Assert.Equal("Çıkış", localizer["Quit"]);
        Localizer.UseCulture("en-US");
        Assert.Equal("Quit", localizer["Quit"]);
    }

    [Theory]
    [InlineData("this is not a culture")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    [InlineData("")]
    public void An_unusable_culture_in_the_configuration_falls_back_instead_of_throwing(string culture)
    {
        Localizer.UseCulture("tr-TR");
        Localizer.UseCulture(culture);
        Assert.Equal("Quit", new Localizer()["Quit"]);
    }
}

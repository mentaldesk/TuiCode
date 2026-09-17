using Terminal.Gui.Views;
using TuiCode.Workbench.About;

namespace TuiCode.Tests;

public class AboutViewTests
{
    [Fact]
    public void Ctor_shows_the_version_and_repository()
    {
        using var view = new AboutView("1.2.3");

        var details = view.SubViews.OfType<Label>().Single(label => label.Text.Contains("Version"));

        Assert.Contains("Version 1.2.3", details.Text);
        Assert.Contains(AboutView.RepositoryUrl, details.Text);
    }
}

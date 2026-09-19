using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using TuiCode.Workbench.Settings;
using TuiCode.Workbench.Themes;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace TuiCode.Tests;

public class RowStripesTests : StaticConfigurationTest
{
    private static readonly Attribute Normal = new(new Color(200, 200, 200), new Color(20, 20, 20));

    [Fact]
    public void For_stripes_odd_rows_and_leaves_even_rows_alone()
    {
        Assert.Null(RowStripes.For(0, null, Normal));
        Assert.Equal(RowStripes.Stripe(Normal), RowStripes.For(1, null, Normal));
        Assert.Null(RowStripes.For(2, null, Normal));
        Assert.Equal(RowStripes.Stripe(Normal), RowStripes.For(3, null, Normal));
    }

    [Fact]
    public void For_leaves_the_selected_row_to_the_list()
    {
        Assert.Null(RowStripes.For(3, 3, Normal));
        Assert.Null(RowStripes.For(2, 2, Normal));
    }

    [Fact]
    public void Stripe_nudges_the_background_towards_the_text_and_keeps_the_text_colour()
    {
        var stripe = RowStripes.Stripe(Normal);

        Assert.Equal(Normal.Foreground, stripe.Foreground);
        Assert.Equal(new Color(38, 38, 38), stripe.Background);
    }

    [Fact]
    public void Every_bundled_theme_keeps_the_stripe_apart_from_the_background_and_the_selection()
    {
        ConfigurationManager.Enable(ConfigLocations.None);
        try
        {
            ConfigurationManager.RuntimeConfig = BundledThemes.Config;
            ConfigurationManager.Load(ConfigLocations.LibraryResources | ConfigLocations.Runtime);

            foreach (var theme in BundledThemes.Names)
            {
                ThemeManager.Theme = theme;
                ConfigurationManager.Apply();
                foreach (var name in new[] { "Base", "Dialog" })
                {
                    Assert.True(SchemeManager.TryGetScheme(name, out var scheme));
                    var stripe = RowStripes.Stripe(scheme!.GetAttributeForRole(VisualRole.Normal)).Background;
                    foreach (var role in new[] { VisualRole.Normal, VisualRole.Focus, VisualRole.Active })
                        Assert.True(scheme.GetAttributeForRole(role).Background != stripe, $"{theme} {name} {role}");
                }
            }
        }
        finally
        {
            ThemeManager.Theme = "Default";
            ConfigurationManager.Disable(resetToHardCodedDefaults: true);
        }
    }
}

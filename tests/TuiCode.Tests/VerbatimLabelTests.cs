using ButtonKind = MentalDesk.Tui.Dialogs.ButtonKind;
using ConfirmAction = MentalDesk.Tui.Dialogs.ConfirmAction;
using System.Reflection;
using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Workbench;
using TuiCode.Workbench.Controls;
using TuiCode.Workbench.DocumentInfo;
using TuiCode.Workbench.Files;
using TuiCode.Workbench.Git;
using TuiCode.Workbench.Services;
using TuiCode.Workbench.Settings;
using TuiCode.Workbench.Usages;

namespace TuiCode.Tests;

public class VerbatimLabelTests : StaticConfigurationTest
{
    private readonly IApplication _app = Application.Create().Init(DriverRegistry.Names.ANSI);

    public VerbatimLabelTests() => _app.Driver!.SetScreenSize(120, 30);

    public override void Dispose()
    {
        _app.Dispose();
        base.Dispose();
    }

    [Theory]
    [InlineData("_recipes")]
    [InlineData("__init__")]
    [InlineData("my_long_name")]
    public void Blame_shows_every_underscore_in_the_file_line_author_and_subject(string text)
    {
        var blame = new GitBlameLine(
            "4a91c0e2b7d1f3e5a6c8b9d0e1f2a3b4c5d6e7f8", $"{text}_author", DateTimeOffset.UnixEpoch,
            $"Fix {text}", $"var x = {text};", $"{text}.cs", null);
        using var view = new BlameView($"{text}.cs", 2, blame, DateTimeOffset.UnixEpoch);

        var screen = Render(view);

        Assert.Contains($"{text}.cs:2", screen);
        Assert.Contains($"{text}_author", screen);
        Assert.Contains($"Fix {text}", screen);
        Assert.Contains($"var x = {text};", screen);
        AssertNoLabelHotKeys(view);
    }

    [Fact]
    public void Document_info_shows_every_underscore_in_the_path()
    {
        using var view = new DocumentInfoView("/work/_my_notes.md", "Markdown", new DocumentStats(1, 1, 1, 1), null);

        Assert.Contains("/work/_my_notes.md", Render(view));
        AssertNoLabelHotKeys(view);
    }

    [Fact]
    public void A_confirm_prompt_names_the_file_with_every_underscore()
    {
        using var host = new View { App = _app, Width = 120, Height = 30, CanFocus = true };
        host.BeginInit();
        host.EndInit();
        HostedConfirm.Show(host, new InputScopeStack(), "Delete", "Delete _test_data.json?",
            [new ConfirmAction("Delete", ButtonKind.Danger, HostedConfirm.MainKey)], _ => { });

        Assert.Contains("Delete _test_data.json?", Render(host));
        AssertNoLabelHotKeys(host);
    }

    [Fact]
    public void Find_usages_names_the_symbol_with_every_underscore()
    {
        using var view = new UsagesView { Width = 60, Height = 10 };
        view.ShowUsages("__cache_", []);

        Assert.Contains("No usages of __cache_", Render(view));
        AssertNoLabelHotKeys(view);
    }

    [Fact]
    public void The_keybinding_conflict_names_the_command_with_every_underscore()
    {
        using var host = new View { App = _app, Width = 120, Height = 30, CanFocus = true };
        host.BeginInit();
        host.EndInit();
        KeybindingRefusal.Show(host, new InputScopeStack(), "Ctrl+X runs _my_command_.", () => { });

        Assert.Contains("Ctrl+X runs _my_command_.", Render(host));
        AssertNoLabelHotKeys(host);
    }

    [Fact]
    public void An_alert_shows_every_underscore()
    {
        using var view = new AlertView(60);
        view.Show("Couldn't read _a_b.txt", AlertSeverity.Error);

        Assert.Contains("Couldn't read _a_b.txt", Render(view));
        Assert.Equal(Key.Empty, view.HotKey);
    }

    [Fact]
    public void Buttons_keep_their_hotkeys()
    {
        var button = new Button { Text = "_Delete" };

        Assert.Equal(Key.D, button.HotKey);
    }

    [Fact]
    public void No_view_creates_a_label_that_takes_underscore_as_a_hotkey()
    {
        var offenders = new[] { typeof(WorkbenchHost).Assembly, typeof(TuiCode.Search.SearchView).Assembly, typeof(PaneTabs).Assembly }
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type => type.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Cast<MethodBase>()
                .Concat(type.GetConstructors(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                .Where(method => ConstructsLabel(method))
                .Select(method => $"{type.FullName}.{method.Name}"))
            .ToList();

        Assert.True(offenders.Count == 0, $"Use VerbatimLabel, not Label, in: {string.Join(", ", offenders)}");
    }

    private static bool ConstructsLabel(MethodBase method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null) return false;
        const byte newobj = 0x73;
        for (var i = 0; i + 4 < il.Length; i++)
        {
            if (il[i] != newobj) continue;
            try
            {
                var target = method.Module.ResolveMethod(BitConverter.ToInt32(il, i + 1),
                    method.DeclaringType?.IsGenericType == true ? method.DeclaringType.GetGenericArguments() : null,
                    method.IsGenericMethod ? method.GetGenericArguments() : null);
                if (target?.DeclaringType == typeof(Label)) return true;
            }
            catch (ArgumentException)
            {
            }
        }
        return false;
    }

    private static void AssertNoLabelHotKeys(View root)
    {
        foreach (var label in Descendants(root).Where(v => v is Label || v is AlertView))
        {
            Assert.Equal(Key.Empty, label.HotKey);
        }
    }

    private static IEnumerable<View> Descendants(View view) =>
        view.SubViews.SelectMany(sub => Descendants(sub).Prepend(sub));

    private string Render(View view)
    {
        view.App ??= _app;
        if (!view.IsInitialized)
        {
            view.BeginInit();
            view.EndInit();
        }
        view.Layout(new System.Drawing.Size(120, 30));
        var driver = _app.Driver!;
        driver.ClearContents();
        driver.Clip = new Region(driver.Screen);
        view.SetNeedsDraw();
        view.Draw();

        var contents = driver.Contents!;
        var sb = new StringBuilder();
        for (var row = 0; row < contents.GetLength(0); row++)
        {
            for (var col = 0; col < contents.GetLength(1); col++) sb.Append(contents[row, col].Grapheme);
            sb.Append('\n');
        }
        return sb.ToString();
    }
}

using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using TuiCode.Abstractions;
using TuiCode.Editor;

namespace TuiCode.Tests;

public class TabTitleTests : StaticConfigurationTest
{
    private readonly IApplication _app = Application.Create().Init(DriverRegistry.Names.ANSI);
    private readonly MockFileSystem _fs = new();

    public TabTitleTests() => _app.Driver!.SetScreenSize(120, 8);

    public override void Dispose()
    {
        _app.Dispose();
        base.Dispose();
    }

    [Theory]
    [InlineData("COMMIT_EDITMSG")]
    [InlineData("__init__.py")]
    [InlineData("my_long_script.sh")]
    public void An_editor_tab_shows_every_underscore_in_its_name(string name)
    {
        using var group = Group();
        var tab = Open(group, name);

        Render(group);

        Assert.Contains(name, Header());
        Assert.Equal(Key.Empty, HeaderOf(tab).HotKey);
    }

    [Fact]
    public void A_dirty_tab_shows_the_underscores_after_its_mark()
    {
        using var group = Group();
        var tab = Open(group, "a_b.cs");

        tab.Content = "changed\n";
        Render(group);

        Assert.Contains("● a_b.cs", Header());
    }

    [Fact]
    public void A_diff_tab_shows_the_underscores_in_its_title()
    {
        using var group = Group();
        var source = Open(group, "a_b.cs");
        var diff = group.Compare(source, "origin/main", () => ["other"])!;

        Render(group);

        Assert.Contains("a_b.cs ↔ origin/main", Header());
        Assert.Equal(Key.Empty, HeaderOf(diff).HotKey);
    }

    [Fact]
    public void A_document_tab_shows_the_underscores_in_its_title()
    {
        using var group = Group();
        var document = group.OpenDocument(_fs.FileInfo.New("/work/#1 Overview"), "text", "_my_notes_");

        Render(group);

        Assert.Contains("_my_notes_", Header());
        Assert.Equal(Key.Empty, HeaderOf(document).HotKey);
    }

    [Fact]
    public void Renaming_a_file_to_a_name_with_underscores_retitles_its_tab()
    {
        using var group = Group();
        var tab = Open(group, "plain.cs");
        Render(group);

        _fs.File.Move("/work/plain.cs", "/work/with_under_scores.cs");
        group.Relocate(_fs.Path.GetFullPath("/work/plain.cs"), _fs.Path.GetFullPath("/work/with_under_scores.cs"));
        Render(group);

        Assert.Contains("with_under_scores.cs", Header());
        Assert.Equal(Key.Empty, HeaderOf(tab).HotKey);
    }

    [Fact]
    public void Saving_a_dirty_tab_leaves_nothing_after_its_header()
    {
        using var group = Group();
        var tab = Open(group, "LICENSE");
        tab.Content = "changed\n";
        Render(group);

        tab.Save();
        Redraw(group);

        Assert.Equal("", TextAfter("│LICENSE│"));
    }

    [Fact]
    public void Shortening_a_tab_that_isnt_last_redraws_the_tabs_after_it()
    {
        using var group = Group();
        var first = Open(group, "long_name.cs");
        Open(group, "next.cs");
        first.Content = "changed\n";
        Render(group);

        first.Save();
        Redraw(group);

        Assert.Equal("", TextAfter("│long_name.cs│next.cs│"));
    }

    [Fact]
    public void Clearing_the_disk_marker_leaves_nothing_after_its_header()
    {
        using var group = Group();
        var tab = Open(group, "a.cs");
        tab.MarkOnDisk(DiskState.Changed);
        Render(group);

        tab.MarkOnDisk(DiskState.Unchanged);
        Redraw(group);

        Assert.Equal("", TextAfter("│a.cs│"));
    }

    [Fact]
    public void Renaming_a_file_to_a_shorter_name_leaves_nothing_after_its_header()
    {
        using var group = Group();
        Open(group, "a_much_longer_name.cs");
        Render(group);

        Rename(group, "a_much_longer_name.cs", "b.cs");
        Redraw(group);

        Assert.Equal("", TextAfter("│b.cs│"));
    }

    [Fact]
    public void A_diff_tab_whose_title_shortens_leaves_nothing_after_its_header()
    {
        using var group = Group();
        var source = Open(group, "a_much_longer_name.cs");
        group.Compare(source, "origin/main", () => ["other"]);
        Render(group);

        Rename(group, "a_much_longer_name.cs", "b.cs");
        Redraw(group);

        Assert.Equal("", TextAfter("b.cs ↔ origin/main│"));
    }

    private EditorGroup Group() => new() { App = _app, Width = Dim.Fill(), Height = Dim.Fill() };

    private EditorTab Open(EditorGroup group, string name)
    {
        _fs.AddFile($"/work/{name}", new MockFileData("alpha\n"));
        return group.OpenOrFocus(_fs.FileInfo.New($"/work/{name}"));
    }

    private void Rename(EditorGroup group, string from, string to)
    {
        _fs.File.Move($"/work/{from}", $"/work/{to}");
        group.Relocate(_fs.Path.GetFullPath($"/work/{from}"), _fs.Path.GetFullPath($"/work/{to}"));
    }

    private static View HeaderOf(View tab) => ((BorderView)tab.Border.View!).TitleView!;

    private string Header()
    {
        var sb = new StringBuilder();
        var contents = _app.Driver!.Contents!;
        for (var col = 0; col < contents.GetLength(1); col++) sb.Append(contents[1, col].Grapheme);
        return sb.ToString();
    }

    private string TextAfter(string headers)
    {
        var header = Header();
        var end = header.IndexOf(headers, StringComparison.Ordinal);
        Assert.True(end >= 0, $"'{headers}' not in '{header}'");
        return header[(end + headers.Length)..].Trim();
    }

    private void Redraw(View view)
    {
        view.Layout();
        _app.Driver!.Clip = new Region(_app.Driver.Screen);
        view.Draw();
    }

    private void Render(View view)
    {
        view.Layout();
        var driver = _app.Driver!;
        driver.ClearContents();
        driver.Clip = new Region(driver.Screen);
        view.SetNeedsDraw();
        view.Draw();
    }
}

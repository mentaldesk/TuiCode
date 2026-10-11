using TuiCode.Editor;
using TuiCode.Syntax;

namespace TuiCode.Workbench.Parts;

public sealed class EditorPart : View
{
    public EditorGroups Groups { get; }

    /// <summary>The focused group: the one whose active tab the editor commands act on.</summary>
    public EditorGroup Group => Groups.Focused;

    /// <summary>Each group's bordered frame, in the order of <see cref="EditorGroups.All"/>.</summary>
    public IReadOnlyList<FrameView> Frames { get; }

    public event EventHandler<IFileInfo>? FileSaved
    {
        add => Groups.FileSaved += value;
        remove => Groups.FileSaved -= value;
    }

    public IFileInfo? CurrentFile => Group.ActiveTab?.File;
    public bool IsDirty => Group.ActiveTab?.IsDirty ?? false;

    public string? Content
    {
        get => Group.ActiveTab?.Content;
        set
        {
            if (Group.ActiveTab is { } tab && value is not null)
                tab.Content = value;
        }
    }

    public EditorPart(SyntaxHighlighter? syntax = null)
    {
        CanFocus = true;
        Groups = new EditorGroups(new EditorGroup(syntax), new EditorGroup(syntax));
        Frames = [.. Groups.All.Select(Frame)];
        Frames[0].Title = "Editor";
        Frames[1].Visible = false;
        Add([.. Frames]);
        Layout(split: false);
        Groups.SplitChanged += (_, _) => Layout(Groups.IsSplit);
    }

    private static FrameView Frame(EditorGroup group)
    {
        var frame = new FrameView { BorderStyle = LineStyle.Single, Y = 0, Height = Dim.Fill(), CanFocus = true };
        group.X = 0;
        group.Y = 0;
        group.Width = Dim.Fill();
        group.Height = Dim.Fill();
        frame.Add(group);
        return frame;
    }

    private void Layout(bool split)
    {
        Frames[0].X = 0;
        Frames[0].Width = split ? Dim.Percent(50) : Dim.Fill();
        Frames[1].X = Pos.Right(Frames[0]);
        Frames[1].Width = Dim.Fill();
        Frames[1].Visible = split;
        SetNeedsLayout();
    }

    /// <summary>The group <paramref name="view"/> sits in, if any.</summary>
    public EditorGroup? GroupOf(View? view)
    {
        for (var i = 0; i < Frames.Count; i++)
            if (view is not null && IsInHierarchy(Frames[i], view, includeAdornments: true))
                return Groups.All[i];
        return null;
    }

    public EditorTab Open(IFileInfo file) => Groups.Open(file);
    public void Save() => Group.SaveActive();
    public void CloseActive() => Group.CloseActive();
    public void NextTab() => Group.NextTab();
    public void PreviousTab() => Group.PreviousTab();
}

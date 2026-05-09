using TuiCode.Editor;

namespace TuiCode.Workbench.Parts;

public sealed class EditorPart : FrameView
{
    public EditorGroup Group { get; }

    public event EventHandler<IFileInfo>? FileSaved
    {
        add => Group.FileSaved += value;
        remove => Group.FileSaved -= value;
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

    public EditorPart()
    {
        Title = "Editor";
        BorderStyle = LineStyle.Single;
        SchemeName = Services.SchemeNames.Editor;

        Group = new EditorGroup
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            SchemeName = Services.SchemeNames.Tabs
        };
        Add(Group);
    }

    public EditorTab Open(IFileInfo file) => Group.OpenOrFocus(file);
    public void Save() => Group.SaveActive();
    public void CloseActive() => Group.CloseActive();
    public void NextTab() => Group.NextTab();
    public void PreviousTab() => Group.PreviousTab();
}

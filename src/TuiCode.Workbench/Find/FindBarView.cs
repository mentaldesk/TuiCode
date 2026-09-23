namespace TuiCode.Workbench.Find;

/// <summary>
/// The in-editor find/replace strip (#33), docked by <see cref="FindController"/> at the top of the
/// active editor tab. Purely presentational — the controller owns the matching and key handling.
/// </summary>
public sealed class FindBarView : View
{
    private const int LabelWidth = 9;
    // Right-hand column for "12 of 345"; the input fields take the rest.
    private const int SideWidth = 14;

    private readonly Label _queryLabel;
    private readonly TextField _query;
    private readonly Label _replaceLabel;
    private readonly TextField _replacement;
    private readonly Label _status;

    public event EventHandler? QueryChanged;

    /// <summary>Raised when focus moves into or out of either input, so key hints can follow the field.</summary>
    public event EventHandler? FieldFocusChanged;

    public FindBarView()
    {
        CanFocus = true;
        Height = 2;
        BorderStyle = LineStyle.Single;
        Border.Thickness = new Thickness(0, 0, 0, 1);

        _queryLabel = new Label { X = 1, Y = 0, Text = "Find" };
        _query = new TextField { X = LabelWidth, Y = 0, Width = Dim.Fill(SideWidth + 1) };
        _status = new Label { X = Pos.AnchorEnd(SideWidth), Y = 0, Width = SideWidth, Text = string.Empty };
        _replaceLabel = new Label { X = 1, Y = 1, Text = "Replace", Visible = false };
        _replacement = new TextField { X = LabelWidth, Y = 1, Width = Dim.Fill(SideWidth + 1), Visible = false };

        Add(_queryLabel, _query, _status, _replaceLabel, _replacement);

        NameFocusedField(_queryLabel, _query);
        NameFocusedField(_replaceLabel, _replacement);

        _query.TextChanged += (_, _) => QueryChanged?.Invoke(this, EventArgs.Empty);
        _query.HasFocusChanged += (_, _) => OnFieldFocusChanged();
        _replacement.HasFocusChanged += (_, _) => OnFieldFocusChanged();
        HasFocusChanged += (_, _) => SetNeedsDraw();
    }

    /// <summary>Whether that input's label is drawn as the one holding the keys.</summary>
    internal bool MarksAsFocused(bool replace) =>
        (replace ? _replaceLabel : _queryLabel).GetAttributeForRole(VisualRole.Normal) == GetAttributeForRole(VisualRole.Focus);

    public string Query
    {
        get => _query.Text ?? string.Empty;
        set => _query.Text = value;
    }

    public string Replacement
    {
        get => _replacement.Text ?? string.Empty;
        set => _replacement.Text = value;
    }

    public string Status
    {
        get => _status.Text;
        set => _status.Text = value;
    }

    public bool ReplaceVisible => _replacement.Visible;
    public bool ReplacementHasFocus => _replacement.HasFocus;

    public void ShowReplace(bool visible)
    {
        _replaceLabel.Visible = visible;
        _replacement.Visible = visible;
        // Content rows plus the bottom rule.
        Height = visible ? 3 : 2;
        SetNeedsLayout();
    }

    public bool FocusQuery()
    {
        var focused = _query.SetFocus();
        _query.SelectAll();
        return focused;
    }

    public bool FocusReplacement()
    {
        var focused = _replacement.SetFocus();
        _replacement.SelectAll();
        return focused;
    }

    // The terminal's cursor is the only other sign of which field the keys go to, and a theme or a terminal
    // profile can hide it, so name the focused one in the theme's focus colour as well (#229).
    private void NameFocusedField(Label label, View field) =>
        label.GettingAttributeForRole += (_, e) =>
        {
            if (e.Role is not VisualRole.Normal || !Holds(field)) return;
            e.Result = GetAttributeForRole(VisualRole.Focus);
            e.Handled = true;
        };

    // TG leaves HasFocus set on a view the keys have moved on from, so ask the app which one actually holds them.
    private bool Holds(View field) =>
        App?.Navigation?.GetFocused() is { } focused && ReferenceEquals(focused.MostFocused ?? focused, field);

    private void OnFieldFocusChanged()
    {
        SetNeedsDraw();
        FieldFocusChanged?.Invoke(this, EventArgs.Empty);
    }
}

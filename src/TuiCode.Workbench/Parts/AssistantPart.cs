using TuiCode.Workbench.Assistant;

namespace TuiCode.Workbench.Parts;

/// <summary>The AI assistance sidebar on the right of the window (#619).</summary>
public sealed class AssistantPart : FrameView
{
    public AssistantView Assistant { get; }

    public AssistantPart(AssistantView? assistant = null)
    {
        Assistant = assistant ?? new AssistantView(new Conversation());
        Assistant.Width = Dim.Fill();
        Assistant.Height = Dim.Fill();
        Title = AssistantView.TabTitle;
        BorderStyle = LineStyle.Single;
        CanFocus = true;
        Add(Assistant);
    }
}

using TuiCode.Editor;

namespace TuiCode.Tests;

// The list behind Undo close tab (#578).
public sealed class ClosedTabsTests
{
    private readonly MockFileSystem _fs = new();
    private readonly ClosedTabs _closed = new();

    [Fact]
    public void Take_gives_the_newest_first()
    {
        _closed.Push(Closed("a.txt"));
        _closed.Push(Closed("b.txt"));

        Assert.Equal("b.txt", _closed.Take(_ => true)!.File.Name);
        Assert.Equal("a.txt", _closed.Take(_ => true)!.File.Name);
        Assert.Null(_closed.Take(_ => true));
    }

    [Fact]
    public void Push_forgets_the_oldest_past_twenty()
    {
        for (var i = 0; i <= ClosedTabs.Max; i++)
            _closed.Push(Closed($"{i}.txt"));

        Assert.Equal(ClosedTabs.Max, _closed.Count);
        Assert.False(_closed.Any(f => f.Name == "0.txt"));
        Assert.True(_closed.Any(f => f.Name == "1.txt"));
    }

    [Fact]
    public void Take_skips_and_drops_tabs_that_cant_come_back()
    {
        _closed.Push(Closed("a.txt"));
        _closed.Push(Closed("open.txt"));
        _closed.Push(Closed("deleted.txt"));

        Assert.Equal("a.txt", _closed.Take(f => f.Name == "a.txt")!.File.Name);
        Assert.Equal(0, _closed.Count);
    }

    [Fact]
    public void Any_says_whether_one_can_come_back()
    {
        Assert.False(_closed.Any(_ => true));
        _closed.Push(Closed("a.txt"));

        Assert.True(_closed.Any(_ => true));
        Assert.False(_closed.Any(_ => false));
    }

    private ClosedTab Closed(string name)
    {
        _fs.AddFile($"/work/{name}", new MockFileData("one\n"));
        using var tab = new EditorTab(_fs.FileInfo.New($"/work/{name}"));
        return new ClosedTab(tab.File, 0, 0, tab.ViewPosition, tab.History);
    }
}

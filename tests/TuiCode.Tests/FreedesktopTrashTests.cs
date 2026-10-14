using System.Runtime.Versioning;
using TuiCode.Workbench.Files;

namespace TuiCode.Tests;

[UnsupportedOSPlatform("windows")]
public class FreedesktopTrashTests
{
    private const uint Uid = 1000;
    private readonly MockFileSystem _fs = new();
    private readonly FakeEnvironment _environment = new FakeEnvironment().Set("HOME", "/home/me");

    public FreedesktopTrashTests()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The freedesktop.org Trash is Unix-only.");
    }

    [Fact]
    public void Move_puts_the_file_in_the_home_trash_with_a_trashinfo()
    {
        _fs.AddFile("/home/me/work/my notes.md", new MockFileData("notes"));

        Trash().Move("/home/me/work/my notes.md");

        Assert.False(_fs.File.Exists("/home/me/work/my notes.md"));
        Assert.Equal("notes", _fs.File.ReadAllText("/home/me/.local/share/Trash/files/my notes.md"));
        Assert.Equal(
            "[Trash Info]\nPath=/home/me/work/my%20notes.md\nDeletionDate=2026-10-13T10:42:49\n",
            _fs.File.ReadAllText("/home/me/.local/share/Trash/info/my notes.md.trashinfo"));
    }

    [Fact]
    public void Move_uses_XDG_DATA_HOME_when_it_is_set()
    {
        _environment.Set("XDG_DATA_HOME", "/home/me/data");
        _fs.AddFile("/home/me/a.txt", new MockFileData("a"));

        Trash().Move("/home/me/a.txt");

        Assert.True(_fs.File.Exists("/home/me/data/Trash/files/a.txt"));
        Assert.True(_fs.File.Exists("/home/me/data/Trash/info/a.txt.trashinfo"));
    }

    [Fact]
    public void Move_takes_a_folder_and_everything_in_it()
    {
        _fs.AddFile("/home/me/work/fixtures/one.json", new MockFileData("{}"));

        Trash().Move("/home/me/work/fixtures");

        Assert.False(_fs.Directory.Exists("/home/me/work/fixtures"));
        Assert.True(_fs.File.Exists("/home/me/.local/share/Trash/files/fixtures/one.json"));
        Assert.Contains("Path=/home/me/work/fixtures\n", _fs.File.ReadAllText("/home/me/.local/share/Trash/info/fixtures.trashinfo"));
    }

    [Fact]
    public void Move_picks_another_name_when_the_trash_already_has_one()
    {
        _fs.AddFile("/home/me/.local/share/Trash/files/notes.md", new MockFileData("older"));
        _fs.AddFile("/home/me/.local/share/Trash/info/notes.md.trashinfo", new MockFileData(""));
        _fs.AddFile("/home/me/.local/share/Trash/info/notes.2.md.trashinfo", new MockFileData(""));
        _fs.AddFile("/home/me/notes.md", new MockFileData("newer"));

        Trash().Move("/home/me/notes.md");

        Assert.Equal("older", _fs.File.ReadAllText("/home/me/.local/share/Trash/files/notes.md"));
        Assert.Equal("newer", _fs.File.ReadAllText("/home/me/.local/share/Trash/files/notes.3.md"));
        Assert.Contains("Path=/home/me/notes.md\n", _fs.File.ReadAllText("/home/me/.local/share/Trash/info/notes.3.md.trashinfo"));
    }

    [Fact]
    public void Move_on_another_mount_uses_its_shared_Trash_when_it_is_sticky()
    {
        Mounts("/dev/sdb1 /mnt/data ext4 rw 0 0");
        _fs.AddDirectory("/mnt/data/.Trash");
        _fs.File.SetUnixFileMode("/mnt/data/.Trash", UnixFileMode.StickyBit | (UnixFileMode)0x1FF);
        _fs.AddFile("/mnt/data/logs/build.log", new MockFileData("log"));

        Trash().Move("/mnt/data/logs/build.log");

        Assert.True(_fs.File.Exists("/mnt/data/.Trash/1000/files/build.log"));
        Assert.Contains("Path=logs/build.log\n", _fs.File.ReadAllText("/mnt/data/.Trash/1000/info/build.log.trashinfo"));
        Assert.False(_fs.Directory.Exists("/home/me/.local/share/Trash"));
    }

    [Fact]
    public void Move_on_another_mount_passes_over_a_shared_Trash_without_the_sticky_bit()
    {
        Mounts("/dev/sdb1 /mnt/data ext4 rw 0 0");
        _fs.AddDirectory("/mnt/data/.Trash");
        _fs.File.SetUnixFileMode("/mnt/data/.Trash", (UnixFileMode)0x1FF);
        _fs.AddFile("/mnt/data/build.log", new MockFileData("log"));

        Trash().Move("/mnt/data/build.log");

        Assert.True(_fs.File.Exists("/mnt/data/.Trash-1000/files/build.log"));
        Assert.False(_fs.Directory.Exists("/mnt/data/.Trash/1000"));
    }

    [Fact]
    public void Move_on_another_mount_passes_over_a_shared_Trash_that_is_a_symlink()
    {
        Mounts("/dev/sdb1 /mnt/data ext4 rw 0 0");
        _fs.AddDirectory("/elsewhere");
        _fs.File.SetUnixFileMode("/elsewhere", UnixFileMode.StickyBit | (UnixFileMode)0x1FF);
        _fs.Directory.CreateSymbolicLink("/mnt/data/.Trash", "/elsewhere");
        _fs.AddFile("/mnt/data/build.log", new MockFileData("log"));

        Trash().Move("/mnt/data/build.log");

        Assert.True(_fs.File.Exists("/mnt/data/.Trash-1000/files/build.log"));
        Assert.False(_fs.Directory.Exists("/elsewhere/1000"));
    }

    [Fact]
    public void Move_reads_a_mount_point_with_a_space_in_it()
    {
        Mounts(@"/dev/disk2 /media/me/USB\040Stick vfat rw 0 0");
        _fs.AddFile("/media/me/USB Stick/photo.jpg", new MockFileData("jpg"));

        Trash().Move("/media/me/USB Stick/photo.jpg");

        Assert.True(_fs.File.Exists("/media/me/USB Stick/.Trash-1000/files/photo.jpg"));
    }

    [Fact]
    public void Move_fails_and_leaves_the_file_when_the_mount_has_no_usable_Trash()
    {
        Mounts("/dev/sdb1 /mnt/data ext4 rw 0 0");
        _fs.AddDirectory("/elsewhere");
        _fs.Directory.CreateSymbolicLink("/mnt/data/.Trash-1000", "/elsewhere");
        _fs.AddFile("/mnt/data/build.log", new MockFileData("log"));

        var error = Assert.Throws<IOException>(() => Trash().Move("/mnt/data/build.log"));

        Assert.Equal("/mnt/data has no Trash folder.", error.Message);
        Assert.True(_fs.File.Exists("/mnt/data/build.log"));
        Assert.Empty(_fs.Directory.GetFiles("/elsewhere", "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void Move_of_a_symlink_trashes_the_link_and_leaves_what_it_points_to()
    {
        _fs.AddFile("/home/me/real.txt", new MockFileData("real"));
        _fs.File.CreateSymbolicLink("/home/me/link.txt", "/home/me/real.txt");

        Trash().Move("/home/me/link.txt");

        Assert.Equal("real", _fs.File.ReadAllText("/home/me/real.txt"));
        Assert.False(_fs.File.Exists("/home/me/link.txt"));
        Assert.Equal("/home/me/real.txt", _fs.FileInfo.New("/home/me/.local/share/Trash/files/link.txt").LinkTarget);
    }

    private void Mounts(string line) =>
        _fs.AddFile("/proc/self/mounts", new MockFileData($"/dev/sda1 / ext4 rw 0 0\n{line}\n"));

    private FreedesktopTrash Trash() => new(_fs, _environment, Uid, () => new DateTime(2026, 10, 13, 10, 42, 49));
}

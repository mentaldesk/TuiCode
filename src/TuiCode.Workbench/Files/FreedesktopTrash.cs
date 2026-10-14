using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using TuiCode.Abstractions;

namespace TuiCode.Workbench.Files;

/// <summary>
/// The <see href="https://specifications.freedesktop.org/trash-spec/latest/">freedesktop.org Trash</see>, as
/// <c>gio trash</c> fills it: the home trash for the home mount, the mount's own <c>.Trash</c> for any other.
/// </summary>
[UnsupportedOSPlatform("windows")]
public sealed class FreedesktopTrash(IFileSystem fs, IEnvironment environment, uint uid, Func<DateTime> now) : ITrash
{
    private const string MountTable = "/proc/self/mounts";
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    public static FreedesktopTrash ForCurrentUser(IFileSystem fs, IEnvironment environment) =>
        new(fs, environment, getuid(), () => DateTime.Now);

    public string Name => "Trash";

    public void Move(string path)
    {
        path = fs.Path.GetFullPath(path);
        var trash = TrashFor(path, out var topdir);
        var files = fs.Path.Combine(trash, "files");
        var info = fs.Path.Combine(trash, "info");
        fs.Directory.CreateDirectory(files);
        fs.Directory.CreateDirectory(info);

        var (name, infoFile) = Reserve(files, info, fs.Path.GetFileName(path), TrashInfo(path, topdir));
        try
        {
            var target = fs.Path.Combine(files, name);
            // Directory.Move renames a symlink to a folder rather than following it.
            if (fs.Directory.Exists(path))
                fs.Directory.Move(path, target);
            else
                fs.File.Move(path, target);
        }
        catch
        {
            fs.File.Delete(infoFile);
            throw;
        }
    }

    private string TrashFor(string path, out string? topdir)
    {
        var home = HomeTrash();
        var mount = MountOf(fs.Path.GetDirectoryName(path) ?? path);
        topdir = null;
        if (mount == MountOf(home)) return home;

        topdir = mount;
        var shared = fs.Path.Combine(mount, ".Trash");
        if (IsSharedTrash(shared))
        {
            try
            {
                return Create(fs.Path.Combine(shared, uid.ToString(CultureInfo.InvariantCulture)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        try
        {
            return Create(fs.Path.Combine(mount, $".Trash-{uid}"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"{mount} has no Trash folder.", ex);
        }
    }

    private bool IsSharedTrash(string path)
    {
        if (!fs.Directory.Exists(path)) return false;
        var info = fs.DirectoryInfo.New(path);
        return info.LinkTarget is null && fs.File.GetUnixFileMode(path).HasFlag(UnixFileMode.StickyBit);
    }

    private string Create(string path)
    {
        var directory = fs.DirectoryInfo.New(path);
        if (!directory.Exists)
        {
            directory.Create();
            directory.UnixFileMode = OwnerOnly;
        }
        else if (directory.LinkTarget is not null)
        {
            throw new IOException($"{path} is a symlink.");
        }
        return path;
    }

    private string HomeTrash()
    {
        var dataHome = environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrEmpty(dataHome) || !fs.Path.IsPathRooted(dataHome))
        {
            var home = environment.GetEnvironmentVariable("HOME") is { Length: > 0 } h
                ? h
                : environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            dataHome = fs.Path.Combine(home, ".local", "share");
        }
        return fs.Path.Combine(dataHome, "Trash");
    }

    private string MountOf(string path)
    {
        var best = "/";
        foreach (var mount in MountPoints())
            if (mount.Length > best.Length && FilePaths.IsSameOrUnder(path, mount))
                best = mount;
        return best;
    }

    private IEnumerable<string> MountPoints()
    {
        string[] lines;
        try
        {
            lines = fs.File.ReadAllLines(MountTable);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
        return lines.Select(line => line.Split(' ')).Where(fields => fields.Length > 1).Select(fields => Unescape(fields[1]));
    }

    // The mount table writes space, tab, newline and backslash as \ooo.
    private static string Unescape(string field)
    {
        var text = new StringBuilder(field.Length);
        for (var i = 0; i < field.Length; i++)
        {
            if (field[i] == '\\' && IsOctal(field, i + 1))
            {
                text.Append((char)Convert.ToInt32(field.Substring(i + 1, 3), 8));
                i += 3;
            }
            else
            {
                text.Append(field[i]);
            }
        }
        return text.ToString();
    }

    private static bool IsOctal(string field, int start) =>
        start + 3 <= field.Length && field.AsSpan(start, 3).IndexOfAnyExceptInRange('0', '7') < 0;

    private string TrashInfo(string path, string? topdir)
    {
        var original = topdir is null ? path : fs.Path.GetRelativePath(topdir, path);
        var encoded = string.Join('/', original.Split('/').Select(Uri.EscapeDataString));
        return $"[Trash Info]\nPath={encoded}\nDeletionDate={now():yyyy-MM-ddTHH:mm:ss}\n";
    }

    // Creating the .trashinfo exclusively is what claims a name, so two trashers never pick the same one.
    private (string Name, string InfoFile) Reserve(string files, string info, string name, string contents)
    {
        var stem = fs.Path.GetFileNameWithoutExtension(name);
        var extension = fs.Path.GetExtension(name);
        if (stem.Length == 0) (stem, extension) = (name, "");

        for (var n = 1; ; n++)
        {
            var candidate = n == 1 ? name : $"{stem}.{n}{extension}";
            var target = fs.Path.Combine(files, candidate);
            if (fs.File.Exists(target) || fs.Directory.Exists(target)) continue;

            var infoFile = fs.Path.Combine(info, candidate + ".trashinfo");
            try
            {
                using var stream = fs.File.Open(infoFile, FileMode.CreateNew, FileAccess.Write);
                stream.Write(Encoding.UTF8.GetBytes(contents));
                return (candidate, infoFile);
            }
            catch (IOException) when (fs.File.Exists(infoFile))
            {
            }
        }
    }

    [DllImport("libc")]
    private static extern uint getuid();
}

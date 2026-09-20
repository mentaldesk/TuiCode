using System.Diagnostics;

namespace TuiCode.Tests;

/// <summary>A throwaway git repo on disk, for the tests that drive a real CLI.</summary>
internal sealed class TempRepo : IDisposable
{
    public TempRepo(bool init)
    {
        Assert.SkipUnless(GitAvailable.Value, "git isn't on PATH");
        Path = Directory.CreateTempSubdirectory("tuicode-git-").FullName;
        if (init)
            Git("init", "-q", "-b", "main");
    }

    public string Path { get; }

    public string File(string relative) => System.IO.Path.Combine(Path, relative);

    public void Write(string relative, string content)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(File(relative))!);
        System.IO.File.WriteAllText(File(relative), content);
    }

    public void Commit(string relative, string content, string message)
    {
        Write(relative, content);
        Git("add", relative);
        Git("-c", "user.name=Test", "-c", "user.email=test@example.com", "-c", "commit.gpgsign=false",
            "commit", "-q", "-m", message);
    }

    public void Git(params string[] arguments)
    {
        var info = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("-C");
        info.ArgumentList.Add(Path);
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
    }

    public void Dispose()
    {
        // Git's object files are read-only, which stops Directory.Delete on Windows.
        foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
            System.IO.File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(Path, recursive: true);
    }

    private static readonly Lazy<bool> GitAvailable = new(() =>
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("git", "--version") { RedirectStandardOutput = true })!;
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    });
}

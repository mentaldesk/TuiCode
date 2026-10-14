using TuiCode.Workbench.Services;

namespace TuiCode.Workbench.Files;

/// <summary>Trashes a scratch file with this platform's trash, so CI runs the native call in the AOT binary.</summary>
public static class TrashSmoke
{
    public static int Run(TextWriter output)
    {
        try
        {
            var file = Path.Combine(Path.GetTempPath(), $"tuicode-trash-smoke-{Guid.NewGuid():N}.txt");
            File.WriteAllText(file, "smoke");
            var trash = SystemTrash.For(new FileSystem(), new SystemEnvironment());
            trash.Move(file);
            if (File.Exists(file))
            {
                output.WriteLine($"FAIL: {file} is still there");
                return 1;
            }
            output.WriteLine($"OK: moved {file} to the {trash.Name}");
            return 0;
        }
        catch (Exception ex)
        {
            output.WriteLine($"FAIL: {ex}");
            return 1;
        }
    }
}

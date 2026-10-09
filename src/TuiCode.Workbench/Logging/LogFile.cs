using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace TuiCode.Workbench.Logging;

/// <summary>
/// Writes Warning and above to <c>~/.tui/logs/TuiCode.log</c>, one file per session with the previous kept as
/// <c>TuiCode.1.log</c> (#473), and counts the warnings written since the log was last opened.
/// </summary>
public sealed class LogFile : ILoggerProvider
{
    public const string FileName = "TuiCode.log";
    public const string PreviousFileName = "TuiCode.1.log";

    private readonly IFileSystem _fs;
    private readonly string _previousPath;
    private readonly Func<DateTimeOffset> _now;
    private readonly Lock _lock = new();
    private bool _started;
    private int _unseen;

    public LogFile(IFileSystem fs, string directory, Func<DateTimeOffset>? now = null)
    {
        _fs = fs;
        Path = fs.Path.Combine(directory, FileName);
        _previousPath = fs.Path.Combine(directory, PreviousFileName);
        _now = now ?? (() => DateTimeOffset.Now);
    }

    public static LogFile ForUser(IFileSystem fs) =>
        new(fs, fs.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tui", "logs"));

    public string Path { get; }

    public IFileInfo File => _fs.FileInfo.New(Path);

    /// <summary><see cref="Path"/> with the home folder as <c>~</c>.</summary>
    public string DisplayPath
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return home.Length > 0 && Path.StartsWith(home, StringComparison.Ordinal) ? "~" + Path[home.Length..] : Path;
        }
    }

    /// <summary>Warnings written since the log was last opened.</summary>
    public int Unseen => Volatile.Read(ref _unseen);

    public void MarkSeen() => Interlocked.Exchange(ref _unseen, 0);

    /// <summary>Start this session's log, keeping the last one as <see cref="PreviousFileName"/>. The first write starts it too.</summary>
    public void Start()
    {
        lock (_lock)
        {
            if (_started) return;
            _started = true;
            try
            {
                _fs.Directory.CreateDirectory(_fs.Path.GetDirectoryName(Path)!);
                if (_fs.File.Exists(Path)) _fs.File.Move(Path, _previousPath, overwrite: true);
                _fs.File.WriteAllText(Path, string.Empty);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    public void Write(LogLevel level, string message, Exception? exception)
    {
        if (level < LogLevel.Warning || level == LogLevel.None) return;
        var line = Format(_now(), level, message, exception);
        Start();
        lock (_lock)
        {
            try
            {
                _fs.File.AppendAllText(Path, line);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
        Interlocked.Increment(ref _unseen);
    }

    internal static string Format(DateTimeOffset time, LogLevel level, string message, Exception? exception)
    {
        var text = new StringBuilder()
            .Append(time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
            .Append(' ')
            .Append(level switch { LogLevel.Warning => "WARN ", LogLevel.Error => "ERROR", _ => "CRIT " })
            .Append(' ')
            .Append(message);
        if (exception is not null) text.Append(": ").Append(exception.Message);
        text.Append('\n');
        foreach (var frame in exception?.StackTrace?.Split('\n') ?? [])
            text.Append("    ").Append(frame.Trim()).Append('\n');
        return text.ToString();
    }

    public ILogger CreateLogger(string categoryName) => new Logger(this);

    public void Dispose()
    {
    }

    private sealed class Logger(LogFile file) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel is >= LogLevel.Warning and < LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) file.Write(logLevel, formatter(state, exception), exception);
        }
    }
}

using TuiCode.Abstractions;

namespace TuiCode.Workbench.TerminalIntegration;

/// <summary>
/// CLI surface for managing terminal integrations from the shell — install / uninstall / list /
/// check. Parses argv up-front so <c>Program.cs</c> can short-circuit before booting the TUI.
/// </summary>
/// <remarks>
/// <para>All four flags accept an optional <c>=id</c> suffix to target a specific integration
/// (e.g. <c>--install-terminal-integration=iterm2</c>). With no id, the operation runs against
/// the first integration whose <see cref="ITerminalIntegration.IsAvailable"/> returns true; if
/// nothing matches, the command fails with a usage error.</para>
/// <para>Exit codes: <c>0</c> success, <c>1</c> failure or stale (for <c>--check</c>),
/// <c>2</c> not installed (for <c>--check</c>). Anything else (no flag matched) returns
/// <c>null</c> so the caller boots the TUI as normal.</para>
/// </remarks>
public sealed class TerminalIntegrationCli
{
    private readonly IReadOnlyList<ITerminalIntegration> _integrations;
    private readonly TextWriter _out;

    public TerminalIntegrationCli(IEnumerable<ITerminalIntegration> integrations, TextWriter @out)
    {
        ArgumentNullException.ThrowIfNull(integrations);
        ArgumentNullException.ThrowIfNull(@out);
        _integrations = integrations.ToArray();
        _out = @out;
    }

    public int? TryHandle(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        foreach (var raw in args)
        {
            var (flag, value) = SplitFlag(raw);
            switch (flag)
            {
                case "--list-terminal-integrations":
                    return List();
                case "--install-terminal-integration":
                    return Install(value);
                case "--uninstall-terminal-integration":
                    return Uninstall(value);
                case "--check-terminal-integration":
                    return Check(value);
            }
        }
        return null;
    }

    private int List()
    {
        if (_integrations.Count == 0)
        {
            _out.WriteLine("No terminal integrations are built into this version of TuiCode.");
            return 0;
        }

        foreach (var i in _integrations)
        {
            var available = i.IsAvailable() ? "available" : "not detected";
            _out.WriteLine($"{i.Id,-12} {i.DisplayName,-20} {available,-14} {i.GetStatus()}");
        }
        return 0;
    }

    private int Install(string? id)
    {
        if (!TryResolve(id, "install", out var integration))
            return 1;

        integration.Install();
        _out.WriteLine($"Installed {integration.DisplayName} integration.");
        return 0;
    }

    private int Uninstall(string? id)
    {
        if (!TryResolve(id, "uninstall", out var integration))
            return 1;

        integration.Uninstall();
        _out.WriteLine($"Removed {integration.DisplayName} integration.");
        return 0;
    }

    private int Check(string? id)
    {
        if (!TryResolve(id, "check", out var integration))
            return 1;

        var status = integration.GetStatus();
        _out.WriteLine($"{integration.DisplayName}: {status}");
        return status switch
        {
            TerminalIntegrationStatus.Installed => 0,
            TerminalIntegrationStatus.Stale => 1,
            TerminalIntegrationStatus.NotInstalled => 2,
            _ => 1,
        };
    }

    private bool TryResolve(string? id, string action, out ITerminalIntegration integration)
    {
        if (!string.IsNullOrEmpty(id))
        {
            var match = _integrations.FirstOrDefault(
                i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                _out.WriteLine($"Unknown terminal integration '{id}'. Available ids: " +
                    $"{string.Join(", ", _integrations.Select(i => i.Id))}.");
                integration = null!;
                return false;
            }
            integration = match;
            return true;
        }

        var detected = _integrations.FirstOrDefault(i => i.IsAvailable());
        if (detected is null)
        {
            _out.WriteLine($"Could not detect a supported terminal to {action}. " +
                $"Re-run with --{action}-terminal-integration=<id> to target one explicitly.");
            integration = null!;
            return false;
        }
        integration = detected;
        return true;
    }

    private static (string flag, string? value) SplitFlag(string arg)
    {
        var eq = arg.IndexOf('=');
        return eq < 0 ? (arg, null) : (arg[..eq], arg[(eq + 1)..]);
    }
}

using System.IO.Pipelines;
using System.Text.Json.Nodes;

namespace TuiCode.Workbench.Languages;

/// <summary>A language server in this process, joined to the client by in-memory pipes: the client's end.</summary>
public sealed class InProcessServer : ILanguageServerProcess
{
    private readonly Pipe _toServer = new();
    private readonly Pipe _toClient = new();
    private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public InProcessServer()
    {
        Connection = new JsonRpcConnection(_toServer.Reader.AsStream(), _toClient.Writer.AsStream());
    }

    /// <summary>The server's end.</summary>
    public JsonRpcConnection Connection { get; }

    public Stream Output => _toClient.Reader.AsStream();
    public Stream Input => _toServer.Writer.AsStream();
    public Task Exited => _exited.Task;
    public bool Killed { get; private set; }

    /// <summary>Ends the server as if its process exited, closing its output.</summary>
    public void Exit()
    {
        _toClient.Writer.Complete();
        _exited.TrySetResult();
    }

    public void Kill()
    {
        Killed = true;
        Exit();
    }

    public void Dispose() => Connection.Dispose();
}

/// <summary><c>--smoke-language-server</c>: CI runs the client against a fake server on each RID's AOT binary, where trimming could break it.</summary>
public static class LanguageServerSmoke
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public static int Run(TextWriter output)
    {
        try
        {
            var root = Path.Combine(Path.GetTempPath(), "tuicode-smoke");
            var file = Path.Combine(root, "Widget.cs");
            var fake = new InProcessServer();
            var exited = new TaskCompletionSource();
            fake.Connection.RequestHandler = (method, parameters) => method switch
            {
                "initialize" => new JsonObject { ["capabilities"] = new JsonObject { ["definitionProvider"] = true } },
                "textDocument/definition" => new JsonArray(new JsonObject
                {
                    ["uri"] = parameters!["textDocument"]!["uri"]!.DeepClone(),
                    ["range"] = new JsonObject { ["start"] = parameters["position"]!.DeepClone(), ["end"] = parameters["position"]!.DeepClone() },
                }),
                _ => null,
            };
            fake.Connection.NotificationReceived += (method, _) =>
            {
                if (method == "initialized")
                {
                    fake.Connection.Notify("$/progress", new JsonObject { ["token"] = 1, ["value"] = new JsonObject { ["kind"] = "begin" } });
                    fake.Connection.Notify("$/progress", new JsonObject { ["token"] = 1, ["value"] = new JsonObject { ["kind"] = "end" } });
                }
                if (method != "exit") return;
                exited.TrySetResult();
                fake.Exit();
            };
            fake.Connection.Start();

            using var ready = new ManualResetEventSlim();
            var server = new LanguageServer(LanguageServerSpec.CSharp, root, new Launcher(fake), action => action())
            {
                QuietLoad = Timeout,
            };
            server.StateChanged += (_, _) =>
            {
                if (server.State == LanguageServerState.Ready) ready.Set();
            };
            server.Start();
            server.Open(file, "class Widget { }");
            if (!ready.Wait(Timeout)) throw new InvalidOperationException($"server never became ready ({server.State})");

            var found = server.DefinitionAsync(file, 0, 6).WaitAsync(Timeout).GetAwaiter().GetResult();
            if (found is not [{ Line: 0, Character: 6 } location] || location.Path != file)
                throw new InvalidOperationException($"unexpected definition: {string.Join(", ", found)}");

            server.StopAsync().WaitAsync(Timeout).GetAwaiter().GetResult();
            exited.Task.WaitAsync(Timeout).GetAwaiter().GetResult();
            output.WriteLine("OK language server");
            return 0;
        }
        catch (Exception e)
        {
            output.WriteLine($"FAIL language server: {e}");
            return 1;
        }
    }

    private sealed class Launcher(InProcessServer server) : ILanguageServerLauncher
    {
        public ILanguageServerProcess Launch(string command, IReadOnlyList<string> arguments, string directory) => server;
    }
}

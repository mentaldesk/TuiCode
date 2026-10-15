using System.Text.Json.Nodes;
using TuiCode.Workbench.Languages;

namespace TuiCode.Workbench.Assistant;

/// <summary><c>--smoke-acp</c>: CI runs the ACP client against a fake agent on each RID's AOT binary, where trimming could break it.</summary>
public static class AcpSmoke
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public static int Run(TextWriter output)
    {
        try
        {
            var root = Path.Combine(Path.GetTempPath(), "tuicode-smoke");
            var fake = new InProcessServer(JsonRpcFraming.Lines);
            fake.Connection.AsyncRequestHandler = async (method, parameters) =>
            {
                switch (method)
                {
                    case "initialize":
                        return new JsonObject { ["protocolVersion"] = 1, ["agentCapabilities"] = new JsonObject { ["loadSession"] = true } };
                    case "session/new":
                        return new JsonObject { ["sessionId"] = "smoke" };
                    case "session/prompt":
                        var read = await fake.Connection.RequestAsync("fs/read_text_file",
                            new JsonObject { ["sessionId"] = "smoke", ["path"] = Path.Combine(root, "Widget.cs") }).ConfigureAwait(false);
                        fake.Connection.Notify("session/update", new JsonObject
                        {
                            ["sessionId"] = "smoke",
                            ["update"] = new JsonObject
                            {
                                ["sessionUpdate"] = "agent_message_chunk",
                                ["content"] = new JsonObject { ["type"] = "text", ["text"] = read?["content"]?.GetValue<string>() },
                            },
                        });
                        return new JsonObject { ["stopReason"] = "end_turn" };
                    default:
                        throw new JsonRpcException(JsonRpcConnection.MethodNotFound, method);
                }
            };
            fake.Connection.Start();

            var requests = new Requests();
            using var client = new AcpClient(AgentPresets.ClaudeCode, root, new Launcher(fake), requests);
            var answer = new System.Text.StringBuilder();
            client.Updated += update =>
            {
                if (update is MessageChunk chunk) lock (answer) answer.Append(chunk.Text);
            };
            client.StartAsync().WaitAsync(Timeout).GetAwaiter().GetResult();
            client.NewSessionAsync().WaitAsync(Timeout).GetAwaiter().GetResult();
            var reason = client.PromptAsync([new JsonObject { ["type"] = "text", ["text"] = "What is this?" }]).WaitAsync(Timeout).GetAwaiter().GetResult();
            if (reason != "end_turn") throw new InvalidOperationException($"unexpected stop reason: {reason}");
            lock (answer)
                if (answer.ToString() != "class Widget { }") throw new InvalidOperationException($"unexpected answer: {answer}");
            output.WriteLine("OK acp");
            return 0;
        }
        catch (Exception e)
        {
            output.WriteLine($"FAIL acp: {e}");
            return 1;
        }
    }

    private sealed class Requests : IAgentRequests
    {
        public Task<string> ReadTextFileAsync(string path, int? line, int? limit) => Task.FromResult("class Widget { }");
        public Task<string?> RequestPermissionAsync(PermissionRequest request) => Task.FromResult<string?>(null);
        public void WriteRefused(string path) { }
    }

    private sealed class Launcher(InProcessServer server) : ILanguageServerLauncher
    {
        public ILanguageServerProcess Launch(string command, IReadOnlyList<string> arguments, string directory) => server;
    }
}

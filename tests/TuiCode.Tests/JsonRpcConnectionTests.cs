using System.Text;
using System.Text.Json.Nodes;
using TuiCode.Workbench.Languages;

namespace TuiCode.Tests;

public class JsonRpcConnectionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task A_message_split_over_many_reads_comes_out_whole()
    {
        var stream = new ChunkedStream(Framed("""{"a":1}""", """{"b":"é"}"""), chunk: 1);
        var reader = new LspMessageReader(stream);

        Assert.Equal("""{"a":1}""", Encoding.UTF8.GetString((await reader.ReadAsync())!));
        Assert.Equal("""{"b":"é"}""", Encoding.UTF8.GetString((await reader.ReadAsync())!));
        Assert.Null(await reader.ReadAsync());
    }

    [Fact]
    public async Task Messages_joined_in_one_read_come_out_one_at_a_time()
    {
        var body = new string('x', 40_000);
        var stream = new ChunkedStream(Framed("1", $"\"{body}\"", "3"), chunk: int.MaxValue);
        var reader = new LspMessageReader(stream);

        Assert.Equal("1", Encoding.UTF8.GetString((await reader.ReadAsync())!));
        Assert.Equal($"\"{body}\"", Encoding.UTF8.GetString((await reader.ReadAsync())!));
        Assert.Equal("3", Encoding.UTF8.GetString((await reader.ReadAsync())!));
    }

    [Fact]
    public async Task Headers_other_than_the_length_are_ignored()
    {
        var bytes = Encoding.ASCII.GetBytes("Content-Type: application/vscode-jsonrpc; charset=utf-8\r\ncontent-length: 2\r\n\r\n{}");
        var reader = new LspMessageReader(new MemoryStream(bytes));

        Assert.Equal("{}", Encoding.UTF8.GetString((await reader.ReadAsync())!));
    }

    [Fact]
    public async Task Answers_reach_their_own_requests_whatever_order_they_come_in()
    {
        using var server = new InProcessServer();
        var asked = new List<JsonNode?>();
        server.Connection.RequestHandler = (method, parameters) =>
        {
            asked.Add(parameters);
            return method == "echo" ? parameters!["value"]!.DeepClone() : throw new JsonRpcException(-32000, "nope");
        };
        server.Connection.Start();
        using var client = Client(server);

        var first = client.RequestAsync("echo", new JsonObject { ["value"] = "one" });
        var second = client.RequestAsync("echo", new JsonObject { ["value"] = 2 });
        var failing = client.RequestAsync("other");

        Assert.Equal(2, (await second.WaitAsync(Timeout))!.GetValue<int>());
        Assert.Equal("one", (await first.WaitAsync(Timeout))!.GetValue<string>());
        var error = await Assert.ThrowsAsync<JsonRpcException>(() => failing.WaitAsync(Timeout));
        Assert.Equal(-32000, error.Code);
        Assert.Equal("nope", error.Message);
    }

    [Fact]
    public async Task Notifications_reach_the_other_side_with_their_parameters()
    {
        using var server = new InProcessServer();
        var received = new TaskCompletionSource<(string, JsonNode?)>();
        server.Connection.NotificationReceived += (method, parameters) => received.TrySetResult((method, parameters));
        server.Connection.Start();
        using var client = Client(server);

        client.Notify("textDocument/didSave", new JsonObject { ["text"] = "x" });

        var (method, parameters) = await received.Task.WaitAsync(Timeout);
        Assert.Equal("textDocument/didSave", method);
        Assert.Equal("x", parameters!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_request_from_the_server_is_answered_and_one_with_no_handler_is_method_not_found()
    {
        using var server = new InProcessServer();
        server.Connection.Start();
        using var client = Client(server);
        client.RequestHandler = (method, _) => method == "workspace/configuration"
            ? new JsonArray(null, null)
            : throw new JsonRpcException(JsonRpcConnection.MethodNotFound, "Method not found");

        var answered = await server.Connection.RequestAsync("workspace/configuration").WaitAsync(Timeout);
        var unknown = await Assert.ThrowsAsync<JsonRpcException>(() => server.Connection.RequestAsync("custom/thing").WaitAsync(Timeout));

        Assert.Equal("[null,null]", answered!.ToJsonString());
        Assert.Equal(JsonRpcConnection.MethodNotFound, unknown.Code);
    }

    [Fact]
    public async Task Requests_still_waiting_fail_when_the_other_side_goes_away()
    {
        using var server = new InProcessServer();
        server.Connection.RequestHandler = (_, _) => { server.Exit(); return null; };
        server.Connection.Start();
        using var client = Client(server);

        var pending = client.RequestAsync("textDocument/definition");

        await Assert.ThrowsAsync<IOException>(() => pending.WaitAsync(Timeout));
        await client.Completion.WaitAsync(Timeout);
        await Assert.ThrowsAsync<IOException>(() => client.RequestAsync("after").WaitAsync(Timeout));
    }

    private static JsonRpcConnection Client(InProcessServer server)
    {
        var client = new JsonRpcConnection(server.Output, server.Input);
        client.Start();
        return client;
    }

    private static byte[] Framed(params string[] bodies) =>
        [.. bodies.SelectMany(body => JsonRpcConnection.Frame(Encoding.UTF8.GetBytes(body)))];

    private sealed class ChunkedStream(byte[] data, int chunk) : MemoryStream(data)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, chunk)], cancellationToken);
    }
}

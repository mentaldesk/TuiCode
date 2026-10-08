using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace TuiCode.Workbench.Languages;

/// <summary>JSON-RPC 2.0 over LSP's <c>Content-Length</c> framing; symmetric, so it's also the fake server in tests and the AOT smoke.</summary>
public sealed class JsonRpcConnection : IDisposable
{
    public const int MethodNotFound = -32601;

    private readonly Stream _input;
    private readonly Stream _output;
    private readonly Channel<byte[]> _outgoing = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonNode?>> _pending = new();
    private readonly CancellationTokenSource _stopping = new();
    private long _nextId;
    private Task? _reading;
    private Task? _writing;

    public JsonRpcConnection(Stream input, Stream output)
    {
        _input = input;
        _output = output;
    }

    /// <summary>A notification from the other side, raised on the reading thread.</summary>
    public event Action<string, JsonNode?>? NotificationReceived;

    /// <summary>Answers the other side's requests on the reading thread; throw <see cref="JsonRpcException"/> for an error.</summary>
    public Func<string, JsonNode?, JsonNode?>? RequestHandler { get; set; }

    /// <summary>Completes when the other side's stream ends or can't be read.</summary>
    public Task Completion => _reading ?? Task.CompletedTask;

    public void Start()
    {
        _reading = Task.Run(ReadLoop);
        _writing = Task.Run(WriteLoop);
    }

    public Task<JsonNode?> RequestAsync(string method, JsonNode? parameters = null)
    {
        var id = Interlocked.Increment(ref _nextId);
        var answer = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = answer;
        if (_reading is { IsCompleted: true } && _pending.TryRemove(id, out _)) Fail(answer);
        var message = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method };
        if (parameters is not null) message["params"] = parameters;
        Send(message);
        return answer.Task;
    }

    public void Notify(string method, JsonNode? parameters = null)
    {
        var message = new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method };
        if (parameters is not null) message["params"] = parameters;
        Send(message);
    }

    private void Send(JsonObject message) => _outgoing.Writer.TryWrite(Encoding.UTF8.GetBytes(message.ToJsonString()));

    public static byte[] Frame(byte[] body) =>
        [.. Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n"), .. body];

    private async Task WriteLoop()
    {
        try
        {
            await foreach (var body in _outgoing.Reader.ReadAllAsync(_stopping.Token))
            {
                await _output.WriteAsync(Frame(body), _stopping.Token);
                await _output.FlushAsync(_stopping.Token);
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException or InvalidOperationException)
        {
        }
    }

    private async Task ReadLoop()
    {
        var reader = new LspMessageReader(_input);
        try
        {
            while (await reader.ReadAsync(_stopping.Token) is { } body)
                Dispatch(body);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException or FormatException)
        {
        }
        finally
        {
            foreach (var id in _pending.Keys)
                if (_pending.TryRemove(id, out var answer)) Fail(answer);
        }
    }

    private static void Fail(TaskCompletionSource<JsonNode?> answer) =>
        answer.TrySetException(new IOException("The connection closed before an answer came."));

    private void Dispatch(byte[] body)
    {
        JsonObject? message;
        try
        {
            message = JsonNode.Parse(body) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return;
        }
        if (message is null) return;

        var method = message["method"]?.GetValue<string>();
        var id = message["id"];
        var parameters = message["params"];
        if (method is null)
        {
            if (id is null || !TryReadId(id, out var number) || !_pending.TryRemove(number, out var answer)) return;
            if (message["error"] is JsonObject error)
                answer.TrySetException(new JsonRpcException(
                    error["code"]?.GetValue<int>() ?? 0, error["message"]?.GetValue<string>() ?? "Unknown error"));
            else
                answer.TrySetResult(message["result"]?.DeepClone());
            return;
        }
        if (id is null)
        {
            NotificationReceived?.Invoke(method, parameters);
            return;
        }
        Answer(id.DeepClone(), method, parameters);
    }

    private void Answer(JsonNode id, string method, JsonNode? parameters)
    {
        var reply = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id };
        try
        {
            if (RequestHandler is not { } handler) throw new JsonRpcException(MethodNotFound, $"Method not found: {method}");
            reply["result"] = handler(method, parameters);
        }
        catch (JsonRpcException e)
        {
            reply["error"] = new JsonObject { ["code"] = e.Code, ["message"] = e.Message };
        }
        Send(reply);
    }

    private static bool TryReadId(JsonNode id, out long number)
    {
        number = 0;
        return id is JsonValue value && value.TryGetValue(out number);
    }

    public void Dispose()
    {
        _outgoing.Writer.TryComplete();
        _stopping.Cancel();
        _stopping.Dispose();
    }
}

public sealed class JsonRpcException(int code, string message) : Exception(message)
{
    public int Code { get; } = code;
}

/// <summary>Reads framed message bodies off a stream, however the reads happen to split or join them.</summary>
internal sealed class LspMessageReader(Stream stream)
{
    private const string LengthHeader = "Content-Length:";

    private byte[] _buffer = new byte[16 * 1024];
    private int _start;
    private int _end;

    /// <summary>The next body, or null at the end of the stream.</summary>
    public async Task<byte[]?> ReadAsync(CancellationToken cancellationToken = default)
    {
        int headerEnd;
        while ((headerEnd = HeaderEnd()) < 0)
            if (!await FillAsync(cancellationToken)) return null;

        var length = ContentLength(Encoding.ASCII.GetString(_buffer, _start, headerEnd - _start));
        _start = headerEnd + 4;
        while (_end - _start < length)
            if (!await FillAsync(cancellationToken)) return null;

        var body = _buffer.AsSpan(_start, length).ToArray();
        _start += length;
        return body;
    }

    private int HeaderEnd()
    {
        var index = _buffer.AsSpan(_start, _end - _start).IndexOf("\r\n\r\n"u8);
        return index < 0 ? -1 : _start + index;
    }

    private static int ContentLength(string headers)
    {
        foreach (var line in headers.Split("\r\n"))
            if (line.StartsWith(LengthHeader, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(line.AsSpan(LengthHeader.Length).Trim(), out var length) && length >= 0)
                return length;
        throw new FormatException($"No Content-Length in \"{headers}\".");
    }

    private async Task<bool> FillAsync(CancellationToken cancellationToken)
    {
        if (_start > 0)
        {
            Buffer.BlockCopy(_buffer, _start, _buffer, 0, _end - _start);
            _end -= _start;
            _start = 0;
        }
        if (_end == _buffer.Length) Array.Resize(ref _buffer, _buffer.Length * 2);
        var read = await stream.ReadAsync(_buffer.AsMemory(_end), cancellationToken);
        _end += read;
        return read > 0;
    }
}

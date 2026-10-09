using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Channels;
using Runesmith.Lsp.Protocol;

namespace Runesmith.Lsp;

/// <summary>A JSON-RPC 2.0 connection over a pair of streams, framed with <c>Content-Length</c> headers as the Language Server Protocol does.</summary>
/// <remarks>Notifications are handled one at a time in the order they arrive; requests from the other side are handled concurrently. Handlers
/// never run on the read loop, so a slow handler does not hold up reading.</remarks>
public sealed class JsonRpcConnection : IAsyncDisposable
{
    private const string CancelRequest = "$/cancelRequest";

    private readonly Stream _input;
    private readonly Stream _output;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement?>> _pending = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (string Method, long Sent)> _sent = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Func<JsonElement?, CancellationToken, Task<object?>>> _requestHandlers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Action<JsonElement?>> _notificationHandlers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _incoming = new(StringComparer.Ordinal);
    // Requests pass through the same queue as notifications, so a request starts only after every notification received before it ran.
    private readonly Channel<Incoming> _notifications =
        Channel.CreateUnbounded<Incoming>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _lifetime = new();
    private long _nextId;
    private Task? _readLoop;
    private Task? _notificationLoop;
    private int _closed;

    public JsonRpcConnection(Stream input, Stream output)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        OnNotification(CancelRequest, OnCancelRequest);
    }

    /// <summary>Gets the options messages are serialized with: the protocol types' generated metadata, then reflection for other types.</summary>
    public static JsonSerializerOptions SerializerOptions { get; } = new(LspJsonContext.Default.Options)
    {
        TypeInfoResolver = JsonTypeInfoResolver.Combine(LspJsonContext.Default, new DefaultJsonTypeInfoResolver())
    };

    /// <summary>Raised for every message sent or received; the JSON is only produced while there are subscribers.</summary>
    public event Action<JsonRpcTrace>? Trace;

    /// <summary>Raised once when the connection closes, with the exception that closed it, or null at the end of the input.</summary>
    public event EventHandler<Exception?>? Closed;

    /// <summary>Starts reading messages on a background task.</summary>
    public void Start()
    {
        if (_readLoop is not null)
            throw new InvalidOperationException("The connection has already started.");
        _notificationLoop = Task.Run(DispatchNotificationsAsync);
        _readLoop = Task.Run(ReadLoopAsync);
    }

    /// <summary>Handles requests with <paramref name="method"/> from the other side; the handler's result is the response.</summary>
    public void OnRequest(string method, Func<JsonElement?, CancellationToken, Task<object?>> handler) => _requestHandlers[method] = handler;

    /// <summary>Handles notifications with <paramref name="method"/>, in the order they arrive.</summary>
    public void OnNotification(string method, Action<JsonElement?> handler) => _notificationHandlers[method] = handler;

    /// <summary>Sends a request and waits for its result; canceling sends <c>$/cancelRequest</c> and cancels the task.</summary>
    /// <exception cref="LspException">The other side answered with an error.</exception>
    /// <exception cref="IOException">The connection closed before the answer came.</exception>
    public async Task<TResult?> SendRequestAsync<TResult>(string method, object? parameters, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_closed != 0, this);
        var id = Interlocked.Increment(ref _nextId);
        var key = id.ToString(CultureInfo.InvariantCulture);
        var completion = new TaskCompletionSource<JsonElement?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[key] = completion;
        _sent[key] = (method, Stopwatch.GetTimestamp());
        await using var registration = cancellationToken.Register(() =>
        {
            _sent.TryRemove(key, out _);
            if (_pending.TryRemove(key, out var pending) && pending.TrySetCanceled(cancellationToken))
                _ = SendNotificationAsync(CancelRequest, new Dictionary<string, long> { ["id"] = id });
        }).ConfigureAwait(false);

        await WriteAsync(method, key, writer =>
        {
            writer.WriteNumber("id", id);
            writer.WriteString("method", method);
            WriteValue(writer, "params", parameters);
        }).ConfigureAwait(false);

        var result = await completion.Task.ConfigureAwait(false);
        if (result is not { } element || element.ValueKind == JsonValueKind.Null)
            return default;
        if (typeof(TResult) == typeof(JsonElement))
            return (TResult)(object)element;
        return (TResult?)element.Deserialize(SerializerOptions.GetTypeInfo(typeof(TResult)));
    }

    /// <summary>Sends a notification.</summary>
    public Task SendNotificationAsync(string method, object? parameters) =>
        _closed != 0
            ? Task.CompletedTask
            : WriteAsync(method, null, writer =>
            {
                writer.WriteString("method", method);
                WriteValue(writer, "params", parameters);
            });

    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync().ConfigureAwait(false);
        Close(null);
        if (_readLoop is not null)
            await _readLoop.ConfigureAwait(false);
        if (_notificationLoop is not null)
            await _notificationLoop.ConfigureAwait(false);
        _lifetime.Dispose();
        _writeLock.Dispose();
    }

    private static void WriteValue(Utf8JsonWriter writer, string name, object? value)
    {
        if (value is null)
            return;
        writer.WritePropertyName(name);
        if (value is JsonElement element)
            element.WriteTo(writer);
        else
            JsonSerializer.Serialize(writer, value, SerializerOptions.GetTypeInfo(value.GetType()));
    }

    private async Task WriteAsync(string? method, string? id, Action<Utf8JsonWriter> writeBody)
    {
        var body = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(body))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            writeBody(writer);
            writer.WriteEndObject();
        }

        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.WrittenCount}\r\n\r\n");
        await _writeLock.WaitAsync(_lifetime.Token).ConfigureAwait(false);
        try
        {
            await _output.WriteAsync(header, _lifetime.Token).ConfigureAwait(false);
            await _output.WriteAsync(body.WrittenMemory, _lifetime.Token).ConfigureAwait(false);
            await _output.FlushAsync(_lifetime.Token).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }

        Trace?.Invoke(new JsonRpcTrace(JsonRpcDirection.Sent, method, id, Encoding.UTF8.GetString(body.WrittenSpan)));
    }

    private async Task ReadLoopAsync()
    {
        var reader = new MessageReader(_input);
        Exception? failure = null;
        try
        {
            while (await reader.ReadAsync(_lifetime.Token).ConfigureAwait(false) is { } message)
                Receive(message);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or JsonException or FormatException)
        {
            failure = ex;
        }

        Close(failure);
    }

    private void Receive(byte[] message)
    {
        using var document = JsonDocument.Parse(message);
        var root = document.RootElement;
        var method = root.TryGetProperty("method", out var methodElement) ? methodElement.GetString() : null;
        var hasId = root.TryGetProperty("id", out var idElement) && idElement.ValueKind != JsonValueKind.Null;
        var id = hasId ? IdText(idElement) : null;
        Trace?.Invoke(new JsonRpcTrace(JsonRpcDirection.Received, method, id, Encoding.UTF8.GetString(message)));
        JsonElement? parameters = root.TryGetProperty("params", out var paramsElement) ? paramsElement.Clone() : null;

        if (method is not null && hasId)
        {
            var requestId = idElement.Clone();
            _notifications.Writer.TryWrite(new Incoming(null, parameters, () => AnswerAsync(method, requestId, id!, parameters)));
        }
        else if (method is not null)
        {
            if (_notificationHandlers.TryGetValue(method, out var handler))
                _notifications.Writer.TryWrite(new Incoming(handler, parameters, null));
        }
        else if (id is not null && _pending.TryRemove(id, out var pending))
        {
            if (_sent.TryRemove(id, out var sent))
            {
                var tag = new KeyValuePair<string, object?>("method", sent.Method);
                LspMetrics.Request.Record(Stopwatch.GetElapsedTime(sent.Sent).TotalMilliseconds, tag);
                LspMetrics.ResponseBytes.Record(message.Length, tag);
            }

            if (root.TryGetProperty("error", out var error))
            {
                var code = error.TryGetProperty("code", out var codeElement) ? codeElement.GetInt32() : LspException.InternalError;
                var text = error.TryGetProperty("message", out var messageElement) ? messageElement.GetString() ?? "" : "";
                JsonElement? data = error.TryGetProperty("data", out var dataElement) ? dataElement.Clone() : null;
                pending.TrySetException(new LspException(code, text, data));
            }
            else
            {
                pending.TrySetResult(root.TryGetProperty("result", out var result) ? result.Clone() : null);
            }
        }
    }

    private async Task AnswerAsync(string method, JsonElement idElement, string id, JsonElement? parameters)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _incoming[id] = cancellation;
        object? result = null;
        LspException? error = null;
        try
        {
            if (_requestHandlers.TryGetValue(method, out var handler))
                result = await handler(parameters, cancellation.Token).ConfigureAwait(false);
            else
                error = new LspException(LspException.MethodNotFound, $"Unhandled method {method}");
        }
        catch (OperationCanceledException)
        {
            error = new LspException(LspException.RequestCancelled, "The request was cancelled.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            error = new LspException(LspException.InternalError, ex.Message);
        }
        finally
        {
            _incoming.TryRemove(id, out _);
        }

        if (_closed != 0)
            return;
        try
        {
            await WriteAsync(null, id, writer =>
            {
                writer.WritePropertyName("id");
                idElement.WriteTo(writer);
                if (error is null)
                {
                    if (result is null)
                        writer.WriteNull("result");
                    else
                        WriteValue(writer, "result", result);
                }
                else
                {
                    writer.WriteStartObject("error");
                    writer.WriteNumber("code", error.Code);
                    writer.WriteString("message", error.Message);
                    writer.WriteEndObject();
                }
            }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
        }
    }

    private void OnCancelRequest(JsonElement? parameters)
    {
        if (parameters is { } element && element.TryGetProperty("id", out var id) && _incoming.TryGetValue(IdText(id), out var cancellation))
            cancellation.Cancel();
    }

    private async Task DispatchNotificationsAsync()
    {
        await foreach (var (handler, parameters, request) in _notifications.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (request is not null)
            {
                _ = Task.Run(request);
                continue;
            }

            try
            {
                handler!(parameters);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Trace?.Invoke(new JsonRpcTrace(JsonRpcDirection.Received, null, null, $"A notification handler failed: {ex.Message}"));
            }
        }
    }

    private void Close(Exception? failure)
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
            return;
        _notifications.Writer.TryComplete();
        foreach (var key in _pending.Keys)
        {
            if (_pending.TryRemove(key, out var pending))
                pending.TrySetException(new IOException("The connection closed before the answer came.", failure));
        }

        Closed?.Invoke(this, failure);
    }

    private static string IdText(JsonElement id) => id.ValueKind == JsonValueKind.Number ? id.GetRawText() : id.GetString() ?? "";

    private readonly record struct Incoming(Action<JsonElement?>? Handler, JsonElement? Parameters, Func<Task>? Request);
}

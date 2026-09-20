using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace Gitland.Core;

public sealed record HoswlItem(string? Id = null, string? Label = null, string? Key = null, bool? Enabled = null, bool? Check = null, bool? Sep = null, IReadOnlyList<HoswlItem>? Items = null);

/// <summary>Hisashi v1 client. Pipe I/O and reconnects never run on the UI thread.</summary>
public sealed class HoswlClient : IAsyncDisposable {
    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    readonly string _pipeName, _version;
    readonly CancellationTokenSource _stop = new();
    readonly Channel<bool> _updates = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
    readonly object _gate = new();
    IReadOnlyList<HoswlItem> _menus = [];
    bool _enabled;
    int _disposed;
    Task? _loop;
    public bool Connected { get; private set; }
    public event Action<string>? Clicked;
    public event Action<bool>? ConnectionChanged;
    public HoswlClient(string version, string pipeName = "hoswl") { _version = version; _pipeName = pipeName; }
    public void Update(IReadOnlyList<HoswlItem> menus, bool enabled) {
        lock (_gate) {
            if (_disposed != 0) return;
            _menus = menus; _enabled = enabled;
            // An untouched, disabled integration never attempts to connect.
            if (_loop == null && enabled) _loop = Task.Run(Run);
        }
        _updates.Writer.TryWrite(true);
    }
    async Task Run() {
        var token = _stop.Token;
        while (!token.IsCancellationRequested) {
            try {
                using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(1000, token);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { NewLine = "\n", AutoFlush = true };
                await Send(writer, new { t = "hello", v = 1, app = "com.fezcode.gitland", name = "Gitland", ver = _version, pid = Environment.ProcessId }, token);
                using var session = CancellationTokenSource.CreateLinkedTokenSource(token);
                Task write = WriteUpdates(writer, session.Token), read = ReadClicks(pipe, session.Token);
                await Task.WhenAny(write, read);
                await session.CancelAsync();
                try { await Task.WhenAll(write, read); } catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
            } catch (Exception e) when (e is IOException or TimeoutException or OperationCanceledException or UnauthorizedAccessException or JsonException or InvalidOperationException) { }
            SetConnected(false);
            try { await Task.Delay(2000, token); } catch (OperationCanceledException) { break; }
        }
    }
    async Task WriteUpdates(StreamWriter writer, CancellationToken token) {
        do {
            while (_updates.Reader.TryRead(out _)) { }
            IReadOnlyList<HoswlItem> menus; bool enabled;
            lock (_gate) { menus = _menus; enabled = _enabled; }
            await Send(writer, new { t = "menu", menus }, token);
            await Send(writer, new { t = "enable", on = enabled }, token);
        } while (await _updates.Reader.WaitToReadAsync(token));
    }
    static async Task Send(StreamWriter writer, object message, CancellationToken token) {
        string line = JsonSerializer.Serialize(message, Json);
        if (Encoding.UTF8.GetByteCount(line) > 65536) throw new IOException("hoswl menu exceeds the protocol limit.");
        await writer.WriteLineAsync(line.AsMemory(), token);
    }
    async Task ReadClicks(Stream pipe, CancellationToken token) {
        var buffer = new byte[4096]; using var line = new MemoryStream();
        while (true) {
            int count = await pipe.ReadAsync(buffer, token); if (count == 0) return;
            for (int i = 0; i < count; i++) {
                if (buffer[i] != (byte)'\n') { if (line.Length >= 65536) throw new IOException("hoswl response exceeds the protocol limit."); line.WriteByte(buffer[i]); continue; }
                try {
                    using var json = JsonDocument.Parse(line.ToArray()); var root = json.RootElement;
                    if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("t", out var type)) continue;
                    if (type.ValueKind == JsonValueKind.String && type.GetString() == "welcome") SetConnected(true);
                    if (type.ValueKind == JsonValueKind.String && type.GetString() == "click" && root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String) {
                        string action = id.GetString()!; bool allowed;
                        lock (_gate) allowed = _enabled && IsEnabledLeaf(_menus, action);
                        if (allowed) Clicked?.Invoke(action);
                    }
                } catch (JsonException) { /* Ignore malformed host messages; keep the app alive. */ }
                finally { line.SetLength(0); }
            }
        }
    }
    static bool IsEnabledLeaf(IEnumerable<HoswlItem> items, string id) => items.Any(item => item.Enabled != false && (item.Items != null ? IsEnabledLeaf(item.Items, id) : item.Sep != true && item.Id == id));
    void SetConnected(bool connected) { if (Connected == connected) return; Connected = connected; ConnectionChanged?.Invoke(connected); }
    public async ValueTask DisposeAsync() { if (Interlocked.Exchange(ref _disposed, 1) != 0) return; await _stop.CancelAsync(); if (_loop != null) await _loop; _stop.Dispose(); }
}

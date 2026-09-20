using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Gitland.Core;
using Xunit;

namespace Gitland.Tests;

public sealed class HoswlTests {
    static NamedPipeServerStream Server(string name) => new(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
    static async Task<JsonElement> Read(StreamReader reader) {
        string? line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(8));
        Assert.NotNull(line); using var json = JsonDocument.Parse(line); return json.RootElement.Clone();
    }
    [Fact] public async Task HandshakeMenusClicksDisableAndReconnectFollowProtocol() {
        string name = "gitland-hoswl-test-" + Guid.NewGuid().ToString("N");
        await using var client = new HoswlClient("test", name);
        IReadOnlyList<HoswlItem> menus = [new("file", "File", Items: [new("open", "Open", "Ctrl+O"), new("blocked", "Disabled", Enabled: false)])];
        var clicks = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var clicked = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Clicked += id => { clicks.Enqueue(id); clicked.TrySetResult(id); };
        using (var pipe = Server(name)) {
            var connect = pipe.WaitForConnectionAsync(); client.Update(menus, true); await connect.WaitAsync(TimeSpan.FromSeconds(8));
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
            var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true, NewLine = "\n" };
            var hello = await Read(reader); Assert.Equal("hello", hello.GetProperty("t").GetString()); Assert.Equal(Environment.ProcessId, hello.GetProperty("pid").GetInt32()); Assert.Equal(1, hello.GetProperty("v").GetInt32());
            var menu = await Read(reader); Assert.Equal("menu", menu.GetProperty("t").GetString()); Assert.False(menu.GetProperty("menus")[0].GetProperty("items")[1].GetProperty("enabled").GetBoolean());
            var enable = await Read(reader); Assert.True(enable.GetProperty("on").GetBoolean());
            await writer.WriteLineAsync("{\"t\":\"welcome\",\"v\":1}");
            await writer.WriteLineAsync("invalid JSON"); await writer.WriteLineAsync("{\"t\":\"future-message\"}");
            await writer.WriteLineAsync("{\"t\":\"click\",\"id\":\"blocked\"}"); await writer.WriteLineAsync("{\"t\":\"click\",\"id\":\"unknown\"}");
            await writer.WriteLineAsync("{\"t\":\"click\",\"id\":\"open\"}");
            Assert.Equal("open", await clicked.Task.WaitAsync(TimeSpan.FromSeconds(8))); Assert.True(client.Connected); Assert.Single(clicks);
            client.Update(menus, false); await Read(reader); Assert.False((await Read(reader)).GetProperty("on").GetBoolean());
            await writer.WriteLineAsync("{\"t\":\"click\",\"id\":\"open\"}");
            // Closing the server simulates Hisashi restarting.
            var welcome = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            client.ConnectionChanged += state => { if (!state) welcome.TrySetResult(); };
            writer.Dispose(); pipe.Disconnect(); await welcome.Task.WaitAsync(TimeSpan.FromSeconds(8)); Assert.Single(clicks);
        }
        using (var pipe = Server(name)) {
            client.Update(menus, true); await pipe.WaitForConnectionAsync().WaitAsync(TimeSpan.FromSeconds(8));
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
            Assert.Equal("hello", (await Read(reader)).GetProperty("t").GetString()); Assert.Equal("menu", (await Read(reader)).GetProperty("t").GetString()); Assert.True((await Read(reader)).GetProperty("on").GetBoolean());
        }
    }
    [Fact] public async Task MissingHostDoesNotBlockUpdatesOrShutdown() {
        await using var client = new HoswlClient("test", "gitland-absent-" + Guid.NewGuid().ToString("N"));
        client.Update([], true); client.Update([], false); Assert.False(client.Connected);
        await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
    }
}

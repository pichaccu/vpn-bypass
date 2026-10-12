using System.IO;
using System.IO.Pipes;
using System.Text;
using VpnBypass.Core.Ipc;

namespace VpnBypass.Tray;

// A szolgáltatás named pipe-jának kliense. Kérésenként friss kapcsolat (a szerver ciklusban fogad).
public sealed class PipeClient
{
    private const string PipeName = "vpn-bypass";

    public async Task<string?> SendAsync(string json, CancellationToken token = default)
    {
        using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(2000, token);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
        using var reader = new StreamReader(pipe, Encoding.UTF8);
        await writer.WriteLineAsync(json.AsMemory(), token);
        return await reader.ReadLineAsync(token);
    }

    public async Task<StatusResponse?> GetStatusAsync(CancellationToken token = default)
    {
        var r = await SendAsync(IpcJson.Serialize(new IpcRequest(IpcRequestType.GetStatus)), token);
        return r is null ? null : IpcJson.DeserializeStatus(r);
    }

    public async Task<AppsResponse?> GetAppsAsync(CancellationToken token = default)
    {
        var r = await SendAsync(IpcJson.Serialize(new IpcRequest(IpcRequestType.GetApps)), token);
        return r is null ? null : IpcJson.DeserializeApps(r);
    }

    public async Task<IpcResult?> SetExcludedAsync(string pathOrPattern, bool excluded, CancellationToken token = default)
    {
        var r = await SendAsync(IpcJson.Serialize(
            new IpcRequest(IpcRequestType.SetExcluded, Path: pathOrPattern, Excluded: excluded)), token);
        return r is null ? null : IpcJson.DeserializeResult(r);
    }

    public async Task<IpcResult?> SetPausedAsync(bool paused, CancellationToken token = default)
    {
        var r = await SendAsync(IpcJson.Serialize(new IpcRequest(IpcRequestType.SetPaused, Paused: paused)), token);
        return r is null ? null : IpcJson.DeserializeResult(r);
    }
}

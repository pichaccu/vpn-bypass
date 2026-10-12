using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Microsoft.Extensions.Logging;

namespace VpnBypass.Service.Ipc;

// Named pipe szerver a tálcaalkalmazásnak. Soronként egy JSON-kérés → egy JSON-válasz.
// Jogosultság: SYSTEM + Administrators teljes, a megadott felhasználói SID olvas/ír, más semmi.
// A kéréseket a handler dolgozza fel (kérés-sor → válasz-sor).
public sealed class PipeServer(ILogger<PipeServer> log, Func<string, string> handler)
{
    public const string PipeName = "vpn-bypass";

    public async Task RunAsync(string? allowedUserSid, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var server = CreateServer(allowedUserSid);
                await server.WaitForConnectionAsync(token);
                await ServeAsync(server, token);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Pipe-kapcsolat hiba – újrapróbálom.");
                try { await Task.Delay(500, token); } catch { break; }
            }
        }
    }

    private async Task ServeAsync(NamedPipeServerStream server, CancellationToken token)
    {
        using var reader = new StreamReader(server, Encoding.UTF8, false, 1 << 16, leaveOpen: true);
        using var writer = new StreamWriter(server, new UTF8Encoding(false), 1 << 16, leaveOpen: true) { AutoFlush = true };

        // Egy kapcsolaton több kérés is mehet, amíg a kliens nyitva tartja.
        while (!token.IsCancellationRequested && server.IsConnected)
        {
            var line = await reader.ReadLineAsync(token);
            if (line is null) break;
            if (line.Length == 0) continue;

            string response;
            try { response = handler(line); }
            catch (Exception ex)
            {
                log.LogError(ex, "Kérés feldolgozása sikertelen.");
                response = "{\"ok\":false,\"error\":\"belső hiba\"}";
            }
            await writer.WriteLineAsync(response.AsMemory(), token);
        }
    }

    private static NamedPipeServerStream CreateServer(string? allowedUserSid)
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));

        // A futtató felhasználó (konzol/dry-run módban az operátor; SYSTEM-ként redundáns) is kapjon hozzáférést.
        using (var me = WindowsIdentity.GetCurrent())
        {
            if (me.User is { } meSid)
                security.AddAccessRule(new PipeAccessRule(meSid,
                    PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
        }

        if (allowedUserSid is not null)
        {
            try
            {
                security.AddAccessRule(new PipeAccessRule(
                    new SecurityIdentifier(allowedUserSid),
                    PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
                    AccessControlType.Allow));
            }
            catch { /* érvénytelen SID: marad a SYSTEM+Admin hozzáférés */ }
        }

        return NamedPipeServerStreamAcl.Create(
            PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
    }
}

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
        // Az ELSŐ példány FirstPipeInstance-szel: ha a nevet már más birtokolja (squatting), elbukunk.
        NamedPipeServerStream? current;
        try
        {
            current = CreateServer(allowedUserSid, firstInstance: true);
        }
        catch (IOException ex)
        {
            log.LogError(ex, "A '{Pipe}' named pipe nevét már birtokolja valaki – lehetséges squatting. A kiszolgálás NEM indul.", PipeName);
            return;
        }

        while (!token.IsCancellationRequested)
        {
            try
            {
                await current.WaitForConnectionAsync(token);
                // A nevet FOLYAMATOSAN birtokban tartjuk: a következő figyelő példányt még a jelenlegi
                // kiszolgálása ELŐTT létrehozzuk, hogy sose legyen rés, amikor senki sem birtokolja a nevet.
                var next = CreateServer(allowedUserSid, firstInstance: false);
                try { await ServeAsync(current, token); }
                finally { current.Dispose(); current = next; }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Pipe-kapcsolat hiba – folytatom.");
                try { current.Dispose(); } catch { }
                try { current = CreateServer(allowedUserSid, firstInstance: false); }
                catch (Exception ex2) { log.LogError(ex2, "Nem hozható létre új pipe-példány."); break; }
                try { await Task.Delay(500, token); } catch { break; }
            }
        }

        current.Dispose();
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

    private static NamedPipeServerStream CreateServer(string? allowedUserSid, bool firstInstance)
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

        // FirstPipeInstance csak az első példánynál: így a létrehozás elbukik, ha a nevet már más birtokolja.
        var options = PipeOptions.Asynchronous;
        if (firstInstance) options |= PipeOptions.FirstPipeInstance;

        return NamedPipeServerStreamAcl.Create(
            PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte, options, 0, 0, security);
    }
}

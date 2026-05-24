using System.IO.Pipes;
using System.Text;
using Canary.Cli;

namespace Canary.UI;

// Single-instance forwarder for Canary.UI per design §C3 + impl §3.
//
// Pattern:
//   - Program.Main tries to acquire a global mutex `Global\Canary.UI.SingleInstance`.
//   - If acquired: it's the first instance. Start a SingleInstancePipeServer thread
//     that listens on the named pipe `canary-ui-singleinstance-pipe`. Run the form.
//   - If not acquired: another instance is alive. Send our AutoRunArgs JSON via
//     SingleInstancePipeClient.TrySend and exit. The running instance receives the
//     payload via the AutoRunRequested event and dispatches it to MainForm.AutoRunAsync.
//
// Pipe is local-only (PipeOptions.None binds to the current user's session).
// Each handshake is one short JSON line; the server reads to EOF then loops.
public sealed class SingleInstancePipeServer : IDisposable
{
    public const string PipeName = "canary-ui-singleinstance-pipe";

    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public event Action<AutoRunArgs>? AutoRunRequested;

    public void Start()
    {
        _loop = Task.Run(() => RunLoopAsync(_cts.Token));
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);

                using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
                var json = await reader.ReadToEndAsync(ct).ConfigureAwait(false);

                if (AutoRunArgs.TryParseJson(json, out var args))
                {
                    AutoRunRequested?.Invoke(args);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
                return;
            }
            catch
            {
                // Swallow per-iteration errors so a malformed connection doesn't
                // kill the server. Wait briefly to avoid spin-looping on a
                // persistent error.
                try { await Task.Delay(250, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { }
        try { _loop?.Wait(1000); } catch { }
        _cts.Dispose();
    }
}

public static class SingleInstancePipeClient
{
    // One-shot send. Returns true on successful delivery. The server is expected
    // to read-to-EOF; we close the pipe to signal end of message.
    public static bool TrySend(AutoRunArgs args, int connectTimeoutMs = 1500)
    {
        try
        {
            using var client = new NamedPipeClientStream(
                serverName: ".",
                pipeName: SingleInstancePipeServer.PipeName,
                PipeDirection.Out,
                PipeOptions.None);

            client.Connect(connectTimeoutMs);

            var json = args.ToJson();
            var bytes = Encoding.UTF8.GetBytes(json);
            client.Write(bytes, 0, bytes.Length);
            client.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }
}

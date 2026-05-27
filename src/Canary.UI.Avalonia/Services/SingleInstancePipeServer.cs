using System.IO.Pipes;
using System.Text;
using Canary.Cli;

namespace Canary.UI.Avalonia.Services;

// Single-instance forwarder for Canary.UI.Avalonia. Pattern matches the
// WinForms Canary.UI/SingleInstancePipeServer.cs verbatim (port from
// Phase 0 of the Avalonia migration). Pipe name + protocol unchanged so
// `canary run` from the CLI talks to whichever exe is running.
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
                return;
            }
            catch
            {
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

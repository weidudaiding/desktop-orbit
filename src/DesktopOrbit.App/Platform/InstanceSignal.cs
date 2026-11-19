using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace DesktopOrbit.App.Platform;

/// <summary>
/// Single-instance guard plus a tiny named-pipe channel so a second launch can
/// ask the running instance to open the menu instead of starting a duplicate.
/// </summary>
public sealed class InstanceSignal : IDisposable
{
    private const string MutexName = @"Local\DesktopOrbit.SingleInstance";
    private const string PipeName = "DesktopOrbit.Ipc";

    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _cts = new();
    private Task? _server;

    public InstanceSignal()
    {
        _mutex = new Mutex(true, MutexName, out bool createdNew);
        IsFirst = createdNew;
    }

    public bool IsFirst { get; }

    public event Action? ShowRequested;

    public void StartListening()
    {
        if (!IsFirst || _server is not null) return;
        _server = Task.Run(() => ListenLoop(_cts.Token));
    }

    private async Task ListenLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(token).ConfigureAwait(false);

                using var reader = new StreamReader(server);
                string? command = await reader.ReadLineAsync(token).ConfigureAwait(false);
                if (string.Equals(command, "show", StringComparison.OrdinalIgnoreCase))
                {
                    ShowRequested?.Invoke();
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
                await Task.Delay(250, token).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }
    }

    public static bool TrySignalExisting(int timeoutMs = 600)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(timeoutMs);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine("show");
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _server?.Wait(TimeSpan.FromMilliseconds(500));
        }
        catch (AggregateException)
        {
        }

        if (IsFirst)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
        }

        _mutex.Dispose();
        _cts.Dispose();
    }
}

using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using Claxe.Interop;

namespace Claxe.Services;

/// <summary>
/// One Claxe per profile: a second launch (or Windows opening a link) hands its arguments to
/// the running instance over a named pipe that only the same Windows user can open.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _stop = new();

    private SingleInstance(Mutex mutex) => _mutex = mutex;

    /// <summary>Per profile, so a test profile (CLAXE_PROFILE_DIR) runs next to the real one.</summary>
    private static string Name { get; } = "Sorune-" + Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(AppPaths.DataRoot.ToLowerInvariant())))[..16];

    /// <summary>Returns the lock when this is the first instance, otherwise null.</summary>
    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, Name, out var first);
        if (first)
            return new SingleInstance(mutex);

        mutex.Dispose();
        return null;
    }

    /// <summary>Second instance: pass the links to the running one. False if it didn't answer.</summary>
    public static bool SendToRunning(IReadOnlyList<string> args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", Name, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(3000);
            if (NativeMethods.GetNamedPipeServerProcessId(client.SafePipeHandle.DangerousGetHandle(), out var pid))
                NativeMethods.AllowSetForegroundWindow(pid);

            using var writer = new StreamWriter(client, new UTF8Encoding(false));
            writer.Write(string.Join('\n', args));
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            Log.Error("Açık olan Sorune'ye ulaşılamadı.", ex);
            return false;
        }
    }

    /// <summary>First instance: receives links from later launches; <paramref name="onArgs"/> runs on a worker thread.</summary>
    public void Listen(Action<string[]> onArgs)
    {
        var token = _stop.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(Name, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(token);
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var text = await reader.ReadToEndAsync(token);
                    onArgs(text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (IOException ex)
                {
                    Log.Error("Tek pencere kanalı hatası.", ex);
                    await Task.Delay(500, CancellationToken.None);
                }
            }
        }, token);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}

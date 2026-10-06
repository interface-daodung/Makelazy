using System.IO;
using System.IO.Pipes;

namespace Makelazy.App.Services;

/// <summary>
/// Single-instance: instance thứ 2 (vd: double-click Makefile khi app đang mở)
/// chuyển path sang instance đầu rồi thoát, thay vì mở thêm cửa sổ.
/// </summary>
public static class SingleInstance
{
    private const string MutexName = @"Local\Makelazy_MakefileRunner_SingleInstance";
    private const string PipeName = "Makelazy_Makefile_Pipe";
    private static Mutex? _mutex;

    public static bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool first);
        return first;
    }

    public static void SendToRunning(string? file, string? target)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(timeout: 2000);
            using var w = new StreamWriter(client) { AutoFlush = true };
            w.WriteLine(file ?? string.Empty);
            w.WriteLine(target ?? string.Empty);
        }
        catch { /* instance đầu chưa kịp mở pipe: bỏ qua */ }
    }

    public static void StartServer(Func<string?, string?, Task> onFile, CancellationToken token)
    {
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In);
                    await server.WaitForConnectionAsync(token);
                    using var r = new StreamReader(server);
                    var file = await r.ReadLineAsync();
                    var target = await r.ReadLineAsync();
                    await onFile(
                        string.IsNullOrWhiteSpace(file) ? null : file,
                        string.IsNullOrWhiteSpace(target) ? null : target);
                }
                catch (OperationCanceledException) { break; }
                catch { await Task.Delay(300, token).ContinueWith(_ => { }); }
            }
        }, token);
    }
}

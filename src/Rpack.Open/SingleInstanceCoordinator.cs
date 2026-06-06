using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace Rpack.Open;

internal sealed class SingleInstanceCoordinator : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipeName;

    private SingleInstanceCoordinator(Mutex mutex, bool isPrimary, string pipeName)
    {
        _mutex = mutex;
        IsPrimary = isPrimary;
        _pipeName = pipeName;
    }

    public bool IsPrimary { get; }

    public static SingleInstanceCoordinator Create()
    {
        var identity = $"{Environment.UserDomainName}-{Environment.UserName}";
        var suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..12];
        var pipeName = $"rpack-open-{suffix}";
        var mutex = new Mutex(initiallyOwned: true, $"Local\\rpack-open-{suffix}", out var createdNew);
        return new SingleInstanceCoordinator(mutex, createdNew, pipeName);
    }

    public bool SendToPrimary(OpenRequest request)
    {
        var payload = JsonSerializer.Serialize(request);
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
                client.Connect(150);
                using var writer = new StreamWriter(client, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.WriteLine(payload);
                return true;
            }
            catch (TimeoutException)
            {
                Thread.Sleep(100);
            }
            catch (IOException)
            {
                Thread.Sleep(100);
            }
        }

        MessageBox.Show(
            "Another rpack window is already open, but this package could not be sent to it. Close the existing rpack window and try again.",
            "rpack open",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
        return false;
    }

    public IDisposable StartServer(Action<OpenRequest> onRequest)
    {
        var cancellation = new CancellationTokenSource();
        _ = Task.Run(() => RunServer(onRequest, cancellation.Token));
        return cancellation;
    }

    public void Dispose()
    {
        if (IsPrimary)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }

    private async Task RunServer(Action<OpenRequest> onRequest, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(server, Encoding.UTF8);
                var payload = await reader.ReadLineAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(payload))
                {
                    var request = JsonSerializer.Deserialize<OpenRequest>(payload);
                    if (request is not null)
                    {
                        onRequest(request);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                await Task.Delay(100, cancellationToken);
            }
        }
    }
}

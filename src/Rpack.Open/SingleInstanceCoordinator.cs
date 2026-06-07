using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace Rpack.Open;

internal sealed class SingleInstanceCoordinator : IDisposable
{
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly string _handoffErrorMutexName;

    private SingleInstanceCoordinator(Mutex mutex, bool isPrimary, string pipeName, string handoffErrorMutexName)
    {
        _mutex = mutex;
        IsPrimary = isPrimary;
        _pipeName = pipeName;
        _handoffErrorMutexName = handoffErrorMutexName;
    }

    public bool IsPrimary { get; }

    public static SingleInstanceCoordinator Create()
    {
        var identity = $"{Environment.UserDomainName}-{Environment.UserName}";
        var suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..12];
        var pipeName = $"rpack-open-{suffix}";
        var handoffErrorMutexName = $"Local\\rpack-open-{suffix}-handoff-error";
        var mutex = new Mutex(initiallyOwned: true, $"Local\\rpack-open-{suffix}", out var createdNew);
        return new SingleInstanceCoordinator(mutex, createdNew, pipeName, handoffErrorMutexName);
    }

    public bool SendToPrimary(OpenRequest request)
    {
        var payload = JsonSerializer.Serialize(request);
        var deadline = DateTimeOffset.UtcNow + SendTimeout;
        Exception? lastError = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
                client.Connect((int)ConnectTimeout.TotalMilliseconds);
                using var writer = new StreamWriter(client, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.WriteLine(payload);
                return true;
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
            {
                lastError = ex;
                SleepBeforeRetry(deadline);
            }
        }

        ShowHandoffFailure(lastError);
        return false;
    }

    public IDisposable StartServer(Action<OpenRequest> onRequest)
    {
        var cancellation = new CancellationTokenSource();
        _ = Task.Run(() => RunServer(onRequest, cancellation.Token));
        return new ServerLease(cancellation);
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
                try
                {
                    await Task.Delay(100, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private static void SleepBeforeRetry(DateTimeOffset deadline)
    {
        var remaining = deadline - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            return;
        }

        var delay = remaining < RetryDelay ? remaining : RetryDelay;
        Thread.Sleep(delay);
    }

    private void ShowHandoffFailure(Exception? lastError)
    {
        using var messageMutex = new Mutex(initiallyOwned: true, _handoffErrorMutexName, out var shouldShow);
        if (!shouldShow)
        {
            return;
        }

        var details = lastError is null ? "" : $"{Environment.NewLine}{Environment.NewLine}Last error: {lastError.Message}";
        MessageBox.Show(
            "Another rpack window is already open or starting, but this package could not be sent to it. Close the existing rpack window and try again." + details,
            "rpack open",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private sealed class ServerLease(CancellationTokenSource cancellation) : IDisposable
    {
        public void Dispose()
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }
    }
}

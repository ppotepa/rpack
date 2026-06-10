using System.Diagnostics;
using System.Text;

namespace Rpack.Core;

public sealed class ProcessRunner
{
    private readonly Action<ProcessLogEntry>? _log;

    public ProcessRunner(Action<ProcessLogEntry>? log = null)
    {
        _log = log;
    }

    public ProcessResult Run(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environmentVariables = null)
    {
        var argumentList = arguments.ToArray();
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in argumentList)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (environmentVariables is not null)
        {
            foreach (var (key, value) in environmentVariables)
            {
                startInfo.Environment[key] = value;
            }
        }

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            var failedStart = new ProcessResult(-1, "", $"Failed to start process: {fileName}");
            Log(new ProcessLogEntry(DateTimeOffset.Now, fileName, argumentList, workingDirectory, failedStart.ExitCode, failedStart.StandardOutput, failedStart.StandardError));
            return failedStart;
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();

        var result = new ProcessResult(process.ExitCode, stdout, stderr);
        Log(new ProcessLogEntry(DateTimeOffset.Now, fileName, argumentList, workingDirectory, result.ExitCode, result.StandardOutput, result.StandardError));
        return result;
    }

    private void Log(ProcessLogEntry entry)
    {
        try
        {
            _log?.Invoke(entry);
        }
        catch
        {
            // Logging must never change command execution behavior.
        }
    }
}

public sealed record ProcessLogEntry(
    DateTimeOffset Timestamp,
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    int ExitCode,
    string StandardOutput,
    string StandardError);

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Success => ExitCode == 0;
    public string CombinedOutput => string.Join(Environment.NewLine, new[] { StandardOutput, StandardError }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

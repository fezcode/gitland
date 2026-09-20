using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Gitland.Core;

public sealed record CommandRequest(string Executable, string Directory, IReadOnlyList<string> Arguments, string? Input = null, int TimeoutSeconds = 30);
public sealed record CommandResult(int ExitCode, string Output, string Error);
public interface ICommandRunner { Task<CommandResult> RunAsync(CommandRequest command); }
public sealed class CommandFailedException(string message, int exitCode) : InvalidOperationException(message) { public int ExitCode { get; } = exitCode; }

public sealed class CommandRunner : ICommandRunner {
    public async Task<CommandResult> RunAsync(CommandRequest command) {
        var start = new ProcessStartInfo(command.Executable) { WorkingDirectory = command.Directory, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, StandardInputEncoding = new UTF8Encoding(false) };
        foreach (var arg in command.Arguments) start.ArgumentList.Add(arg);
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GCM_INTERACTIVE"] = "never";
        start.Environment["GH_PROMPT_DISABLED"] = "1";
        start.Environment["GH_PAGER"] = "cat";
        start.Environment["NO_COLOR"] = "1";
        if (command.Executable == "gh") start.Environment["GH_HOST"] = "github.com";
        Process process;
        try { process = Process.Start(start) ?? throw new IOException($"{command.Executable} could not start."); }
        catch (Win32Exception e) { throw new InvalidOperationException($"{command.Executable} is unavailable. Install it and add it to PATH.", e); }
        using (process) {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(command.TimeoutSeconds));
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            try {
                if (command.Input != null) await process.StandardInput.WriteAsync(command.Input.AsMemory(), timeout.Token);
                process.StandardInput.Close();
                await process.WaitForExitAsync(timeout.Token);
                return new(process.ExitCode, await output, await error);
            } catch (OperationCanceledException) {
                try { process.Kill(true); } catch (InvalidOperationException) { }
                throw new TimeoutException($"{command.Executable} took longer than {command.TimeoutSeconds} seconds. Check the operation's status before retrying.");
            }
        }
    }
}

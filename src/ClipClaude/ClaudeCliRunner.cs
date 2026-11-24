using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace ClipClaude;

public enum CliOutcome
{
    Success,
    NotFound,
    StartFailed,
    Timeout,
    Cancelled,
    NonZeroExit,
    NotAuthorized,
    EmptyOutput,
}

public sealed record CliResult(CliOutcome Outcome, string Output, string Stderr, int? ExitCode, TimeSpan Elapsed);

/// <summary>Запуск Claude Code CLI: текст через stdin в UTF-8, без оболочки, с таймаутом и убийством дерева процессов.</summary>
public static class ClaudeCliRunner
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly Regex AuthErrorPattern = new(
        @"not logged in|run /login|invalid api key|authentication_error|oauth token|\bunauthorized\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex SafeArg = new(@"^[A-Za-z0-9._:\[\]\-=/]+$", RegexOptions.CultureInvariant);

    public static string WorkDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipClaude", "work");

    /// <summary>
    /// Аргументы неинтерактивного режима (сверено с claude 2.1.x):
    /// --tools "" — никаких инструментов; --safe-mode — без CLAUDE.md, MCP, хуков и плагинов;
    /// --no-session-persistence — сессия не сохраняется на диск.
    /// </summary>
    public static IReadOnlyList<string> BuildArguments(string? model)
    {
        var args = new List<string>
        {
            "-p", "--output-format", "text",
            "--tools", "",
            "--safe-mode",
            "--strict-mcp-config",
            "--no-session-persistence",
        };
        if (!string.IsNullOrWhiteSpace(model))
        {
            if (!AppSettings.IsValidModel(model)) throw new ArgumentException("Недопустимое имя модели", nameof(model));
            args.Add("--model");
            args.Add(model.Trim());
        }
        return args;
    }

    /// <summary>Путь из настроек → claude.exe / claude.cmd в PATH → %USERPROFILE%\.local\bin (нативный установщик).</summary>
    public static string? ResolveExecutable(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
            return File.Exists(configuredPath) ? Path.GetFullPath(configuredPath) : null;

        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin"));

        foreach (var name in new[] { "claude.exe", "claude.cmd" })
        {
            foreach (var dir in dirs)
            {
                try
                {
                    var candidate = Path.Combine(dir.Trim('"'), name);
                    if (File.Exists(candidate)) return candidate;
                }
                catch (ArgumentException)
                {
                    // мусор в PATH
                }
            }
        }
        return null;
    }

    public static ProcessStartInfo CreateStartInfo(string executable, IReadOnlyList<string> args, string workDir)
    {
        var psi = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8NoBom,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = workDir,
        };

        if (executable.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
            executable.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
        {
            // npm-установка: через cmd.exe /c с фиксированными аргументами; пользовательский текст — только через stdin.
            psi.FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            var inner = "\"" + executable + "\" " + string.Join(" ", args.Select(QuoteForCmd));
            psi.Arguments = "/d /s /c \"" + inner + "\"";
        }
        else
        {
            psi.FileName = executable;
            foreach (var a in args) psi.ArgumentList.Add(a);
        }
        return psi;
    }

    private static string QuoteForCmd(string arg)
    {
        if (arg.Length == 0) return "\"\"";
        if (!SafeArg.IsMatch(arg)) throw new ArgumentException($"Недопустимый аргумент для cmd: {arg}");
        return arg;
    }

    public static Task<CliResult> RunPromptAsync(AppSettings settings, string prompt, CancellationToken ct)
    {
        var exe = ResolveExecutable(settings.ClaudePath);
        if (exe is null)
            return Task.FromResult(new CliResult(CliOutcome.NotFound, "", "", null, TimeSpan.Zero));

        Directory.CreateDirectory(WorkDirectory);
        var psi = CreateStartInfo(exe, BuildArguments(settings.Model), WorkDirectory);
        return RunProcessAsync(psi, prompt, TimeSpan.FromSeconds(settings.TimeoutSeconds), ct);
    }

    public static Task<CliResult> GetVersionAsync(string? configuredPath)
    {
        var exe = ResolveExecutable(configuredPath);
        if (exe is null)
            return Task.FromResult(new CliResult(CliOutcome.NotFound, "", "", null, TimeSpan.Zero));
        Directory.CreateDirectory(WorkDirectory);
        var psi = CreateStartInfo(exe, new[] { "--version" }, WorkDirectory);
        return RunProcessAsync(psi, null, TimeSpan.FromSeconds(20), CancellationToken.None);
    }

    public static async Task<CliResult> RunProcessAsync(ProcessStartInfo psi, string? stdin, TimeSpan timeout, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        using var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            Log.Error("Claude CLI start failed", ex);
            return new CliResult(CliOutcome.StartFailed, "", ex.Message, null, sw.Elapsed);
        }

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        try
        {
            try
            {
                if (stdin is not null)
                    await process.StandardInput.WriteAsync(stdin.AsMemory(), linked.Token);
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // процесс закрыл stdin раньше времени — итог определит код выхода
            }

            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            KillTree(process);
            var outcome = ct.IsCancellationRequested ? CliOutcome.Cancelled : CliOutcome.Timeout;
            Log.Info($"Claude CLI {outcome} after {sw.Elapsed.TotalSeconds:F1}s");
            return new CliResult(outcome, "", await SafeRead(stderrTask), null, sw.Elapsed);
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        sw.Stop();
        var exitCode = process.ExitCode;
        Log.Info($"Claude CLI exit={exitCode} in {sw.Elapsed.TotalSeconds:F1}s, stdoutLen={stdout.Length}, stderrLen={stderr.Length}");

        if (exitCode != 0)
        {
            var kind = AuthErrorPattern.IsMatch(stderr) || AuthErrorPattern.IsMatch(stdout)
                ? CliOutcome.NotAuthorized
                : CliOutcome.NonZeroExit;
            var details = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            return new CliResult(kind, "", details.Trim(), exitCode, sw.Elapsed);
        }

        var cleaned = ResponseParser.Clean(stdout);
        return cleaned.Length == 0
            ? new CliResult(CliOutcome.EmptyOutput, "", stderr.Trim(), exitCode, sw.Elapsed)
            : new CliResult(CliOutcome.Success, cleaned, stderr.Trim(), exitCode, sw.Elapsed);
    }

    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            Log.Error("Kill failed", ex);
        }
    }

    private static async Task<string> SafeRead(Task<string> task)
    {
        try
        {
            var done = await Task.WhenAny(task, Task.Delay(1000));
            return done == task ? (await task).Trim() : "";
        }
        catch
        {
            return "";
        }
    }
}

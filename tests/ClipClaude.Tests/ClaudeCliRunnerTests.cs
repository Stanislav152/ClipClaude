using System.Diagnostics;
using ClipClaude;
using Xunit;

namespace ClipClaude.Tests;

public class ClaudeCliRunnerTests
{
    private static readonly string FakeExe = FindFakeExe();

    private static string FindFakeExe()
    {
        var local = Path.Combine(AppContext.BaseDirectory, "FakeClaude.exe");
        if (File.Exists(local)) return local;
        // tests/ClipClaude.Tests/bin/<cfg>/<tfm>/ → tests/FakeClaude/bin/<cfg>/net8.0/
        var baseDir = new DirectoryInfo(AppContext.BaseDirectory);
        var cfg = baseDir.Parent!.Name;
        var testsDir = baseDir.Parent!.Parent!.Parent!.Parent!.FullName;
        return Path.Combine(testsDir, "FakeClaude", "bin", cfg, "net8.0", "FakeClaude.exe");
    }

    private static Task<CliResult> Run(string mode, string? stdin, int timeoutSeconds = 20, CancellationToken ct = default)
    {
        var psi = ClaudeCliRunner.CreateStartInfo(FakeExe, ClaudeCliRunner.BuildArguments("sonnet"), Path.GetTempPath());
        psi.Environment["FAKE_CLAUDE_MODE"] = mode;
        return ClaudeCliRunner.RunProcessAsync(psi, stdin, TimeSpan.FromSeconds(timeoutSeconds), ct);
    }

    [Fact]
    public async Task Unicode_quotes_newlines_and_long_text_roundtrip_through_stdin()
    {
        var text = string.Concat(Enumerable.Repeat("Текст с \"кавычками\", 'апострофами', эмодзи 😀👍🏽 и & | < > ^ %PATH%\r\nновая строка\n", 200));
        Assert.True(text.Length > 10_000);
        var r = await Run("echo", text);
        Assert.Equal(CliOutcome.Success, r.Outcome);
        Assert.Equal(text.Trim(), r.Output);
    }

    [Fact]
    public async Task Wrapping_tags_are_stripped()
    {
        var r = await Run("tags", "привет");
        Assert.Equal(CliOutcome.Success, r.Outcome);
        Assert.Equal("привет", r.Output);
    }

    [Fact]
    public async Task Arguments_are_passed_exactly()
    {
        var r = await Run("args", "");
        Assert.Equal("-p|--output-format|text|--tools|<empty>|--safe-mode|--strict-mcp-config|--no-session-persistence|--model|sonnet", r.Output);
    }

    [Fact]
    public async Task Nonzero_exit_returns_stderr()
    {
        var r = await Run("fail", "x");
        Assert.Equal(CliOutcome.NonZeroExit, r.Outcome);
        Assert.Equal(3, r.ExitCode);
        Assert.Contains("Something went wrong", r.Stderr);
    }

    [Fact]
    public async Task Auth_error_is_detected()
    {
        var r = await Run("auth", "x");
        Assert.Equal(CliOutcome.NotAuthorized, r.Outcome);
    }

    [Fact]
    public async Task Empty_output_is_reported()
    {
        var r = await Run("empty", "x");
        Assert.Equal(CliOutcome.EmptyOutput, r.Outcome);
    }

    [Fact]
    public async Task Hang_times_out_and_process_is_killed()
    {
        var before = Process.GetProcessesByName("FakeClaude").Select(p => p.Id).ToHashSet();
        var sw = Stopwatch.StartNew();
        var r = await Run("hang", "x", timeoutSeconds: 2);
        Assert.Equal(CliOutcome.Timeout, r.Outcome);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10));
        await Task.Delay(500);
        Assert.DoesNotContain(Process.GetProcessesByName("FakeClaude"), p => !before.Contains(p.Id));
    }

    [Fact]
    public async Task Cancellation_is_reported_as_cancelled()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var r = await Run("hang", "x", timeoutSeconds: 30, ct: cts.Token);
        Assert.Equal(CliOutcome.Cancelled, r.Outcome);
    }

    [Fact]
    public async Task Cmd_wrapper_is_supported()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ClipClaudeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var cmd = Path.Combine(dir, "claude.cmd");
        File.WriteAllText(cmd, $"@\"{FakeExe}\" %*\r\n");

        var psi = ClaudeCliRunner.CreateStartInfo(cmd, ClaudeCliRunner.BuildArguments(null), dir);
        psi.Environment["FAKE_CLAUDE_MODE"] = "echo";
        var text = "Кавычки \" & | > эмодзи 😀";
        var r = await ClaudeCliRunner.RunProcessAsync(psi, text, TimeSpan.FromSeconds(20), default);
        Assert.Equal(CliOutcome.Success, r.Outcome);
        Assert.Equal(text, r.Output);
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Missing_configured_path_is_not_found() =>
        Assert.Null(ClaudeCliRunner.ResolveExecutable(@"C:\definitely\missing\claude.exe"));

    [Fact]
    public void Invalid_model_is_rejected() =>
        Assert.Throws<ArgumentException>(() => ClaudeCliRunner.BuildArguments("x & calc"));
}

using System.Diagnostics;
using Xunit;

namespace LearningPath.Tests;

// Runs one experiment the way a reader does: `dotnet <Name>.dll [args]`, as its own process, and
// captures its exit code and both output streams. Shared by every test file in this project.
internal static class Experiment
{
    internal sealed record Run(int ExitCode, string Stdout, string Stderr)
    {
        public string[] Lines => Stdout.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
    }

    public static async Task<Run> RunAsync(string name, params string[] args)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Dll(name));
        foreach (string arg in args) start.ArgumentList.Add(arg);

        using var process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await process.WaitForExitAsync(timeout.Token);
        return new Run(process.ExitCode, await stdout, await stderr);
    }

    // verify/path/<Name>/bin/<Configuration>/net10.0/<Name>.dll, built through the ProjectReferences.
    private static string Dll(string name)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "check_path.py"))) root = root.Parent;
        Assert.NotNull(root);
        string configuration = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}") ? "Release" : "Debug";
        string dll = Path.Combine(root.FullName, name, "bin", configuration, "net10.0", name + ".dll");
        Assert.True(File.Exists(dll), $"not built: {dll}");
        return dll;
    }
}

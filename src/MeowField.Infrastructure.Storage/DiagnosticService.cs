using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using MeowField.Application;

namespace MeowField.Infrastructure.Storage;

public sealed class DiagnosticService(IUserDataStore userDataStore) : IDiagnosticService
{
    public string LogDirectory => Path.Combine(userDataStore.DataDirectory, "logs");

    public async Task ExportAsync(string outputPath, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        Directory.CreateDirectory(LogDirectory);
        if (Path.GetExtension(outputPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
            using var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create);
            foreach (var file in Directory.EnumerateFiles(LogDirectory, "*.log", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = archive.CreateEntry($"logs/{Path.GetFileName(file)}");
                await using var stream = entry.Open();
                await using var writer = new StreamWriter(stream);
                await writer.WriteAsync(Redact(await File.ReadAllTextAsync(file, cancellationToken)));
            }
            var environment = archive.CreateEntry("environment.txt");
            await using (var stream = environment.Open())
            await using (var writer = new StreamWriter(stream))
            {
                await writer.WriteAsync(BuildEnvironmentSummary());
            }
        }
        else
        {
            await using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            await using var writer = new StreamWriter(output);
            await writer.WriteLineAsync("===== environment =====");
            await writer.WriteLineAsync(BuildEnvironmentSummary());
            foreach (var file in Directory.EnumerateFiles(LogDirectory, "*.log", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await writer.WriteLineAsync($"===== {Path.GetFileName(file)} =====");
                await writer.WriteLineAsync(Redact(await File.ReadAllTextAsync(file, cancellationToken)));
            }
        }
    }

    private static string BuildEnvironmentSummary() => string.Join(Environment.NewLine,
    [
        $"Application: {Assembly.GetEntryAssembly()?.GetName().Name ?? "MeowField.App"}",
        $"Version: {Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown"}",
        $"OS: {RuntimeInformation.OSDescription}",
        $"Architecture: {RuntimeInformation.OSArchitecture}",
        $"ProcessArchitecture: {RuntimeInformation.ProcessArchitecture}",
        $"Framework: {RuntimeInformation.FrameworkDescription}",
        $"ProcessorCount: {Environment.ProcessorCount}",
        $"UtcNow: {DateTimeOffset.UtcNow:O}",
    ]) + Environment.NewLine;

    private static string Redact(string value)
    {
        var replacements = new[]
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "%LOCALAPPDATA%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "%APPDATA%"),
            (Environment.MachineName, "<MACHINE>"),
            (Environment.UserName, "<USER>"),
        };
        foreach (var (source, replacement) in replacements.OrderByDescending(item => item.Item1.Length))
        {
            if (!string.IsNullOrWhiteSpace(source)) value = value.Replace(source, replacement, StringComparison.OrdinalIgnoreCase);
        }
        return value;
    }
}

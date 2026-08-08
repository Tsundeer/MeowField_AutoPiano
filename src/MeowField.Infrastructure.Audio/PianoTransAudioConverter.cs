using System.Diagnostics;
using MeowField.Application;
using MeowField.Domain;

namespace MeowField.Infrastructure.Audio;

public sealed class PianoTransAudioConverter : IAudioConverter
{
    private readonly SemaphoreSlim _conversionGate = new(1, 1);

    public PianoTransAudioConverter()
    {
        ExecutablePath = FindExecutable([
            Path.Combine(AppContext.BaseDirectory, "PianoTrans-v1.0"),
            Path.Combine(Environment.CurrentDirectory, "PianoTrans-v1.0"),
            AppContext.BaseDirectory,
        ]);
    }

    public string? ExecutablePath { get; private set; }
    public bool IsAvailable => ExecutablePath is not null && File.Exists(ExecutablePath);
    public IReadOnlyList<string> SupportedExtensions { get; } = [".wav", ".mp3", ".flac", ".ogg", ".m4a", ".aac"];

    public Task<(bool Success, string Message)> SetPathAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var executable = FindExecutable([path]);
        if (executable is null)
        {
            return Task.FromResult((false, $"未找到 PianoTrans.exe：{path}"));
        }

        ExecutablePath = executable;
        return Task.FromResult((true, $"已找到 PianoTrans.exe：{executable}"));
    }

    public async Task<ConversionResult> ConvertAsync(
        string audioPath,
        string? outputPath = null,
        IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            return new ConversionResult(false, "PianoTrans.exe 未设置或不存在", null);
        }

        if (!File.Exists(audioPath))
        {
            return new ConversionResult(false, $"音频文件不存在：{audioPath}", null);
        }

        if (!SupportedExtensions.Contains(Path.GetExtension(audioPath), StringComparer.OrdinalIgnoreCase))
        {
            return new ConversionResult(false, $"不支持的音频格式：{Path.GetExtension(audioPath)}", null);
        }

        await _conversionGate.WaitAsync(cancellationToken);
        var stopwatch = Stopwatch.StartNew();
        var executable = ExecutablePath!;
        var workingDirectory = Path.GetDirectoryName(executable)!;
        var uniqueStem = $"meowfield-{Guid.NewGuid():N}";
        var temporaryAudio = Path.Combine(workingDirectory, uniqueStem + Path.GetExtension(audioPath));
        var expectedMidi = Path.Combine(workingDirectory, uniqueStem + ".mid");
        var genericMidi = Path.Combine(workingDirectory, "output.mid");
        outputPath ??= Path.Combine(Path.GetDirectoryName(Path.GetFullPath(audioPath))!, Path.GetFileNameWithoutExtension(audioPath) + ".mid");

        try
        {
            File.Copy(audioPath, temporaryAudio, overwrite: false);
            progress?.Report(new ConversionProgress("starting", "正在启动 PianoTrans...", stopwatch.Elapsed));

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add(Path.GetFileName(temporaryAudio));
            startInfo.Environment["PATH"] = BuildPath(workingDirectory);

            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            if (!process.Start())
            {
                return new ConversionResult(false, "PianoTrans 启动失败", null);
            }

            using var registration = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Process may have exited between the checks.
                }
            });

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            while (!process.HasExited)
            {
                await Task.WhenAny(process.WaitForExitAsync(cancellationToken), Task.Delay(TimeSpan.FromSeconds(2), cancellationToken));
                if (!process.HasExited)
                {
                    progress?.Report(new ConversionProgress("converting", $"转换进行中...（{stopwatch.Elapsed.TotalSeconds:0} 秒）", stopwatch.Elapsed));
                }
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var generated = File.Exists(expectedMidi) ? expectedMidi : File.Exists(genericMidi) ? genericMidi : null;
            if (generated is null)
            {
                var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                return new ConversionResult(false,
                    process.ExitCode == 0 ? "转换结束，但未找到输出 MIDI 文件" : $"转换失败（{process.ExitCode}）：{detail.Trim()}", null);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.Move(generated, outputPath, overwrite: true);
            progress?.Report(new ConversionProgress("completed", "转换完成", stopwatch.Elapsed));
            return new ConversionResult(true, $"MIDI 已保存到：{outputPath}", outputPath);
        }
        catch (OperationCanceledException)
        {
            return new ConversionResult(false, "转换已取消", null);
        }
        catch (Exception exception)
        {
            return new ConversionResult(false, $"转换失败：{exception.Message}", null);
        }
        finally
        {
            TryDelete(temporaryAudio);
            TryDelete(expectedMidi);
            stopwatch.Stop();
            _conversionGate.Release();
        }
    }

    private static string? FindExecutable(IEnumerable<string> paths)
    {
        foreach (var path in paths.Where(item => !string.IsNullOrWhiteSpace(item)))
        {
            if (File.Exists(path) && string.Equals(Path.GetFileName(path), "PianoTrans.exe", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFullPath(path);
            }

            if (!Directory.Exists(path)) continue;
            var direct = Path.Combine(path, "PianoTrans.exe");
            if (File.Exists(direct)) return Path.GetFullPath(direct);
            try
            {
                var nested = Directory.EnumerateFiles(path, "PianoTrans.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (nested is not null) return Path.GetFullPath(nested);
            }
            catch (UnauthorizedAccessException)
            {
                // Ignore inaccessible nested folders while locating an optional tool.
            }
        }

        return null;
    }

    private static string BuildPath(string workingDirectory)
    {
        var segments = new[] { workingDirectory, Path.Combine(workingDirectory, "ffmpeg"), Environment.GetEnvironmentVariable("PATH") };
        return string.Join(Path.PathSeparator, segments.Where(item => !string.IsNullOrWhiteSpace(item) && (item == Environment.GetEnvironmentVariable("PATH") || Directory.Exists(item))));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Cleanup failure should not hide the conversion result.
        }
    }
}

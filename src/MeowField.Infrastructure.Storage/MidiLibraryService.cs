using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MeowField.Application;
using MeowField.Domain;

namespace MeowField.Infrastructure.Storage;

public sealed class MidiLibraryService : ILibraryService
{
    private readonly IMidiFileReader _midiReader;
    private readonly string _libraryPath;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly object _gate = new();
    private readonly string? _legacyLibraryPath;
    private Dictionary<string, LibraryEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public MidiLibraryService(IMidiFileReader midiReader, IUserDataStore userDataStore, string? legacyLibraryPath = null)
    {
        _midiReader = midiReader;
        _libraryPath = Path.Combine(userDataStore.DataDirectory, "library.json");
        _legacyLibraryPath = legacyLibraryPath;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var document = await AtomicJsonFile.ReadAsync<LibraryDocument>(_libraryPath, cancellationToken);
        if (document is null)
        {
            var migrated = await ImportLegacyLibraryAsync(cancellationToken);
            if (migrated is not null)
            {
                document = new LibraryDocument(1, migrated, DateTimeOffset.Now);
                await AtomicJsonFile.WriteAsync(_libraryPath, document, cancellationToken);
            }
        }
        lock (_gate)
        {
            _entries = document?.Entries?.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase)
                ?? new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task<Dictionary<string, LibraryEntry>?> ImportLegacyLibraryAsync(CancellationToken cancellationToken)
    {
        var candidates = _legacyLibraryPath is not null
            ? [_legacyLibraryPath]
            : new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MeowField_Autoplayer_Lite", "data", "midi_library.json"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MeowField_Autoplayer_Lite", "midi_library.json"),
                Path.Combine(AppContext.BaseDirectory, "midi_library.json"),
            };
        var source = candidates.FirstOrDefault(path => File.Exists(path) && !string.Equals(Path.GetFullPath(path), Path.GetFullPath(_libraryPath), StringComparison.OrdinalIgnoreCase));
        if (source is null) return null;

        try
        {
            await using var stream = File.OpenRead(source);
            var legacy = await JsonSerializer.DeserializeAsync<LegacyLibraryDocument>(stream, cancellationToken: cancellationToken);
            if (legacy?.Entries is null || legacy.Entries.Count == 0) return null;
            var result = new Dictionary<string, LibraryEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in legacy.Entries)
            {
                var item = pair.Value;
                if (item is null || string.IsNullOrWhiteSpace(item.Path)) continue;
                var id = string.IsNullOrWhiteSpace(item.Id) ? pair.Key : item.Id;
                if (string.IsNullOrWhiteSpace(id)) id = GenerateId(item.Path);
                var added = DateTimeOffset.TryParse(item.AddedAt, out var parsed) ? parsed : DateTimeOffset.Now;
                result[id] = new LibraryEntry(id, item.Path, string.IsNullOrWhiteSpace(item.Name) ? Path.GetFileNameWithoutExtension(item.Path) : item.Name,
                    item.Folder ?? "默认", item.DurationMs, item.Notes, added);
            }

            if (result.Count == 0) return null;
            var backupDirectory = Path.Combine(Path.GetDirectoryName(_libraryPath)!, "migration-backups", DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(backupDirectory);
            File.Copy(source, Path.Combine(backupDirectory, Path.GetFileName(source)), overwrite: false);
            return result;
        }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
    }

    public LibraryPage GetPage(int offset, int limit, string? folder = null, string? query = null)
    {
        offset = Math.Max(0, offset);
        limit = Math.Clamp(limit, 1, 1000);
        LibraryEntry[] snapshot;
        lock (_gate)
        {
            snapshot = _entries.Values.ToArray();
        }

        var filtered = snapshot.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(folder))
        {
            filtered = filtered.Where(item => string.Equals(item.Folder, folder, StringComparison.CurrentCultureIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(item =>
                item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                item.Folder.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        }

        var sorted = filtered.OrderBy(item => item.Folder, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        var folders = snapshot.Select(item => item.Folder).Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.CurrentCultureIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase).ToArray();
        return new LibraryPage(sorted.Skip(offset).Take(limit).ToArray(), folders, sorted.Length, offset, limit);
    }

    public async Task<IReadOnlyList<LibraryEntry>> ScanFolderAsync(
        string path,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException(path);
        }

        var files = EnumerateMidiFiles(path).ToArray();
        var folder = new DirectoryInfo(path).Name;
        var added = new List<LibraryEntry>();
        var processed = 0;
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount * 2, 4, 24),
        };

        await Parallel.ForEachAsync(files, parallelOptions, async (file, token) =>
        {
            var id = GenerateId(file);
            lock (_gate)
            {
                if (_entries.ContainsKey(id))
                {
                    var current = Interlocked.Increment(ref processed);
                    progress?.Report(new ScanProgress(current, files.Length, added.Count, Path.GetFileNameWithoutExtension(file)));
                    return;
                }
            }

            try
            {
                var parsed = await _midiReader.ReadAsync(file, token);
                var entry = new LibraryEntry(id, Path.GetFullPath(file), Path.GetFileNameWithoutExtension(file), folder,
                    parsed.DurationMs, parsed.Notes.Count, DateTimeOffset.Now);
                lock (_gate)
                {
                    if (_entries.TryAdd(id, entry))
                    {
                        added.Add(entry);
                    }
                }
            }
            catch (Exception) when (!token.IsCancellationRequested)
            {
                // A bad MIDI file does not abort the rest of a large folder scan.
            }
            finally
            {
                var current = Interlocked.Increment(ref processed);
                int count;
                lock (_gate) count = added.Count;
                progress?.Report(new ScanProgress(current, files.Length, count, Path.GetFileNameWithoutExtension(file)));
            }
        });

        if (added.Count > 0)
        {
            await SaveAsync(cancellationToken);
        }

        return added.ToArray();
    }

    public async Task<LibraryEntry?> AddAsync(string path, string? folder = null, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("MIDI file was not found.", path);
        }

        var id = GenerateId(path);
        lock (_gate)
        {
            if (_entries.TryGetValue(id, out var existing))
            {
                return existing;
            }
        }

        var parsed = await _midiReader.ReadAsync(path, cancellationToken);
        var entry = new LibraryEntry(id, Path.GetFullPath(path), Path.GetFileNameWithoutExtension(path), folder ?? "默认",
            parsed.DurationMs, parsed.Notes.Count, DateTimeOffset.Now);
        lock (_gate) _entries[id] = entry;
        await SaveAsync(cancellationToken);
        return entry;
    }

    public async Task<bool> RemoveAsync(string id, CancellationToken cancellationToken = default)
    {
        bool removed;
        lock (_gate) removed = _entries.Remove(id);
        if (removed) await SaveAsync(cancellationToken);
        return removed;
    }

    public async Task<bool> DeleteSourceAsync(string id, CancellationToken cancellationToken = default)
    {
        LibraryEntry? entry;
        lock (_gate) _entries.TryGetValue(id, out entry);
        if (entry is null) return false;

        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(entry.Path)) File.Delete(entry.Path);
        return await RemoveAsync(id, cancellationToken);
    }

    public async Task<int> RemoveFolderAsync(string folder, CancellationToken cancellationToken = default)
    {
        int removed;
        lock (_gate)
        {
            var ids = _entries.Values.Where(item => string.Equals(item.Folder, folder, StringComparison.CurrentCultureIgnoreCase))
                .Select(item => item.Id).ToArray();
            foreach (var id in ids) _entries.Remove(id);
            removed = ids.Length;
        }

        if (removed > 0) await SaveAsync(cancellationToken);
        return removed;
    }

    public async Task<int> ClearAsync(CancellationToken cancellationToken = default)
    {
        int count;
        lock (_gate)
        {
            count = _entries.Count;
            _entries.Clear();
        }

        await SaveAsync(cancellationToken);
        return count;
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            Dictionary<string, LibraryEntry> snapshot;
            lock (_gate) snapshot = new Dictionary<string, LibraryEntry>(_entries, StringComparer.OrdinalIgnoreCase);
            await AtomicJsonFile.WriteAsync(_libraryPath, new LibraryDocument(1, snapshot, DateTimeOffset.Now), cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private static IEnumerable<string> EnumerateMidiFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            IEnumerable<string> files;
            IEnumerable<string> directories;
            try
            {
                files = Directory.EnumerateFiles(directory).ToArray();
                directories = Directory.EnumerateDirectories(directory).ToArray();
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var file in files)
            {
                var extension = Path.GetExtension(file);
                if (extension.Equals(".mid", StringComparison.OrdinalIgnoreCase) || extension.Equals(".midi", StringComparison.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }

            foreach (var child in directories) pending.Push(child);
        }
    }

    private static string GenerateId(string path)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(path));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..12];
    }

    private sealed record LibraryDocument(int SchemaVersion, Dictionary<string, LibraryEntry> Entries, DateTimeOffset UpdatedAt);

    private sealed record LegacyLibraryDocument(
        [property: JsonPropertyName("entries")] Dictionary<string, LegacyLibraryEntry?>? Entries);

    private sealed record LegacyLibraryEntry(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("path")] string? Path,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("folder")] string? Folder,
        [property: JsonPropertyName("duration_ms")] int DurationMs,
        [property: JsonPropertyName("notes")] int Notes,
        [property: JsonPropertyName("added_at")] string? AddedAt);
}

using System.Text;

namespace Evaluatr.Core.Graders;

/// <summary>
/// Bounded, inspection-only workspace operations for judge adapters. Paths and
/// symlink targets are confined to the workspace; concurrent replacement races
/// require an OS sandbox and are outside this abstraction's guarantees.
/// </summary>
public sealed class WorkspaceInspection(string workingDirectory)
{
    private const int MaxCharacters = 16000;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    /// <summary>Reads a UTF-8 character range, with a zero-based offset and at most 16,000 characters.</summary>
    public async Task<WorkspaceReadResult> ReadAsync(string path, int offset = 0, int count = MaxCharacters,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (offset is < 0 or > 1000000 || count is < 1 or > MaxCharacters)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset must be 0–1,000,000 and count 1–16,000.");
        }

        var file = Resolve(path);
        if (!WorkspaceFile.IsFile(file)) throw new IOException("Requested path is not a file.");

        using var reader = new StreamReader(File.OpenRead(file), Utf8, detectEncodingFromByteOrderMarks: false);
        var buffer = new char[4096];
        var remaining = offset;
        while (remaining > 0)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, Math.Min(remaining, buffer.Length)), cancellationToken);
            if (read == 0) return new WorkspaceReadResult("", null);
            remaining -= read;
        }

        var content = new char[count + 1];
        var length = 0;
        while (length < content.Length)
        {
            var read = await reader.ReadAsync(content.AsMemory(length), cancellationToken);
            if (read == 0) break;
            length += read;
        }

        var returned = Math.Min(count, length);
        return new WorkspaceReadResult(new string(content, 0, returned), length > count ? offset + count : null);
    }

    /// <summary>Lists at most 200 immediate entry names, without following directory links.</summary>
    public WorkspaceDirectoryResult List(string path = ".", CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Resolve(path);
        if (!Directory.Exists(directory)) throw new IOException("Requested path is not a directory.");

        var entries = new List<string>();
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entries.Count == 200)
            {
                entries.Sort(StringComparer.Ordinal);
                return new WorkspaceDirectoryResult(entries, true);
            }
            entries.Add(Path.GetFileName(entry));
        }

        entries.Sort(StringComparer.Ordinal);
        return new WorkspaceDirectoryResult(entries, false);
    }

    /// <summary>Finds literal case-sensitive text in one UTF-8 file of at most 256 KiB; returns up to 100 line numbers.</summary>
    public async Task<WorkspaceSearchResult> SearchAsync(string path, string text, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(text) || text.Length > MaxCharacters)
        {
            throw new ArgumentException("Search text must contain 1–16,000 characters.", nameof(text));
        }

        var file = Resolve(path);
        if (!WorkspaceFile.IsFile(file)) throw new IOException("Requested path is not a file.");

        await using var stream = File.OpenRead(file);
        if (stream.Length > 262144) throw new IOException("Search is limited to files of at most 256 KiB; use read_file ranges instead.");

        // Bound the read itself as well as checking length, since files may grow.
        using var reader = new StreamReader(stream, Utf8, detectEncodingFromByteOrderMarks: false);
        var buffer = new char[262145];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(length), cancellationToken);
            if (read == 0) break;
            length += read;
        }
        if (length == buffer.Length) throw new IOException("File exceeded the search size limit while reading.");

        var content = new string(buffer, 0, length);
        var lines = new List<int>();
        var position = 0;
        var line = 1;
        var consumed = 0;
        while ((position = content.IndexOf(text, position, StringComparison.Ordinal)) >= 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (lines.Count == 100) return new WorkspaceSearchResult(lines, true);
            line += content.AsSpan(consumed, position - consumed).Count('\n');
            lines.Add(line);
            consumed = position;
            position += text.Length;
        }

        return new WorkspaceSearchResult(lines, false);
    }

    private string Resolve(string path) => WorkspaceFile.Resolve(workingDirectory, path);
}

/// <summary>Read content and the next character offset when more content remains.</summary>
public sealed record WorkspaceReadResult(string Content, int? NextOffset);

/// <summary>Immediate names and whether the entry limit was reached.</summary>
public sealed record WorkspaceDirectoryResult(IReadOnlyList<string> Entries, bool Truncated);

/// <summary>One-based match line numbers and whether the match limit was reached.</summary>
public sealed record WorkspaceSearchResult(IReadOnlyList<int> Lines, bool Truncated);

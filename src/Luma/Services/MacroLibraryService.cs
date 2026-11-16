using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Luma.Services;

public sealed record SavedMacro(string Name, DateTimeOffset UpdatedAt)
{
    public override string ToString() => $"{Name}  ·  {UpdatedAt:g}";
}

/// <summary>Named, local-only presets. Names never become filesystem paths and
/// MacroService.ExportJson refuses plaintext input values and unsafe URLs.</summary>
public sealed class MacroLibraryService(string dataRoot)
{
    private readonly string _folder = Path.Combine(Path.GetFullPath(dataRoot), "Macros");
    private const int MaxFileBytes = 1024 * 1024;

    private sealed record StoredMacro(string Name, string StepsJson, DateTimeOffset UpdatedAt);

    public static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var normalized = name.Trim().Normalize(NormalizationForm.FormC);
        if (normalized.Length > 60 || normalized.Any(char.IsControl) ||
            normalized.IndexOfAny(['/', '\\', '<', '>', ':', '"', '|', '?', '*']) >= 0)
            throw new ArgumentException("Use a short name without path separators or control characters.");
        return normalized;
    }

    private string FileFor(string name)
    {
        var key = NormalizeName(name).ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(_folder, hash + ".json");
    }

    private static void RejectLink(string path)
    {
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Linked macro files are not supported.");
    }

    public async Task SaveAsync(string name, MacroService macro, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(macro);
        if (macro.Steps.Count == 0) throw new InvalidOperationException("Add at least one step before saving a named macro.");
        name = NormalizeName(name);
        var safeJson = macro.ExportJson();
        var content = JsonSerializer.Serialize(new StoredMacro(name, safeJson, DateTimeOffset.Now));
        if (Encoding.UTF8.GetByteCount(content) > MaxFileBytes)
            throw new InvalidOperationException("Named macro is too large.");
        Directory.CreateDirectory(_folder);
        var path = FileFor(name);
        RejectLink(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, content, new UTF8Encoding(false), token);
            token.ThrowIfCancellationRequested();
            RejectLink(path);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task LoadAsync(string name, MacroService macro, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(macro);
        var path = FileFor(name);
        var saved = await ReadAsync(path, token) ?? throw new FileNotFoundException("Saved macro was not found.");
        if (!string.Equals(NormalizeName(saved.Name), NormalizeName(name), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Saved macro name does not match its identifier.");
        macro.ImportJson(saved.StepsJson);
    }

    public async Task<IReadOnlyList<SavedMacro>> ListAsync(CancellationToken token = default)
    {
        if (!Directory.Exists(_folder)) return [];
        var result = new List<SavedMacro>();
        foreach (var path in Directory.EnumerateFiles(_folder, "*.json").Take(201))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var entry = await ReadAsync(path, token);
                if (entry is not null && FileFor(entry.Name) == path)
                    result.Add(new SavedMacro(entry.Name, entry.UpdatedAt));
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or ArgumentException or IOException)
            { /* Ignore malformed local entries; never execute them while listing. */ }
        }
        return result.OrderByDescending(item => item.UpdatedAt).Take(200).ToArray();
    }

    private static async Task<StoredMacro?> ReadAsync(string path, CancellationToken token)
    {
        if (!File.Exists(path)) return null;
        RejectLink(path);
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > MaxFileBytes) throw new InvalidDataException("Macro file is too large.");
        using var reader = new StreamReader(file, new UTF8Encoding(false, true));
        var json = await reader.ReadToEndAsync(token);
        return JsonSerializer.Deserialize<StoredMacro>(json)
            ?? throw new InvalidDataException("Macro file is empty.");
    }
}

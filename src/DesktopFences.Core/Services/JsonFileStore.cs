using System.Text.Json;

namespace DesktopFences.Core.Services;

/// <summary>
/// Atomic JSON file IO helpers. Centralizes the read-or-default and
/// write-via-temp-file-then-rename patterns previously duplicated in every
/// Save/Load method on <see cref="JsonLayoutStore"/>.
/// </summary>
internal static class JsonFileStore
{
    /// <summary>
    /// Deserialize <typeparamref name="T"/> from <paramref name="path"/>, or
    /// return <see langword="default"/> if the file does not exist.
    /// </summary>
    public static async Task<T?> ReadAsync<T>(string path, JsonSerializerOptions options)
    {
        if (!File.Exists(path)) return default;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, options);
    }

    /// <summary>
    /// Atomically write <paramref name="value"/> as JSON to <paramref name="path"/>:
    /// serialize to <c>path + ".tmp"</c>, then rename. Prevents corruption if the
    /// process is killed mid-write.
    /// </summary>
    public static async Task WriteAtomicAsync<T>(string path, T value, JsonSerializerOptions options)
    {
        var tempPath = path + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, value, options);
        }
        File.Move(tempPath, path, overwrite: true);
    }
}

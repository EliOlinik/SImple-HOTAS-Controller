using System.Text;

namespace HotasController.Core.Storage;

/// <summary>
/// Reads and writes the app's data files defensively: never follows a link, never reads more than
/// <see cref="DataFileLimits.MaxFileBytes"/>, and writes atomically.
/// </summary>
internal static class SafeJsonFile
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Returns the text of the file, or null when it does not exist or cannot be used. <paramref name="problem"/> is
    /// set (to a short reason) only when the file exists but must not be used.
    /// </summary>
    public static string? TryRead(string path, out string? problem)
    {
        problem = null;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return null;
            if (IsLink(info))
            {
                problem = "it is a link, not a regular file";
                return null;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
            {
                if (buffer.Length + read > DataFileLimits.MaxFileBytes)
                {
                    problem = $"it is larger than {DataFileLimits.MaxFileBytes / (1024 * 1024)} MB";
                    return null;
                }
                buffer.Write(chunk, 0, read);
            }

            var bytes = buffer.GetBuffer();
            var length = (int)buffer.Length;
            var offset = length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0; // skip a UTF-8 BOM
            return StrictUtf8.GetString(bytes, offset, length - offset);
        }
        catch (DecoderFallbackException)
        {
            problem = "it is not valid UTF-8 text";
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problem = $"it could not be read ({ex.Message})";
            return null;
        }
    }

    /// <summary>Keeps a copy of a damaged file next to it as <c>name.corrupt</c>. Best effort; never copies a link or a huge file.</summary>
    public static bool TryBackup(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || IsLink(info) || info.Length > DataFileLimits.MaxFileBytes) return false;
            File.Copy(path, path + ".corrupt", overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Writes a temp file next to the target, then moves it over the target. Refuses to write through links.</summary>
    public static void WriteAtomic(string path, string contents)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(dir);

        if (IsLink(new DirectoryInfo(dir)))
            throw new IOException($"Refusing to write to \"{dir}\" because it is a link.");
        var target = new FileInfo(full);
        if (target.Exists && IsLink(target))
            throw new IOException($"Refusing to write to \"{full}\" because it is a link.");

        var tmp = Path.Combine(dir, Path.GetFileName(full) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(tmp, contents);
            File.Move(tmp, full, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ }
        }
    }

    /// <summary>True for symbolic links and junctions (anything with the reparse-point attribute).</summary>
    public static bool IsLink(FileSystemInfo info) => info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0;
}

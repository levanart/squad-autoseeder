using System.IO;
using System.Text.RegularExpressions;

namespace Autoseeder.Client.Services;

internal sealed partial class AppLog : IDisposable
{
    private const long MaxFileBytes = 2 * 1024 * 1024;
    private const int MaxFiles = 10;
    private readonly object _sync = new();
    private readonly string _directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "5thMR", "Autoseeder", "logs");
    private StreamWriter? _writer;
    private string? _activePath;

    public event Action<string>? EntryWritten;

    public void Write(string message)
    {
        var safeMessage = Sanitize(message);
        var entry = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] {safeMessage}";

        lock (_sync)
        {
            try
            {
                EnsureWriter(entry.Length);
                _writer!.WriteLine(entry);
                _writer.Flush();
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        EntryWritten?.Invoke(entry);
    }

    public void Write(Exception exception, string? context = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Write(string.IsNullOrWhiteSpace(context)
            ? exception.ToString()
            : $"{context}: {exception}");
    }

    private void EnsureWriter(int incomingCharacters)
    {
        Directory.CreateDirectory(_directory);
        if (_writer is not null && _activePath is not null)
        {
            var estimatedBytes = new FileInfo(_activePath).Length + incomingCharacters * 3L;
            if (estimatedBytes < MaxFileBytes)
                return;
            _writer.Dispose();
            _writer = null;
        }

        var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        _activePath = Path.Combine(_directory, $"autoseeder-{stamp}-{Guid.NewGuid():N}.log");
        _writer = new StreamWriter(new FileStream(_activePath, FileMode.Append, FileAccess.Write, FileShare.Read));
        CleanupOldFiles();
    }

    private void CleanupOldFiles()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_directory, "autoseeder-*.log")
                         .OrderByDescending(File.GetLastWriteTimeUtc)
                         .Skip(MaxFiles))
                File.Delete(file);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    internal static string Sanitize(string value)
    {
        var result = JsonSecretRegex().Replace(value, "$1\"[REDACTED]\"");
        result = AuthCookieRegex().Replace(result, "$1[REDACTED]");
        result = BearerTokenRegex().Replace(result, "$1[REDACTED]");
        result = SensitiveParameterRegex().Replace(result, "$1[REDACTED]");
        return JwtRegex().Replace(result, "[REDACTED-JWT]");
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    [GeneratedRegex("(?i)(bearer\\s+)[^\\s,;]+")]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex("(?i)(\"(?:access_token|refresh_token|token|secret)\"\\s*:\\s*)(?:\"(?:\\\\.|[^\"\\\\])*\"|[^,\\s}\\]]+)")]
    private static partial Regex JsonSecretRegex();

    [GeneratedRegex("(?i)((?:discord5thmr\\.(?:refresh|auth))\\s*=\\s*)[^;\\s,]+")]
    private static partial Regex AuthCookieRegex();

    [GeneratedRegex("(?i)((?:token|refresh|access_token|refresh_token|jwt|secret)[=:]\\s*)[^&\\s,;]+")]
    private static partial Regex SensitiveParameterRegex();

    [GeneratedRegex("eyJ[A-Za-z0-9_-]+\\.[A-Za-z0-9_-]+\\.[A-Za-z0-9_-]+")]
    private static partial Regex JwtRegex();
}

using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace DotNetLab.M3UProxyApp;

[ProviderAlias("File")]
internal sealed class FileLogProvider : ILoggerProvider
{
    private readonly object writeLock = new();
    private readonly FileLogOptions options;
    private readonly LogRedactor redactor;
    private readonly Action<string> reportFailure;
    private readonly string filePath;
    private FileStream? stream;
    private DateTime fileDate;

    public FileLogProvider(FileLogOptions options, LogRedactor redactor, Action<string>? reportFailure = null)
    {
        if (options.MaxFileSizeBytes < 256 || options.RetainedFileCount < 1)
        {
            throw new InvalidOperationException("Logging:File requires MaxFileSizeBytes >= 256 and RetainedFileCount >= 1.");
        }

        this.options = options;
        this.redactor = redactor;
        this.reportFailure = reportFailure ?? Console.Error.WriteLine;
        filePath = Path.GetFullPath(options.Path, AppContext.BaseDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        fileDate = File.Exists(filePath) ? File.GetLastWriteTimeUtc(filePath).Date : DateTime.UtcNow.Date;
        stream = OpenFile();

        try
        {
            ApplyRetention();
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public ILogger CreateLogger(string categoryName) => new LogEntryLogger(categoryName, redactor, Write);

    public void Dispose()
    {
        lock (writeLock)
        {
            try
            {
                stream?.Dispose();
            }
            catch (IOException exception)
            {
                ReportFailure(exception);
            }

            stream = null;
        }
    }

    private void Write(string entry)
    {
        lock (writeLock)
        {
            if (stream is null)
            {
                return;
            }

            try
            {
                var bytes = Encoding.UTF8.GetBytes(entry + Environment.NewLine);

                if (bytes.LongLength > options.MaxFileSizeBytes)
                {
                    bytes = Encoding.UTF8.GetBytes($"{DateTimeOffset.UtcNow:O} [Warning] Log entry omitted because it exceeds MaxFileSizeBytes.{Environment.NewLine}");
                }

                if (stream.Length > 0 && (stream.Length + bytes.LongLength > options.MaxFileSizeBytes || fileDate < DateTime.UtcNow.Date))
                {
                    Rotate();
                }

                stream.Write(bytes);
                stream.Flush();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                ReportFailure(exception);

                try
                {
                    stream?.Dispose();
                }
                catch (IOException)
                {
                }

                stream = null;
            }
        }
    }

    private FileStream OpenFile() => new(filePath, FileMode.Append, FileAccess.Write, FileShare.Read);

    private void Rotate()
    {
        stream!.Dispose();
        var archivePath = $"{filePath}.archive-{DateTime.UtcNow:yyyyMMddHHmmssfffffff}-{Guid.NewGuid():N}";
        File.Move(filePath, archivePath);
        fileDate = DateTime.UtcNow.Date;
        stream = OpenFile();
        ApplyRetention();
    }

    private void ApplyRetention()
    {
        var directory = Path.GetDirectoryName(filePath)!;
        var prefix = Path.GetFileName(filePath) + ".archive-";
        var pattern = $"^{Regex.Escape(prefix)}[0-9]{{21}}-[a-f0-9]{{32}}$";
        var archives = Directory.EnumerateFiles(directory).Where(path => Regex.IsMatch(Path.GetFileName(path), pattern)).OrderByDescending(path => Path.GetFileName(path), StringComparer.Ordinal).Skip(options.RetainedFileCount - 1);

        foreach (var archive in archives)
        {
            File.Delete(archive);
        }
    }

    private void ReportFailure(Exception exception) => reportFailure($"File logging failed and has been disabled: {redactor.Redact(exception.Message)}");
}

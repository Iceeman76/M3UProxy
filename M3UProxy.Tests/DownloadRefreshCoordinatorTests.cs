using M3UProxyApp;
using NUnit.Framework;

namespace M3UProxy.Tests;

public sealed class DownloadRefreshCoordinatorTests
{
    [Test]
    public void NeedsUpdate_ReturnsFalseWhenAllFilesAreFresh()
    {
        var paths = CreateTemporaryFiles();
        var utcNow = DateTime.UtcNow;
        paths.ForEach(path => File.SetLastWriteTimeUtc(path, utcNow.AddMinutes(-30)));

        try
        {
            var coordinator = CreateCoordinator(paths, TimeSpan.FromHours(1));

            var result = coordinator.NeedsUpdate(utcNow);

            Assert.That(result, Is.False);
        }
        finally
        {
            paths.ForEach(File.Delete);
        }
    }

    [Test]
    public void NeedsUpdate_ReturnsTrueWhenAFileIsTooOld()
    {
        var paths = CreateTemporaryFiles();
        var utcNow = DateTime.UtcNow;
        File.SetLastWriteTimeUtc(paths[0], utcNow.AddMinutes(-30));
        File.SetLastWriteTimeUtc(paths[1], utcNow.AddHours(-1));

        try
        {
            var coordinator = CreateCoordinator(paths, TimeSpan.FromHours(1));

            var result = coordinator.NeedsUpdate(utcNow);

            Assert.That(result, Is.True);
        }
        finally
        {
            paths.ForEach(File.Delete);
        }
    }

    [Test]
    public void NeedsUpdate_ReturnsTrueWhenAFileIsMissing()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"missing-m3u-proxy-{Guid.NewGuid():N}.m3u");
        var coordinator = CreateCoordinator([missingPath], TimeSpan.FromHours(1));

        var result = coordinator.NeedsUpdate(DateTime.UtcNow);

        Assert.That(result, Is.True);
    }

    private static DownloadRefreshCoordinator CreateCoordinator(IReadOnlyList<string> paths, TimeSpan maxAge)
    {
        var jobs = paths.Select((path, index) => ProxyTestFactory.CreatePlaylist($"Playlist {index}", filePath: path)).ToArray();

        return new DownloadRefreshCoordinator(jobs, maxAge);
    }

    private static List<string> CreateTemporaryFiles()
    {
        var paths = Enumerable.Range(0, 2).Select(_ => Path.Combine(Path.GetTempPath(), $"m3u-proxy-{Guid.NewGuid():N}.m3u")).ToList();
        paths.ForEach(path => File.WriteAllText(path, "#EXTM3U\n"));

        return paths;
    }
}

using Caly.Core.Utilities;
using System;
using System.IO;

namespace Caly.Tests;

public class CalyFileMutexTests
{
    [Fact]
    public void SecondInstanceIsRefusedUntilFirstReleases()
    {
        string path = Path.Combine(Path.GetTempPath(), $"caly_lock_test_{Guid.NewGuid():N}.lock");

        // A lock file left behind by a crashed instance must not block anyone.
        File.WriteAllText(path, string.Empty);

        try
        {
            var first = new CalyFileMutex(path);
            var second = new CalyFileMutex(path);

            Assert.True(first.WaitOne(TimeSpan.Zero, true));
            Assert.False(second.WaitOne(TimeSpan.Zero, true));

            if (OperatingSystem.IsWindows())
            {
                // A live lock cannot be deleted out from under its owner (Unix allows the unlink,
                // but the lock lives on the open file, so it holds anyway).
                Assert.ThrowsAny<IOException>(() => File.Delete(path));
            }

            first.ReleaseMutex();
            Assert.True(second.WaitOne(TimeSpan.Zero, true));
            second.ReleaseMutex();
        }
        finally
        {
            File.Delete(path);
        }
    }
}


// Copyright (c) 2025 BobLd
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.IO;
using Caly.Core.Services;

namespace Caly.Core.Utilities;

// Known issue with named System.Threading.Mutex on Linux/macOS with AOT
// See https://github.com/dotnet/runtime/issues/110348
// https://www.martyndavis.com/?p=440

// Solution:
// https://mvolo.com/fun-with-file-locking/

/// <summary>
/// File mutex implementation to ensure cross-platform compatibility.
/// <para>
/// Known issue with named System.Threading.Mutex on Linux/macOS with AOT.
/// See <see href="https://github.com/dotnet/runtime/issues/110348"/> and
/// <see href="https://www.martyndavis.com/?p=440"/>
/// </para>
/// </summary>
public sealed class CalyFileMutex
{
    // Per user, not the temp folder: on Linux that is the shared /tmp, where one user's lock blocked every other user.
    private static readonly string DefaultLockFilePath = Path.Combine(JsonSettingsService.SettingsFilePath, "caly.lock");

    private readonly string _lockFilePath;

    private FileStream? _lockFile;

    /// <summary>
    /// Same signature as default System.Threading.Mutex.
    /// </summary>
    /// <param name="initiallyOwned">Not in use.</param>
    /// <param name="name">Not in use.</param>
    public CalyFileMutex(bool initiallyOwned, string? name) : this(DefaultLockFilePath)
    {
    }

    /// <summary>
    /// Locks a specific file (used by tests).
    /// </summary>
    internal CalyFileMutex(string lockFilePath)
    {
        _lockFilePath = lockFilePath;
    }

    /// <summary>
    /// Takes the lock if no other process holds it.
    /// </summary>
    /// <param name="timeout">Not in use.</param>
    /// <param name="b">Not in use.</param>
    public bool WaitOne(TimeSpan timeout, bool b)
    {
        // Same signature as default System.Threading.Mutex

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_lockFilePath)!);

            // FileShare.None is an OS lock (sharing mode on Windows, flock on Unix) that dies with
            // the process, so a crashed instance leaves no stale lock and nobody can delete a live one.
            _lockFile = new FileStream(_lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            // Held by another instance
            return false;
        }

        return true;
    }

    public void ReleaseMutex()
    {
        if (_lockFile is null)
        {
            throw new InvalidOperationException("Cannot release file mutex, because it is not held.");
        }

        // The file is left in place: on Unix, deleting it would let a newcomer lock a fresh file
        // while a second newcomer still holds the unlinked one, giving two main instances.
        _lockFile.Dispose();
        _lockFile = null;
    }
}

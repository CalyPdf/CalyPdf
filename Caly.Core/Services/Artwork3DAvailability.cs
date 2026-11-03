// Copyright (c) BobLd
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

using System.Threading;

namespace Caly.Core.Services;

/// <summary>
/// Whether interactive 3D was found to be unavailable in this run of the application.
/// <para>
/// 3D views are drawn with OpenGL, which Avalonia offers only under its GPU renderer. Nothing tells in
/// advance whether it will work (software rendering by choice, a platform without it, a driver that
/// refuses), so it is learnt: the first 3D control that never gets its OpenGL context sets this, and
/// from then on 3D artwork says so instead of trying.
/// </para>
/// </summary>
internal static class Artwork3DAvailability
{
    public const string UnavailableMessage = "3D needs the GPU renderer.";

    private static int _unavailable;

    public static bool IsUnavailable => Volatile.Read(ref _unavailable) != 0;

    public static void MarkUnavailable() => Volatile.Write(ref _unavailable, 1);

    /// <summary>A 3D control drew: whatever was thought before, 3D works.</summary>
    public static void MarkAvailable() => Volatile.Write(ref _unavailable, 0);
}

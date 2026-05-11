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

using Caly.Core.Models;
using Caly.Core.Services.Interfaces;
using static Caly.Core.Models.CalySettings;

namespace Caly.UiTests.TestDoubles;

public sealed class TestSettingsService : ISettingsService
{
    private CalySettings _current = Default;

    public void Reset() => _current = Default;

    public void SetProperty(CalySettingsProperty property, object value) { /* no-op */ }
    public CalySettings GetSettings() => _current;
    public ValueTask<CalySettings> GetSettingsAsync() => new(_current);
    public void Load() { /* no-op */ }
    public Task LoadAsync() => Task.CompletedTask;
    public void Save() { /* no-op */ }
    public Task SaveAsync() => Task.CompletedTask;
}

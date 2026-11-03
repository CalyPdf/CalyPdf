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

using Avalonia;
using Avalonia.Threading;
using Caly.Scene3D.Avalonia;
using Caly.Core.Models;
using Caly.Core.Services;
using Caly.Core.Services.Interfaces;
using Caly.Pdf.Artwork;
using Caly.Pdf.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Caly.Rendering.Scene3D;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UglyToad.PdfPig.Artwork;
using UglyToad.PdfPig.Core;

namespace Caly.Core.ViewModels;

public enum Artwork3DState : byte
{
    /// <summary>The model is being read out of the document for the first time.</summary>
    Loading,

    /// <summary>There is a scene to draw.</summary>
    Ready
}

/// <summary>
/// A piece of 3D artwork the user is viewing in place on its page. At most one per document.
/// <para>
/// Owns the scene the 3D control draws and the artwork's data. The control takes the scene's geometry to
/// the GPU and loses it there whenever its page leaves the screen, so the data is kept to read the scene
/// again when the page comes back, without going to the document.
/// </para>
/// <para>
/// Everything but the reading itself happens on the UI thread.
/// </para>
/// </summary>
public sealed partial class Artwork3DViewModel : ViewModelBase, IDisposable
{
    /// <summary>The last entry of <see cref="ViewNames"/>: no authored view, the whole model framed.</summary>
    public const string WholeModelName = "Whole model";

    /// <summary>Said of artwork in a format there is no reader for (U3D and PRC are read).</summary>
    internal const string UnsupportedFormatMessage = "This 3D model is in a format that is not supported.";

    /// <summary>Said of a model a part of which could not be decoded, when the reader says no more.</summary>
    internal const string IncompleteMessage = "Part of this 3D model could not be decoded.";

    /// <summary>
    /// How long a 3D control may stay without OpenGL before 3D is called unavailable. The control says
    /// "unavailable" half a second after it appears, and takes it back if OpenGL was only slow to
    /// start: a verdict that lasts for the whole run waits longer than that.
    /// </summary>
    internal static TimeSpan UnavailableGrace { get; set; } = TimeSpan.FromSeconds(3);

    private readonly IPdfDocumentService _pdfService;
    private readonly PdfArtwork3D _artwork;
    private readonly Action<Artwork3DViewModel, string?> _close;
    private readonly CancellationTokenSource _cts = new();

    private IReadOnlyList<SceneView> _views = [];
    private ReadOnlyMemory<byte> _payload;
    private Artwork3DFormat _format;
    private Scene3DStatus _controlStatus;
    private bool _isReading;
    private bool _isDisposed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading))]
    private Artwork3DState _state = Artwork3DState.Loading;

    [ObservableProperty] private Scene? _scene;

    [ObservableProperty] private IReadOnlyList<string> _viewNames = [];

    /// <summary>
    /// The camera and animation playhead the view was left with when its page last left the screen, for
    /// the view built when the page comes back. Opaque here; null until a view has been left.
    /// </summary>
    [ObservableProperty] private Scene3DCameraState? _cameraState;

    /// <summary>
    /// Whether one of the artwork's views draws edges found from the geometry (silhouettes or every edge): the
    /// control finds them while it still holds the geometry. A render mode chosen later that draws them has the
    /// model read again.
    /// </summary>
    [ObservableProperty] private bool _hasEdgeViews;

    /// <summary>The render mode the model is drawn in: the selected view's, until the reader chooses another.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RenderModeIndex))]
    private SceneRenderMode _renderMode;

    /// <summary>The names of the render modes, in the order of <see cref="SceneRenderMode"/>, for the choice of one.</summary>
    public static IReadOnlyList<string> RenderModeNames { get; } =
    [
        "Solid", "Solid wireframe", "Transparent", "Transparent wireframe", "Bounding box", "Transparent bounding box",
        "Transparent bounding box outline", "Wireframe", "Shaded wireframe", "Hidden wireframe", "Vertices",
        "Shaded vertices", "Illustration", "Solid outline", "Shaded illustration"
    ];

    /// <summary><see cref="RenderMode"/> as an index into <see cref="RenderModeNames"/>.</summary>
    public int RenderModeIndex
    {
        get => (int)RenderMode;
        set
        {
            if ((uint)value < (uint)RenderModeNames.Count)
            {
                RenderMode = (SceneRenderMode)value;
            }
        }
    }

    // Choosing a view draws it as the document asks.
    partial void OnSelectedViewIndexChanged(int value) => RenderMode = SelectedView?.RenderMode ?? SceneRenderMode.Solid;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedView))]
    private int _selectedViewIndex = -1;

    // The reader's changes to how the model is lit and shaded (the lighting panel's sliders). They start
    // from the file's values for each artwork and are not saved.

    /// <summary>Strength of the lights, 1 as the file has them.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Shading))]
    private double _lightIntensity = 1;

    /// <summary>Grey light added to the ambient light, 0 to 1.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Shading))]
    private double _addedAmbient;

    /// <summary>Brightness of the highlights, 1 as the file has them.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Shading))]
    private double _specularIntensity = 1;

    /// <summary>Size of the highlights in steps of two: 0 as the file has them, above 0 broader, below tighter.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Shading))]
    [NotifyPropertyChangedFor(nameof(HighlightSize))]
    private double _highlightSizeSteps;

    /// <summary>How many times broader the highlights are than the file has them.</summary>
    public double HighlightSize => Math.Pow(2, HighlightSizeSteps);

    /// <summary>How much of their own colour surfaces show, 1 as the file has it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Shading))]
    private double _diffuseIntensity = 1;

    /// <summary>What the sliders make of the file's lights and materials, for the 3D control.</summary>
    public Scene3DShading Shading => new()
    {
        LightIntensity = (float)LightIntensity,
        AddedAmbient = (float)AddedAmbient,
        SpecularIntensity = (float)SpecularIntensity,
        HighlightSharpness = (float)(1 / HighlightSize),
        DiffuseIntensity = (float)DiffuseIntensity
    };

    /// <param name="close">
    /// Closes this view: called with a message for the user when the view cannot go on, with
    /// <c>null</c> when it simply ends.
    /// </param>
    internal Artwork3DViewModel(Artwork3DActivationRequest request, IPdfDocumentService pdfService,
        Action<Artwork3DViewModel, string?> close)
    {
        ArgumentNullException.ThrowIfNull(request);
        _artwork = request.Artwork;
        _pdfService = pdfService;
        _close = close;
        PageNumber = request.PageNumber;
        ArtworkIndex = request.ArtworkIndex;
        Bounds = GetBounds(request.Artwork.BoundingBox);
    }

    public int PageNumber { get; }

    /// <summary>
    /// The artwork's position in its page's <see cref="PdfTextLayer.Artworks3D"/>. This, not the
    /// <see cref="PdfArtwork3D"/> object, says which artwork is active: text layers are dropped and
    /// built again as pages come and go.
    /// </summary>
    public int ArtworkIndex { get; }

    /// <summary>The rectangle the view fills, in the page's layer coordinates.</summary>
    public Rect Bounds { get; }

    public bool IsLoading => State == Artwork3DState.Loading;

    /// <summary>
    /// Why the model cannot be shown, when reading it said so: a verdict on the model, which another
    /// attempt would only repeat. Null for failures of the attempt (the document, the control).
    /// </summary>
    internal string? ModelRefusal { get; private set; }

    /// <summary>The authored view to show, or <c>null</c> for the whole model.</summary>
    public SceneView? SelectedView => (uint)SelectedViewIndex < (uint)_views.Count ? _views[SelectedViewIndex] : null;

    /// <summary>The reading in progress or last done; for tests.</summary>
    internal Task Completion { get; private set; } = Task.CompletedTask;

    /// <summary>Starts reading the model out of the document.</summary>
    internal void Load()
    {
        Debug.ThrowNotOnUiThread();
        if (_isDisposed || _isReading)
        {
            return;
        }

        _isReading = true;
        Completion = Task.Run(LoadAsync);
    }

    private async Task LoadAsync()
    {
        CancellationToken token = _cts.Token;
        Artwork3DContent? content = null;
        Scene? scene = null;
        IReadOnlyList<SceneView> views = [];
        string? failure = null;
        string? refusal = null;
        bool silent = false;
        try
        {
            content = await _pdfService.GetArtwork3DContentAsync(_artwork, token).ConfigureAwait(false);
            if (content is null)
            {
                // The document is closed or closing.
                silent = true;
            }
            else
            {
                failure = refusal = Read(content.Format, content.Payload, token, out scene);
                if (scene is not null)
                {
                    views = Artwork3DSceneReader.ResolveViews(content.Format, scene, content.Views);
                }
            }
        }
        catch (OperationCanceledException)
        {
            silent = true;
        }
        catch (Artwork3DContentException e)
        {
            failure = CouldNotBeRead(e.Message);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // A document broken in a way nothing foresaw. The view must still end, with a reason,
            // rather than say "Loading" for ever while the exception goes unobserved.
            scene?.Dispose();
            scene = null;
            failure = CouldNotBeRead(e.Message);
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _isReading = false;
            if (_isDisposed)
            {
                scene?.Dispose();
                return;
            }

            if (scene is null || content is null)
            {
                ModelRefusal = silent ? null : refusal;
                _close(this, silent ? null : failure);
                return;
            }

            _payload = content.Payload;
            _format = content.Format;
            _views = views;

            var names = new string[content.ViewNames.Count + 1];
            for (int i = 0; i < names.Length - 1; i++)
            {
                names[i] = content.ViewNames[i];
            }

            names[^1] = WholeModelName;
            ViewNames = names;

            // The index may be the one it already has while the views behind it are new.
            SelectedViewIndex = (uint)content.DefaultViewIndex < (uint)_views.Count ? content.DefaultViewIndex : _views.Count;
            OnPropertyChanged(nameof(SelectedView));

            // Before the scene: the control finds its outlines while it still holds its geometry.
            bool hasEdgeViews = false;
            foreach (SceneView view in _views)
            {
                hasEdgeViews |= Scene3DControl.DrawsEdges(view.RenderMode);
            }

            HasEdgeViews = hasEdgeViews;
            RenderMode = SelectedView?.RenderMode ?? SceneRenderMode.Solid;
            Scene = scene;
            State = Artwork3DState.Ready;
        });
    }

    /// <summary>Reads the kept data again into a scene with its geometry.</summary>
    private void Reload()
    {
        if (_isDisposed || _isReading || _payload.IsEmpty)
        {
            return;
        }

        _isReading = true;
        Artwork3DFormat format = _format;
        ReadOnlyMemory<byte> payload = _payload;
        CancellationToken token = _cts.Token;
        Completion = Task.Run(async () =>
        {
            Scene? scene = null;
            string? failure = null;
            try
            {
                failure = Read(format, payload, token, out scene);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                // As when first read: the view must end with a reason, not stay blank with the task
                // faulted and nobody to read it again.
                scene?.Dispose();
                scene = null;
                failure = CouldNotBeRead(e.Message);
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _isReading = false;
                if (_isDisposed)
                {
                    scene?.Dispose();
                    return;
                }

                if (scene is null)
                {
                    ModelRefusal = failure;
                    _close(this, failure);
                    return;
                }

                // The control lets go of the old scene when it is given the new one; only then may the
                // old one be disposed.
                Scene? old = Scene;
                Scene = scene;
                old?.Dispose();
            });
        });
    }

    /// <summary>The scene, or the message that says why there is none.</summary>
    private static string? Read(Artwork3DFormat format, ReadOnlyMemory<byte> payload, CancellationToken token, out Scene? scene)
    {
        switch (Artwork3DSceneReader.TryRead(format, payload, out scene, out string? detail, token))
        {
            case Artwork3DReadResult.Ok:
                return null;
            case Artwork3DReadResult.NoGeometry:
                return detail ?? "This 3D model has no geometry Caly can show yet.";
            case Artwork3DReadResult.Incomplete:
                return detail ?? IncompleteMessage;
            case Artwork3DReadResult.NoDisplayMesh:
                return "This 3D model has no display mesh (exact geometry only).";
            case Artwork3DReadResult.UnsupportedFormat:
                return UnsupportedFormatMessage;
            default:
                return CouldNotBeRead(detail);
        }
    }

    private static string CouldNotBeRead(string? detail)
    {
        return string.IsNullOrEmpty(detail) ? "This 3D model could not be read." : "This 3D model could not be read: " + detail;
    }

    /// <summary>Puts the lights and materials back as the file has them.</summary>
    [RelayCommand]
    private void ResetShading()
    {
        LightIntensity = 1;
        AddedAmbient = 0;
        SpecularIntensity = 1;
        HighlightSizeSteps = 0;
        DiffuseIntensity = 1;
    }

    [RelayCommand]
    private void Close()
    {
        if (!_isDisposed)
        {
            _close(this, null);
        }
    }

    /// <summary>What the 3D control showing this artwork reports about itself.</summary>
    [RelayCommand]
    private void ControlStatusChanged(Artwork3DControlStatus status)
    {
        Debug.ThrowNotOnUiThread();
        if (_isDisposed)
        {
            return;
        }

        if (!status.OnPage)
        {
            // Nothing the control said stands once it is off the page: in particular a slow OpenGL start
            // reported as "unavailable" must not become a verdict for the whole run.
            _controlStatus = Scene3DStatus.Empty;
            return;
        }

        _controlStatus = status.Status;
        switch (status.Status)
        {
            case Scene3DStatus.NeedsScene:
                // The control lost its GPU resources (its page left the screen and came back) after the
                // scene had given its geometry away.
                Reload();
                break;

            case Scene3DStatus.Ready:
                Artwork3DAvailability.MarkAvailable();
                break;

            case Scene3DStatus.Failed:
                _close(this, string.IsNullOrEmpty(status.Message) ? "The 3D view failed." : "The 3D view failed: " + status.Message);
                break;

            case Scene3DStatus.Unavailable:
                _ = CloseIfStillUnavailableAsync();
                break;
        }
    }

    private async Task CloseIfStillUnavailableAsync()
    {
        try
        {
            await Task.Delay(UnavailableGrace, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // Back on the UI thread (the command ran there). Anything the control said since stands.
        if (!_isDisposed && _controlStatus == Scene3DStatus.Unavailable)
        {
            Artwork3DAvailability.MarkUnavailable();
            _close(this, Artwork3DAvailability.UnavailableMessage);
        }
    }

    /// <summary>Stops any reading, disposes the scene and lets go of the artwork's data.</summary>
    public void Dispose()
    {
        Debug.ThrowNotOnUiThread();
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _cts.Cancel();

        // First out of the control's hands (it is bound to Scene), then disposed.
        Scene? scene = Scene;
        Scene = null;
        scene?.Dispose();

        _payload = default;
        _views = [];
    }

    private static Rect GetBounds(PdfRectangle box)
    {
        double left = Math.Min(Math.Min(box.TopLeft.X, box.TopRight.X), Math.Min(box.BottomLeft.X, box.BottomRight.X));
        double right = Math.Max(Math.Max(box.TopLeft.X, box.TopRight.X), Math.Max(box.BottomLeft.X, box.BottomRight.X));
        double top = Math.Min(Math.Min(box.TopLeft.Y, box.TopRight.Y), Math.Min(box.BottomLeft.Y, box.BottomRight.Y));
        double bottom = Math.Max(Math.Max(box.TopLeft.Y, box.TopRight.Y), Math.Max(box.BottomLeft.Y, box.BottomRight.Y));
        return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }
}

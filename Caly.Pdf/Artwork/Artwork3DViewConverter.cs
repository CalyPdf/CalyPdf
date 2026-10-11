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

using System.Numerics;
using Scene3D;
using UglyToad.PdfPig.Artwork;

namespace Caly.Pdf.Artwork;

/// <summary>
/// Turns a 3D view of a PDF document (ISO 32000-2, 13.6.4) into a <see cref="SceneView"/>: plain data a
/// renderer can use without knowing PDF.
/// <para>
/// Reads the view's lazily parsed parts, so it must be called where the document may be read: while the
/// page is processed, under the document lock.
/// </para>
/// </summary>
public static class Artwork3DViewConverter
{
    /// <summary>
    /// Converts a view.
    /// </summary>
    /// <param name="view">The view.</param>
    /// <param name="viewWidth">Width of the annotation's 3D view box, in its own units; 0 if unknown.</param>
    /// <param name="viewHeight">Height of the annotation's 3D view box; 0 if unknown.</param>
    public static SceneView Convert(Artwork3DView view, double viewWidth, double viewHeight)
    {
        ArgumentNullException.ThrowIfNull(view);

        // Without a matrix selection the view has no camera of its own (the artwork's default applies);
        // with one, a path to a view node is preferred and the matrix, where given too, is the fallback.
        Matrix4x4? cameraToWorld = null;
        IReadOnlyList<string> cameraNodePath = [];
        if (view.MatrixSource != Artwork3DMatrixSource.None)
        {
            if (view.CameraToWorld is { } matrix)
            {
                cameraToWorld = ToCameraToWorld(matrix);
            }

            if (view.MatrixSource == Artwork3DMatrixSource.U3DPath)
            {
                cameraNodePath = view.U3DPath;
            }
        }

        SceneViewProjection? projection = null;
        if (view.Projection is { } p)
        {
            projection = ToProjection(p.Type, p.FieldOfView, p.PerspectiveScaling, p.PerspectiveScalingValue, p.OrthographicScale, p.OrthographicBinding,
                p.ClippingStyle, p.Near, p.Far, viewWidth, viewHeight);
        }

        SceneNodeOverride[] overrides = [];
        if (view.Nodes.Count > 0)
        {
            overrides = new SceneNodeOverride[view.Nodes.Count];
            for (int i = 0; i < overrides.Length; i++)
            {
                Artwork3DNode node = view.Nodes[i];
                overrides[i] = new SceneNodeOverride(node.Name, node.Visible, (float?)node.Opacity, node.Matrix is { } m ? ToTransform(m) : null);
            }
        }

        return new SceneView
        {
            Name = view.ExternalName ?? view.InternalName ?? "",
            CameraToWorld = cameraToWorld,
            CameraNodePath = cameraNodePath,
            OrbitDistance = (float?)view.CenterOfOrbit,
            Projection = projection,
            Background = view.Background is { } background ? new Vector3((float)background.Color.R, (float)background.Color.G, (float)background.Color.B) : null,
            Lighting = ToLighting(view.LightingScheme?.Style ?? Artwork3DLightingStyle.Artwork),
            NodeOverrides = overrides
        };
    }

    /// <summary>
    /// A camera-to-world matrix as a scene camera pose.
    /// <para>
    /// A PDF camera looks along its +Z axis with +Y up; a scene camera looks along −Z with +Y up. Both
    /// spaces are right-handed, so PDF's +X is the viewer's left and the scene's the viewer's right: the
    /// X and Z axes change sign, a half turn about Y. Checked against documents that give each view both
    /// as a matrix and as a U3D view node.
    /// </para>
    /// </summary>
    public static Matrix4x4 ToCameraToWorld(Artwork3DMatrix m)
    {
        return new Matrix4x4(
            -(float)m.A, -(float)m.B, -(float)m.C, 0,
            (float)m.D, (float)m.E, (float)m.F, 0,
            -(float)m.G, -(float)m.H, -(float)m.I, 0,
            (float)m.Tx, (float)m.Ty, (float)m.Tz, 1);
    }

    /// <summary>
    /// A 3D transformation matrix of PDF as a <see cref="Matrix4x4"/>. The conventions agree: points are
    /// rows and the translation is the last row.
    /// </summary>
    public static Matrix4x4 ToTransform(Artwork3DMatrix m)
    {
        return new Matrix4x4(
            (float)m.A, (float)m.B, (float)m.C, 0,
            (float)m.D, (float)m.E, (float)m.F, 0,
            (float)m.G, (float)m.H, (float)m.I, 0,
            (float)m.Tx, (float)m.Ty, (float)m.Tz, 1);
    }

    /// <summary>
    /// A projection dictionary as a projection that does not depend on the size it is shown at.
    /// </summary>
    /// <param name="viewWidth">Width of the 3D view box, needed for sizes given in its units; 0 if unknown.</param>
    /// <param name="viewHeight">Height of the 3D view box; 0 if unknown.</param>
    public static SceneViewProjection ToProjection(
        Artwork3DProjectionType type,
        double? fieldOfView,
        Artwork3DScaling perspectiveScaling,
        double? perspectiveScalingValue,
        double orthographicScale,
        Artwork3DScaling orthographicBinding,
        Artwork3DClippingStyle clippingStyle,
        double? near,
        double? far,
        double viewWidth,
        double viewHeight)
    {
        // Explicit clip planes are distances along the view direction in both conventions.
        bool explicitPlanes = clippingStyle == Artwork3DClippingStyle.ExplicitNearFar;
        float? nearPlane = explicitPlanes ? (float?)near : null;
        float? farPlane = explicitPlanes ? (float?)far : null;
        bool sizeKnown = viewWidth > 0 && viewHeight > 0;

        if (type == Artwork3DProjectionType.Orthographic)
        {
            SceneViewBinding binding;
            double scale = orthographicScale;
            if (orthographicBinding == Artwork3DScaling.Absolute && sizeKnown)
            {
                // "scale" view-box units per scene unit: the height shows viewHeight / scale scene units.
                binding = SceneViewBinding.Height;
                scale = orthographicScale / viewHeight;
            }
            else
            {
                binding = ToBinding(orthographicBinding, SceneViewBinding.Min);
            }

            return new SceneViewProjection(SceneProjection.Orthographic, 0, binding, (float)scale, nearPlane, farPlane);
        }

        double degrees = fieldOfView ?? 0;
        if (perspectiveScaling == Artwork3DScaling.Value && perspectiveScalingValue is > 0 && sizeKnown && degrees is > 0 and < 180)
        {
            // The cone of the field of view covers a circle of the given diameter in view-box units; the
            // cone that covers the height instead is narrower or wider in proportion.
            double half = Math.Atan(Math.Tan(degrees * Math.PI / 360) * viewHeight / perspectiveScalingValue.Value);
            return new SceneViewProjection(SceneProjection.Perspective, (float)(half * 360 / Math.PI), SceneViewBinding.Height, 1, nearPlane, farPlane);
        }

        return new SceneViewProjection(SceneProjection.Perspective, (float)degrees, ToBinding(perspectiveScaling, SceneViewBinding.Width), 1, nearPlane, farPlane);
    }

    /// <summary>The lighting scheme a lighting style names.</summary>
    public static SceneLighting ToLighting(Artwork3DLightingStyle style) => style switch
    {
        Artwork3DLightingStyle.None => SceneLighting.None,
        Artwork3DLightingStyle.White => SceneLighting.White,
        Artwork3DLightingStyle.Day => SceneLighting.Day,
        Artwork3DLightingStyle.Night => SceneLighting.Night,
        Artwork3DLightingStyle.Hard => SceneLighting.Hard,
        Artwork3DLightingStyle.Primary => SceneLighting.Primary,
        Artwork3DLightingStyle.Blue => SceneLighting.Blue,
        Artwork3DLightingStyle.Red => SceneLighting.Red,
        Artwork3DLightingStyle.Cube => SceneLighting.Cube,
        Artwork3DLightingStyle.CAD => SceneLighting.Cad,
        Artwork3DLightingStyle.Headlamp => SceneLighting.Headlamp,
        _ => SceneLighting.Artwork
    };

    private static SceneViewBinding ToBinding(Artwork3DScaling scaling, SceneViewBinding fallback) => scaling switch
    {
        Artwork3DScaling.Width => SceneViewBinding.Width,
        Artwork3DScaling.Height => SceneViewBinding.Height,
        Artwork3DScaling.Min => SceneViewBinding.Min,
        Artwork3DScaling.Max => SceneViewBinding.Max,
        _ => fallback
    };
}

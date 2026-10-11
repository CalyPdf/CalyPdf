namespace Caly.Avalonia.Scene3D.Internal.Gl;

/// <summary>
/// The one shader program: the classic ambient / diffuse / specular model the 3D formats describe, per
/// pixel, in camera space, with two-sided lighting.
/// </summary>
internal static class Shaders
{
    public const int AttributePosition = 0, AttributeNormal = 1, AttributeTexCoord = 2, AttributeColor = 3;

    public static readonly string[] Attributes = ["aPosition", "aNormal", "aTexCoord", "aColor"];

    // Bits of uFlags.
    public const int FlagLit = 1, FlagHasNormals = 2, FlagVertexColor = 4, FlagTexture = 8, FlagTextureReplaces = 16, FlagAlphaTest = 32, FlagOrthographic = 64;

    /// <summary>The first lines of a shader: OpenGL ES 3.0 or desktop OpenGL 3.2 core.</summary>
    public static string Header(bool embedded) => embedded ? "#version 300 es\nprecision highp float;\nprecision highp int;\n" : "#version 150\n";

    public const string Vertex = """
        uniform mat4 uModelView;
        uniform mat4 uProjection;
        uniform mat3 uNormalMatrix;
        uniform mat4 uTexTransform;

        in vec3 aPosition;
        in vec3 aNormal;
        in vec2 aTexCoord;
        in vec4 aColor;

        out vec3 vPosition;
        out vec3 vNormal;
        out vec2 vTexCoord;
        out vec4 vColor;

        void main()
        {
            vec4 position = uModelView * vec4(aPosition, 1.0);
            vPosition = position.xyz;
            vNormal = uNormalMatrix * aNormal;
            vTexCoord = (uTexTransform * vec4(aTexCoord, 0.0, 1.0)).xy;
            vColor = aColor;
            gl_Position = uProjection * position;
        }
        """;

    public const string Fragment = """
        const int MAX_LIGHTS = 8;

        uniform int uFlags;
        uniform vec3 uDiffuse;
        uniform vec3 uSpecular;
        uniform vec3 uEmissive;
        uniform float uOpacity;
        uniform float uShininess;
        uniform float uAlphaCutoff;
        uniform sampler2D uTexture;

        uniform vec3 uAmbientLight;
        uniform int uLightCount;
        uniform vec4 uLightPosition[MAX_LIGHTS];
        uniform vec3 uLightColor[MAX_LIGHTS];
        uniform vec3 uLightAttenuation[MAX_LIGHTS];
        uniform vec4 uLightSpot[MAX_LIGHTS];

        in vec3 vPosition;
        in vec3 vNormal;
        in vec2 vTexCoord;
        in vec4 vColor;

        out vec4 oColor;

        void main()
        {
            vec3 base = uDiffuse;
            float alpha = uOpacity;

            if ((uFlags & 4) != 0)
            {
                base *= vColor.rgb;
                alpha *= vColor.a;
            }

            if ((uFlags & 8) != 0)
            {
                vec4 texel = texture(uTexture, vTexCoord);
                base = (uFlags & 16) != 0 ? texel.rgb : base * texel.rgb;
                alpha *= texel.a;
            }

            if ((uFlags & 32) != 0 && alpha < uAlphaCutoff)
            {
                discard;
            }

            vec3 color = base;
            if ((uFlags & 1) != 0)
            {
                // Without normals the surface is shaded flat, from how its position changes across the pixel.
                vec3 normal = (uFlags & 2) != 0 ? vNormal : cross(dFdx(vPosition), dFdy(vPosition));
                float normalLength = length(normal);
                normal = normalLength > 0.0 ? normal / normalLength : vec3(0.0, 0.0, 1.0);

                // Towards the viewer; whichever side of the surface faces the viewer is the lit one.
                vec3 toViewer = (uFlags & 64) != 0 ? vec3(0.0, 0.0, 1.0) : normalize(-vPosition);
                if (dot(normal, toViewer) < 0.0)
                {
                    normal = -normal;
                }

                color = uEmissive + base * uAmbientLight;
                for (int i = 0; i < MAX_LIGHTS; i++)
                {
                    if (i >= uLightCount)
                    {
                        break;
                    }

                    vec3 toLight;
                    float strength = 1.0;
                    if (uLightPosition[i].w == 0.0)
                    {
                        toLight = uLightPosition[i].xyz;
                    }
                    else
                    {
                        vec3 offset = uLightPosition[i].xyz - vPosition;
                        float distance = length(offset);
                        toLight = distance > 0.0 ? offset / distance : vec3(0.0, 0.0, 1.0);
                        vec3 k = uLightAttenuation[i];
                        strength = 1.0 / max(k.x + k.y * distance + k.z * distance * distance, 0.0001);
                        if (dot(-toLight, uLightSpot[i].xyz) < uLightSpot[i].w)
                        {
                            strength = 0.0;
                        }
                    }

                    float diffuse = max(dot(normal, toLight), 0.0);
                    if (diffuse > 0.0 && strength > 0.0)
                    {
                        vec3 halfway = normalize(toLight + toViewer);
                        float specular = pow(max(dot(normal, halfway), 0.0), uShininess);
                        color += (base * diffuse + uSpecular * specular) * uLightColor[i] * strength;
                    }
                }
            }

            // Premultiplied, as the surface is composited.
            oColor = vec4(clamp(color, 0.0, 1.0) * alpha, alpha);
        }
        """;
}

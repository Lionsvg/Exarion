// ===========================================================================
//  Exarion - Ciel
//
//  Un degrade zenith -> horizon, plus un halo autour du soleil.
//
//  POURQUOI CA COMPTE : un fond noir uni ne ressemble a rien et supprime
//  toute profondeur. Un simple degrade suffit a donner une direction a la
//  lumiere et une echelle a l'espace, pour un cout quasi nul.
//
//  TECHNIQUE : un triangle plein ecran, sans tampon de sommets. On
//  reconstruit la direction du rayon par pixel en inversant la matrice
//  vue-projection. Dessine EN PREMIER, sans ecriture de profondeur, donc
//  toute la geometrie passe devant.
// ===========================================================================

using System;
using System.Numerics;
using Silk.NET.OpenGL;

namespace Exarion.Engine.Graphics
{
    public sealed class SkyRenderer : IDisposable
    {
        // Astuce classique : un seul triangle qui couvre l'ecran, genere
        // depuis gl_VertexID. Aucun tampon de sommets a allouer.
        private const string VertexSource = @"#version 330 core

uniform mat4 uInverseViewProj;
uniform vec3 uCameraPos;

out vec3 vRayDir;

void main()
{
    vec2 pos = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2) * 2.0 - 1.0;
    gl_Position = vec4(pos, 1.0, 1.0);

    // Reconstruction de la direction du rayon a travers ce pixel.
    vec4 near = uInverseViewProj * vec4(pos, -1.0, 1.0);
    vec4 far  = uInverseViewProj * vec4(pos,  1.0, 1.0);
    vRayDir = normalize(far.xyz / far.w - near.xyz / near.w);
}";

        private const string FragmentSource = @"#version 330 core

in vec3 vRayDir;

uniform vec3  uZenith;
uniform vec3  uHorizon;
uniform vec3  uGround;
uniform vec3  uSunDir;      // direction VERS le soleil
uniform vec3  uSunColor;
uniform float uSunSize;

out vec4 FragColor;

vec3 toSrgb(vec3 c) { return pow(max(c, 0.0), vec3(1.0 / 2.2)); }

void main()
{
    vec3 d = normalize(vRayDir);

    // Degrade vertical. La puissance concentre la transition pres de
    // l'horizon, comme dans un vrai ciel.
    float up = d.y;
    vec3 sky = mix(uHorizon, uZenith, pow(clamp(up, 0.0, 1.0), 0.45));

    // Sous l'horizon : une teinte sol sombre, pour ne pas voir un ciel
    // clair sous les pieds quand on regarde par-dessus un bord.
    sky = mix(sky, uGround, clamp(-up * 4.0, 0.0, 1.0));

    // Halo solaire : un lobe large plus un coeur intense.
    float cosSun = max(dot(d, normalize(uSunDir)), 0.0);
    float glow   = pow(cosSun, max(uSunSize, 1.0));
    float core   = pow(cosSun, max(uSunSize * 40.0, 1.0));

    sky += uSunColor * (glow * 0.35 + core * 6.0) * step(0.0, up + 0.1);

    // En LINEAIRE, comme la geometrie : c'est HdrTarget.Compose qui
    // applique l'exposition, la courbe ACES et le sRGB, une seule fois pour
    // toute l'image. Le ciel y passe desormais lui aussi - il y echappait,
    // et son halo solaire ne pouvait donc pas alimenter le bloom.
    FragColor = vec4(sky, 1.0);
}";

        private readonly GL _gl;
        private readonly ShaderProgram _shader;
        private readonly uint _emptyVao;

        /// <summary>Couleurs en lineaire, pas en sRGB : le shader convertit a la fin.</summary>
        public Vector3 Zenith = new(0.045f, 0.075f, 0.16f);
        public Vector3 Horizon = new(0.22f, 0.26f, 0.34f);
        public Vector3 Ground = new(0.02f, 0.02f, 0.025f);
        public Vector3 SunColor = new(1.0f, 0.85f, 0.62f);

        /// <summary>Plus la valeur est grande, plus le halo est resserre.</summary>
        public float SunSize = 24f;

        public SkyRenderer(GL gl)
        {
            _gl = gl;
            _shader = new ShaderProgram(gl, VertexSource, FragmentSource);

            // OpenGL core exige un VAO lie, meme sans attribut.
            _emptyVao = _gl.GenVertexArray();
        }

        public void Draw(Camera camera, Vector3 sunDirection)
        {
            if (!Matrix4x4.Invert(camera.GetViewProjection(), out Matrix4x4 inverse))
                return;

            // Le ciel est infiniment loin : on lit la profondeur mais on ne
            // l'ecrit pas, sinon il masquerait la geometrie dessinee apres.
            _gl.DepthMask(false);
            _gl.Disable(EnableCap.DepthTest);

            _shader.Use();
            _shader.SetUniform("uInverseViewProj", inverse);
            _shader.SetUniform("uCameraPos", camera.Position);
            _shader.SetUniform("uZenith", Zenith);
            _shader.SetUniform("uHorizon", Horizon);
            _shader.SetUniform("uGround", Ground);
            _shader.SetUniform("uSunDir", -Vector3.Normalize(sunDirection));
            _shader.SetUniform("uSunColor", SunColor);
            _shader.SetUniform("uSunSize", SunSize);

            _gl.BindVertexArray(_emptyVao);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
            _gl.BindVertexArray(0);

            _gl.Enable(EnableCap.DepthTest);
            _gl.DepthMask(true);
        }

        public void Dispose()
        {
            _gl.DeleteVertexArray(_emptyVao);
            _shader.Dispose();
        }
    }
}

// ===========================================================================
//  Exarion - Rendu des particules
//
//  Chaque particule est un QUADRILATERE TOUJOURS FACE A LA CAMERA - un
//  "billboard". C'est la technique de base, et elle n'est pas un pis-aller :
//  une etincelle ou une bouffee de fumee n'ont pas de forme propre a montrer,
//  seulement une position et une taille.
//
//  ---------------------------------------------------------------------
//  UN SEUL QUADRILATERE, DESSINE N FOIS - L'INSTANCIATION.
//
//  Envoyer quatre sommets complets par particule, c'est reecrire son centre,
//  sa taille et sa couleur quatre fois. En instanciation, le quadrilatere est
//  envoye UNE FOIS pour toutes et seules les donnees qui changent d'une
//  particule a l'autre voyagent : 32 octets au lieu de 160. Le diviseur
//  d'attribut (glVertexAttribDivisor) est dans le coeur d'OpenGL 3.3.
//
//  Le quadrilatere est oriente DANS LE NUANCEUR, a partir des vecteurs droite
//  et haut de la camera. Le processeur n'a donc aucun calcul de sommet a
//  faire, et changer d'orientation - face a la camera, aligne sur la vitesse -
//  tiendrait en une ligne.
//  ---------------------------------------------------------------------
//
//  ⚠️ LE TRI. Le melange ADDITIF est independant de l'ordre : additionner des
//  lumieres donne le meme total dans n'importe quel sens. La transparence
//  ordinaire, NON - deux bouffees de fumee superposees ne donnent pas la meme
//  image selon celle qu'on dessine en premier. Les particules opaques sont
//  donc triees de l'arriere vers l'avant, les additives ne le sont pas.
// ===========================================================================

using System;
using System.Numerics;
using Silk.NET.OpenGL;
using Exarion.Core.Effects;

namespace Exarion.Engine.Graphics
{
    public sealed class ParticleRenderer : IDisposable
    {
        private const string VertexSource = @"#version 330 core

layout(location = 0) in vec2 aCorner;        // le quadrilatere, partage
layout(location = 1) in vec4 aCenterSize;    // xyz = centre monde, w = taille
layout(location = 2) in vec4 aColor;         // rgb en HDR, a = opacite

uniform mat4 uViewProjection;
uniform vec3 uRight;
uniform vec3 uUp;

out vec2 vCorner;
out vec4 vColor;

void main()
{
    // Face a la camera : on etale le coin le long de SES axes a elle. La
    // particule n'a donc pas d'orientation propre a maintenir, et ne se
    // reduit jamais a un trait quand on tourne autour.
    vec3 world = aCenterSize.xyz + (uRight * aCorner.x + uUp * aCorner.y) * aCenterSize.w;

    gl_Position = uViewProjection * vec4(world, 1.0);

    vCorner = aCorner;
    vColor = aColor;
}";

        private const string FragmentSource = @"#version 330 core

in vec2 vCorner;
in vec4 vColor;

out vec4 FragColor;

void main()
{
    // Disque a bord doux, CALCULE et non texture. Une belle texture de fumee
    // serait du contenu, et il n'y en a pas encore ; surtout, un carre net
    // se lit immediatement comme un carre, ce qui ruine l'effet. Le degrade
    // suffit a faire un grain.
    float d = length(vCorner) * 2.0;

    float mask = 1.0 - smoothstep(0.45, 1.0, d);

    // Hors du disque, on ne calcule rien : sur des milliers de particules qui
    // se recouvrent, la moitie des pixels d'un quadrilatere sont perdus.
    if (mask <= 0.002)
        discard;

    FragColor = vec4(vColor.rgb, vColor.a * mask);
}";

        /// <summary>Ce qui change d'une particule a l'autre. 32 octets.</summary>
        private struct Instance
        {
            public Vector4 CenterSize;
            public Vector4 Color;

            public const int SizeInBytes = 8 * sizeof(float);
        }

        private readonly GL _gl;

        private readonly ShaderProgram _shader;
        private readonly uint _vao;
        private readonly uint _quadVbo;
        private readonly uint _quadEbo;
        private readonly uint _instanceVbo;

        private readonly Instance[] _instances;
        private readonly float[] _depthKeys;

        public ParticleRenderer(GL gl, int capacity)
        {
            _gl = gl;

            _instances = new Instance[capacity];
            _depthKeys = new float[capacity];

            _shader = new ShaderProgram(gl, VertexSource, FragmentSource);

            // --- Le quadrilatere, une fois pour toutes ---
            // Les coins vont de -0,5 a +0,5 : ils servent A LA FOIS de decalage
            // et de coordonnee pour le disque du fragment. Deux usages, un
            // seul attribut.
            Span<float> corners = stackalloc float[]
            {
                -0.5f, -0.5f,
                 0.5f, -0.5f,
                 0.5f,  0.5f,
                -0.5f,  0.5f,
            };

            Span<ushort> indices = stackalloc ushort[] { 0, 1, 2, 0, 2, 3 };

            _vao = _gl.GenVertexArray();
            _gl.BindVertexArray(_vao);

            unsafe
            {
                _quadVbo = _gl.GenBuffer();
                _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _quadVbo);
                fixed (float* p = corners)
                    _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(corners.Length * sizeof(float)),
                                   p, BufferUsageARB.StaticDraw);

                _gl.EnableVertexAttribArray(0);
                _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), (void*)0);

                _quadEbo = _gl.GenBuffer();
                _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _quadEbo);
                fixed (ushort* p = indices)
                    _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indices.Length * sizeof(ushort)),
                                   p, BufferUsageARB.StaticDraw);

                // --- Le tampon d'instances, reecrit a chaque image ---
                // Alloue UNE FOIS a sa taille maximale : reallouer chaque image
                // ferait travailler le pilote pour rien.
                _instanceVbo = _gl.GenBuffer();
                _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceVbo);
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(capacity * Instance.SizeInBytes),
                               null, BufferUsageARB.DynamicDraw);

                _gl.EnableVertexAttribArray(1);
                _gl.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, Instance.SizeInBytes, (void*)0);

                _gl.EnableVertexAttribArray(2);
                _gl.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, Instance.SizeInBytes,
                                        (void*)(4 * sizeof(float)));
            }

            // LE point de l'instanciation : ces deux attributs avancent d'un
            // cran par PARTICULE et non par sommet. Sans ces deux lignes, les
            // quatre coins liraient quatre particules differentes et le
            // quadrilatere serait etire a travers toute la scene.
            _gl.VertexAttribDivisor(1, 1);
            _gl.VertexAttribDivisor(2, 1);

            _gl.BindVertexArray(0);
        }

        /// <summary>Particules effectivement dessinees a la derniere image.</summary>
        public int LastDrawn { get; private set; }

        /// <summary>
        /// Dessine toutes les particules vivantes.
        ///
        /// A appeler DANS le tampon HDR, apres la geometrie opaque : une
        /// etincelle a une couleur bien au-dela de 1,0, et c'est ce qui lui
        /// donne son halo au moment du bloom. Dessinee apres le tone mapping,
        /// elle serait un simple point blanc.
        /// </summary>
        /// <param name="alpha">Fraction de pas ecoulee, pour l'interpolation.</param>
        public unsafe void Draw(ReadOnlySpan<Particle> particles, Camera camera, float alpha)
        {
            LastDrawn = 0;

            if (particles.Length == 0)
                return;

            // Les opaques se remplissent depuis le debut, les additives depuis
            // la fin : une seule passe de tri, un seul tableau, et les deux
            // groupes se retrouvent contigus.
            int opaqueCount = 0;
            int additiveStart = _instances.Length;

            Vector3 eye = camera.Position;

            for (int i = 0; i < particles.Length; i++)
            {
                ref readonly Particle p = ref particles[i];

                float opacity = p.Alpha;

                if (opacity <= 0.002f)
                    continue;

                Vector3 center = p.At(alpha);

                var instance = new Instance
                {
                    CenterSize = new Vector4(center, p.Size),
                    Color = new Vector4(p.Color, opacity),
                };

                if (p.Additive)
                {
                    if (additiveStart <= opaqueCount)
                        continue;

                    _instances[--additiveStart] = instance;
                }
                else
                {
                    if (opaqueCount >= additiveStart)
                        continue;

                    // Le carre de la distance suffit : on ne compare que des
                    // rangs, et la racine ne changerait aucun ordre.
                    _depthKeys[opaqueCount] = -Vector3.DistanceSquared(center, eye);
                    _instances[opaqueCount] = instance;
                    opaqueCount++;
                }
            }

            int additiveCount = _instances.Length - additiveStart;

            if (opaqueCount + additiveCount == 0)
                return;

            // De l'arriere vers l'avant. La cle est l'oppose de la distance,
            // donc un tri croissant place bien les plus lointaines en premier.
            if (opaqueCount > 1)
                Array.Sort(_depthKeys, _instances, 0, opaqueCount);

            _gl.BindVertexArray(_vao);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceVbo);

            _shader.Use();
            _shader.SetUniform("uViewProjection", camera.GetViewProjection());
            _shader.SetUniform("uRight", camera.Right);
            _shader.SetUniform("uUp", camera.Up);

            _gl.Enable(EnableCap.Blend);

            // ⚠️ On teste la profondeur mais on ne l'ECRIT PAS. Le test garde
            // les particules derriere les murs ; l'ecriture, elle, ferait que
            // la premiere particule dessinee masquerait toutes celles qui
            // passent derriere - et comme elles sont transparentes, ce serait
            // un trou noir en forme de disque.
            _gl.Enable(EnableCap.DepthTest);
            _gl.DepthMask(false);

            if (opaqueCount > 0)
            {
                _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                Upload(0, opaqueCount);
                _gl.DrawElementsInstanced(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedShort,
                                          (void*)0, (uint)opaqueCount);
            }

            if (additiveCount > 0)
            {
                // Additif : la source S'AJOUTE au fond. C'est ce que fait une
                // source de lumiere, et c'est aussi pourquoi l'ordre n'a
                // aucune importance ici.
                _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
                Upload(additiveStart, additiveCount);
                _gl.DrawElementsInstanced(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedShort,
                                          (void*)0, (uint)additiveCount);
            }

            // Retour a l'etat que le reste du moteur attend. Un DepthMask
            // laisse a false rendrait le tampon de profondeur inutilisable a
            // l'image suivante, et le decor se dessinerait dans le desordre.
            _gl.DepthMask(true);
            _gl.Disable(EnableCap.Blend);
            _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            _gl.BindVertexArray(0);

            LastDrawn = opaqueCount + additiveCount;
        }

        private unsafe void Upload(int start, int count)
        {
            fixed (Instance* p = &_instances[start])
            {
                _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0,
                                  (nuint)(count * Instance.SizeInBytes), p);
            }
        }

        public void Dispose()
        {
            _gl.DeleteBuffer(_quadVbo);
            _gl.DeleteBuffer(_quadEbo);
            _gl.DeleteBuffer(_instanceVbo);
            _gl.DeleteVertexArray(_vao);
            _shader.Dispose();
        }
    }
}

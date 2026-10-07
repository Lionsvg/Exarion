// ===========================================================================
//  Exarion - Pipeline de rendu
//
//  ---------------------------------------------------------------------
//  POURQUOI CETTE CLASSE EXISTE
//
//  L'enchainement des passes - ombre, normales, occlusion, ciel, geometrie,
//  particules, resolution, halo, composition, interface - vivait dans les
//  cent cinquante lignes de OnRender du bac a sable. Ca marchait, et ca
//  rendait le moteur INUTILISABLE PAR QUELQU'UN D'AUTRE : ecrire un second
//  jeu voulait dire recopier cet enchainement, donc en maintenir deux
//  versions qui divergeraient.
//
//  Le jeu ne fournit plus que DEUX choses :
//     - sa geometrie brute, pour les passes qui n'ont besoin que de formes
//       (ombre, profondeur) ;
//     - son dessin final, materiaux et teintes comprises.
//
//  Tout le reste - l'ordre des passes, les tampons, les chronometres GPU -
//  appartient au moteur, et il n'en existe qu'une copie.
//  ---------------------------------------------------------------------
//
//  ⚠️ L'ORDRE DES PASSES N'EST PAS NEGOCIABLE, et chaque contrainte a coute
//  une session de mise au point :
//
//   - le CIEL passe avant la geometrie, sans ecrire la profondeur ;
//   - les PARTICULES passent apres la geometrie opaque (pour se cacher
//     derriere les murs) mais AVANT le tone mapping (pour que leur couleur
//     au-dela de 1,0 alimente le halo) ;
//   - le HALO se calcule sur le tampon RESOLU, un tampon multi-echantillonne
//     ne se lisant pas comme une texture ;
//   - l'INTERFACE arrive apres la composition, sinon la courbe ACES la
//     delaverait.
// ===========================================================================

using System;
using System.Numerics;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Exarion.Core.Effects;

namespace Exarion.Engine.Graphics
{
    /// <summary>
    /// Recoit un maillage a dessiner. Les os valent null pour un objet rigide.
    ///
    /// Un delegue plutot qu'une interface : les passes de geometrie ne font
    /// rien d'autre que recevoir des triplets, et le jeu les fournit souvent
    /// depuis une simple boucle.
    /// </summary>
    public delegate void GeometrySink(Mesh mesh, Matrix4x4 model, Matrix4x4[]? bones);

    /// <summary>
    /// Ce qu'un jeu doit savoir dessiner pour que le pipeline fasse le reste.
    /// </summary>
    public interface IWorldRenderer
    {
        /// <summary>
        /// TOUTE la geometrie, sans materiau.
        ///
        /// Appele plusieurs fois par image - une fois par passe qui ne veut que
        /// des formes. ⚠️ Les deux passes doivent voir EXACTEMENT la meme
        /// chose : une divergence donne une ombre ou une occlusion decalee,
        /// pratiquement introuvable a l'oeil. C'est la raison d'etre de ce
        /// point unique.
        /// </summary>
        void DrawGeometry(GeometrySink sink);

        /// <summary>La passe finale : materiaux, teintes, os.</summary>
        void DrawOpaque(SceneRenderer scene);

        /// <summary>L'interface, en couleurs d'affichage. Peut ne rien faire.</summary>
        void DrawHud(HudRenderer hud, int width, int height);
    }

    public sealed class RenderPipeline : IDisposable
    {
        private readonly GL _gl;

        public RenderPipeline(GL gl, int width, int height, uint samples = 4)
        {
            _gl = gl ?? throw new ArgumentNullException(nameof(gl));

            Scene = new SceneRenderer(gl);
            Shadows = new ShadowMap(gl);
            Sky = new SkyRenderer(gl);
            Hud = new HudRenderer(gl);

            Ssao = new Ssao(gl, width, height);
            Hdr = new HdrTarget(gl, width, height, samples);
            Bloom = new Bloom(gl, width, height);

            Gpu = new GpuProfiler(gl);
        }

        public SceneRenderer Scene { get; }
        public ShadowMap Shadows { get; }
        public SkyRenderer Sky { get; }
        public HudRenderer Hud { get; }
        public Ssao Ssao { get; }
        public HdrTarget Hdr { get; }
        public Bloom Bloom { get; }
        public GpuProfiler Gpu { get; }

        /// <summary>Rendu des particules. Cree a la demande, avec sa capacite.</summary>
        public ParticleRenderer? Particles { get; private set; }

        public bool SsaoEnabled { get; set; } = true;
        public bool BloomEnabled { get; set; } = true;
        public bool ParticlesEnabled { get; set; } = true;

        /// <summary>Vue en fil de fer. Coupe aussi le ciel et les particules.</summary>
        public bool Wireframe { get; set; }

        /// <summary>0 = rien, 1 = normales, 2 = profondeur.</summary>
        public int SsaoDebug { get; set; }

        public float SsaoDebugRange { get; set; } = 30f;

        public void CreateParticleRenderer(int capacity)
        {
            Particles?.Dispose();
            Particles = new ParticleRenderer(_gl, capacity);
        }

        /// <summary>
        /// Dessine une image complete.
        /// </summary>
        /// <param name="alpha">Fraction de pas ecoulee, pour l'interpolation.</param>
        public void Render(Vector2D<int> framebuffer, Camera camera, Vector3 sunDirection,
                           IWorldRenderer world, ReadOnlySpan<Particle> particles, float alpha)
        {
            ArgumentNullException.ThrowIfNull(world);

            Gpu.BeginFrame();

            // --- PASSE 1 : profondeur vue depuis la lumiere -----------------
            if (Scene.ShadowStrength > 0f && Scene.ShadowCastingLightIndex >= 0)
            {
                Gpu.Begin(GpuZone.Shadow);
                Shadows.Begin(sunDirection, camera.Position);

                world.DrawGeometry((mesh, model, bones) =>
                {
                    if (bones != null) Shadows.SetBones(bones);
                    Shadows.Draw(mesh, model);
                    if (bones != null) Shadows.ClearBones();
                });

                Shadows.End(framebuffer);
                Gpu.End();

                _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            }

            // --- PASSE 1 bis : normales et profondeur pour l'occlusion -------
            if (SsaoEnabled || SsaoDebug > 0)
            {
                Ssao.Resize(framebuffer.X, framebuffer.Y);

                Gpu.Begin(GpuZone.SsaoGeometry);
                Ssao.BeginGeometry(camera);

                world.DrawGeometry((mesh, model, bones) =>
                {
                    if (bones != null) Ssao.SetBones(bones);
                    Ssao.Draw(mesh, model);
                    if (bones != null) Ssao.ClearBones();
                });

                Ssao.EndGeometry(framebuffer);
                Gpu.End();

                Gpu.Begin(GpuZone.SsaoOcclusion);
                Ssao.ComputeOcclusion(camera, framebuffer);
                Gpu.End();

                _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            }

            // --- PASSE 2 : rendu final, EN HDR HORS ECRAN --------------------
            Hdr.Resize(framebuffer.X, framebuffer.Y);
            Hdr.Begin();

            if (!Wireframe)
            {
                Gpu.Begin(GpuZone.Sky);
                Sky.Draw(camera, sunDirection);
                Gpu.End();
            }

            _gl.PolygonMode(TriangleFace.FrontAndBack, Wireframe ? PolygonMode.Line : PolygonMode.Fill);

            Gpu.Begin(GpuZone.Geometry);

            Scene.AmbientOcclusionTexture = SsaoEnabled ? Ssao.OcclusionTexture : 0;
            Scene.ScreenSize = new Vector2(framebuffer.X, framebuffer.Y);

            Scene.Begin(camera);
            world.DrawOpaque(Scene);

            Gpu.End();

            _gl.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);

            // --- PASSE 2 bis : particules, ENCORE EN HDR ---------------------
            if (Particles != null && ParticlesEnabled && !Wireframe)
            {
                Gpu.Begin(GpuZone.Particles);
                Particles.Draw(particles, camera, alpha);
                Gpu.End();
            }

            // --- PASSE 3 : exposition, halo, tone mapping, sRGB -------------
            Hdr.Resolve();

            if (BloomEnabled)
            {
                Gpu.Begin(GpuZone.Bloom);
                Bloom.Resize(framebuffer.X, framebuffer.Y);
                Bloom.Generate(Hdr.ResolvedTexture, Scene.Exposure);
                Gpu.End();

                Hdr.BloomTexture = Bloom.Texture;
            }
            else
            {
                Hdr.BloomTexture = 0;
            }

            Gpu.Begin(GpuZone.Composite);
            Hdr.Compose(framebuffer, Scene.Exposure);
            Gpu.End();

            // Visualisation du tampon d'occlusion, par-dessus tout le reste.
            if (SsaoDebug > 0)
                Ssao.DebugBlit(SsaoDebug, SsaoDebugRange);

            // ⚠️ L'interface arrive APRES la composition : elle est deja en
            // couleurs d'affichage, la passer dans la courbe ACES la delaverait.
            Gpu.Begin(GpuZone.Hud);
            world.DrawHud(Hud, framebuffer.X, framebuffer.Y);
            Gpu.End();

            Gpu.EndFrame();
        }

        public void Dispose()
        {
            Particles?.Dispose();
            Gpu.Dispose();
            Bloom.Dispose();
            Hdr.Dispose();
            Ssao.Dispose();
            Hud.Dispose();
            Sky.Dispose();
            Shadows.Dispose();
            Scene.Dispose();
        }
    }
}

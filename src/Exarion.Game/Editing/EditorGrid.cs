// ===========================================================================
//  La grille de l'editeur
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Exarion.Core.Physics;
using Exarion.Engine.Graphics;
using Silk.NET.OpenGL;

namespace Exarion.Game.Editing
{
    internal sealed class EditorGrid
    {
        private Mesh? _mesh;
        private float _step;

        /// <summary>« ed_show_grid », touche H.</summary>
        public bool Visible { get; set; } = true;

        public Mesh? Mesh => _mesh;

        /// <summary>Le pas a change : la grille sera refaite a la prochaine image.</summary>
        public void Invalidate() => _step = -1f;

        /// <summary>
        /// De fines lattes posees au sol.
        ///
        /// Le moteur ne dessine que des triangles - il n'a pas de primitive de
        /// ligne. Une latte de 2 cm de large fait donc l'affaire, et coute
        /// 12 triangles : 81 lattes, moins de mille triangles au total.
        /// Reconstruite SEULEMENT quand le pas change.
        /// </summary>
        public void Ensure(GL gl, float step)
        {
            if (_mesh != null && MathF.Abs(_step - step) < 1e-6f)
                return;

            _mesh?.Dispose();

            var verts = new List<Vertex>();
            var indices = new List<uint>();

            const int half = 20;
            float extent = half * step;
            float thickness = MathF.Max(0.012f, step * 0.012f);

            var minor = new Vector3(0.22f, 0.26f, 0.32f);
            var major = new Vector3(0.55f, 0.62f, 0.72f);

            for (int i = -half; i <= half; i++)
            {
                float p = i * step;

                // Un trait sur cinq est plus clair : sans reperes, une grille
                // uniforme ne permet pas de compter les pas.
                Vector3 tint = i % 5 == 0 ? major : minor;

                MeshBuilder.AddBox(verts, indices,
                    Aabb.FromCenterSize(new Vector3(p, 0.01f, 0f), new Vector3(thickness, 0.004f, extent * 2f)), tint);

                MeshBuilder.AddBox(verts, indices,
                    Aabb.FromCenterSize(new Vector3(0f, 0.01f, p), new Vector3(extent * 2f, 0.004f, thickness)), tint);
            }

            _mesh = new Mesh(gl,
                CollectionsMarshal.AsSpan(verts),
                CollectionsMarshal.AsSpan(indices));

            _step = step;
        }
    }
}

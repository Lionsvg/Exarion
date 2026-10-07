// ===========================================================================
//  Exarion - Maillage
//
//  Un tampon de sommets + un tampon d'indices, decrits par un VAO.
//  Format de sommet : position (3) + normale (3) + couleur (3) = 9 floats.
//  La normale sert a l'eclairage : sans elle, toutes les faces d'un cube
//  ont la meme teinte et le relief est illisible.
// ===========================================================================

using System;
using System.Numerics;
using Silk.NET.OpenGL;

namespace Exarion.Engine.Graphics
{
    public struct Vertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector3 Color;
        public Vector2 Uv;

        public Vertex(Vector3 position, Vector3 normal, Vector3 color, Vector2 uv = default)
        {
            Position = position;
            Normal = normal;
            Color = color;
            Uv = uv;
        }

        /// <summary>11 floats : position 3, normale 3, couleur 3, UV 2.</summary>
        public const int SizeInBytes = 11 * sizeof(float);
    }

    /// <summary>
    /// Sommet d'un maillage anime : le sommet ordinaire, plus les quatre os
    /// qui le tirent et leurs poids.
    ///
    /// Les indices d'os passent en FLOTTANTS, pas en entiers. C'est le seul
    /// point discutable : ca gaspille de la place et impose un arrondi dans
    /// le shader. Mais les attributs entiers demandent VertexAttribIPointer
    /// et un type distinct cote GLSL, pour un gain nul a cette echelle -
    /// 8 000 sommets, une seule fois au chargement.
    ///
    /// Type SEPARE de Vertex a dessein : sans ca, chaque sommet du decor et
    /// chaque caisse trainerait huit flottants inutiles.
    /// </summary>
    public struct SkinnedVertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector3 Color;
        public Vector2 Uv;
        public Vector4 Joints;
        public Vector4 Weights;

        public SkinnedVertex(Vector3 position, Vector3 normal, Vector3 color, Vector2 uv,
                             Vector4 joints, Vector4 weights)
        {
            Position = position;
            Normal = normal;
            Color = color;
            Uv = uv;
            Joints = joints;
            Weights = weights;
        }

        /// <summary>19 floats : les 11 du sommet ordinaire, plus 4 os et 4 poids.</summary>
        public const int SizeInBytes = 19 * sizeof(float);
    }

    public sealed class Mesh : IDisposable
    {
        private readonly GL _gl;
        private readonly uint _vao;
        private readonly uint _vbo;
        private readonly uint _ebo;

        public int IndexCount { get; }

        public unsafe Mesh(GL gl, ReadOnlySpan<Vertex> vertices, ReadOnlySpan<uint> indices)
        {
            _gl = gl;
            IndexCount = indices.Length;

            _vao = _gl.GenVertexArray();
            _gl.BindVertexArray(_vao);

            _vbo = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
            fixed (Vertex* p = vertices)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer,
                    (nuint)(vertices.Length * Vertex.SizeInBytes), p, BufferUsageARB.StaticDraw);
            }

            _ebo = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
            fixed (uint* p = indices)
            {
                _gl.BufferData(BufferTargetARB.ElementArrayBuffer,
                    (nuint)(indices.Length * sizeof(uint)), p, BufferUsageARB.StaticDraw);
            }

            const int stride = Vertex.SizeInBytes;

            _gl.EnableVertexAttribArray(0);   // position
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);

            _gl.EnableVertexAttribArray(1);   // normale
            _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));

            _gl.EnableVertexAttribArray(2);   // couleur
            _gl.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, stride, (void*)(6 * sizeof(float)));

            _gl.EnableVertexAttribArray(3);   // coordonnees de texture
            _gl.VertexAttribPointer(3, 2, VertexAttribPointerType.Float, false, stride, (void*)(9 * sizeof(float)));

            _gl.BindVertexArray(0);
        }

        /// <summary>
        /// Maillage anime. Memes quatre attributs que l'ordinaire, plus les
        /// os (4) et les poids (5).
        ///
        /// Un maillage ordinaire n'active tout simplement pas ces deux
        /// attributs : OpenGL fournit alors (0,0,0,0), et le shader les
        /// ignore de toute facon puisque uSkinned vaut zero.
        /// </summary>
        public unsafe Mesh(GL gl, ReadOnlySpan<SkinnedVertex> vertices, ReadOnlySpan<uint> indices)
        {
            _gl = gl;
            IndexCount = indices.Length;

            _vao = _gl.GenVertexArray();
            _gl.BindVertexArray(_vao);

            _vbo = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
            fixed (SkinnedVertex* p = vertices)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer,
                    (nuint)(vertices.Length * SkinnedVertex.SizeInBytes), p, BufferUsageARB.StaticDraw);
            }

            _ebo = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
            fixed (uint* p = indices)
            {
                _gl.BufferData(BufferTargetARB.ElementArrayBuffer,
                    (nuint)(indices.Length * sizeof(uint)), p, BufferUsageARB.StaticDraw);
            }

            const int stride = SkinnedVertex.SizeInBytes;

            _gl.EnableVertexAttribArray(0);   // position
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);

            _gl.EnableVertexAttribArray(1);   // normale
            _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));

            _gl.EnableVertexAttribArray(2);   // couleur
            _gl.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, stride, (void*)(6 * sizeof(float)));

            _gl.EnableVertexAttribArray(3);   // coordonnees de texture
            _gl.VertexAttribPointer(3, 2, VertexAttribPointerType.Float, false, stride, (void*)(9 * sizeof(float)));

            _gl.EnableVertexAttribArray(4);   // indices des quatre os
            _gl.VertexAttribPointer(4, 4, VertexAttribPointerType.Float, false, stride, (void*)(11 * sizeof(float)));

            _gl.EnableVertexAttribArray(5);   // poids des quatre os
            _gl.VertexAttribPointer(5, 4, VertexAttribPointerType.Float, false, stride, (void*)(15 * sizeof(float)));

            _gl.BindVertexArray(0);
        }

        public unsafe void Draw()
        {
            _gl.BindVertexArray(_vao);
            _gl.DrawElements(PrimitiveType.Triangles, (uint)IndexCount, DrawElementsType.UnsignedInt, (void*)0);
            _gl.BindVertexArray(0);
        }

        public void Dispose()
        {
            _gl.DeleteBuffer(_vbo);
            _gl.DeleteBuffer(_ebo);
            _gl.DeleteVertexArray(_vao);
        }
    }
}

// ===========================================================================
//  Exarion - Programme de shader
//
//  Compile un vertex + fragment shader et les lie. Rien de plus pour
//  l'instant : on ajoutera les uniformes quand on aura une camera.
//
//  Ecrit a partir de la specification OpenGL, pas copie d'un moteur existant.
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Numerics;
using Silk.NET.OpenGL;

namespace Exarion.Engine.Graphics
{
    public sealed class ShaderProgram : IDisposable
    {
        private readonly GL _gl;
        private readonly uint _handle;
        private readonly Dictionary<string, int> _uniformCache = new();

        public ShaderProgram(GL gl, string vertexSource, string fragmentSource)
        {
            _gl = gl;

            uint vs = Compile(ShaderType.VertexShader, vertexSource);
            uint fs = Compile(ShaderType.FragmentShader, fragmentSource);

            _handle = _gl.CreateProgram();
            _gl.AttachShader(_handle, vs);
            _gl.AttachShader(_handle, fs);
            _gl.LinkProgram(_handle);

            _gl.GetProgram(_handle, ProgramPropertyARB.LinkStatus, out int linked);
            if (linked == 0)
                throw new InvalidOperationException("Edition de liens du shader echouee : " + _gl.GetProgramInfoLog(_handle));

            // Les shaders individuels ne servent plus une fois lies.
            _gl.DetachShader(_handle, vs);
            _gl.DetachShader(_handle, fs);
            _gl.DeleteShader(vs);
            _gl.DeleteShader(fs);
        }

        private uint Compile(ShaderType type, string source)
        {
            uint shader = _gl.CreateShader(type);
            _gl.ShaderSource(shader, source);
            _gl.CompileShader(shader);

            _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int status);
            if (status == 0)
            {
                string log = _gl.GetShaderInfoLog(shader);
                _gl.DeleteShader(shader);
                throw new InvalidOperationException($"Compilation du shader {type} echouee : {log}");
            }

            return shader;
        }

        public void Use() => _gl.UseProgram(_handle);

        private int GetUniformLocation(string name)
        {
            if (_uniformCache.TryGetValue(name, out int cached))
                return cached;

            int loc = _gl.GetUniformLocation(_handle, name);
            _uniformCache[name] = loc;
            return loc;
        }

        /// <summary>
        /// Vrai si l'uniform existe dans le programme compile.
        ///
        /// SetUniform ignore SILENCIEUSEMENT un nom inconnu - c'est voulu,
        /// un shader peut legitimement ne pas utiliser tous les uniforms
        /// qu'on lui envoie. Mais ce silence rend un nom mal orthographie
        /// invisible : la valeur part dans le vide et rien ne le signale.
        /// </summary>
        public bool HasUniform(string name) => GetUniformLocation(name) >= 0;

        public void SetUniform(string name, float value)
        {
            int loc = GetUniformLocation(name);
            if (loc >= 0) _gl.Uniform1(loc, value);
        }

        public void SetUniform(string name, int value)
        {
            int loc = GetUniformLocation(name);
            if (loc >= 0) _gl.Uniform1(loc, value);
        }

        public void SetUniform(string name, bool value) => SetUniform(name, value ? 1 : 0);

        public void SetUniform(string name, Vector2 value)
        {
            int loc = GetUniformLocation(name);
            if (loc >= 0) _gl.Uniform2(loc, value.X, value.Y);
        }

        public void SetUniform(string name, Vector3 value)
        {
            int loc = GetUniformLocation(name);
            if (loc >= 0) _gl.Uniform3(loc, value.X, value.Y, value.Z);
        }

        public void SetUniform(string name, Vector4 value)
        {
            int loc = GetUniformLocation(name);
            if (loc >= 0) _gl.Uniform4(loc, value.X, value.Y, value.Z, value.W);
        }

        public unsafe void SetUniform(string name, Matrix4x4 value)
        {
            int loc = GetUniformLocation(name);
            if (loc < 0) return;

            // ATTENTION - ce flag a ete faux pendant tout J2/J3, et c'est ce
            // qui deformait l'image.
            //
            // System.Numerics est en convention VECTEUR-LIGNE : v * M.
            // GLSL est en convention VECTEUR-COLONNE : M * v.
            // Il faut donc que la matrice vue par GLSL soit la TRANSPOSEE de
            // celle de System.Numerics.
            //
            // La memoire de System.Numerics est ligne-major :
            //     [M11 M12 M13 M14 | M21 ... ]
            // Avec transpose = FALSE, OpenGL lit ce tableau comme des COLONNES,
            // ce qui produit exactement la transposee voulue. C'est donc false,
            // et non true.
            //
            // Avec true, la composante de division perspective (M34 = -1) se
            // retrouvait a la mauvaise place : d'ou l'etirement de l'image.
            _gl.UniformMatrix4(loc, 1, false, (float*)&value);
        }

        public void Dispose() => _gl.DeleteProgram(_handle);
    }
}

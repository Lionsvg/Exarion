// ===========================================================================
//  Exarion - Texture GPU
//
//  Enveloppe une texture 2D OpenGL. Les mipmaps sont generees par defaut :
//  sans elles, une surface vue de loin scintille violemment des qu'elle
//  porte une grille - exactement notre cas.
//
//  Le filtrage anisotrope est active quand le pilote l'expose. C'est LE
//  reglage qui rend un sol texture net quand on le regarde en rasant,
//  et il coute presque rien.
// ===========================================================================

using System;
using Silk.NET.OpenGL;
using Exarion.Core.Assets;

namespace Exarion.Engine.Graphics
{
    public sealed class Texture : IDisposable
    {
        private const int GlTextureMaxAnisotropy = 0x84FE;
        private const int GlMaxTextureMaxAnisotropy = 0x84FF;

        private readonly GL _gl;

        public uint Handle { get; }
        public int Width { get; }
        public int Height { get; }

        public unsafe Texture(GL gl, Image32 image, bool generateMipmaps = true, bool repeat = true)
        {
            _gl = gl;
            Width = image.Width;
            Height = image.Height;

            Handle = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, Handle);

            fixed (byte* pixels = image.Pixels)
            {
                _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8,
                    (uint)image.Width, (uint)image.Height, 0,
                    PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
            }

            var wrap = repeat ? TextureWrapMode.Repeat : TextureWrapMode.ClampToEdge;
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)wrap);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)wrap);

            if (generateMipmaps)
            {
                _gl.GenerateMipmap(TextureTarget.Texture2D);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                    (int)TextureMinFilter.LinearMipmapLinear);
                TryEnableAnisotropy();
            }
            else
            {
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                    (int)TextureMinFilter.Linear);
            }

            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
                (int)TextureMagFilter.Linear);

            _gl.BindTexture(TextureTarget.Texture2D, 0);
        }

        /// <summary>
        /// Le filtrage anisotrope est une extension : tous les pilotes ne
        /// l'exposent pas. On demande le maximum disponible, et on ignore
        /// silencieusement s'il n'existe pas.
        /// </summary>
        private void TryEnableAnisotropy()
        {
            try
            {
                _gl.GetFloat((GLEnum)GlMaxTextureMaxAnisotropy, out float max);

                if (max >= 2f)
                {
                    _gl.TexParameter(TextureTarget.Texture2D,
                        (TextureParameterName)GlTextureMaxAnisotropy, MathF.Min(max, 16f));
                }
            }
            catch
            {
                // Extension absente : le rendu reste correct, juste un peu
                // plus flou aux angles rasants.
            }
        }

        public void Bind(TextureUnit unit)
        {
            _gl.ActiveTexture(unit);
            _gl.BindTexture(TextureTarget.Texture2D, Handle);
        }

        public void Dispose() => _gl.DeleteTexture(Handle);
    }
}

// ===========================================================================
//  Exarion - Rendu du HUD
//
//  Dessine des rectangles en coordonnees ECRAN (pixels, origine en haut a
//  gauche). Suffisant pour un reticule, des barres et un marqueur de touche.
//
//  Pas encore de texte : un rendu de police est un sous-systeme a part
//  entiere (atlas, metriques, kerning). Les valeurs chiffrees passent par
//  la console en attendant.
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Numerics;
using Silk.NET.OpenGL;

namespace Exarion.Engine.Graphics
{
    public sealed class HudRenderer : IDisposable
    {
        private const string VertexSource = @"#version 330 core
layout (location = 0) in vec2 aPos;
layout (location = 1) in vec4 aColor;

uniform vec2 uScreen;

out vec4 vColor;

void main()
{
    // Pixels (origine en haut a gauche) -> coordonnees normalisees OpenGL.
    vec2 ndc = vec2(aPos.x / uScreen.x * 2.0 - 1.0,
                    1.0 - aPos.y / uScreen.y * 2.0);
    gl_Position = vec4(ndc, 0.0, 1.0);
    vColor = aColor;
}";

        private const string FragmentSource = @"#version 330 core
in vec4 vColor;
out vec4 FragColor;

void main()
{
    FragColor = vColor;
}";

        private struct HudVertex
        {
            public Vector2 Position;
            public Vector4 Color;
            public const int SizeInBytes = 6 * sizeof(float);
        }

        private readonly GL _gl;
        private readonly ShaderProgram _shader;
        private readonly uint _vao;
        private readonly uint _vbo;

        private readonly List<HudVertex> _vertices = new();
        private Vector2 _screen;

        public unsafe HudRenderer(GL gl)
        {
            _gl = gl;
            _shader = new ShaderProgram(gl, VertexSource, FragmentSource);

            _vao = _gl.GenVertexArray();
            _gl.BindVertexArray(_vao);

            _vbo = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, HudVertex.SizeInBytes, (void*)0);

            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, HudVertex.SizeInBytes, (void*)(2 * sizeof(float)));

            _gl.BindVertexArray(0);
        }

        public void Begin(int screenWidth, int screenHeight)
        {
            _screen = new Vector2(Math.Max(1, screenWidth), Math.Max(1, screenHeight));
            _vertices.Clear();
        }

        /// <summary>Rectangle plein. x,y = coin haut-gauche, en pixels.</summary>
        public void Rect(float x, float y, float w, float h, Vector4 color)
        {
            var a = new Vector2(x, y);
            var b = new Vector2(x + w, y);
            var c = new Vector2(x + w, y + h);
            var d = new Vector2(x, y + h);

            _vertices.Add(new HudVertex { Position = a, Color = color });
            _vertices.Add(new HudVertex { Position = b, Color = color });
            _vertices.Add(new HudVertex { Position = c, Color = color });

            _vertices.Add(new HudVertex { Position = a, Color = color });
            _vertices.Add(new HudVertex { Position = c, Color = color });
            _vertices.Add(new HudVertex { Position = d, Color = color });
        }

        /// <summary>Rectangle centre sur un point.</summary>
        public void RectCentered(float cx, float cy, float w, float h, Vector4 color)
            => Rect(cx - w * 0.5f, cy - h * 0.5f, w, h, color);

        /// <summary>
        /// Reticule a 4 branches. L'ecartement doit refleter la dispersion
        /// reelle de l'arme : c'est ainsi que le joueur apprend a s'arreter
        /// avant de tirer.
        /// </summary>
        public void Crosshair(float gap, float length, float thickness, Vector4 color)
        {
            float cx = _screen.X * 0.5f;
            float cy = _screen.Y * 0.5f;

            RectCentered(cx, cy - gap - length * 0.5f, thickness, length, color);
            RectCentered(cx, cy + gap + length * 0.5f, thickness, length, color);
            RectCentered(cx - gap - length * 0.5f, cy, length, thickness, color);
            RectCentered(cx + gap + length * 0.5f, cy, length, thickness, color);
        }

        /// <summary>Marqueur de touche : un X au centre. Retour immediat et lisible.</summary>
        public void HitMarker(float size, float thickness, Vector4 color)
        {
            float cx = _screen.X * 0.5f;
            float cy = _screen.Y * 0.5f;

            // Approxime les diagonales par de petits carres : suffisant pour
            // un greybox, et evite d'introduire une rotation ici.
            int steps = 6;
            for (int i = 1; i <= steps; i++)
            {
                float o = size * i / steps;
                RectCentered(cx - o, cy - o, thickness, thickness, color);
                RectCentered(cx + o, cy - o, thickness, thickness, color);
                RectCentered(cx - o, cy + o, thickness, thickness, color);
                RectCentered(cx + o, cy + o, thickness, thickness, color);
            }
        }

        // -------------------------------------------------------------------
        //  Texte
        //
        //  Un pixel de glyphe = un rectangle. Couteux en sommets pour de
        //  longs textes, negligeable pour une console de debug : une ligne
        //  de 40 caracteres fait au pire ~700 rectangles, soit 4200 sommets.
        // -------------------------------------------------------------------

        /// <summary>Dessine du texte. Retourne la largeur occupee, en pixels.</summary>
        public float Text(float x, float y, float scale, string text, Vector4 color)
        {
            if (string.IsNullOrEmpty(text))
                return 0f;

            float cursor = x;

            foreach (char c in text)
            {
                for (int col = 0; col < BitmapFont.GlyphWidth; col++)
                {
                    byte bits = BitmapFont.GetColumn(c, col);
                    if (bits == 0)
                        continue;

                    for (int row = 0; row < BitmapFont.GlyphHeight; row++)
                    {
                        if ((bits & (1 << row)) == 0)
                            continue;

                        Rect(cursor + col * scale, y + row * scale, scale, scale, color);
                    }
                }

                cursor += BitmapFont.Advance * scale;
            }

            return cursor - x;
        }

        public void TextCentered(float centerX, float y, float scale, string text, Vector4 color)
            => Text(centerX - BitmapFont.MeasureWidth(text, scale) * 0.5f, y, scale, text, color);

        /// <summary>Texte avec un fond sombre : lisible sur n'importe quel decor.</summary>
        public void TextBoxed(float x, float y, float scale, string text, Vector4 color, float padding = 4f)
        {
            float w = BitmapFont.MeasureWidth(text, scale);
            float h = BitmapFont.LineHeight(scale);

            Rect(x - padding, y - padding, w + padding * 2f, h + padding * 2f, new Vector4(0f, 0f, 0f, 0.6f));
            Text(x, y, scale, text, color);
        }

        // -------------------------------------------------------------------
        //  Chiffres 7 segments
        //
        //  Un vrai rendu de texte demande un atlas de police, des metriques
        //  et du kerning : c'est un sous-systeme a part. Pour un timer, un
        //  score et un compteur d'argent, sept rectangles par chiffre
        //  suffisent - et ca reste parfaitement lisible.
        //
        //       --a--
        //      |     |
        //      f     b
        //      |     |
        //       --g--
        //      |     |
        //      e     c
        //      |     |
        //       --d--
        // -------------------------------------------------------------------

        /// <summary>Segments allumes pour chaque chiffre : bits a,b,c,d,e,f,g.</summary>
        private static readonly byte[] SevenSegment =
        {
            0b0111111,   // 0 : a b c d e f
            0b0000110,   // 1 : b c
            0b1011011,   // 2 : a b d e g
            0b1001111,   // 3 : a b c d g
            0b1100110,   // 4 : b c f g
            0b1101101,   // 5 : a c d f g
            0b1111101,   // 6 : a c d e f g
            0b0000111,   // 7 : a b c
            0b1111111,   // 8 : tous
            0b1101111,   // 9 : a b c d f g
        };

        /// <summary>Largeur d'un chiffre pour une hauteur donnee.</summary>
        public static float DigitWidth(float height) => height * 0.58f;

        public void Digit(float x, float y, float height, int digit, Vector4 color)
        {
            if (digit < 0 || digit > 9)
                return;

            byte mask = SevenSegment[digit];
            float w = DigitWidth(height);
            float t = MathF.Max(1.5f, height * 0.14f);   // epaisseur de trait
            float half = height * 0.5f;

            // a : haut
            if ((mask & 0b0000001) != 0) Rect(x + t, y, w - 2f * t, t, color);
            // b : haut droit
            if ((mask & 0b0000010) != 0) Rect(x + w - t, y + t, t, half - 1.5f * t, color);
            // c : bas droit
            if ((mask & 0b0000100) != 0) Rect(x + w - t, y + half + t * 0.5f, t, half - 1.5f * t, color);
            // d : bas
            if ((mask & 0b0001000) != 0) Rect(x + t, y + height - t, w - 2f * t, t, color);
            // e : bas gauche
            if ((mask & 0b0010000) != 0) Rect(x, y + half + t * 0.5f, t, half - 1.5f * t, color);
            // f : haut gauche
            if ((mask & 0b0100000) != 0) Rect(x, y + t, t, half - 1.5f * t, color);
            // g : milieu
            if ((mask & 0b1000000) != 0) Rect(x + t, y + half - t * 0.5f, w - 2f * t, t, color);
        }

        /// <summary>Largeur totale qu'occupera Number().</summary>
        public static float NumberWidth(int value, float height, int minDigits = 1)
        {
            int digits = CountDigits(value, minDigits);
            bool negative = value < 0;
            float w = DigitWidth(height);
            return digits * (w + height * 0.14f) + (negative ? w * 0.7f : 0f);
        }

        private static int CountDigits(int value, int minDigits)
        {
            int v = System.Math.Abs(value);
            int n = 1;
            while (v >= 10) { v /= 10; n++; }
            return System.Math.Max(n, minDigits);
        }

        /// <summary>Dessine un entier. x,y = coin haut-gauche. Retourne la largeur occupee.</summary>
        public float Number(float x, float y, float height, int value, Vector4 color, int minDigits = 1)
        {
            float w = DigitWidth(height);
            float spacing = height * 0.14f;
            float cursor = x;

            if (value < 0)
            {
                // Signe moins : un simple trait median.
                Rect(cursor, y + height * 0.5f - MathF.Max(1.5f, height * 0.07f), w * 0.55f,
                     MathF.Max(1.5f, height * 0.14f), color);
                cursor += w * 0.7f;
                value = -value;
            }

            int digits = CountDigits(value, minDigits);
            for (int i = digits - 1; i >= 0; i--)
            {
                int d = value;
                for (int k = 0; k < i; k++) d /= 10;
                Digit(cursor, y, height, d % 10, color);
                cursor += w + spacing;
            }

            return cursor - x;
        }

        public void NumberCentered(float centerX, float y, float height, int value, Vector4 color, int minDigits = 1)
            => Number(centerX - NumberWidth(value, height, minDigits) * 0.5f, y, height, value, color, minDigits);

        /// <summary>Chronometre au format M:SS.</summary>
        public void Timer(float centerX, float y, float height, int totalSeconds, Vector4 color)
        {
            if (totalSeconds < 0) totalSeconds = 0;

            int minutes = totalSeconds / 60;
            int seconds = totalSeconds % 60;

            float w = DigitWidth(height);
            float spacing = height * 0.14f;
            float colonW = w * 0.45f;

            float total = (w + spacing) + colonW + 2f * (w + spacing);
            float cursor = centerX - total * 0.5f;

            cursor += Number(cursor, y, height, minutes, color);

            // Deux-points : deux carres.
            float dot = MathF.Max(2f, height * 0.13f);
            Rect(cursor + colonW * 0.3f, y + height * 0.30f, dot, dot, color);
            Rect(cursor + colonW * 0.3f, y + height * 0.62f, dot, dot, color);
            cursor += colonW;

            Number(cursor, y, height, seconds, color, minDigits: 2);
        }

        /// <summary>Barre avec fond, pour la vie ou les munitions.</summary>
        public void Bar(float x, float y, float w, float h, float fill, Vector4 color)
        {
            Rect(x, y, w, h, new Vector4(0f, 0f, 0f, 0.55f));
            Rect(x + 2f, y + 2f, (w - 4f) * Math.Clamp(fill, 0f, 1f), h - 4f, color);
        }

        public unsafe void End()
        {
            if (_vertices.Count == 0)
                return;

            // Le HUD se dessine par-dessus tout, sans profondeur, avec alpha.
            _gl.Disable(EnableCap.DepthTest);
            _gl.Enable(EnableCap.Blend);
            _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            _shader.Use();
            _shader.SetUniform("uScreen", _screen);

            _gl.BindVertexArray(_vao);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

            var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_vertices);
            fixed (HudVertex* p = span)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer,
                    (nuint)(_vertices.Count * HudVertex.SizeInBytes), p, BufferUsageARB.DynamicDraw);
            }

            _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_vertices.Count);
            _gl.BindVertexArray(0);

            _gl.Disable(EnableCap.Blend);
            _gl.Enable(EnableCap.DepthTest);
        }

        public void Dispose()
        {
            _gl.DeleteBuffer(_vbo);
            _gl.DeleteVertexArray(_vao);
            _shader.Dispose();
        }
    }
}

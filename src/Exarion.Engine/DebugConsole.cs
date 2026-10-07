// ===========================================================================
//  Exarion - Console de debug a l'ecran
//
//  Couche d'affichage et de saisie autour de ConsoleSystem, qui vit dans
//  Core et ne connait ni clavier ni pixels.
//
//  Ouverture par F1 : volontairement pas la touche sous Echap, dont la
//  position et le caractere changent selon la disposition du clavier.
//
//  Quand la console est ouverte, le jeu ne doit RIEN recevoir : sans ca on
//  tire en tapant une commande.
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Silk.NET.Input;
using Exarion.Core.Console;
using Exarion.Engine.Graphics;

namespace Exarion.Engine
{
    public sealed class DebugConsole
    {
        public ConsoleSystem System { get; } = new();

        public bool IsOpen { get; private set; }

        /// <summary>Touche d'ouverture. F1 par defaut, independante de la disposition.</summary>
        public Key ToggleKey { get; set; } = Key.F1;

        /// <summary>Nombre de lignes de sortie affichees.</summary>
        public int VisibleLines { get; set; } = 14;

        public float TextScale { get; set; } = 2f;

        private readonly StringBuilder _input = new();
        private int _historyIndex = -1;
        private float _caretTimer;

        public event Action<bool>? OpenedOrClosed;

        public void Attach(IKeyboard keyboard)
        {
            keyboard.KeyDown += OnKeyDown;
            keyboard.KeyChar += OnKeyChar;
        }

        public void Detach(IKeyboard keyboard)
        {
            keyboard.KeyDown -= OnKeyDown;
            keyboard.KeyChar -= OnKeyChar;
        }

        private void OnKeyDown(IKeyboard keyboard, Key key, int _)
        {
            if (key == ToggleKey)
            {
                Toggle();
                return;
            }

            if (!IsOpen)
                return;

            switch (key)
            {
                case Key.Enter:
                case Key.KeypadEnter:
                    Submit();
                    break;

                case Key.Backspace:
                    if (_input.Length > 0)
                        _input.Length--;
                    break;

                case Key.Tab:
                    string completed = System.Complete(_input.ToString());
                    _input.Clear();
                    _input.Append(completed);
                    break;

                case Key.Up:
                    NavigateHistory(-1);
                    break;

                case Key.Down:
                    NavigateHistory(1);
                    break;

                case Key.Escape:
                    Close();
                    break;
            }
        }

        private void OnKeyChar(IKeyboard keyboard, char c)
        {
            if (!IsOpen)
                return;

            // Le caractere de la touche d'ouverture ne doit pas se retrouver
            // dans la ligne quand on ouvre la console.
            if (c < ' ' || c > '~')
                return;

            _input.Append(c);
        }

        private void NavigateHistory(int direction)
        {
            IReadOnlyList<string> history = System.History;
            if (history.Count == 0)
                return;

            if (_historyIndex < 0)
                _historyIndex = history.Count;

            _historyIndex = Math.Clamp(_historyIndex + direction, 0, history.Count);

            _input.Clear();
            if (_historyIndex < history.Count)
                _input.Append(history[_historyIndex]);
        }

        private void Submit()
        {
            string line = _input.ToString();
            _input.Clear();
            _historyIndex = -1;

            System.Execute(line);
        }

        public void Toggle()
        {
            IsOpen = !IsOpen;

            if (IsOpen)
            {
                _historyIndex = -1;
                _input.Clear();
            }

            OpenedOrClosed?.Invoke(IsOpen);
        }

        public void Close()
        {
            if (!IsOpen)
                return;

            IsOpen = false;
            OpenedOrClosed?.Invoke(false);
        }

        public void Update(float dt) => _caretTimer += dt;

        // -------------------------------------------------------------------

        public void Render(HudRenderer hud, int screenWidth, int screenHeight)
        {
            if (!IsOpen)
                return;

            float scale = TextScale;
            float lineHeight = BitmapFont.LineHeight(scale) + scale * 2f;
            float panelHeight = (VisibleLines + 2) * lineHeight + 16f;

            var background = new Vector4(0.04f, 0.05f, 0.07f, 0.92f);
            var textColor = new Vector4(0.82f, 0.86f, 0.90f, 1f);
            var promptColor = new Vector4(0.45f, 0.95f, 0.55f, 1f);
            var edgeColor = new Vector4(0.25f, 0.65f, 0.45f, 0.9f);

            hud.Rect(0f, 0f, screenWidth, panelHeight, background);
            hud.Rect(0f, panelHeight - 2f, screenWidth, 2f, edgeColor);

            // Sortie : les plus recentes en bas, juste au-dessus de la saisie.
            IReadOnlyList<string> output = System.Output;
            int first = Math.Max(0, output.Count - VisibleLines);

            float y = 8f;
            for (int i = first; i < output.Count; i++)
            {
                hud.Text(10f, y, scale, output[i], textColor);
                y += lineHeight;
            }

            // Ligne de saisie, toujours en bas du panneau.
            float inputY = panelHeight - lineHeight - 8f;
            hud.Text(10f, inputY, scale, ">", promptColor);

            float textX = 10f + BitmapFont.Advance * scale * 2f;
            float width = hud.Text(textX, inputY, scale, _input.ToString(), textColor);

            // Curseur clignotant.
            if ((int)(_caretTimer * 2f) % 2 == 0)
                hud.Rect(textX + width, inputY, scale * 4f, BitmapFont.LineHeight(scale), promptColor);
        }
    }
}

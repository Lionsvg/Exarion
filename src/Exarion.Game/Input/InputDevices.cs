// ===========================================================================
//  Clavier et souris, partages par le jeu et l'editeur
//
//  Un seul exemplaire pour toute l'application : le delta de souris
//  s'accumule entre deux pas, et c'est l'ETAT courant (partie, editeur, menu)
//  qui decide qui le consomme. Deux accumulateurs donneraient deux cameras
//  qui tournent a des vitesses differentes selon l'ordre des appels.
// ===========================================================================

using System;
using System.Numerics;
using Silk.NET.Input;

namespace Exarion.Game.Input
{
    internal sealed class InputDevices
    {
        public IKeyboard? Keyboard { get; private set; }
        public IMouse? Mouse { get; private set; }

        /// <summary>Deplacement de souris accumule depuis le dernier pas.</summary>
        public Vector2 MouseDelta;

        public Vector2 LastMousePosition;

        /// <summary>
        /// Faux quand le prochain deplacement ne veut rien dire : au premier
        /// mouvement, et a chaque changement de mode de curseur, qui teleporte
        /// la position signalee par le systeme.
        /// </summary>
        public bool HasLastMouse;

        /// <summary>« sensitivity ». La valeur de depart vient de la ligne de commande.</summary>
        public float Sensitivity = 0.0022f;

        /// <summary>Vitesse de rotation aux fleches, en radians par seconde.</summary>
        public float KeyLookSpeed = 130f * MathF.PI / 180f;

        /// <summary>
        /// Branche les peripheriques. La souris passe en mode brut et accumule
        /// ses deplacements.
        /// </summary>
        public void Attach(IKeyboard? keyboard, IMouse? mouse)
        {
            Keyboard = keyboard;
            Mouse = mouse;

            if (mouse != null)
            {
                mouse.Cursor.CursorMode = CursorMode.Raw;
                mouse.MouseMove += (_, pos) =>
                {
                    if (HasLastMouse)
                        MouseDelta += pos - LastMousePosition;

                    LastMousePosition = pos;
                    HasLastMouse = true;
                };
            }
        }

        public bool IsDown(Key key) => Keyboard?.IsKeyPressed(key) == true;

        public bool Shift => IsDown(Key.ShiftLeft) || IsDown(Key.ShiftRight);

        public bool Ctrl => IsDown(Key.ControlLeft) || IsDown(Key.ControlRight);

        public bool IsButtonDown(MouseButton button) => Mouse?.IsButtonPressed(button) == true;

        /// <summary>Change le mode du curseur, s'il y a une souris.</summary>
        public void SetCursor(CursorMode mode)
        {
            if (Mouse != null)
                Mouse.Cursor.CursorMode = mode;
        }
    }
}

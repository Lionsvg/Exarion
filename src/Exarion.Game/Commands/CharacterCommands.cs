// ===========================================================================
//  Console : le personnage importe et ses animations
//
//  Pouvoir le tourner en direct est le seul moyen de verifier une geometrie
//  sans la juger sur une seule face.
// ===========================================================================

using System;
using Exarion.Core.Console;
using Exarion.Game.Rendering;

namespace Exarion.Game.Commands
{
    internal static class CharacterCommands
    {
        public static void Register(ConsoleSystem c, CharacterRig rig)
        {
            c.Bind("character_yaw", 0f, -180f, 180f, "Orientation du personnage, en degres",
                   x => rig.Yaw = x * MathF.PI / 180f);
            c.Bind("character_z", 15f, -60f, 60f, "Position du personnage sur Z",
                   x => rig.Position.Z = x);
            c.Bind("character_anim", 0f, 0f, 8f, "Cycle de repli si les noms ne sont pas reconnus",
                   x => { rig.AnimationIndex = (int)x; rig.SelectClips(); });
            c.Bind("character_speed", 1f, 0f, 4f, "Vitesse de l'animation. 0 fige la pose",
                   x => rig.Speed = x);

            // Melange de poses (2.2). 0 = marche, 1 = course, et TOUT ce qu'il
            // y a entre les deux - qu'aucun animateur n'a eu a dessiner.
            c.Bind("character_blend", 0f, 0f, 1f, "Melange marche (0) vers course (1)",
                   x => rig.Blend = x);

            // Le decalage de pied ne se calcule pas : il depend de la jambe
            // sur laquelle chaque export demarre. Seul un oeil peut le regler.
            c.Bind("character_offset", 0f, 0f, 1f, "Decalage du cycle de course, en fractions de pas",
                   x => rig.Offset = x);

            // -1 laisse courir l'animation. Toute autre valeur fige la phase,
            // ce qui est le SEUL moyen de comparer deux melanges au meme
            // instant de la foulee - leurs cadences etant differentes.
            c.Bind("character_phase", -1f, -1f, 1f, "Phase imposee du pas, -1 pour laisser courir",
                   x => rig.Phase = x);
        }
    }
}

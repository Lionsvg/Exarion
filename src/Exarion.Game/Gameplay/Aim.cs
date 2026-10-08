// ===========================================================================
//  Ou l'on vise, au sol
// ===========================================================================

using System;
using System.Numerics;
using Exarion.Core.Physics;

namespace Exarion.Game.Gameplay
{
    internal static class Aim
    {
        /// <summary>
        /// Point vise sur le decor, ou 6 m devant si on vise le vide.
        ///
        /// On s'arrete 60 cm AVANT la surface touchee, et jamais a moins de
        /// 1,5 m : une caisse ou une entite posee « au point vise » naitrait
        /// sinon a moitie dans le mur, ou dans les pieds de celui qui vise.
        /// </summary>
        public static Vector3 PointOnFloor(CollisionWorld level, Vector3 eye, Vector3 direction)
        {
            if (level.Raycast(eye, direction, 60f, out float d, out _))
                return eye + direction * MathF.Max(1.5f, d - 0.6f);

            return eye + direction * 6f;
        }
    }
}

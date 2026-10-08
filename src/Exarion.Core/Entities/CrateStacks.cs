// ===========================================================================
//  Les piles de caisses que declare une carte (prop_crate)
//
//  Une fonction pure : ce que la carte demande, caisse par caisse. Le monde
//  physique en fait des corps ; ici on ne decide que OU et COMBIEN, ce qui se
//  verifie en ligne de commande.
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Numerics;
using Exarion.Core.Assets;

namespace Exarion.Core.Entities
{
    /// <summary>Une caisse a faire naitre.</summary>
    public readonly struct CrateSpawn
    {
        public readonly Vector3 Center;
        public readonly float Size;
        public readonly float Mass;

        public CrateSpawn(Vector3 center, float size, float mass)
        {
            Center = center;
            Size = size;
            Mass = mass;
        }
    }

    public static class CrateStacks
    {
        public const float DefaultSize = 0.9f;
        public const float DefaultMass = 18f;

        /// <summary>
        /// Jeu entre deux caisses empilees. Sans lui, elles naitraient en
        /// contact exact, et le solveur les ecarterait d'un coup au premier
        /// pas : la pile « saute » avant de tomber.
        /// </summary>
        public const float Gap = 0.02f;

        // Bornes : les MEMES que les ConVar crate_size et crate_mass, et une
        // pile qui reste raisonnable. Une valeur hors bornes est ramenee, pas
        // refusee - une carte ne doit pas faire planter le chargement pour une
        // caisse de 0 kg.
        public const float MinSize = 0.2f, MaxSize = 4f;
        public const float MinMass = 0.5f, MaxMass = 500f;
        public const int MaxCount = 60;

        /// <summary>
        /// Toutes les caisses declarees par la carte, dans l'ordre du fichier,
        /// et de BAS en HAUT dans chaque pile.
        ///
        /// ⚠️ L'ORDRE COMPTE : c'est l'ordre d'insertion dans le monde
        /// physique, donc celui dans lequel le solveur les traite. Le changer
        /// changerait l'effondrement d'une pile d'une capture a l'autre.
        /// </summary>
        public static List<CrateSpawn> From(MapData map)
        {
            ArgumentNullException.ThrowIfNull(map);

            var spawns = new List<CrateSpawn>();

            foreach (MapEntity entity in map.Entities)
                if (string.Equals(entity.Type, EntityTypes.Crate, StringComparison.Ordinal))
                    AppendStack(entity, spawns);

            return spawns;
        }

        /// <summary>
        /// Les caisses d'UNE entite. Le marqueur de l'editeur s'en sert aussi :
        /// il dessine exactement ce que le jeu fera naitre, et pas une
        /// approximation qui mentirait sur la hauteur de la pile.
        /// </summary>
        public static void AppendStack(MapEntity entity, List<CrateSpawn> spawns)
        {
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(spawns);

            float size = Math.Clamp(entity.GetFloat("size", DefaultSize), MinSize, MaxSize);
            float mass = Math.Clamp(entity.GetFloat("mass", DefaultMass), MinMass, MaxMass);
            int count = Math.Clamp(entity.GetInt("count", 1), 1, MaxCount);

            for (int i = 0; i < count; i++)
            {
                // La position de l'entite est le SOL sous la pile : la
                // premiere caisse y pose sa face basse.
                var center = new Vector3(entity.Position.X,
                                         entity.Position.Y + size * 0.5f + i * (size + Gap),
                                         entity.Position.Z);

                spawns.Add(new CrateSpawn(center, size, mass));
            }
        }
    }
}

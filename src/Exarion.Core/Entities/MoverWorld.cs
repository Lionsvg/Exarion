// ===========================================================================
//  Exarion - Le monde des geometries mobiles (E17)
//
//  Instancie les func_door d'une carte, les fait avancer, les donne au monde
//  de collision, et porte ce qui se tient dessus.
//
//  ⚠️ MEME REGLE QUE POUR LES PNJ : les movers sont instancies en ENTRANT EN
//  PARTIE, pas au chargement de la carte. C'est un instantane. Entrer en
//  partie remet donc toutes les portes a leur position de depart, ce qui est
//  exactement ce qu'on veut apres avoir edite un niveau.
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Numerics;
using Exarion.Core.Assets;
using Exarion.Core.Physics;

namespace Exarion.Core.Entities
{
    public sealed class MoverWorld
    {
        private readonly List<Mover> _movers = new();

        public IReadOnlyList<Mover> Movers => _movers;

        /// <summary>Combien ont bouge au dernier pas. Sert au HUD et aux mesures.</summary>
        public int MovingCount { get; private set; }

        /// <summary>
        /// (Re)construit depuis une carte. Tout etat precedent est perdu, et
        /// c'est voulu - voir la note en tete de fichier.
        /// </summary>
        public void Load(MapData map)
        {
            ArgumentNullException.ThrowIfNull(map);

            _movers.Clear();

            foreach (MapEntity entity in map.Entities)
                if (string.Equals(entity.Type, EntityTypes.Door, StringComparison.Ordinal))
                    _movers.Add(new Mover(entity));
        }

        public void Clear() => _movers.Clear();

        /// <summary>Fait avancer tous les movers. Au pas fixe.</summary>
        public void Step(float dt)
        {
            MovingCount = 0;

            for (int i = 0; i < _movers.Count; i++)
            {
                _movers[i].Step(dt);

                if (_movers[i].State is MoverState.Ouverture or MoverState.Fermeture)
                    MovingCount++;
            }
        }

        /// <summary>
        /// Ajoute les boites des movers a un monde de collision.
        ///
        /// ⚠️ A rappeler A CHAQUE PAS, apres Step. Une porte dont la boite de
        /// collision date du pas precedent laisse passer au travers pendant son
        /// mouvement - un decalage d'un seul pas suffit a traverser une porte
        /// fine a 2 m/s.
        /// </summary>
        public void AppendTo(CollisionWorld world)
        {
            ArgumentNullException.ThrowIfNull(world);

            for (int i = 0; i < _movers.Count; i++)
                world.Add(_movers[i].Box);
        }

        /// <summary>
        /// Somme des deplacements des movers qui portent cette boite.
        ///
        /// ⚠️ SANS CECI, UN ASCENSEUR EST INUTILISABLE : la plateforme monte et
        /// le joueur reste en l'air, puis retombe dessus, puis reste en l'air.
        /// Ce n'est pas un raffinement, c'est ce qui fait qu'une plateforme
        /// mobile a un sens.
        ///
        /// On additionne au lieu de prendre le premier : deux plateformes
        /// jointives qui montent ensemble ne doivent pas porter deux fois moins
        /// vite selon celle qu'on regarde en premier.
        /// </summary>
        public Vector3 CarryDelta(in Aabb rider)
        {
            Vector3 total = Vector3.Zero;
            int carriers = 0;

            for (int i = 0; i < _movers.Count; i++)
            {
                if (!_movers[i].Carries(rider))
                    continue;

                total += _movers[i].Delta;
                carriers++;
            }

            // A cheval sur deux plateformes qui montent a la meme vitesse, la
            // somme donnerait le double. On prend la moyenne : c'est le seul
            // choix qui donne le meme resultat qu'une plateforme unique.
            return carriers > 1 ? total / carriers : total;
        }

        /// <summary>
        /// Jeu laisse entre les pieds du passager et le dessus de la plateforme.
        ///
        /// ⚠️⚠️ CE N'EST PAS UN BRICOLAGE, C'EST LA CORRECTION D'UN VRAI DEFAUT,
        /// et il a fallu une CAPTURE pour le voir : les 27 tests passaient et
        /// l'ascenseur ne portait personne en jeu. Mesure du defaut : la
        /// plateforme arrivait a Y 3,80 et le joueur restait a Y 0,02.
        ///
        /// CharacterMotor decide qu'il est au sol par un BALAYAGE de 5 cm vers
        /// le bas, et ce balayage IGNORE ce qui chevauche deja la boite au
        /// depart - comportement volontaire, sinon on resterait coince dans un
        /// mur. Verifie en instrumentant : pieds a 1 mm SOUS le dessus de la
        /// plateforme, le balayage repond hit=False.
        ///
        /// ⚠️⚠️ CE QUI CORRIGE LE DEFAUT N'EST PAS CE JEU, C'EST LE RECALAGE.
        /// Soyons precis, parce que je l'ai d'abord ecrit de travers : le vrai
        /// defaut etait de se contenter de « pieds += deplacement ». La position
        /// du joueur s'accumule pas apres pas, celle de la plateforme se
        /// recalcule par interpolation, et les deux derivent de quelques 10⁻⁵ -
        /// une fois sur deux le joueur passe un cheveu SOUS la surface, donc en
        /// chevauchement, donc pas au sol. CarryRider REPOSE donc les pieds sur
        /// le dessus a chaque pas. Verifie par mutation : sans le recalage, le
        /// test d'integration tombe.
        ///
        /// Ce jeu-ci, lui, sert seulement a ne pas rester sur le fil du rasoir :
        /// pose EXACTEMENT a zero, on est pile a la frontiere du test de
        /// chevauchement, et 1 mm de l'autre cote suffit a tout casser.
        ///
        /// ⚠️ HONNETETE SUR CETTE VALEUR : aucun test ne la discrimine. Mise a
        /// zero, les 497 tests passent - le recalage suffit dans un monde
        /// deterministe. Ce qui EST verifie : un jeu negatif fait tomber le test
        /// d'integration (le passager s'enfonce et n'est plus au sol), et un jeu
        /// de 8 cm aussi (il sort des 5 cm du balayage). La valeur est donc
        /// encadree par la mesure, mais son interet propre reste un choix de
        /// prudence, pas une necessite demontree.
        /// </summary>
        public const float ContactGap = 0.002f;

        /// <summary>
        /// Deplace un passager avec la plateforme qui le porte.
        ///
        /// C'est la forme a utiliser dans un jeu : elle applique le deplacement
        /// ET le jeu de contact. CarryDelta seule rend la geometrie brute, ce
        /// qui laisse le defaut ci-dessus a recommettre.
        /// </summary>
        /// <returns>Vrai si quelqu'un l'a porte.</returns>
        public bool CarryRider(ref Vector3 feet, in Aabb rider)
        {
            Vector3 delta = CarryDelta(rider);

            if (delta == Vector3.Zero)
                return false;

            feet += delta;

            // On REPOSE les pieds sur le dessus, plus un jeu. Se contenter
            // d'ajouter le deplacement laisse la derive flottante decider, et
            // elle decide mal une fois sur deux.
            for (int i = 0; i < _movers.Count; i++)
            {
                if (!_movers[i].Carries(rider))
                    continue;

                feet.Y = _movers[i].Box.Max.Y + ContactGap;
                break;
            }

            return true;
        }

        public Mover? Find(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            for (int i = 0; i < _movers.Count; i++)
                if (string.Equals(_movers[i].Name, name, StringComparison.Ordinal))
                    return _movers[i];

            return null;
        }

        /// <summary>Inscrit tous les movers comme cibles d'entrees.</summary>
        public void RegisterInto(EntityWorld world)
        {
            ArgumentNullException.ThrowIfNull(world);

            for (int i = 0; i < _movers.Count; i++)
                world.Register(_movers[i]);
        }
    }
}

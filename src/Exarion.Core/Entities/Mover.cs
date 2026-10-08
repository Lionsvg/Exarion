// ===========================================================================
//  Exarion - Geometrie qui BOUGE, et entites qui recoivent (E17)
//
//  ---------------------------------------------------------------------
//  POURQUOI C'ETAIT LE MANQUE LE PLUS GRAVE
//
//  E13 a donne aux entites des SORTIES : « piege.OnEnter -> porte.Ouvrir »
//  se lisait dans le fichier, s'affichait, se testait. Et il ne se passait
//  RIEN, pour une raison simple : aucune entite ne savait recevoir, et
//  surtout il n'existait aucune porte. Un niveau Exarion etait du decor
//  strictement immobile.
//
//  C'est le pas qui fait passer d'un decor a un JEU. Un declencheur qui
//  ouvre une porte, une plateforme qui monte, un pont qui se deploie : a
//  partir d'ici, l'auteur du niveau peut fabriquer une consequence sans
//  ecrire une ligne de C#.
//  ---------------------------------------------------------------------
//
//  ⚠️ UNE PORTE ET UN ASCENSEUR SONT LA MEME CHOSE, et c'est deliberre.
//  Les deux sont une boite solide qui va d'un point A a un point B a une
//  vitesse donnee. Source fait la meme economie (func_door et
//  func_movelinear sont quasi identiques). Deux classes auraient donne deux
//  comportements a faire diverger, pour zero gain.
//
//  ⚠️ LE FORMAT INTERDIT LES ESPACES DANS UNE VALEUR DE PROPRIETE. On ne
//  peut donc PAS ecrire « move=0 1 0 ». D'ou axis=x|y|z plus une distance
//  signee : ca couvre porte coulissante, herse, ascenseur et pont. Le
//  deplacement en diagonale n'est pas exprimable - limite assumee, notee.
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Numerics;
using Exarion.Core.Assets;
using Exarion.Core.Physics;

namespace Exarion.Core.Entities
{
    /// <summary>
    /// Ce qu'une entite doit savoir faire pour etre la CIBLE d'une sortie.
    ///
    /// Une interface minuscule, et c'est voulu : le cablage est de la donnee,
    /// donc tout ce qu'une cible a besoin d'exposer c'est un nom et « tiens,
    /// on te demande ca ».
    /// </summary>
    public interface IEntityInput
    {
        /// <summary>Nom de l'entite. C'est par lui qu'une sortie la trouve.</summary>
        string Name { get; }

        /// <summary>
        /// Recoit une entree.
        /// </summary>
        /// <returns>
        /// Faux si l'entree est inconnue. ⚠️ Ce retour n'est pas decoratif :
        /// il permet de distinguer « personne n'ecoute » de « quelqu'un ecoute
        /// mais ne comprend pas ce mot ». Une faute de frappe sur un nom
        /// d'entree, avalee en silence, donnerait une porte qui ne s'ouvre
        /// jamais sans que rien ne l'explique.
        /// </returns>
        bool Receive(string input);
    }

    public enum MoverState
    {
        /// <summary>Au repos sur sa position de depart.</summary>
        Ferme,

        /// <summary>En route vers la position ouverte.</summary>
        Ouverture,

        /// <summary>Au repos sur sa position d'arrivee.</summary>
        Ouvert,

        /// <summary>En route vers la position de depart.</summary>
        Fermeture,
    }

    /// <summary>
    /// Une boite solide qui va-et-vient entre deux positions.
    ///
    /// Porte, herse, ascenseur, pont, piston : tout ca.
    /// </summary>
    public sealed class Mover : IEntityInput
    {
        /// <summary>Entrees comprises. Sans accent, comme tout le code.</summary>
        public const string InputOpen = "Ouvrir";
        public const string InputClose = "Fermer";
        public const string InputToggle = "Basculer";

        /// <summary>Sorties emises quand le mouvement se termine.</summary>
        public const string OnFullyOpen = "OnOuvert";
        public const string OnFullyClosed = "OnFerme";

        public const float DefaultSpeed = 2f;
        public const float DefaultDistance = 3f;
        public const float DefaultThickness = 0.3f;

        /// <summary>Cote minimal d'une boite, en metres.</summary>
        public const float MinimumSize = 0.05f;

        private float _fraction;
        private float _waitLeft;
        private Vector3 _previousCenter;

        /// <summary>
        /// Construit depuis une entite de carte.
        ///
        /// Toutes les valeurs ont un repli : une carte qui ne declare qu'un
        /// « entity func_door 0 1 0 » doit donner une porte fonctionnelle, pas
        /// une exception. Un auteur decouvre un type d'entite en le posant.
        /// </summary>
        public Mover(MapEntity entity)
        {
            ArgumentNullException.ThrowIfNull(entity);

            Entity = entity;
            Name = entity.Get("name");

            // ⚠️ Une taille nulle donnerait un intervalle inverse, que
            // Intersects ne croise JAMAIS : la porte serait invisible aux
            // collisions tout en s'affichant. Meme piege que les boites de
            // decor et que les volumes de E13.
            Size = new Vector3(
                MathF.Max(MinimumSize, entity.GetFloat("width", 2f)),
                MathF.Max(MinimumSize, entity.GetFloat("height", 3f)),
                MathF.Max(MinimumSize, entity.GetFloat("depth", DefaultThickness)));

            Speed = MathF.Max(0.01f, entity.GetFloat("speed", DefaultSpeed));
            Wait = entity.GetFloat("wait", 0f);

            float distance = entity.GetFloat("distance", DefaultDistance);
            Vector3 direction = AxisOf(entity.Get("axis", "y"));

            ClosedCenter = entity.Position + new Vector3(0f, Size.Y * 0.5f, 0f);
            OpenCenter = ClosedCenter + direction * distance;

            // ⚠️ Une porte de distance nulle ne bougera jamais. On la garde
            // solide et on le DIT (Degenerate), plutot que de diviser par zero
            // en calculant l'avancement.
            Degenerate = MathF.Abs(distance) < 1e-4f;

            if (entity.GetInt("start_open", 0) != 0)
            {
                _fraction = 1f;
                State = MoverState.Ouvert;
            }

            Blocked = entity.Get("blocked", "reverse").Trim().ToLowerInvariant() switch
            {
                "stop" => BlockPolicy.Stop,
                "push" => BlockPolicy.Push,
                _ => BlockPolicy.Reverse,
            };

            _previousCenter = Center;
        }

        /// <summary>L'entite d'origine. Sert a relire une propriete non prevue.</summary>
        public MapEntity Entity { get; }

        public string Name { get; }

        public Vector3 Size { get; }
        public Vector3 ClosedCenter { get; }
        public Vector3 OpenCenter { get; }

        /// <summary>Vitesse de deplacement, en metres par seconde.</summary>
        public float Speed { get; }

        /// <summary>
        /// Secondes d'attente en position ouverte avant de se refermer seule.
        /// Zero ou moins : elle reste ouverte.
        /// </summary>
        public float Wait { get; }

        /// <summary>Vrai si les deux positions sont confondues : elle ne bougera pas.</summary>
        public bool Degenerate { get; }

        public MoverState State { get; private set; } = MoverState.Ferme;

        /// <summary>0 = ferme, 1 = ouvert. Toujours dans [0,1].</summary>
        public float Fraction => _fraction;

        /// <summary>Centre actuel.</summary>
        public Vector3 Center => Vector3.Lerp(ClosedCenter, OpenCenter, _fraction);

        /// <summary>
        /// Deplacement REEL du dernier pas.
        ///
        /// ⚠️ C'est bien (nouveau centre - ancien centre), et PAS
        /// direction x vitesse x dt. Au dernier pas la course est ecretee par
        /// la butee : la valeur theorique depasse alors le mouvement effectif,
        /// et un joueur porte par la plateforme se ferait pousser plus loin
        /// qu'elle n'a bouge. L'erreur s'accumule a chaque aller-retour et
        /// finit par le decaler du niveau.
        /// </summary>
        public Vector3 Delta { get; private set; }

        /// <summary>Boite solide actuelle.</summary>
        public Aabb Box => Aabb.FromCenterSize(Center, Size);

        /// <summary>
        /// Boite d'AVANT le dernier pas.
        ///
        /// ⚠️ C'est contre celle-ci qu'on decide si quelqu'un est porte, pas
        /// contre la boite actuelle. Le passager n'a pas encore bouge : ses
        /// pieds sont au niveau de l'ancien dessus. En testant contre le
        /// nouveau, la tolerance devient une limite de vitesse deguisee - une
        /// plateforme assez rapide larguerait son passager, et le seuil exact
        /// dependrait du tickrate.
        /// </summary>
        public Aabb PreviousBox => Aabb.FromCenterSize(_previousCenter, Size);

        /// <summary>Sortie a emettre a la fin du dernier pas, ou la chaine vide.</summary>
        public string PendingOutput { get; private set; } = "";

        public bool Receive(string input)
        {
            if (string.Equals(input, InputOpen, StringComparison.OrdinalIgnoreCase))
            {
                Open();
                return true;
            }

            if (string.Equals(input, InputClose, StringComparison.OrdinalIgnoreCase))
            {
                Close();
                return true;
            }

            if (string.Equals(input, InputToggle, StringComparison.OrdinalIgnoreCase))
            {
                // Une porte en cours de mouvement repart dans l'autre sens :
                // c'est ce qu'on attend d'un bouton actionne deux fois de suite.
                if (State is MoverState.Ouvert or MoverState.Ouverture)
                    Close();
                else
                    Open();

                return true;
            }

            return false;
        }

        public void Open()
        {
            if (State == MoverState.Ouvert)
            {
                // Deja ouverte : on RECHARGE le compte a rebours. Un joueur qui
                // reste dans le volume d'un declencheur ne doit pas voir la
                // porte se refermer sur lui.
                _waitLeft = Wait;
                return;
            }

            State = MoverState.Ouverture;
        }

        public void Close()
        {
            if (State == MoverState.Ferme)
                return;

            State = MoverState.Fermeture;
        }

        /// <summary>
        /// Avance d'un pas de simulation.
        ///
        /// Au PAS FIXE, comme la physique : une porte est un etat qui
        /// s'accumule, pas une fonction du temps. La simuler au rendu ferait
        /// dependre sa vitesse du nombre d'images par seconde.
        /// </summary>
        /// <param name="blockers">
        /// Ce qui ne doit pas etre traverse : joueur, PNJ, caisses. Null : rien
        /// ne bloque (le comportement d'avant le 08/10).
        /// </param>
        public void Step(float dt, IReadOnlyList<Aabb>? blockers = null)
        {
            PendingOutput = "";
            _previousCenter = Center;

            if (dt > 0f && !Degenerate)
            {
                float course = Vector3.Distance(ClosedCenter, OpenCenter);
                float step = Speed * dt / course;

                switch (State)
                {
                    case MoverState.Ouverture:
                    {
                        float next = MathF.Min(1f, _fraction + step);

                        if (WouldHit(next, blockers))
                        {
                            OnBlock();
                            break;
                        }

                        _wasBlocked = false;
                        _fraction = next;

                        if (_fraction >= 1f)
                        {
                            _fraction = 1f;
                            State = MoverState.Ouvert;
                            PendingOutput = OnFullyOpen;
                            _waitLeft = Wait;
                        }
                        break;
                    }

                    case MoverState.Fermeture:
                    {
                        float next = MathF.Max(0f, _fraction - step);

                        if (WouldHit(next, blockers))
                        {
                            OnBlock();
                            break;
                        }

                        _wasBlocked = false;
                        _fraction = next;

                        if (_fraction <= 0f)
                        {
                            _fraction = 0f;
                            State = MoverState.Ferme;
                            PendingOutput = OnFullyClosed;
                        }
                        break;
                    }

                    // ⚠️⚠️ CE « when » EST CE QUI PROTEGE LA FERMETURE AUTOMATIQUE,
                    // et je l'ai d'abord attribue au mauvais endroit du code.
                    //
                    // J'avais ecrit que le compte a rebours devait demarrer a
                    // l'ARRIVEE, sinon une porte de 3 s avec wait=1 se
                    // refermerait en pleine montee. Verifie par mutation : c'est
                    // FAUX. En le lancant a l'ouverture ET en le retirant de
                    // l'arrivee, AUCUN des 26 tests ne tombe - parce que le
                    // compteur ne s'ecoule que dans l'etat Ouvert, donc jamais
                    // pendant la course. C'est ce filtre d'etat qui fait le
                    // travail, pas la place de l'initialisation.
                    //
                    // Ce qui compte reellement, lui, est verifie : mettre autre
                    // chose que Wait a l'arrivee fait tomber 2 tests.
                    case MoverState.Ouvert when Wait > 0f:
                        _waitLeft -= dt;

                        if (_waitLeft <= 0f)
                            State = MoverState.Fermeture;
                        break;
                }
            }

            Delta = Center - _previousCenter;
        }

        // ------------------------------------------------------------------
        //  BLOCAGE (08/10)
        //
        //  Avant, une porte avancait sans regarder : refermee sur le joueur,
        //  elle le CHEVAUCHAIT, et c'etait au CharacterMotor de l'en sortir. Une
        //  porte de Source repart en arriere. C'est le defaut ici aussi - et
        //  l'ancien comportement reste disponible (blocked=push).
        // ------------------------------------------------------------------

        /// <summary>Sortie emise quand la course est BLOQUEE par quelqu'un. Une fois par blocage.</summary>
        public const string OnBlocked = "OnBloque";

        /// <summary>Que faire quand la course rencontre quelqu'un.</summary>
        public enum BlockPolicy
        {
            /// <summary>Repartir dans l'autre sens - la porte de Source. Defaut.</summary>
            Reverse,

            /// <summary>Attendre sur place que la voie se libere.</summary>
            Stop,

            /// <summary>Passer quand meme : le comportement d'avant le 08/10.</summary>
            Push,
        }

        /// <summary>« blocked » : reverse (defaut), stop, push. Un mot inconnu retombe sur reverse.</summary>
        public BlockPolicy Blocked { get; }

        private bool _wasBlocked;

        /// <summary>La course, poursuivie jusqu'a cette fraction, toucherait-elle quelqu'un ?</summary>
        private bool WouldHit(float fraction, IReadOnlyList<Aabb>? blockers)
        {
            if (blockers is null || Blocked == BlockPolicy.Push)
                return false;

            Aabb next = Aabb.FromCenterSize(Vector3.Lerp(ClosedCenter, OpenCenter, fraction), Size);
            Aabb now = Box;

            for (int i = 0; i < blockers.Count; i++)
            {
                if (!next.Intersects(blockers[i]))
                    continue;

                // ⚠️⚠️ CE QU'ON CHEVAUCHE DEJA N'EST PAS UN OBSTACLE. Trouve en
                // jeu, pas en test : un joueur teleporte DANS une porte fermee
                // la bloquait a l'ouverture - la position suivante le
                // chevauchait encore, la porte repartait, se rebloquait, et
                // oscillait sur place sans jamais s'ouvrir. Une porte doit
                // toujours pouvoir S'ELOIGNER de ce qu'elle touche ; seul ce que
                // la course touche NOUVELLEMENT l'arrete.
                if (now.Intersects(blockers[i]))
                    continue;

                // ⚠️ Un PASSAGER n'est pas un obstacle : sinon tout ascenseur se
                // figerait des qu'on monte dessus.
                if (Carries(blockers[i]))
                    continue;

                return true;
            }

            return false;
        }

        private void OnBlock()
        {
            // ⚠️ UNE sortie par blocage, pas une par pas : en mode stop, une
            // porte qui attend emettrait sinon OnBloque 64 fois par seconde -
            // la classe de defaut des volumes sans memoire (E13).
            if (!_wasBlocked)
                PendingOutput = OnBlocked;

            _wasBlocked = true;

            if (Blocked == BlockPolicy.Reverse)
                State = State == MoverState.Ouverture ? MoverState.Fermeture : MoverState.Ouverture;
        }

        /// <summary>
        /// Vrai si cette boite repose SUR le mover.
        ///
        /// ⚠️ On teste le dessus, avec une tolerance : un corps pose par un
        /// solveur n'est jamais exactement a la surface, il flotte a quelques
        /// millimetres ou s'y enfonce d'autant. Sans tolerance, une plateforme
        /// qui monte laisserait son passager sur place une image sur deux.
        /// </summary>
        public bool Carries(in Aabb rider, float tolerance = 0.12f)
        {
            Aabb box = PreviousBox;

            // Chevauchement horizontal : sinon on porterait quelqu'un qui passe
            // a cote a la meme hauteur.
            if (rider.Max.X <= box.Min.X || rider.Min.X >= box.Max.X) return false;
            if (rider.Max.Z <= box.Min.Z || rider.Min.Z >= box.Max.Z) return false;

            float feet = rider.Min.Y;
            float top = box.Max.Y;

            return feet >= top - tolerance && feet <= top + tolerance;
        }

        /// <summary>
        /// Axe de deplacement. Un axe inconnu retombe sur la verticale.
        ///
        /// ⚠️ On ne REFUSE pas un axe inconnu : le format conserve deja les
        /// types d'entites qu'il ne connait pas, refuser ici serait incoherent.
        /// La porte monte alors, ce qui se voit tout de suite - contrairement a
        /// une porte qui ne bougerait pas du tout.
        /// </summary>
        public static Vector3 AxisOf(string axis) => axis?.Trim().ToLowerInvariant() switch
        {
            "x" => Vector3.UnitX,
            "z" => Vector3.UnitZ,
            _ => Vector3.UnitY,
        };
    }
}

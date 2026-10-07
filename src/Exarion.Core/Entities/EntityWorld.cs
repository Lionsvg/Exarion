// ===========================================================================
//  Exarion - Entites vivantes : sorties vers entrees (E13)
//
//  Les entites du format savent se decrire, pas AGIR. Ce fichier leur donne
//  un comportement, et il le fait de la maniere dont Source le fait :
//
//      un declencheur a des SORTIES        OnEnter
//      qui visent une CIBLE par son nom    porte_du_hall
//      et lui envoient une ENTREE          Ouvrir
//
//  ---------------------------------------------------------------------
//  POURQUOI CE MODELE ET PAS DU SCRIPT
//
//  Un langage de script serait plus expressif. Il serait aussi une
//  dependance, un bac a sable a securiser, et surtout une boucle
//  d'execution supplementaire a debuguer. Le cablage sorties/entrees, lui,
//  est de la DONNEE : il se lit dans le fichier de carte, se compare, se
//  teste en ligne de commande, et ne peut pas boucler a l'infini par
//  accident.
//
//  Half-Life 2 entier est cable comme ca. Le script viendra par-dessus, pour
//  ce que la donnee ne sait pas exprimer - pas a la place.
//  ---------------------------------------------------------------------
//
//  Ecrit dans Core : aucun rendu, aucune fenetre. Un declencheur se teste en
//  faisant bouger un point dans une boite, sans jamais afficher quoi que ce
//  soit.
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Numerics;
using Exarion.Core.Assets;
using Exarion.Core.Physics;

namespace Exarion.Core.Entities
{
    /// <summary>
    /// Ce qu'est devenue une sortie.
    ///
    /// ⚠️⚠️ CES QUATRE CAS SONT LA RAISON D'ETRE DE CET ENUM, et ils se
    /// ressemblaient tous avant E17 : « rien ne s'est passe ». Or ils ont
    /// quatre causes completement differentes, et trois sont des FAUTES DANS
    /// LA CARTE qu'il faut pouvoir montrer a l'auteur :
    ///
    ///   NonCablee   -> l'entite n'a pas de target. Volontaire la plupart du temps.
    ///   CibleAbsente-> target=porte_hal au lieu de porte_hall. FAUTE DE FRAPPE.
    ///   EntreeInconnue -> la porte existe mais on lui dit « Ouvre » et elle
    ///                     comprend « Ouvrir ». FAUTE DE FRAPPE.
    ///   Delivree    -> quelqu'un a recu et agi.
    ///
    /// Sans cette distinction, l'auteur d'un niveau cherche pourquoi sa porte
    /// ne s'ouvre pas en relisant son declencheur - alors que le declencheur
    /// marche parfaitement et vise un nom qui n'existe pas.
    /// </summary>
    public enum EntityDelivery
    {
        NonCablee,
        CibleAbsente,
        EntreeInconnue,
        Delivree,
    }

    /// <summary>Ce qui vient de se passer, pour qui veut l'afficher ou le jouer.</summary>
    public readonly struct EntityEvent
    {
        /// <summary>Entite qui a declenche.</summary>
        public readonly MapEntity Source;

        /// <summary>Nom de la sortie, par exemple « OnEnter ».</summary>
        public readonly string Output;

        /// <summary>Cible nommee, ou la chaine vide.</summary>
        public readonly string Target;

        /// <summary>Entree envoyee a la cible, ou la chaine vide.</summary>
        public readonly string Input;

        /// <summary>Ce qu'est devenue cette sortie.</summary>
        public readonly EntityDelivery Delivery;

        public EntityEvent(MapEntity source, string output, string target, string input,
                           EntityDelivery delivery = EntityDelivery.NonCablee)
        {
            Source = source;
            Output = output;
            Target = target;
            Input = input;
            Delivery = delivery;
        }

        public override string ToString()
        {
            if (string.IsNullOrEmpty(Target))
                return $"{Source.Type}.{Output}";

            string wiring = $"{Source.Type}.{Output} -> {Target}.{Input}";

            return Delivery switch
            {
                EntityDelivery.Delivree => wiring,
                EntityDelivery.CibleAbsente => wiring + "  [CIBLE INTROUVABLE]",
                EntityDelivery.EntreeInconnue => wiring + "  [ENTREE INCONNUE]",
                _ => wiring,
            };
        }
    }

    public sealed class EntityWorld
    {
        /// <summary>Nom de la sortie emise a l'entree dans un volume.</summary>
        public const string OnEnter = "OnEnter";

        /// <summary>Nom de la sortie emise a la sortie d'un volume.</summary>
        public const string OnExit = "OnExit";

        /// <summary>Cote du volume par defaut, en metres, si l'entite ne dit rien.</summary>
        public const float DefaultVolumeSize = 3f;

        /// <summary>
        /// Etat d'un volume, par entite.
        ///
        /// ⚠️ Un declencheur DOIT se souvenir s'il contenait deja le joueur.
        /// Sans cette memoire, il emettrait OnEnter a chaque pas de simulation
        /// tant qu'on reste dedans - soixante-quatre fois par seconde. Un
        /// checkpoint « passe » deviendrait un checkpoint « passe 64 fois », et
        /// un evenement une avalanche.
        /// </summary>
        private readonly List<bool> _inside = new();

        private readonly List<EntityEvent> _events = new();

        /// <summary>Volumes deja consommes, pour les declencheurs a usage unique.</summary>
        private readonly HashSet<int> _spent = new();

        private MapData _map = new();

        /// <summary>Checkpoints franchis, dans l'ordre de passage.</summary>
        public List<string> Checkpoints { get; } = new();

        /// <summary>Evenements de la derniere mise a jour. Vide la plupart du temps.</summary>
        public IReadOnlyList<EntityEvent> Events => _events;

        /// <summary>Total depuis le debut, pour verifier sans conserver les objets.</summary>
        public int TotalEvents { get; private set; }

        public MapData Map => _map;

        /// <summary>
        /// Repart sur une carte. Remet a zero toute la memoire de declenchement.
        ///
        /// ⚠️ Indispensable : un indice de volume renvoie a une autre entite
        /// d'une carte a l'autre. Garder l'etat ferait qu'un checkpoint deja
        /// franchi bloquerait un declencheur sans rapport dans le niveau suivant.
        /// </summary>
        public void Load(MapData map)
        {
            _map = map ?? throw new ArgumentNullException(nameof(map));

            _inside.Clear();
            _spent.Clear();
            _events.Clear();
            Checkpoints.Clear();

            // ⚠️ LES CIBLES AUSSI. Une porte de la carte precedente resterait
            // inscrite sous son nom : un declencheur du nouveau niveau qui vise
            // « porte_hall » actionnerait une porte qui n'existe plus, donc
            // invisible. Exactement la meme classe de defaut que l'indice de
            // volume conserve d'une carte a l'autre. L'appelant reinscrit
            // APRES Load - c'est l'ordre que suit EnterPlay.
            _receivers.Clear();

            for (int i = 0; i < _map.Entities.Count; i++)
                _inside.Add(false);
        }

        /// <summary>
        /// Fait avancer les entites pour une position de joueur donnee.
        ///
        /// On prend une BOITE et non un point : un joueur mesure 1,80 m, et un
        /// declencheur pose au sol qu'on testerait contre les seuls pieds
        /// raterait quelqu'un qui saute par-dessus.
        /// </summary>
        public void Update(in Aabb player)
        {
            _events.Clear();

            // La liste peut avoir change sous nos pieds - l'editeur ajoute des
            // entites en cours de route. On remet la memoire a la bonne taille
            // plutot que de sortir en erreur.
            while (_inside.Count < _map.Entities.Count) _inside.Add(false);
            while (_inside.Count > _map.Entities.Count) _inside.RemoveAt(_inside.Count - 1);

            for (int i = 0; i < _map.Entities.Count; i++)
            {
                MapEntity entity = _map.Entities[i];

                if (!IsVolume(entity.Type))
                    continue;

                bool wasInside = _inside[i];
                bool nowInside = VolumeOf(entity).Intersects(player);

                _inside[i] = nowInside;

                if (nowInside == wasInside)
                    continue;

                if (nowInside)
                    Enter(i, entity);
                else
                    Emit(entity, OnExit);
            }
        }

        private void Enter(int index, MapEntity entity)
        {
            // Un declencheur a usage unique ne se rearme pas. On garde quand
            // meme l'etat « dedans » a jour : sinon, en ressortant puis
            // rentrant, il faudrait deviner pourquoi rien ne se passe.
            if (_spent.Contains(index))
                return;

            if (entity.GetInt("once", 0) != 0)
                _spent.Add(index);

            if (string.Equals(entity.Type, EntityTypes.Checkpoint, StringComparison.Ordinal))
            {
                string name = entity.Get("name", entity.Get("order", Checkpoints.Count.ToString()));

                // ⚠️ Un checkpoint ne s'enregistre QU'UNE FOIS, meme sans
                // « once » : repasser dessus ne doit pas allonger la liste.
                // C'est ce qui distingue un checkpoint d'un declencheur.
                if (!Checkpoints.Contains(name))
                    Checkpoints.Add(name);
            }

            Emit(entity, OnEnter);
        }

        private void Emit(MapEntity entity, string output)
        {
            // La sortie n'est cablee que si l'entite le dit. Une entite sans
            // cible emet quand meme l'evenement : c'est ce qui permet a
            // l'affichage de montrer un declenchement non cable, plutot que de
            // laisser croire que rien ne s'est produit.
            string target = entity.Get("target");
            string input = entity.Get(output == OnEnter ? "input" : "input_exit",
                                     output == OnEnter ? "Trigger" : "Untrigger");

            _events.Add(new EntityEvent(entity, output, target, input, Deliver(target, input)));
            TotalEvents++;
        }

        /// <summary>
        /// Remet une entree a la cible nommee, si quelqu'un porte ce nom.
        ///
        /// C'est ici que E13 se referme : jusqu'a E17, cette methode n'existait
        /// pas et l'evenement mourait dans la liste.
        /// </summary>
        private EntityDelivery Deliver(string target, string input)
        {
            if (string.IsNullOrEmpty(target))
                return EntityDelivery.NonCablee;

            IEntityInput? receiver = FindReceiver(target);

            if (receiver is null)
                return EntityDelivery.CibleAbsente;

            // ⚠️ Une exception dans une entite de carte ne doit PAS tuer la
            // partie. Meme lecon que les handlers de ConVar, qui ont deja
            // coute un processus : la donnee vient d'un fichier que n'importe
            // qui edite a la main.
            try
            {
                return receiver.Receive(input)
                    ? EntityDelivery.Delivree
                    : EntityDelivery.EntreeInconnue;
            }
            catch (Exception)
            {
                return EntityDelivery.EntreeInconnue;
            }
        }

        /// <summary>Volume d'une entite, centre sur elle.</summary>
        public static Aabb VolumeOf(MapEntity entity)
        {
            float size = entity.GetFloat("size", DefaultVolumeSize);

            // Une taille nulle ou negative donnerait un intervalle inverse, que
            // Intersects ne croise jamais : le declencheur serait muet sans que
            // rien ne le signale. Meme piege que les boites de decor.
            if (size <= 0f)
                size = DefaultVolumeSize;

            var half = new Vector3(size * 0.5f,
                                   entity.GetFloat("height", size) * 0.5f,
                                   size * 0.5f);

            return new Aabb(entity.Position - half, entity.Position + half);
        }

        public static bool IsVolume(string type)
            => string.Equals(type, EntityTypes.Checkpoint, StringComparison.Ordinal)
            || string.Equals(type, EntityTypes.Trigger, StringComparison.Ordinal);

        // -------------------------------------------------------------------
        //  LES CIBLES
        //
        //  Une liste plutot qu'un dictionnaire : il y a quelques dizaines de
        //  cibles dans un niveau, et un declenchement est rare (quelques par
        //  seconde au pire). Un dictionnaire ferait gagner un temps
        //  imperceptible et couterait la garantie d'ordre - deux entites du
        //  meme nom seraient resolues differemment d'un chargement a l'autre.
        //
        //  ⚠️ A DOUBLON DE NOM, LA PREMIERE GAGNE, comme le premier point
        //  d'apparition gagne. Un tirage rendrait les captures incomparables.
        // -------------------------------------------------------------------

        private readonly List<IEntityInput> _receivers = new();

        /// <summary>Cibles inscrites. Vide tant que rien ne sait recevoir.</summary>
        public IReadOnlyList<IEntityInput> Receivers => _receivers;

        /// <summary>
        /// Inscrit une cible. Un nom vide est refuse : une entite anonyme ne
        /// peut pas etre visee, l'inscrire ne ferait que ralentir la recherche.
        /// </summary>
        public bool Register(IEntityInput receiver)
        {
            if (receiver is null || string.IsNullOrEmpty(receiver.Name))
                return false;

            _receivers.Add(receiver);
            return true;
        }

        public void ClearReceivers() => _receivers.Clear();

        public IEntityInput? FindReceiver(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            for (int i = 0; i < _receivers.Count; i++)
                if (string.Equals(_receivers[i].Name, name, StringComparison.Ordinal))
                    return _receivers[i];

            return null;
        }

        /// <summary>
        /// Envoie une entree a la main, hors de tout declencheur.
        ///
        /// Existe pour la CONSOLE : sans elle, tester une porte demanderait de
        /// courir jusqu'a son declencheur, donc serait inverifiable en headless.
        /// C'est la meme raison qui a fait exister ed_select et compagnie.
        /// </summary>
        public EntityDelivery Send(string target, string input) => Deliver(target, input);

        /// <summary>Entite portant ce nom, ou null. Sert a resoudre une cible.</summary>
        public MapEntity? Find(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            foreach (MapEntity entity in _map.Entities)
                if (string.Equals(entity.Get("name"), name, StringComparison.Ordinal))
                    return entity;

            return null;
        }

        /// <summary>Rearme tout, sans changer de carte. Utile en console.</summary>
        public void Rearm()
        {
            _spent.Clear();
            Checkpoints.Clear();

            for (int i = 0; i < _inside.Count; i++)
                _inside[i] = false;
        }
    }
}

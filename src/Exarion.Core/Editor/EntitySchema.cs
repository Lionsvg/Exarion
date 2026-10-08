// ===========================================================================
//  Exarion - Proprietes CONNUES de chaque type d'entite
//
//  Le format de carte accepte n'importe quelle cle : c'est ce qui permet
//  d'ajouter un type d'entite sans toucher au lecteur (voir MapEntity). Ce
//  choix a un revers, et il s'est vu des le premier usage du panneau : une
//  entite fraichement posee n'a AUCUNE propriete, donc le panneau ne montre
//  rien, donc rien ne dit qu'un PNJ se regle avec « speed » et « sight ».
//
//  Ce fichier est le catalogue qui repare ca : pour chaque type, les cles que
//  le moteur lit VRAIMENT, avec leur valeur par defaut. Le panneau les affiche
//  meme absentes - exactement comme la fenetre Properties de Roblox Studio,
//  qui montre toujours toutes les proprietes d'une Part.
//
//  ---------------------------------------------------------------------
//  ⚠️ CE CATALOGUE DOIT SUIVRE LE CODE, ET C'EST SON SEUL RISQUE.
//
//  Chaque cle ci-dessous a ete relevee dans le code qui la LIT - AgentWorld,
//  EntityWorld, Mover, MoverWorld.DeliverOutputs - pas recopiee d'une note.
//  Une cle inventee ici serait pire qu'une cle absente : elle apparaitrait
//  dans le panneau, l'auteur du niveau la reglerait, et rien ne se passerait.
//  Le defaut se lirait comme « le moteur ignore mes reglages ».
//
//  Un defaut affiche qui ne correspond pas au defaut du code a exactement le
//  meme effet, en plus discret : on croit lire l'etat du moteur.
//
//  ⚠️⚠️ ET C'ETAIT LE CAS POUR CINQ D'ENTRE ELLES, trouvees le 08/10 : la
//  taille des volumes (4 affiche, 3 lu), l'epaisseur des portes (0,2 contre
//  0,3), les entrees des PNJ (Trigger/Untrigger contre Alert/Calm) - et trois
//  cles lues mais absentes d'ici (input_goal, input_open, input_close).
//  EntitySchemaTests confronte desormais chaque defaut au moteur : une entite
//  SANS propriete et la meme avec TOUS les defauts ecrits doivent se
//  comporter a l'identique. Une cle MANQUANTE, elle, ne se detecte pas ainsi.
//  ---------------------------------------------------------------------
// ===========================================================================

using System;
using System.Collections.Generic;
using Exarion.Core.Assets;

namespace Exarion.Core.Editor
{
    /// <summary>Une propriete attendue par un type d'entite.</summary>
    public readonly struct PropertySpec
    {
        public readonly string Key;

        /// <summary>Valeur prise par le moteur quand la propriete est absente.</summary>
        public readonly string Default;

        /// <summary>A quoi elle sert, en une ligne. Affichee en bas du panneau.</summary>
        public readonly string Hint;

        public PropertySpec(string key, string @default, string hint)
        {
            Key = key;
            Default = @default;
            Hint = hint;
        }
    }

    public static class EntitySchema
    {
        private static readonly PropertySpec[] None = Array.Empty<PropertySpec>();

        private static readonly Dictionary<string, PropertySpec[]> Table =
            new(StringComparer.Ordinal)
            {
                [EntityTypes.PlayerSpawn] = new[]
                {
                    // ⚠️ « team » n'est lue par AUCUN code aujourd'hui : le
                    // moteur prend le PREMIER point d'apparition, sans regarder
                    // l'equipe. Elle est listee parce que le format la porte et
                    // qu'elle survivra a la relecture, mais son effet est nul.
                    new PropertySpec("team", "", "equipe - PORTEE PAR LE FICHIER, PAS ENCORE LUE"),
                },

                // Defauts releves dans EntityWorld.VolumeOf : DefaultVolumeSize,
                // et une hauteur absente qui REPREND le cote.
                [EntityTypes.Checkpoint] = new[]
                {
                    new PropertySpec("name", "", "nom affiche dans la liste des checkpoints"),
                    new PropertySpec("order", "", "nom de repli sans « name » - absent : rang dans la carte (#n)"),
                    new PropertySpec("size", "3", "cote du volume, en metres"),
                    new PropertySpec("height", "3", "hauteur - ABSENTE, elle reprend size"),
                    new PropertySpec("once", "0", "1 = ne se declenche qu'une fois"),
                },

                [EntityTypes.Trigger] = new[]
                {
                    new PropertySpec("name", "", "nom, pour etre vise par une autre sortie"),
                    new PropertySpec("size", "3", "cote du volume, en metres"),
                    new PropertySpec("height", "3", "hauteur - ABSENTE, elle reprend size"),
                    new PropertySpec("target", "", "nom de l'entite a actionner"),
                    new PropertySpec("input", "Trigger", "entree appelee sur OnEnter"),
                    new PropertySpec("input_exit", "Untrigger", "entree appelee sur OnExit"),
                    new PropertySpec("once", "0", "1 = ne se declenche qu'une fois"),
                },

                // Les entrees par defaut viennent de AgentWorld.DefaultInputFor.
                // Aucune cible ne les comprend : elles servent a montrer qu'une
                // sortie est cablee sans entree choisie (EntreeInconnue).
                [EntityTypes.Npc] = new[]
                {
                    new PropertySpec("name", "", "HUD, et cible de Patrouiller / Arreter / Rentrer"),
                    new PropertySpec("behaviour", "patrol", "patrol ou idle"),
                    new PropertySpec("path", "defaut", "nom du chemin de points de passage"),
                    new PropertySpec("speed", "2.2", "vitesse de deplacement, m/s"),
                    new PropertySpec("sight", "20", "portee de vue, en metres"),
                    new PropertySpec("fov", "110", "champ de vision HORIZONTAL, en degres"),
                    new PropertySpec("target", "", "entite a actionner, pour les TROIS sorties"),
                    new PropertySpec("input_spot", "Alert", "entree appelee sur OnSpotPlayer"),
                    new PropertySpec("input_lose", "Calm", "entree appelee sur OnLosePlayer"),
                    new PropertySpec("input_goal", "Reached", "entree appelee sur OnReachGoal"),
                },

                [EntityTypes.Waypoint] = new[]
                {
                    new PropertySpec("path", "defaut", "chemin auquel ce point appartient"),
                    new PropertySpec("order", "0", "rang dans le chemin"),
                },

                [EntityTypes.Door] = new[]
                {
                    new PropertySpec("name", "", "OBLIGATOIRE pour etre visee par un declencheur"),
                    new PropertySpec("axis", "y", "x, y ou z - pas de diagonale"),
                    new PropertySpec("distance", "3", "course, SIGNEE"),
                    new PropertySpec("speed", "2", "vitesse de la course, m/s"),
                    new PropertySpec("width", "2", "taille en X"),
                    new PropertySpec("height", "3", "taille en Y"),
                    new PropertySpec("depth", "0.3", "taille en Z"),
                    new PropertySpec("wait", "0", "secondes avant refermeture. 0 = reste ouverte"),
                    new PropertySpec("start_open", "0", "1 = commence ouverte"),
                    new PropertySpec("target_open", "", "entite a actionner sur OnOuvert"),
                    new PropertySpec("input_open", "Trigger", "entree envoyee sur OnOuvert"),
                    new PropertySpec("target_close", "", "entite a actionner sur OnFerme"),
                    new PropertySpec("input_close", "Trigger", "entree envoyee sur OnFerme"),
                    new PropertySpec("blocked", "reverse", "si quelqu'un gene : reverse, stop ou push"),
                    new PropertySpec("target_blocked", "", "entite a actionner sur OnBloque"),
                    new PropertySpec("input_blocked", "Trigger", "entree envoyee sur OnBloque"),
                },

                // Relevees dans CrateStacks.From, defauts compris.
                [EntityTypes.Crate] = new[]
                {
                    new PropertySpec("size", "0.9", "cote de chaque caisse, en metres"),
                    new PropertySpec("mass", "18", "masse de chaque caisse, en kilos"),
                    new PropertySpec("count", "1", "nombre de caisses empilees"),
                },

                // Aucune propriete : la position et le lacet de l'entite
                // suffisent. Le type est connu pour que le panneau ne le traite
                // pas comme une entite d'une version future.
                [EntityTypes.Mannequin] = Array.Empty<PropertySpec>(),

                // Relevees dans LogicNode et LogicWorld.Enqueue.
                [EntityTypes.LogicRelay] = new[]
                {
                    new PropertySpec("name", "", "OBLIGATOIRE pour recevoir Declencher"),
                    new PropertySpec("target", "", "entite a actionner sur OnDeclenche"),
                    new PropertySpec("input", "Trigger", "entree envoyee a la cible"),
                    new PropertySpec("delay", "0", "secondes avant de relayer. 0 = au pas suivant"),
                    new PropertySpec("once", "0", "1 = se desactive apres un declenchement"),
                    new PropertySpec("start_disabled", "0", "1 = commence desactive (Activer)"),
                },

                [EntityTypes.LogicTimer] = new[]
                {
                    new PropertySpec("name", "", "nom, pour etre demarree ou arretee"),
                    new PropertySpec("target", "", "entite a actionner sur OnMinuterie"),
                    new PropertySpec("input", "Trigger", "entree envoyee a la cible"),
                    new PropertySpec("interval", "1", "secondes entre deux sorties"),
                    new PropertySpec("start_disabled", "0", "1 = arretee au depart (Demarrer)"),
                },

                [EntityTypes.LogicCounter] = new[]
                {
                    new PropertySpec("name", "", "OBLIGATOIRE pour recevoir Ajouter"),
                    new PropertySpec("target", "", "entite a actionner sur OnMax"),
                    new PropertySpec("input", "Trigger", "entree envoyee a la cible"),
                    new PropertySpec("max", "3", "valeur ou il emet OnMax"),
                    new PropertySpec("start", "0", "valeur de depart et de Remettre"),
                },

                // Relevees dans EnvLight et EnvSound.
                [EntityTypes.EnvLight] = new[]
                {
                    new PropertySpec("name", "", "OBLIGATOIRE pour Allumer / Eteindre"),
                    new PropertySpec("color", "1,0.85,0.6", "r,g,b LINEAIRE, separes par des virgules"),
                    new PropertySpec("intensity", "120", "comme une lumiere de carte : 1/d2 est brutal"),
                    new PropertySpec("range", "16", "portee, en metres"),
                    new PropertySpec("start_off", "0", "1 = eteinte au depart"),
                },

                [EntityTypes.EnvSound] = new[]
                {
                    new PropertySpec("name", "", "OBLIGATOIRE pour Jouer / Arreter"),
                    new PropertySpec("hz", "220", "hauteur du bip, de 20 a 12 000 Hz"),
                    new PropertySpec("volume", "0.5", "de 0 a 1, avant la distance"),
                    new PropertySpec("start_on", "0", "1 = joue des le depart"),
                },
            };

        /// <summary>
        /// Proprietes attendues par ce type. Vide pour un type inconnu.
        ///
        /// Un type inconnu n'est PAS une erreur - le format le conserve
        /// exprime, pour qu'une carte faite pour une version ulterieure ne
        /// perde rien. Le panneau se contente alors de montrer les proprietes
        /// reellement presentes.
        /// </summary>
        public static IReadOnlyList<PropertySpec> For(string? type)
            => type != null && Table.TryGetValue(type, out PropertySpec[]? specs) ? specs : None;

        public static bool Knows(string? type) => type != null && Table.ContainsKey(type);
    }
}

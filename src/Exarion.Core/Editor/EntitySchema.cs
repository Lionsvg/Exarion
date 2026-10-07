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
//  EntityWorld, Mover, et le cablage des portes dans Program.cs - pas
//  recopiee d'une note. Une cle inventee ici serait pire qu'une cle absente :
//  elle apparaitrait dans le panneau, l'auteur du niveau la reglerait, et
//  rien ne se passerait. Le defaut se lirait comme « le moteur ignore mes
//  reglages ».
//
//  Un defaut affiche qui ne correspond pas au defaut du code a exactement le
//  meme effet, en plus discret : on croit lire l'etat du moteur.
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

                [EntityTypes.Checkpoint] = new[]
                {
                    new PropertySpec("name", "", "nom affiche dans la liste des checkpoints"),
                    new PropertySpec("order", "0", "ordre de tri quand il n'y a pas de nom"),
                    new PropertySpec("size", "4", "cote du volume, en metres"),
                    new PropertySpec("height", "4", "hauteur du volume si differente du cote"),
                    new PropertySpec("once", "0", "1 = ne se declenche qu'une fois"),
                },

                [EntityTypes.Trigger] = new[]
                {
                    new PropertySpec("name", "", "nom, pour etre vise par une autre sortie"),
                    new PropertySpec("size", "4", "cote du volume, en metres"),
                    new PropertySpec("height", "4", "hauteur du volume si differente du cote"),
                    new PropertySpec("target", "", "nom de l'entite a actionner"),
                    new PropertySpec("input", "Trigger", "entree appelee sur OnEnter"),
                    new PropertySpec("input_exit", "Untrigger", "entree appelee sur OnExit"),
                    new PropertySpec("once", "0", "1 = ne se declenche qu'une fois"),
                },

                [EntityTypes.Npc] = new[]
                {
                    new PropertySpec("name", "", "nom affiche sur le HUD"),
                    new PropertySpec("behaviour", "patrol", "patrol ou idle"),
                    new PropertySpec("path", "defaut", "nom du chemin de points de passage"),
                    new PropertySpec("speed", "2.2", "vitesse de deplacement, m/s"),
                    new PropertySpec("sight", "20", "portee de vue, en metres"),
                    new PropertySpec("fov", "110", "champ de vision HORIZONTAL, en degres"),
                    new PropertySpec("target", "", "nom de l'entite a actionner quand il voit"),
                    new PropertySpec("input_spot", "Trigger", "entree appelee sur OnSpotPlayer"),
                    new PropertySpec("input_lose", "Untrigger", "entree appelee sur OnLosePlayer"),
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
                    new PropertySpec("depth", "0.2", "taille en Z"),
                    new PropertySpec("wait", "0", "secondes avant refermeture. 0 = reste ouverte"),
                    new PropertySpec("start_open", "0", "1 = commence ouverte"),
                    new PropertySpec("target_open", "", "entite a actionner sur OnOuvert"),
                    new PropertySpec("target_close", "", "entite a actionner sur OnFerme"),
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

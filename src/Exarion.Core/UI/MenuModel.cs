// ===========================================================================
//  Exarion - Navigation d'un menu
//
//  Quarante lignes, et pourtant dans Core plutot que dans le rendu. Deux
//  raisons :
//
//   - un menu est de la LOGIQUE, pas de l'affichage. Le bouclage haut/bas,
//     le cas de la liste vide, l'entree desactivee : ce sont des regles, et
//     elles se testent sans ouvrir de fenetre ;
//   - le meme modele servira au menu pause, au choix de carte et a l'editeur.
//     Un menu ecrit dans la boucle de rendu se recopie a chaque fois.
// ===========================================================================

using System;
using System.Collections.Generic;

namespace Exarion.Core.UI
{
    public sealed class MenuModel
    {
        private readonly List<string> _items = new();

        public MenuModel(params string[] items)
        {
            if (items != null)
                _items.AddRange(items);
        }

        public IReadOnlyList<string> Items => _items;

        public int Count => _items.Count;

        public int Selected { get; private set; }

        public bool IsEmpty => _items.Count == 0;

        /// <summary>Entree courante, ou la chaine vide si le menu est vide.</summary>
        public string Current => _items.Count == 0 ? "" : _items[Selected];

        /// <summary>
        /// Deplace la selection. ELLE BOUCLE : depuis la derniere entree, bas
        /// ramene a la premiere.
        ///
        /// Le bouclage n'est pas un detail de confort. Sans lui, la fleche du
        /// bas ne fait plus rien en bas de liste, et l'utilisateur ne sait pas
        /// si le menu est bloque ou si sa touche n'est pas passee.
        /// </summary>
        public void Move(int direction)
        {
            if (_items.Count == 0)
                return;

            int next = (Selected + Math.Sign(direction)) % _items.Count;

            // Le modulo de C# garde le SIGNE du dividende : -1 % 3 vaut -1 et
            // non 2. Sans ce rattrapage, remonter depuis la premiere entree
            // donne un indice negatif, donc une exception.
            if (next < 0)
                next += _items.Count;

            Selected = next;
        }

        public void Select(int index)
        {
            if (_items.Count == 0)
                return;

            Selected = Math.Clamp(index, 0, _items.Count - 1);
        }

        /// <summary>Remplace les entrees. La selection est ramenee dans les bornes.</summary>
        public void SetItems(IEnumerable<string> items)
        {
            _items.Clear();

            if (items != null)
                _items.AddRange(items);

            Selected = _items.Count == 0 ? 0 : Math.Clamp(Selected, 0, _items.Count - 1);
        }
    }
}

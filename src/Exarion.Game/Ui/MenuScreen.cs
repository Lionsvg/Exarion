// ===========================================================================
//  Le menu (4.1) : Jouer, Editeur, Choisir une carte, Quitter
//
//  Les REGLES de navigation vivent dans Core (MenuModel) et s'y testent. Ici,
//  on traduit les touches et on dessine.
// ===========================================================================

using System;
using System.Numerics;
using Exarion.Core.Assets;
using Exarion.Core.Console;
using Exarion.Core.UI;
using Exarion.Engine.Graphics;
using Silk.NET.Input;

namespace Exarion.Game.Ui
{
    internal sealed class MenuScreen
    {
        private readonly MenuModel _main = new("Jouer", "Editeur de niveau", "Choisir une carte", "Quitter");
        private readonly MenuModel _maps = new();

        private readonly ConsoleSystem _console;
        private readonly Action _play;
        private readonly Action _edit;
        private readonly Action _quit;
        private readonly Action _resume;
        private readonly Action<string> _loadMap;
        private readonly Func<bool> _canLoad;

        /// <param name="play">« Jouer » : entrer en partie, au point de depart.</param>
        /// <param name="edit">« Editeur de niveau ».</param>
        /// <param name="quit">« Quitter ».</param>
        /// <param name="resume">Echap au menu principal : reprendre la partie la ou on l'a laissee.</param>
        /// <param name="loadMap">Charge une carte par son nom puis entre en partie. Peut lever.</param>
        /// <param name="canLoad">Vrai quand le rendu existe et qu'une carte peut etre appliquee.</param>
        public MenuScreen(ConsoleSystem console, Action play, Action edit, Action quit, Action resume,
                          Action<string> loadMap, Func<bool> canLoad)
        {
            _console = console;
            _play = play;
            _edit = edit;
            _quit = quit;
            _resume = resume;
            _loadMap = loadMap;
            _canLoad = canLoad;
        }

        /// <summary>Vrai quand le sous-menu « Choisir une carte » est ouvert.</summary>
        public bool MapPicker { get; set; }

        /// <param name="hasMap">Une carte est chargee : Echap peut reprendre la partie.</param>
        public void OnKeyDown(Key key, bool hasMap)
        {
            MenuModel menu = MapPicker ? _maps : _main;

            switch (key)
            {
                case Key.Up: menu.Move(-1); return;
                case Key.Down: menu.Move(+1); return;

                case Key.Escape:
                    // Dans le sous-menu, Echap remonte ; au menu principal il
                    // reprend la partie plutot que de quitter - quitter est une
                    // entree explicite, on ne perd pas son travail par reflexe.
                    if (MapPicker) MapPicker = false;
                    else if (hasMap) _resume();
                    return;

                case Key.Enter:
                case Key.KeypadEnter:
                    Activate(menu);
                    return;
            }
        }

        private void Activate(MenuModel menu)
        {
            if (MapPicker)
            {
                if (!menu.IsEmpty && _canLoad())
                {
                    try
                    {
                        _loadMap(menu.Current);
                    }
                    catch (Exception ex)
                    {
                        // Une carte illisible ne doit pas fermer le jeu : on
                        // reste au menu et on le dit dans la console.
                        Console.Error.WriteLine("  Carte : " + ex.Message);
                        _console.Print("  carte illisible : " + ex.Message);
                    }
                }

                MapPicker = false;
                return;
            }

            switch (menu.Selected)
            {
                case 0: _play(); return;
                case 1: _edit(); return;

                case 2:
                    _maps.SetItems(MapLibrary.ListNames());
                    MapPicker = true;
                    return;

                case 3: _quit(); return;
            }
        }

        public void Draw(HudRenderer hud, int w, int h, MapData? map)
        {
            // Voile sombre : le decor reste visible derriere, mais le texte se
            // lit. Un menu opaque perdrait le contexte de la carte chargee.
            hud.Rect(0f, 0f, w, h, new Vector4(0.02f, 0.03f, 0.05f, 0.72f));

            float cx = w * 0.5f;

            hud.TextCentered(cx, h * 0.20f, 5f, "EXARION", new Vector4(0.85f, 0.92f, 1f, 1f));
            hud.TextCentered(cx, h * 0.20f + 52f, 2f,
                MapPicker ? "CHOISIR UNE CARTE" : "MOTEUR 3D",
                new Vector4(0.45f, 0.55f, 0.7f, 1f));

            MenuModel menu = MapPicker ? _maps : _main;
            float y = h * 0.40f;

            if (menu.IsEmpty)
            {
                hud.TextCentered(cx, y, 2.5f, "AUCUNE CARTE DANS maps/", new Vector4(1f, 0.6f, 0.4f, 1f));
            }
            else
            {
                for (int i = 0; i < menu.Count; i++)
                {
                    bool active = i == menu.Selected;

                    // Le curseur est un CHEVRON et un fond, pas seulement une
                    // couleur : sur un ecran mal calibre, deux teintes de gris
                    // ne se distinguent pas.
                    if (active)
                        hud.Rect(cx - 210f, y - 6f, 420f, 34f, new Vector4(0.16f, 0.34f, 0.5f, 0.75f));

                    string label = menu.Items[i].ToUpperInvariant();

                    hud.TextCentered(cx, y, 2.6f, active ? "> " + label + " <" : label,
                        active ? new Vector4(1f, 1f, 1f, 1f) : new Vector4(0.6f, 0.65f, 0.72f, 1f));

                    y += 44f;
                }
            }

            hud.TextCentered(cx, h - 64f, 1.8f,
                "FLECHES CHOISIR   ENTREE VALIDER   ECHAP REPRENDRE",
                new Vector4(0.5f, 0.55f, 0.62f, 1f));

            if (map != null)
                hud.TextCentered(cx, h - 36f, 1.8f,
                    $"CARTE : {map.Name.ToUpperInvariant()}   {map.Boxes.Count} BOITES",
                    new Vector4(0.4f, 0.48f, 0.56f, 1f));
        }
    }
}

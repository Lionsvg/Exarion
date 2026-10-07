// ===========================================================================
//  Exarion 1 - Frontiere entre la logique de round et le monde de jeu
//
//  RoundMachine ne connait pas Unity, ne connait pas le reseau, ne connait
//  pas les joueurs. Elle demande et ordonne a travers cette interface.
//
//  Consequence directe : on peut derouler un match entier dans un test
//  automatise, sans lancer le jeu. C'est ce qui manquait a la version C++,
//  ou toute la logique etait soudee a CHL2MPRules.
// ===========================================================================

namespace Exarion.Core
{
    public interface IRoundWorld
    {
        /// <summary>Nombre de joueurs assignes a ce camp, morts ou vivants.</summary>
        int CountPlayers(Team team);

        /// <summary>Nombre de joueurs encore en vie dans ce camp.</summary>
        int CountAlive(Team team);

        /// <summary>Bloque ou libere les deplacements de tout le monde.</summary>
        void FreezeAll(bool frozen);

        /// <summary>
        /// Remet la map et les joueurs en etat de debut de manche.
        /// Ne doit PAS toucher au score de rounds ni au score individuel.
        /// </summary>
        void RestartRound();

        /// <summary>Ferme le menu d'achat de tous les joueurs.</summary>
        void CloseAllBuyMenus();

        /// <summary>Cree l'argent de debut de match pour tout le monde.</summary>
        void ResetAllMoney(int amount);

        /// <summary>Verse un montant a tous les joueurs d'un camp.</summary>
        void AwardMoney(Team team, int amount);

        /// <summary>Message affiche au centre de l'ecran de tous les joueurs.</summary>
        void Announce(string message);

        /// <summary>Notifie un changement d'etat (HUD, sons, effets).</summary>
        void OnStateChanged(RoundState previous, RoundState current);

        /// <summary>Notifie la fin du match.</summary>
        void OnMatchOver(Team winner);
    }
}

// ===========================================================================
//  Exarion 1 - Types du systeme de round
//
//  Portage du systeme valide sur Source SDK 2013. Volontairement sans aucune
//  dependance a UnityEngine ni a FishNet : la logique doit rester testable
//  hors du moteur.
// ===========================================================================

namespace Exarion.Core
{
    /// <summary>Deroulement d'une manche.</summary>
    public enum RoundState
    {
        /// <summary>Pas assez de joueurs, on attend.</summary>
        Warmup = 0,
        /// <summary>Phase d'achat : joueurs figes sur place.</summary>
        Freeze,
        /// <summary>Round en cours.</summary>
        Live,
        /// <summary>Round fini, on affiche le resultat.</summary>
        Ended,
        /// <summary>Un camp a atteint la limite de victoires.</summary>
        MatchOver,
    }

    /// <summary>Pourquoi le round s'est termine.</summary>
    public enum RoundEndReason
    {
        None = 0,
        /// <summary>Un camp entier est mort.</summary>
        Elimination,
        /// <summary>Temps ecoule : la defense conserve le site.</summary>
        Timeout,
        /// <summary>La bombe a explose (a venir).</summary>
        BombExploded,
        /// <summary>La bombe a ete desamorcee (a venir).</summary>
        BombDefused,
    }

    /// <summary>
    /// Camps. Neutre vis-a-vis du theme : "attaquants" posent la bombe,
    /// "defenseurs" la protegent, comme T et CT dans CS.
    /// </summary>
    public enum Team
    {
        None = 0,
        Defenders,
        Attackers,
    }

    /// <summary>
    /// Tous les reglages du round et de l'economie, en un seul endroit.
    /// Valeurs par defaut reprises telles quelles du prototype Source, qui
    /// les tenait de CS. Elles restent a equilibrer au playtest.
    /// </summary>
    [System.Serializable]
    public sealed class RoundConfig
    {
        // --- Rythme du round ---
        public float FreezeTime = 15f;    // phase d'achat
        public float RoundTime = 105f;    // 1 min 45
        public float EndTime = 5f;        // affichage du resultat
        public int WinLimit = 7;          // 0 = illimite
        public int MinPlayers = 2;        // pour sortir du warmup
        public bool RequireBothTeams = true; // false pour tester seul

        // --- Economie ---
        public int MoneyStart = 800;
        public int MoneyMax = 16000;
        public int MoneyKill = 300;
        public int MoneyTeamKill = -300;
        public int MoneyRoundWin = 3250;
        public int MoneyLossBase = 1400;
        public int MoneyLossStep = 500;
        public int MoneyLossMax = 3400;

        /// <summary>
        /// Prime de defaite : croit avec la serie de defaites, plafonnee.
        /// C'est ce qui empeche une equipe menee de s'effondrer faute d'argent.
        /// </summary>
        /// <param name="consecutiveLosses">1 pour la premiere defaite.</param>
        public int GetLossBonus(int consecutiveLosses)
        {
            if (consecutiveLosses < 1)
                consecutiveLosses = 1;

            int bonus = MoneyLossBase + (consecutiveLosses - 1) * MoneyLossStep;
            return bonus > MoneyLossMax ? MoneyLossMax : bonus;
        }
    }
}

// ===========================================================================
//  Monde de jeu factice, pilote a la main par les tests.
//  Permet de derouler un match entier sans lancer Unity.
// ===========================================================================

using System.Collections.Generic;
using Exarion.Core;

namespace Exarion.Tests
{
    public sealed class FakeRoundWorld : IRoundWorld
    {
        public int DefendersTotal;
        public int AttackersTotal;
        public int DefendersAlive;
        public int AttackersAlive;

        public bool Frozen;
        public int RestartCount;
        public int CloseBuyMenuCount;

        public readonly Dictionary<Team, int> Money = new Dictionary<Team, int>
        {
            { Team.Defenders, 0 },
            { Team.Attackers, 0 },
        };

        public readonly List<string> Announcements = new List<string>();
        public readonly List<(RoundState from, RoundState to)> Transitions = new List<(RoundState, RoundState)>();
        public Team MatchWinner = Team.None;
        public bool MatchOverFired;

        /// <summary>Raccourci : peuple les deux camps, tous vivants.</summary>
        public void Populate(int defenders, int attackers)
        {
            DefendersTotal = DefendersAlive = defenders;
            AttackersTotal = AttackersAlive = attackers;
        }

        public int CountPlayers(Team team)
            => team == Team.Defenders ? DefendersTotal : team == Team.Attackers ? AttackersTotal : 0;

        public int CountAlive(Team team)
            => team == Team.Defenders ? DefendersAlive : team == Team.Attackers ? AttackersAlive : 0;

        public void FreezeAll(bool frozen) => Frozen = frozen;

        public void RestartRound()
        {
            RestartCount++;
            // Un vrai restart fait respawn tout le monde.
            DefendersAlive = DefendersTotal;
            AttackersAlive = AttackersTotal;
        }

        public void CloseAllBuyMenus() => CloseBuyMenuCount++;

        public void ResetAllMoney(int amount)
        {
            Money[Team.Defenders] = amount;
            Money[Team.Attackers] = amount;
        }

        public void AwardMoney(Team team, int amount)
        {
            if (Money.ContainsKey(team))
                Money[team] += amount;
        }

        public void Announce(string message) => Announcements.Add(message);

        public void OnStateChanged(RoundState previous, RoundState current)
            => Transitions.Add((previous, current));

        public void OnMatchOver(Team winner)
        {
            MatchWinner = winner;
            MatchOverFired = true;
        }
    }
}

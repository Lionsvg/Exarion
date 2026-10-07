// ===========================================================================
//  Tests de la machine a etats du round.
//
//  Ils rejouent exactement ce qui avait ete mesure sur le prototype Source
//  (transitions a 4 s / 8 s / 3 s), plus les cas qu'on n'avait JAMAIS pu
//  tester la-bas : elimination, fin de match, prime de defaite cumulative.
// ===========================================================================

using NUnit.Framework;
using Exarion.Core;

namespace Exarion.Tests
{
    public class RoundMachineTests
    {
        private RoundConfig _cfg;
        private FakeRoundWorld _world;
        private RoundMachine _m;

        [SetUp]
        public void SetUp()
        {
            // Durees courtes : memes valeurs que le test manuel sur Source.
            _cfg = new RoundConfig
            {
                FreezeTime = 4f,
                RoundTime = 8f,
                EndTime = 3f,
                WinLimit = 3,
                MinPlayers = 2,
                RequireBothTeams = true,
            };

            _world = new FakeRoundWorld();
            _m = new RoundMachine(_cfg, _world);
        }

        /// <summary>Avance le temps par petits pas, comme un vrai tick serveur.</summary>
        private float Advance(float from, float seconds, float step = 0.05f)
        {
            float t = from;
            float target = from + seconds;
            while (t < target)
            {
                t += step;
                _m.Tick(t);
            }
            return t;
        }

        // -------------------------------------------------------------------

        [Test]
        public void Warmup_Attend_Tant_Quil_Manque_Des_Joueurs()
        {
            _world.Populate(1, 0);          // 1 seul joueur, MinPlayers = 2

            Advance(0f, 30f);

            Assert.AreEqual(RoundState.Warmup, _m.State);
            Assert.AreEqual(0, _m.RoundNumber);
        }

        [Test]
        public void Warmup_Demarre_Le_Match_Et_Distribue_Largent_De_Depart()
        {
            _world.Populate(2, 2);

            _m.Tick(1f);

            Assert.AreEqual(RoundState.Freeze, _m.State);
            Assert.AreEqual(1, _m.RoundNumber);
            Assert.AreEqual(_cfg.MoneyStart, _world.Money[Team.Defenders]);
            Assert.AreEqual(_cfg.MoneyStart, _world.Money[Team.Attackers]);
            Assert.IsTrue(_world.Frozen, "les joueurs doivent etre figes en phase d'achat");
        }

        [Test]
        public void Freeze_Passe_En_Live_Apres_La_Duree_Exacte()
        {
            _world.Populate(2, 2);
            _m.Tick(0f);
            Assert.AreEqual(RoundState.Freeze, _m.State);

            float t = Advance(0f, _cfg.FreezeTime - 0.2f);
            Assert.AreEqual(RoundState.Freeze, _m.State, "ne doit pas partir en avance");

            Advance(t, 0.4f);
            Assert.AreEqual(RoundState.Live, _m.State);
            Assert.IsFalse(_world.Frozen, "les joueurs doivent etre liberes");
            Assert.AreEqual(1, _world.CloseBuyMenuCount, "le menu d'achat doit se fermer au GO");
        }

        [Test]
        public void Live_Temps_Ecoule_La_Defense_Gagne()
        {
            _world.Populate(2, 2);
            _m.Tick(0f);

            float t = Advance(0f, _cfg.FreezeTime + _cfg.RoundTime + 0.2f);

            Assert.AreEqual(RoundState.Ended, _m.State);
            Assert.AreEqual(Team.Defenders, _m.LastWinner);
            Assert.AreEqual(RoundEndReason.Timeout, _m.LastEndReason);
            Assert.AreEqual(1, _m.DefenderWins);
            Assert.AreEqual(0, _m.AttackerWins);
        }

        [Test]
        public void Live_Elimination_Des_Defenseurs_Les_Attaquants_Gagnent()
        {
            _world.Populate(2, 2);
            _m.Tick(0f);
            float t = Advance(0f, _cfg.FreezeTime + 0.2f);
            Assert.AreEqual(RoundState.Live, _m.State);

            _world.DefendersAlive = 0;      // les defenseurs tombent
            _m.Tick(t + 0.05f);

            Assert.AreEqual(RoundState.Ended, _m.State);
            Assert.AreEqual(Team.Attackers, _m.LastWinner);
            Assert.AreEqual(RoundEndReason.Elimination, _m.LastEndReason);
            Assert.AreEqual(1, _m.AttackerWins);
        }

        [Test]
        public void Double_KO_Ne_Donne_La_Victoire_A_Personne()
        {
            _world.Populate(2, 2);
            _m.Tick(0f);
            float t = Advance(0f, _cfg.FreezeTime + 0.2f);

            _world.DefendersAlive = 0;
            _world.AttackersAlive = 0;
            _m.Tick(t + 0.05f);

            Assert.AreEqual(RoundState.Ended, _m.State);
            Assert.AreEqual(Team.None, _m.LastWinner);
            Assert.AreEqual(0, _m.DefenderWins);
            Assert.AreEqual(0, _m.AttackerWins);
        }

        [Test]
        public void EndRound_Ne_Compte_Pas_Deux_Fois_Le_Meme_Round()
        {
            _world.Populate(2, 2);
            _m.Tick(0f);
            float t = Advance(0f, _cfg.FreezeTime + 0.2f);

            _m.EndRound(Team.Defenders, RoundEndReason.Elimination, t);
            _m.EndRound(Team.Defenders, RoundEndReason.Elimination, t);   // doit etre ignore
            _m.EndRound(Team.Attackers, RoundEndReason.Timeout, t);       // idem

            Assert.AreEqual(1, _m.DefenderWins);
            Assert.AreEqual(0, _m.AttackerWins);
        }

        [Test]
        public void Le_Score_Survit_Au_Changement_De_Round()
        {
            // C'etait LE piege de la version C++ : RestartGame() remettait les
            // scores d'equipe a zero entre deux manches.
            _world.Populate(2, 2);
            _m.Tick(0f);

            float t = Advance(0f, _cfg.FreezeTime + _cfg.RoundTime + 0.2f);   // round 1, timeout
            Assert.AreEqual(1, _m.DefenderWins);

            t = Advance(t, _cfg.EndTime + 0.2f);                              // round 2 commence
            Assert.AreEqual(RoundState.Freeze, _m.State);
            Assert.AreEqual(2, _m.RoundNumber);
            Assert.AreEqual(1, _m.DefenderWins, "le score ne doit PAS etre remis a zero");
        }

        [Test]
        public void Le_Match_Se_Termine_A_La_Limite_De_Victoires()
        {
            _world.Populate(2, 2);
            _m.Tick(0f);

            float t = 0f;
            // WinLimit = 3, la defense gagne chaque round au temps ecoule.
            for (int i = 0; i < 3; i++)
                t = Advance(t, _cfg.FreezeTime + _cfg.RoundTime + _cfg.EndTime + 0.3f);

            Assert.AreEqual(RoundState.MatchOver, _m.State);
            Assert.AreEqual(3, _m.DefenderWins);
            Assert.IsTrue(_world.MatchOverFired);
            Assert.AreEqual(Team.Defenders, _world.MatchWinner);
        }

        [Test]
        public void La_Prime_De_Defaite_Croit_Avec_La_Serie_Puis_Plafonne()
        {
            Assert.AreEqual(1400, _cfg.GetLossBonus(1));
            Assert.AreEqual(1900, _cfg.GetLossBonus(2));
            Assert.AreEqual(2400, _cfg.GetLossBonus(3));
            Assert.AreEqual(2900, _cfg.GetLossBonus(4));
            Assert.AreEqual(3400, _cfg.GetLossBonus(5));
            Assert.AreEqual(3400, _cfg.GetLossBonus(6), "doit plafonner");
            Assert.AreEqual(3400, _cfg.GetLossBonus(99));
        }

        [Test]
        public void Le_Perdant_Touche_Une_Prime_Croissante_Round_Apres_Round()
        {
            _cfg.WinLimit = 0;              // illimite, pour enchainer
            _world.Populate(2, 2);
            _m.Tick(0f);

            int attackerMoneyStart = _world.Money[Team.Attackers];

            // Round 1 : timeout -> defense gagne, attaque perd (1re defaite).
            float t = Advance(0f, _cfg.FreezeTime + _cfg.RoundTime + 0.2f);
            int afterR1 = _world.Money[Team.Attackers];
            Assert.AreEqual(attackerMoneyStart + 1400, afterR1);

            // Round 2 : meme scenario -> 2e defaite consecutive.
            t = Advance(t, _cfg.EndTime + _cfg.FreezeTime + _cfg.RoundTime + 0.4f);
            int afterR2 = _world.Money[Team.Attackers];
            Assert.AreEqual(afterR1 + 1900, afterR2, "la prime doit augmenter de 500");

            // Le vainqueur, lui, touche la prime fixe a chaque fois.
            Assert.AreEqual(_cfg.MoneyStart + 2 * _cfg.MoneyRoundWin, _world.Money[Team.Defenders]);
        }

        [Test]
        public void Un_Camp_Vide_Ne_Declenche_Pas_Delimination()
        {
            // Cas du test solo : 1 joueur, aucun adversaire. Le round doit
            // aller au bout du temps et non se terminer instantanement.
            _cfg.MinPlayers = 1;
            _world.Populate(0, 1);
            _m.Tick(0f);

            float t = Advance(0f, _cfg.FreezeTime + 0.2f);
            Assert.AreEqual(RoundState.Live, _m.State);

            t = Advance(t, 2f);
            Assert.AreEqual(RoundState.Live, _m.State, "ne doit pas finir juste parce qu'un camp est vide");
        }

        [Test]
        public void Le_Catalogue_Est_Coherent()
        {
            Assert.Greater(BuyCatalog.Items.Count, 0);

            foreach (var item in BuyCatalog.Items)
            {
                Assert.IsNotNull(item.Id);
                Assert.IsNotEmpty(item.Id);
                Assert.IsNotNull(item.Label);
                Assert.Greater(item.Price, 0, $"{item.Id} doit avoir un prix");
            }

            Assert.IsNotNull(BuyCatalog.Find("rifle"));
            Assert.IsNull(BuyCatalog.Find("inexistant"));
            Assert.IsNull(BuyCatalog.ByIndex(-1));
            Assert.IsNull(BuyCatalog.ByIndex(9999));
        }
    }
}

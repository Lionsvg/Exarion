// ===========================================================================
//  Ce qu'un declenchement produit a l'ecran, dans le terminal et a l'oreille
// ===========================================================================

using System;
using System.Numerics;
using Exarion.Core.Entities;
using Exarion.Game.Gameplay;
using Exarion.Game.Sound;

namespace Exarion.Game.World
{
    internal sealed class EventFeed
    {
        private readonly SandboxAudio _audio;
        private readonly SandboxFx _fx;

        public EventFeed(SandboxAudio audio, SandboxFx fx)
        {
            _audio = audio;
            _fx = fx;
        }

        /// <summary>Dernier declenchement, pour l'afficher un instant a l'ecran.</summary>
        public string LastEvent { get; private set; } = "";

        /// <summary>Instant jusqu'auquel il reste affiche.</summary>
        public float LastEventUntil { get; private set; }

        /// <summary>
        /// Un declenchement DOIT se voir et s'entendre, sinon on ne sait pas si
        /// le cablage marche : une ligne dans le terminal, deux secondes et
        /// demie a l'ecran, un son et des etincelles a l'endroit de l'entite.
        /// </summary>
        public void Report(EntityEvent e, float simTime)
        {
            LastEvent = e.ToString();
            LastEventUntil = simTime + 2.5f;

            Console.WriteLine($"  [entite] {LastEvent}");

            _audio.PlayAt(_audio.ImpactClip, e.Source.Position, 0.5f);

            if (_fx.Enabled)
                _fx.Sparks(e.Source.Position, Vector3.UnitY, 10, 4f);
        }
    }
}

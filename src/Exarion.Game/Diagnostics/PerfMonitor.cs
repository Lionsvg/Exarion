// ===========================================================================
//  Profilage du pas : physique, particules, et rendu GPU
//
//  Mesurer les images par seconde ne dit RIEN du cout de la physique : a
//  3 000 img/s le rendu noie tout le reste. On chronometre donc le pas
//  lui-meme, et on le compare a son budget - 15,6 ms a 64 pas/s. C'est le
//  seul chiffre qui dise si le solveur tient la charge.
// ===========================================================================

using System;
using System.Diagnostics;
using System.Text;
using Exarion.Core.Effects;
using Exarion.Core.Physics;
using Exarion.Engine.Graphics;

namespace Exarion.Game.Diagnostics
{
    internal sealed class PerfMonitor
    {
        private readonly Stopwatch _physicsClock = new();
        private readonly Stopwatch _particlesClock = new();

        private double _physicsMsSum;
        private double _physicsMsMax;
        private int _physicsSamples;
        private double _particlesMsSum;
        private double _particlesMsMax;
        private float _nextReport;

        /// <summary>« perf 1 » : une ligne par seconde.</summary>
        public bool Enabled { get; set; }

        public void StartPhysics() => _physicsClock.Restart();

        /// <summary>Arrete le chronometre et rend la duree du pas physique.</summary>
        public double StopPhysics()
        {
            _physicsClock.Stop();
            return _physicsClock.Elapsed.TotalMilliseconds;
        }

        public void StartParticles() => _particlesClock.Restart();

        /// <summary>
        /// Les particules sont chronometrees A PART : melangees a la physique,
        /// on ne saurait plus a qui attribuer un pic.
        /// </summary>
        public void StopParticles()
        {
            _particlesClock.Stop();

            double ms = _particlesClock.Elapsed.TotalMilliseconds;
            _particlesMsSum += ms;
            _particlesMsMax = Math.Max(_particlesMsMax, ms);
        }

        /// <summary>
        /// Accumule le cout du pas physique et en sort une ligne par seconde.
        ///
        /// On rapporte la MOYENNE et le PIRE : c'est le pire qui fait sauter
        /// un pas, pas la moyenne. Le budget est la duree d'un pas - depasser
        /// veut dire que la simulation prend du retard sur le temps reel.
        /// </summary>
        public void ReportPhysics(double milliseconds, float dt, float simTime, PhysicsWorld physics,
                                  ParticleSystem particles, int particlesDrawn, GpuProfiler? gpu)
        {
            _physicsMsSum += milliseconds;
            _physicsMsMax = Math.Max(_physicsMsMax, milliseconds);
            _physicsSamples++;

            if (!Enabled || simTime < _nextReport || _physicsSamples == 0)
                return;

            int awake = 0;
            var bodies = physics.Bodies;
            for (int i = 0; i < bodies.Count; i++)
                if (!bodies[i].Sleeping && !bodies[i].IsStatic) awake++;

            double average = _physicsMsSum / _physicsSamples;
            double budget = dt * 1000.0;

            Console.WriteLine(
                $"[perf] physique {average:0.000} ms/pas (pire {_physicsMsMax:0.000})  " +
                $"budget {budget:0.0} ms -> {average / budget * 100.0:0.0} %  " +
                $"caisses {bodies.Count} dont {awake} actives  contacts {physics.ContactCount}  " +
                $"passes {physics.VelocityIterations}v/{physics.SolverIterations}p");

            Console.WriteLine(
                $"[perf] particules {_particlesMsSum / _physicsSamples:0.000} ms/pas " +
                $"(pire {_particlesMsMax:0.000})  " +
                $"{particles.Count} vivantes, {particlesDrawn} dessinees, " +
                $"{particles.Dropped} refusees");

            ReportGpu(gpu);

            _physicsMsSum = 0;
            _physicsMsMax = 0;
            _particlesMsSum = 0;
            _particlesMsMax = 0;
            _physicsSamples = 0;
            _nextReport = simTime + 1f;
        }

        /// <summary>
        /// Sort une ligne par passe de rendu, en millisecondes GPU.
        ///
        /// ⚠️ NE JAMAIS juger le rendu aux images par seconde. A 3 000 img/s,
        /// une passe a 0,3 ms se perd dans le bruit, et les img/s ne sont
        /// comparables que dos a dos dans une meme session - piege deja paye
        /// plusieurs fois sur ce projet.
        /// </summary>
        private static void ReportGpu(GpuProfiler? gpu)
        {
            if (gpu is null || !gpu.Enabled)
                return;

            gpu.Flush();

            var line = new StringBuilder("[gpu] ");

            for (int z = 0; z < GpuProfiler.Zones; z++)
            {
                var zone = (GpuZone)z;
                double average = gpu.Average(zone);

                // Une passe desactivee n'a rien mesure : l'afficher a 0,000
                // noierait les chiffres qui comptent.
                if (average <= 0.0005)
                    continue;

                line.Append($"{GpuProfiler.NameOf(zone)} {average:0.000}  ");
            }

            // Le nombre de releves est AFFICHE a dessein : une moyenne calculee
            // sur trois mesures n'a pas la meme valeur qu'une moyenne sur mille,
            // et c'est ce chiffre qui dit s'il faut allonger l'anneau.
            line.Append($"|  total {gpu.AverageTotal:0.000} ms  " +
                        $"({gpu.SampleCount(GpuZone.Geometry)} releves)");

            Console.WriteLine(line.ToString());
        }
    }
}

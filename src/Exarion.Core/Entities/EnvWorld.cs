// ===========================================================================
//  Exarion - Entites d'AMBIANCE : une lumiere qui s'allume, un son qui joue
//
//  Le README le disait en une ligne : « rien encore qui allume une lumiere,
//  joue un son ». Le cablage sorties -> entrees savait ouvrir une porte et
//  compter ; il ne savait rien faire de VISIBLE ou d'AUDIBLE d'autre.
//
//    env_light  name=lampe color=1,0.85,0.6 intensity=120 range=16 start_off=0
//               entrees : Allumer, Eteindre, Basculer
//
//    env_sound  name=alarme hz=440 volume=0.5 start_on=0
//               entrees : Jouer, Arreter, Basculer
//
//  ⚠️ ICI, SEULEMENT L'ETAT. Allumee ou non, joue ou non : ca se teste en
//  ligne de commande. Le RENDU d'une lumiere et la VOIX d'un son vivent dans
//  le jeu, qui lit cet etat - exactement comme une porte, dont Core calcule la
//  position et dont le jeu dessine la boite.
//
//  ⚠️ Version : tout changement d'etat l'incremente. Le jeu ne reconstruit
//  l'eclairage (ou ne relance un son) que quand elle bouge - pas a chaque
//  image, et surtout pas « jamais » : une lumiere allumee qui ne s'affiche
//  qu'au prochain chargement de carte est un interrupteur casse.
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Exarion.Core.Assets;

namespace Exarion.Core.Entities
{
    /// <summary>Une lumiere ponctuelle commutable.</summary>
    public sealed class EnvLight : IEntityInput
    {
        public const string InputOn = "Allumer";
        public const string InputOff = "Eteindre";
        public const string InputToggle = "Basculer";

        public const float DefaultIntensity = 120f;
        public const float DefaultRange = 16f;
        public static readonly Vector3 DefaultColor = new(1f, 0.85f, 0.6f);

        private readonly EnvWorld _world;
        private bool _on;

        internal EnvLight(EnvWorld world, MapEntity entity)
        {
            _world = world;
            Entity = entity;
            Name = entity.Get("name");
            Color = EnvWorld.ParseColor(entity.Get("color"), DefaultColor);
            Intensity = MathF.Max(0f, entity.GetFloat("intensity", DefaultIntensity));
            Range = MathF.Max(0.1f, entity.GetFloat("range", DefaultRange));
            _on = entity.GetInt("start_off", 0) == 0;
        }

        public MapEntity Entity { get; }
        public string Name { get; }
        public Vector3 Position => Entity.Position;
        public Vector3 Color { get; }
        public float Intensity { get; }
        public float Range { get; }

        public bool On => _on;

        public bool Receive(string input)
        {
            if (EnvWorld.Is(input, InputOn)) { Set(true); return true; }
            if (EnvWorld.Is(input, InputOff)) { Set(false); return true; }
            if (EnvWorld.Is(input, InputToggle)) { Set(!_on); return true; }
            return false;
        }

        private void Set(bool on)
        {
            // Une entree qui ne change rien ne fait pas bouger la version : le
            // jeu n'a rien a reconstruire. Un interrupteur « Allumer » sur une
            // lampe deja allumee, recu a chaque passage dans un couloir, ne doit
            // pas reconstruire l'eclairage 64 fois par seconde.
            if (_on == on)
                return;

            _on = on;
            _world.Touch();
        }
    }

    /// <summary>Un son en boucle, place dans le monde : une sirene, un moteur, un bourdonnement.</summary>
    public sealed class EnvSound : IEntityInput
    {
        public const string InputPlay = "Jouer";
        public const string InputStop = "Arreter";
        public const string InputToggle = "Basculer";

        public const float DefaultHz = 220f;
        public const float DefaultVolume = 0.5f;

        private readonly EnvWorld _world;
        private bool _playing;

        internal EnvSound(EnvWorld world, MapEntity entity)
        {
            _world = world;
            Entity = entity;
            Name = entity.Get("name");

            // Bornes de l'oreille, pas du code : sous 20 Hz on n'entend rien,
            // au-dessus de 12 kHz un son synthetise siffle et rien d'autre.
            Hz = Math.Clamp(entity.GetFloat("hz", DefaultHz), 20f, 12000f);
            Volume = Math.Clamp(entity.GetFloat("volume", DefaultVolume), 0f, 1f);
            _playing = entity.GetInt("start_on", 0) != 0;
        }

        public MapEntity Entity { get; }
        public string Name { get; }
        public Vector3 Position => Entity.Position;
        public float Hz { get; }
        public float Volume { get; }

        public bool Playing => _playing;

        public bool Receive(string input)
        {
            if (EnvWorld.Is(input, InputPlay)) { Set(true); return true; }
            if (EnvWorld.Is(input, InputStop)) { Set(false); return true; }
            if (EnvWorld.Is(input, InputToggle)) { Set(!_playing); return true; }
            return false;
        }

        private void Set(bool playing)
        {
            if (_playing == playing)
                return;

            _playing = playing;
            _world.Touch();
        }
    }

    public sealed class EnvWorld
    {
        private readonly List<EnvLight> _lights = new();
        private readonly List<EnvSound> _sounds = new();

        public IReadOnlyList<EnvLight> Lights => _lights;
        public IReadOnlyList<EnvSound> Sounds => _sounds;

        /// <summary>
        /// Change a chaque allumage, extinction, lecture, arret - et a chaque
        /// chargement. Le jeu compare avec la derniere qu'il a appliquee.
        /// </summary>
        public int Version { get; private set; }

        internal void Touch() => Version++;

        internal static bool Is(string input, string name)
            => string.Equals(input, name, StringComparison.OrdinalIgnoreCase);

        public void Load(MapData map)
        {
            ArgumentNullException.ThrowIfNull(map);

            _lights.Clear();
            _sounds.Clear();

            foreach (MapEntity entity in map.Entities)
            {
                if (string.Equals(entity.Type, EntityTypes.EnvLight, StringComparison.Ordinal))
                    _lights.Add(new EnvLight(this, entity));
                else if (string.Equals(entity.Type, EntityTypes.EnvSound, StringComparison.Ordinal))
                    _sounds.Add(new EnvSound(this, entity));
            }

            Touch();
        }

        /// <summary>A appeler APRES EntityWorld.Load, qui efface les cibles inscrites.</summary>
        public void RegisterInto(EntityWorld world)
        {
            ArgumentNullException.ThrowIfNull(world);

            foreach (EnvLight light in _lights)
                world.Register(light);

            foreach (EnvSound sound in _sounds)
                world.Register(sound);
        }

        /// <summary>
        /// « r,g,b » en couleur LINEAIRE, comme les lumieres de la carte.
        ///
        /// ⚠️ Des VIRGULES, pas des espaces : le format de carte decoupe une
        /// ligne sur les espaces, une valeur n'en contient donc jamais. Une
        /// couleur illisible retombe sur le defaut plutot que sur du noir : une
        /// lampe noire est une lampe qu'on croit cassee.
        /// </summary>
        public static Vector3 ParseColor(string text, Vector3 fallback)
        {
            if (string.IsNullOrWhiteSpace(text))
                return fallback;

            string[] parts = text.Split(',');

            if (parts.Length != 3)
                return fallback;

            var c = new float[3];

            for (int i = 0; i < 3; i++)
            {
                if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out c[i])
                    || !float.IsFinite(c[i]) || c[i] < 0f)
                    return fallback;
            }

            return new Vector3(c[0], c[1], c[2]);
        }
    }
}

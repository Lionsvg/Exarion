// ===========================================================================
//  Les reglages de rendu que la console pilote
//
//  Recopies dans le pipeline UNE fois par image, plutot que depuis chaque
//  gestionnaire de ConVar : le pipeline n'a alors qu'une source de verite, et
//  une ConVar ajoutee plus tard ne peut pas oublier de le prevenir.
// ===========================================================================

namespace Exarion.Game.Rendering
{
    internal sealed class RenderSettings
    {
        /// <summary>« bloom » : halo autour des sources vives.</summary>
        public bool Bloom { get; set; } = true;

        /// <summary>« ssao » : occlusion ambiante.</summary>
        public bool Ssao { get; set; } = true;

        /// <summary>« ssao_debug » : 0 rien, 1 normales, 2 profondeur, 3 occlusion.</summary>
        public int SsaoDebug { get; set; }

        /// <summary>« ssao_debug_range » : distance affichee en blanc dans la vue de profondeur.</summary>
        public float SsaoDebugRange { get; set; } = 30f;

        /// <summary>« wireframe », touche K : vue des collisions en fil de fer.</summary>
        public bool Wireframe { get; set; }

        /// <summary>« showstats » : compteurs a l'ecran.</summary>
        public bool ShowStats { get; set; } = true;
    }
}

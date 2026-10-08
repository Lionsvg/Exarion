// ===========================================================================
//  Eclairage du bac a sable
//
//  Une directionnelle froide fait office de lumiere generale, et des
//  ponctuelles chaudes marquent les zones. Le contraste chaud/froid est ce
//  qui rend un greybox lisible : sans lui, tout se confond.
// ===========================================================================

using System.Numerics;
using Exarion.Core.Assets;
using Exarion.Core.Console;
using Exarion.Core.Entities;
using Exarion.Engine.Graphics;

namespace Exarion.Game.Rendering
{
    internal sealed class SceneLighting
    {
        /// <summary>Direction du soleil. Partagee par l'eclairage et la carte d'ombre.</summary>
        public Vector3 SunDirection { get; } = Vector3.Normalize(new Vector3(-0.35f, -0.85f, -0.4f));

        /// <summary>« light_enabled ».</summary>
        public bool Enabled = true;

        /// <summary>« light_sun ».</summary>
        public float SunIntensity = 1.1f;

        /// <summary>
        /// « light_lamps ». 140, pas 14 : l'attenuation en 1/d^2 est brutale. A
        /// 10 m d'une lampe, il ne reste qu'un centieme de son intensite.
        /// Verifie par capture d'ecran - a 14 la salle etait quasiment noire.
        /// </summary>
        public float LampIntensity = 140f;

        /// <summary>L'eclairage est a refaire a la prochaine image.</summary>
        public bool Dirty = true;

        // Intensites que la CARTE declare. Servent de reference aux reglages
        // globaux light_sun / light_lamps - voir Rebuild.
        private float _mapSunReference;
        private float _mapLampReference;

        /// <summary>
        /// Reconstruit l'eclairage A PARTIR DE LA CARTE.
        ///
        /// ---------------------------------------------------------------
        /// COMMENT LA CARTE ET LES DEUX REGLAGES GLOBAUX COHABITENT
        ///
        /// La carte porte des intensites ABSOLUES - c'est ce qu'attend un
        /// format reutilisable. Mais `light_sun` et `light_lamps` doivent
        /// continuer de regler l'eclairage a chaud, sinon on perd le seul
        /// moyen de trouver ces valeurs (elles se jugent a l'oeil, pas au
        /// calcul).
        ///
        /// Les deux se concilient par un RAPPORT : au chargement, les deux
        /// ConVar prennent la valeur que la carte declare, et l'eclairage
        /// est ensuite mis a l'echelle de leur ecart a cette reference. Au
        /// chargement le facteur vaut donc exactement 1 - la carte s'affiche
        /// telle qu'elle a ete ecrite - et tourner le bouton fait varier
        /// toutes les lampes ensemble, en gardant leurs rapports.
        ///
        /// C'est aussi ce qui garde la console HONNETE : `light_lamps`
        /// affiche ce que la carte chargee utilise vraiment. Le piege des
        /// ConVar qui affichent autre chose que la realite a deja coute une
        /// session sur la SSAO, puis une autre sur snd_volume.
        /// ---------------------------------------------------------------
        /// </summary>
        public void Rebuild(SceneRenderer? scene, MapData? map, EnvWorld? env = null)
        {
            // Retenue meme si rien n'est dessine : sinon un eclairage coupe
            // (light_enabled 0) se reconstruirait a chaque image.
            EnvVersion = env?.Version ?? 0;

            if (scene == null)
                return;

            scene.ClearLights();

            if (!Enabled || map == null)
                return;

            float sunScale = _mapSunReference > 0f ? SunIntensity / _mapSunReference : 1f;
            float lampScale = _mapLampReference > 0f ? LampIntensity / _mapLampReference : 1f;

            // ⚠️ LA TRADUCTION A DEMENAGE DANS LE MOTEUR (Graphics/MapLighting).
            // Le jeu d'exemple recopiait ce switch a l'identique, ce qui est la
            // definition d'un couplage : un troisieme jeu l'aurait recopie aussi.
            // Ne reste ici que ce qui est propre au bac a sable, ses deux
            // facteurs de reglage en direct.
            scene.LoadLights(map, sunScale, lampScale);

            // Les lampes commutables APRES celles de la carte : au-dela de
            // MaxLights, ce sont elles qui sautent, pas l'eclairage que la carte
            // declare - et le soleil, premier directionnel, garde son ombre.
            // Meme facteur light_lamps que les lampes de la carte.
            if (env is null)
                return;

            foreach (EnvLight light in env.Lights)
                if (light.On)
                    scene.AddLight(Light.Point(light.Position, light.Color, light.Intensity * lampScale, light.Range));
        }

        /// <summary>Version de l'ambiance deja appliquee. Voir EnvWorld.Version.</summary>
        public int EnvVersion { get; private set; } = -1;

        /// <summary>
        /// Prend la carte comme nouvelle reference d'eclairage et aligne les
        /// deux ConVar dessus. Ecrire la ConVar declenche son handler, qui
        /// reaffecte la meme valeur et marque l'eclairage a refaire : c'est
        /// voulu, et c'est ce qui garantit qu'aucun chemin ne reste sur une
        /// ancienne valeur.
        /// </summary>
        public void AdoptMap(MapData map, ConsoleSystem console)
        {
            _mapSunReference = 0f;
            _mapLampReference = 0f;

            foreach (MapLight light in map.Lights)
            {
                if (_mapSunReference <= 0f && light.Kind == MapLightKind.Directional)
                    _mapSunReference = light.Intensity;

                if (_mapLampReference <= 0f && light.Kind == MapLightKind.Point)
                    _mapLampReference = light.Intensity;
            }

            if (_mapSunReference > 0f)
                AdoptUnlessTuned(console, "light_sun", _mapSunReference, ref SunIntensity);

            if (_mapLampReference > 0f)
                AdoptUnlessTuned(console, "light_lamps", _mapLampReference, ref LampIntensity);

            Dirty = true;
        }

        /// <summary>
        /// Aligne un reglage global sur ce que la carte declare - SAUF s'il a
        /// deja ete regle a la main.
        ///
        /// Un reglage d'eclairage se trouve a l'oeil, en tatonnant, et se
        /// range dans exarion.cfg pour ne pas etre refait. Le charger puis
        /// l'ecraser au premier chargement de carte reviendrait a jeter
        /// exactement ce que 2.4 servait a garder. Une valeur laissee au
        /// defaut, en revanche, n'exprime aucune preference : la carte la
        /// prend.
        /// </summary>
        private static void AdoptUnlessTuned(ConsoleSystem console, string name, float declared, ref float field)
        {
            ConVar? variable = console.Find(name);

            // Console pas encore construite : on prend ce que dit la carte.
            if (variable is null)
            {
                field = declared;
                return;
            }

            if (variable.Value != variable.Default)
            {
                // Reglage explicite : il gagne. L'eclairage sera mis a
                // l'echelle du rapport a la reference de la carte.
                field = variable.Value;
                return;
            }

            // La ConVar est bornee : on relit sa valeur apres ecriture, sinon
            // une carte declarant une intensite hors bornes serait affichee
            // autrement qu'elle n'est appliquee.
            variable.Value = declared;
            field = variable.Value;
        }
    }
}

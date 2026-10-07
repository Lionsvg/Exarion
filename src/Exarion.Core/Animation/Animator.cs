// ===========================================================================
//  Exarion - Lecteur d'animation a deux couches
//
//  Melanger deux poses est trivial (voir PoseBlend). Le vrai probleme du
//  melange de locomotion n'est pas geometrique, il est TEMPOREL.
//
//  ---------------------------------------------------------------------
//  LE PIEGE : DEUX CYCLES N'ONT PAS LA MEME DUREE.
//
//  Sur le modele du projet : "Walking" dure 1,033 s, "Running" 0,633 s.
//  Echantillonner les deux au meme temps ABSOLU les desynchronise aussitot -
//  a t = 0,633 s la course a fini son cycle et repose le pied, la marche
//  n'en est qu'a 61 % et a la jambe en l'air. Le melange des deux donne un
//  personnage qui fait le grand ecart, et le defaut EMPIRE avec le temps :
//  au bout de dix secondes les deux cycles n'ont plus aucun rapport.
//
//  La solution est de raisonner en PHASE et non en secondes. Une phase de
//  0,25 veut dire "au quart du pas", dans les deux cycles. Les deux pieds
//  gauches touchent le sol au meme instant, quel que soit le melange.
//
//  Et la phase avance a une cadence elle-meme MELANGEE : a mi-chemin entre
//  marche et course, le pas dure la moyenne des deux. Sans ca, la frequence
//  du pas sauterait brutalement au moment de la bascule.
//  ---------------------------------------------------------------------
//
//  Ce qui reste a la main de l'operateur : DecalageB. Rien ne garantit que
//  deux cycles commencent sur le meme pied - c'est une propriete du contenu,
//  pas du moteur. Si la marche demarre pied gauche et la course pied droit,
//  il faut un demi-cycle de decalage, et seul un oeil humain peut le dire.
// ===========================================================================

using System;
using Exarion.Core.Assets;

namespace Exarion.Core.Animation
{
    public sealed class Animator
    {
        private readonly GltfModel _model;

        private readonly NodePose[] _rest;
        private readonly NodePose[] _sampleA;
        private readonly NodePose[] _sampleB;
        private readonly NodePose[] _blended;
        private readonly NodePose[] _snapshot;
        private readonly NodePose[] _output;

        private float _fadeRemaining;
        private float _fadeDuration;

        public Animator(GltfModel model)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));

            _rest = model.CreateRestPose();
            _sampleA = model.CreateRestPose();
            _sampleB = model.CreateRestPose();
            _blended = model.CreateRestPose();
            _snapshot = model.CreateRestPose();
            _output = model.CreateRestPose();
        }

        /// <summary>Cycle joue quand <see cref="Weight"/> vaut 0. Souvent la marche.</summary>
        public GltfAnimation? ClipA { get; private set; }

        /// <summary>Cycle joue quand <see cref="Weight"/> vaut 1. Souvent la course.</summary>
        public GltfAnimation? ClipB { get; private set; }

        /// <summary>
        /// Position dans le cycle, dans [0, 1[. Partagee par les deux cycles.
        ///
        /// Modifiable : imposer la phase permet de comparer deux melanges au
        /// MEME instant du pas. Sans ca, les cadences different et deux
        /// captures ne montrent pas le meme moment de la foulee.
        /// </summary>
        public float Phase
        {
            get => _phase;
            set => _phase = value - MathF.Floor(value);
        }
        private float _phase;

        /// <summary>Dosage entre les deux cycles. Borne a [0, 1].</summary>
        public float Weight
        {
            get => _weight;
            set => _weight = value < 0f ? 0f : value > 1f ? 1f : value;
        }
        private float _weight;

        /// <summary>
        /// Decalage de phase applique au second cycle, en fractions de cycle.
        /// Sert quand les deux animations ne demarrent pas sur le meme pied :
        /// 0,5 inverse la jambe d'attaque. A regler a l'oeil, pas au calcul.
        /// </summary>
        public float OffsetB { get; set; }

        /// <summary>Vrai tant qu'un fondu enchaine est en cours.</summary>
        public bool Fading => _fadeRemaining > 0f;

        /// <summary>Pose resultante. Le tableau est reutilise a chaque image.</summary>
        public NodePose[] Pose => _output;

        /// <summary>
        /// Choisit les deux cycles. La PHASE EST CONSERVEE : changer de
        /// couple en pleine foulee ne doit pas reposer le pied au depart.
        /// </summary>
        public void SetClips(GltfAnimation? a, GltfAnimation? b)
        {
            ClipA = a;
            ClipB = b;
        }

        /// <summary>
        /// Bascule vers un autre couple de cycles en fondu.
        ///
        /// ⚠️ Le fondu part d'une POSE FIGEE, pas de l'ancienne animation qui
        /// continuerait a jouer. C'est un choix : garder deux couches vivantes
        /// doublerait l'etat pour un gain invisible sur des transitions de
        /// 0,2 s. La limite est reelle mais assumee - au-dela de 0,5 s, on
        /// verrait le personnage se figer avant de repartir.
        ///
        /// Pour marche vers course, ce n'est PAS ce qu'il faut : il n'y a rien
        /// a enchainer, il suffit de faire varier <see cref="Weight"/>.
        /// </summary>
        public void CrossFadeTo(GltfAnimation? a, GltfAnimation? b, float duration)
        {
            if (duration <= 0f)
            {
                SetClips(a, b);
                _fadeRemaining = 0f;
                return;
            }

            PoseBlend.Copy(_output, _snapshot);

            SetClips(a, b);

            _fadeDuration = duration;
            _fadeRemaining = duration;
        }

        /// <summary>
        /// Avance le temps et recalcule la pose.
        ///
        /// Appele au RENDU, pas a la simulation : c'est de l'affichage pur,
        /// et le figer a 64 pas par seconde ferait saccader l'animation des
        /// que le rendu va plus vite.
        /// </summary>
        public void Advance(float dt)
        {
            float durationA = ClipA?.Duration ?? 0f;
            float durationB = ClipB?.Duration ?? 0f;

            // Cadence MELANGEE. Si un seul cycle est fourni, c'est lui qui
            // impose le rythme - sinon un cycle absent (duree 0) tirerait la
            // duree moyenne vers zero et le personnage s'emballerait.
            float cycle;

            if (durationA > 0f && durationB > 0f)
                cycle = durationA + (durationB - durationA) * Weight;
            else
                cycle = durationA > 0f ? durationA : durationB;

            // Le repli dans [0, 1[ est fait par le setter de Phase. Un modulo
            // suffirait pour un dt positif, mais le rembobinage (dt negatif)
            // sert au debogage.
            if (cycle > 1e-6f)
                Phase = _phase + dt / cycle;

            Sample(ClipA, Phase, _sampleA);
            Sample(ClipB, Phase + OffsetB, _sampleB);

            PoseBlend.Mix(_sampleA, _sampleB, Weight, _blended);

            if (_fadeRemaining > 0f)
            {
                _fadeRemaining -= dt;

                if (_fadeRemaining <= 0f)
                {
                    _fadeRemaining = 0f;
                    PoseBlend.Copy(_blended, _output);
                }
                else
                {
                    // 0 au debut du fondu, 1 a la fin.
                    float t = 1f - _fadeRemaining / _fadeDuration;
                    PoseBlend.Mix(_snapshot, _blended, t, _output);
                }
            }
            else
            {
                PoseBlend.Copy(_blended, _output);
            }
        }

        /// <summary>
        /// Echantillonne un cycle a une phase donnee.
        ///
        /// ON REPART TOUJOURS DE LA POSE DE REPOS. Sample n'ecrit que les
        /// noeuds qu'un canal pilote : sans remise a zero, un noeud anime par
        /// le premier cycle et absent du second garderait la valeur du
        /// premier. Le defaut ne se verrait qu'avec deux animations aux
        /// squelettes differents - c'est-a-dire le jour ou on ajoute du
        /// contenu, longtemps apres avoir oublie ce fichier.
        /// </summary>
        private void Sample(GltfAnimation? clip, float phase, NodePose[] destination)
        {
            PoseBlend.Copy(_rest, destination);

            if (clip is null || clip.Duration <= 0f)
                return;

            phase -= MathF.Floor(phase);

            clip.Sample(phase * clip.Duration, destination);
        }

        /// <summary>Matrices d'os pretes pour le nuanceur, pour la peau donnee.</summary>
        public System.Numerics.Matrix4x4[] SkinMatrices(int skin = 0)
            => _model.ComputeSkinMatrices(skin, _model.ComputeWorldTransforms(_output));
    }
}

// ===========================================================================
//  Tests du melange de poses (2.2).
//
//  Deux choses se testent ici, et elles n'ont rien a voir :
//
//  - PoseBlend, de la geometrie pure. Le piege y est le SLERP : melanger
//    deux quaternions en ligne droite donne un resultat qui a l'air juste
//    aux extremes et faux au milieu du trajet.
//
//  - Animator, du TEMPS. Le piege y est la synchronisation de deux cycles
//    de durees differentes - sur le modele du projet, "Walking" dure 1,033 s
//    et "Running" 0,633 s. Ce defaut-la ne se voit pas sur une image fixe :
//    il s'installe seconde apres seconde.
// ===========================================================================

using System;
using System.Numerics;
using NUnit.Framework;
using Exarion.Core.Animation;
using Exarion.Core.Assets;

namespace Exarion.Tests
{
    public class AnimationBlendTests
    {
        // ===================================================================
        //  PoseBlend
        // ===================================================================

        [Test]
        public void Aux_Extremes_On_Retrouve_Les_Poses_D_Origine()
        {
            NodePose[] a = Poses(new Vector3(1f, 2f, 3f));
            NodePose[] b = Poses(new Vector3(9f, 8f, 7f));
            var result = new NodePose[1];

            PoseBlend.Mix(a, b, 0f, result);
            ClassicAssert.AreEqual(1f, result[0].Translation.X, 1e-6f);

            PoseBlend.Mix(a, b, 1f, result);
            ClassicAssert.AreEqual(9f, result[0].Translation.X, 1e-6f);
        }

        [Test]
        public void Une_Rotation_Se_Melange_Sur_L_Arc_Et_Pas_En_Ligne_Droite()
        {
            // LE test de PoseBlend.
            //
            // Melanger lineairement puis renormaliser (le "nlerp") donne le
            // MEME resultat que le slerp a mi-chemin : le point milieu est
            // symetrique. Le defaut n'apparait qu'AILLEURS sur le trajet, et
            // d'autant plus que l'angle est grand.
            //
            // A un quart du chemin sur 160 degres :
            //    slerp -> 40,00 deg (le quart de l'angle, par definition)
            //    nlerp -> 34,49 deg (la corde, parcourue a vitesse constante)
            //
            // 5,5 degres sur une epaule, c'est visible. Et surtout, ca veut
            // dire que le membre ACCELERE au milieu du geste.
            NodePose[] a = Poses(Vector3.Zero, Quaternion.Identity);
            NodePose[] b = Poses(Vector3.Zero,
                Quaternion.CreateFromAxisAngle(Vector3.UnitY, 160f * MathF.PI / 180f));

            var result = new NodePose[1];
            PoseBlend.Mix(a, b, 0.25f, result);

            float degrees = Degrees(result[0].Rotation);

            ClassicAssert.AreEqual(40f, degrees, 0.05f, "le slerp doit rendre le quart de l'angle");
            Assert.That(MathF.Abs(degrees - 34.49f), Is.GreaterThan(3f), "ce n'est pas un nlerp");
        }

        [Test]
        public void Melanger_Vers_Une_Des_Sources_Ne_Corrompt_Rien()
        {
            // L'Animator ecrit parfois dans un tableau qu'il vient de lire.
            // Chaque noeud doit donc etre lu puis ecrit avant de passer au
            // suivant - sinon le noeud 1 melangerait contre un noeud 0 deja
            // ecrase.
            NodePose[] a = Poses(new Vector3(0f, 0f, 0f), Quaternion.Identity, 3);
            NodePose[] b = Poses(new Vector3(10f, 0f, 0f), Quaternion.Identity, 3);

            PoseBlend.Mix(a, b, 0.5f, a);

            for (int i = 0; i < 3; i++)
                ClassicAssert.AreEqual(5f, a[i].Translation.X, 1e-6f);
        }

        // ===================================================================
        //  Animator - synchronisation
        // ===================================================================

        [Test]
        public void Deux_Cycles_De_Durees_Differentes_Restent_Synchronises()
        {
            // LE test de ce fichier.
            //
            // Cycle A : 1,00 s, le noeud 1 glisse de 0 a 1.
            // Cycle B : 0,50 s, le meme noeud glisse de 0 a 10.
            //
            // Si les deux sont echantillonnes a la MEME PHASE, la position
            // melangee vaut exactement phase x melange(1, 10). C'est une
            // egalite, pas une approximation : elle tombe des que l'un des
            // deux est echantillonne au temps absolu.
            var animator = new Animator(Skeleton());
            animator.SetClips(Ramp(1, 1.00f, 0f, 1f), Ramp(1, 0.50f, 0f, 10f));

            // Poids volontairement change en cours de route : la cadence du
            // pas varie, mais la relation doit tenir a chaque image.
            float[] weights = { 0f, 0.15f, 0.5f, 0.85f, 1f, 0.3f };

            foreach (float w in weights)
            {
                animator.Weight = w;
                animator.Advance(0.137f);   // pas rond, pour ne rien aligner par hasard

                float amplitude = 1f + (10f - 1f) * w;

                ClassicAssert.AreEqual(animator.Phase * amplitude,
                                       animator.Pose[1].Translation.X, 1e-4f,
                                       $"desynchronise a un melange de {w}");
            }
        }

        [Test]
        public void La_Cadence_Du_Pas_Se_Melange_Aussi()
        {
            // Sans ca, la frequence du pas sauterait d'un coup au moment de
            // la bascule : le personnage passerait de 1 pas/s a 1,6 pas/s
            // sur une image.
            ClassicAssert.AreEqual(0.5f, PhaseApres(0f, 1.00f / 2f), 1e-4f);
            ClassicAssert.AreEqual(0.5f, PhaseApres(1f, 0.50f / 2f), 1e-4f);

            // A mi-melange, le cycle dure la moyenne des deux : 0,75 s.
            ClassicAssert.AreEqual(0.5f, PhaseApres(0.5f, 0.75f / 2f), 1e-4f);
        }

        [Test]
        public void Un_Seul_Cycle_Impose_Sa_Cadence_Sans_S_Emballer()
        {
            // Piege de division : un cycle absent a une duree de 0. Melanger
            // 2 s et 0 s a plein poids donnerait un cycle de duree nulle, donc
            // une phase infinie - le personnage vibrerait.
            var animator = new Animator(Skeleton());
            animator.SetClips(Ramp(1, 2f, 0f, 1f), null);
            animator.Weight = 1f;

            animator.Advance(1f);

            ClassicAssert.AreEqual(0.5f, animator.Phase, 1e-4f);
        }

        [Test]
        public void Le_Decalage_Avance_Le_Second_Cycle()
        {
            // Rien ne garantit que deux cycles demarrent sur le meme pied.
            // C'est une propriete du CONTENU : le moteur ne peut que fournir
            // le reglage.
            var animator = new Animator(Skeleton());
            animator.SetClips(null, Ramp(1, 1f, 0f, 1f));
            animator.Weight = 1f;

            animator.Advance(0.25f);
            ClassicAssert.AreEqual(0.25f, animator.Pose[1].Translation.X, 1e-4f);

            var decale = new Animator(Skeleton());
            decale.SetClips(null, Ramp(1, 1f, 0f, 1f));
            decale.Weight = 1f;
            decale.OffsetB = 0.5f;

            decale.Advance(0.25f);
            ClassicAssert.AreEqual(0.75f, decale.Pose[1].Translation.X, 1e-4f);
        }

        // ===================================================================
        //  Animator - etat
        // ===================================================================

        [Test]
        public void Changer_De_Cycle_Ne_Repose_Pas_Le_Pied_Au_Depart()
        {
            var animator = new Animator(Skeleton());
            animator.SetClips(Ramp(1, 1f, 0f, 1f), null);

            animator.Advance(0.4f);
            float phase = animator.Phase;

            animator.SetClips(Ramp(1, 1f, 0f, 5f), null);
            ClassicAssert.AreEqual(phase, animator.Phase, 1e-6f);
        }

        [Test]
        public void Un_Noeud_Que_Le_Nouveau_Cycle_N_Anime_Pas_Revient_Au_Repos()
        {
            // Sample n'ecrit que les noeuds qu'un canal pilote. Sans remise a
            // la pose de repos avant chaque echantillonnage, un noeud anime
            // par l'ancien cycle et absent du nouveau garderait sa valeur -
            // un bras reste plie pour toujours.
            //
            // Le noeud 2 est au repos en x = 7 (voir Skeleton).
            var animator = new Animator(Skeleton());

            animator.SetClips(Ramp(2, 1f, 3f, 3f), null);
            animator.Advance(0.1f);
            ClassicAssert.AreEqual(3f, animator.Pose[2].Translation.X, 1e-4f);

            animator.SetClips(Ramp(1, 1f, 0f, 1f), null);
            animator.Advance(0.1f);
            ClassicAssert.AreEqual(7f, animator.Pose[2].Translation.X, 1e-4f, "retour au repos");
        }

        [Test]
        public void Le_Melange_Est_Borne()
        {
            var animator = new Animator(Skeleton()) { Weight = 4f };
            ClassicAssert.AreEqual(1f, animator.Weight, 1e-6f);

            animator.Weight = -2f;
            ClassicAssert.AreEqual(0f, animator.Weight, 1e-6f);
        }

        [Test]
        public void Sans_Aucun_Cycle_La_Pose_De_Repos_Est_Conservee()
        {
            var animator = new Animator(Skeleton());

            animator.Advance(1f);

            ClassicAssert.AreEqual(5f, animator.Pose[1].Translation.X, 1e-6f);
            ClassicAssert.AreEqual(7f, animator.Pose[2].Translation.X, 1e-6f);
        }

        [Test]
        public void Un_Fondu_Part_De_La_Pose_Courante_Et_Arrive_A_La_Nouvelle()
        {
            var animator = new Animator(Skeleton());
            animator.SetClips(Ramp(1, 1f, 0f, 0f), null);

            animator.Advance(0.01f);
            ClassicAssert.AreEqual(0f, animator.Pose[1].Translation.X, 1e-4f);

            animator.CrossFadeTo(Ramp(1, 1f, 10f, 10f), null, 1f);
            Assert.That(animator.Fading, Is.True);

            animator.Advance(0.5f);
            ClassicAssert.AreEqual(5f, animator.Pose[1].Translation.X, 1e-4f, "a mi-fondu");

            animator.Advance(0.5f);
            ClassicAssert.AreEqual(10f, animator.Pose[1].Translation.X, 1e-4f);
            Assert.That(animator.Fading, Is.False);
        }

        [Test]
        public void Un_Fondu_De_Duree_Nulle_Bascule_Sans_Attendre()
        {
            var animator = new Animator(Skeleton());
            animator.SetClips(Ramp(1, 1f, 0f, 0f), null);
            animator.Advance(0.01f);

            animator.CrossFadeTo(Ramp(1, 1f, 10f, 10f), null, 0f);
            Assert.That(animator.Fading, Is.False);

            animator.Advance(0.01f);
            ClassicAssert.AreEqual(10f, animator.Pose[1].Translation.X, 1e-4f);
        }

        // ===================================================================
        //  Animator - sur le vrai modele
        // ===================================================================

        [Test]
        public void Sur_Le_Vrai_Modele_Chaque_Os_Reste_Sur_Son_Arc()
        {
            // Les rampes synthetiques prouvent la mecanique du temps. Elles ne
            // prouvent RIEN sur les rotations, qui sont l'essentiel d'un
            // squelette : une rampe n'a pas d'articulation.
            //
            // Ici on melange les deux vrais cycles du projet et on verifie,
            // os par os, que la pose intermediaire est au bon endroit de
            // l'arc : a un quart du melange, un quart de l'angle.
            //
            // ⚠️ LE MELANGE EST A 0,25 ET PAS A 0,5, ET C'EST DELIBERE. A
            // mi-chemin, le point est symetrique : l'interpolation lineaire y
            // donne exactement le meme resultat que le slerp. Un test ecrit a
            // 0,5 passerait donc avec une implementation fausse - verifie, il
            // le faisait. C'est le genre de test qui rassure sans rien
            // garantir.
            string? path = FindModel();

            if (path is null)
                Assert.Ignore("Model3D/ absent : rien a verifier ici.");

            GltfModel model = GltfModel.Load(path!);

            GltfAnimation? walk = model.FindAnimation("Walking");
            GltfAnimation? run = model.FindAnimation("Running");

            Assert.That(walk, Is.Not.Null);
            Assert.That(run, Is.Not.Null);

            NodePose[] atWalk = PoseAt(model, walk!, run!, 0f);
            NodePose[] atRun = PoseAt(model, walk!, run!, 1f);
            NodePose[] quarter = PoseAt(model, walk!, run!, 0.25f);

            int checkedJoints = 0;
            float widest = 0f;

            for (int i = 0; i < atWalk.Length; i++)
            {
                float full = Between(atWalk[i].Rotation, atRun[i].Rotation);

                // En dessous d'un degre, le rapport n'a plus de sens : on
                // comparerait du bruit d'arrondi.
                if (full < 1f)
                    continue;

                if (full > widest)
                    widest = full;

                float part = Between(atWalk[i].Rotation, quarter[i].Rotation);

                ClassicAssert.AreEqual(full * 0.25f, part, 0.05f,
                    $"os {i} ({model.Nodes[i].Name}) hors de l'arc, ecart total {full:0.0} deg");

                checkedJoints++;
            }

            // Sans ca, le test passerait triomphalement sur un squelette fige.
            Assert.That(checkedJoints, Is.GreaterThan(5),
                        "marche et course doivent differer sur plusieurs os");

            // L'ecart le plus large conditionne le pouvoir de detection : sous
            // une trentaine de degres, lineaire et spherique se confondent a
            // moins d'un dixieme de degre, et ce test ne distingue plus les
            // deux. Il verifie alors la POSITION sur l'arc, pas l'arc lui-meme.
            Assert.That(widest, Is.GreaterThan(20f),
                        $"ecart le plus large entre marche et course : {widest:0.0} deg");
        }

        /// <summary>Pose du melange demande, a une phase imposee.</summary>
        private static NodePose[] PoseAt(GltfModel model, GltfAnimation a, GltfAnimation b, float weight)
        {
            var animator = new Animator(model);
            animator.SetClips(a, b);
            animator.Weight = weight;
            animator.Phase = 0.3f;

            animator.Advance(0f);

            // Le tableau de pose est reutilise a chaque image : sans copie,
            // les trois "captures" pointeraient sur le meme contenu.
            var copy = new NodePose[animator.Pose.Length];
            Array.Copy(animator.Pose, copy, copy.Length);

            return copy;
        }

        /// <summary>Angle entre deux orientations, en degres.</summary>
        private static float Between(Quaternion a, Quaternion b)
        {
            // La valeur absolue traite q et -q comme la meme rotation.
            float dot = MathF.Abs(Quaternion.Dot(Quaternion.Normalize(a), Quaternion.Normalize(b)));

            return 2f * MathF.Acos(Math.Clamp(dot, -1f, 1f)) * 180f / MathF.PI;
        }

        private static string? FindModel()
        {
            var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);

            while (dir is not null)
            {
                var models = new System.IO.DirectoryInfo(System.IO.Path.Combine(dir.FullName, "Model3D"));

                if (models.Exists)
                    foreach (System.IO.FileInfo file in models.GetFiles("*.glb", System.IO.SearchOption.AllDirectories))
                        if (file.Name.Contains("Merged_Animations", StringComparison.OrdinalIgnoreCase))
                            return file.FullName;

                dir = dir.Parent;
            }

            return null;
        }

        // ===================================================================
        //  Outils
        // ===================================================================

        /// <summary>Phase atteinte apres un temps donne, a un melange donne.</summary>
        private static float PhaseApres(float weight, float dt)
        {
            var animator = new Animator(Skeleton());
            animator.SetClips(Ramp(1, 1.00f, 0f, 1f), Ramp(1, 0.50f, 0f, 10f));
            animator.Weight = weight;

            animator.Advance(dt);

            return animator.Phase;
        }

        private static NodePose[] Poses(Vector3 translation, Quaternion? rotation = null, int count = 1)
        {
            var poses = new NodePose[count];

            for (int i = 0; i < count; i++)
                poses[i] = new NodePose
                {
                    Translation = translation,
                    Rotation = rotation ?? Quaternion.Identity,
                    Scale = Vector3.One,
                };

            return poses;
        }

        /// <summary>Angle d'un quaternion, en degres.</summary>
        private static float Degrees(Quaternion q)
            => 2f * MathF.Acos(Math.Clamp(q.W, -1f, 1f)) * 180f / MathF.PI;

        /// <summary>
        /// Cycle synthetique : la position X d'un noeud glisse lineairement.
        /// Une rampe rend la phase LISIBLE - la position vaut la phase.
        /// </summary>
        private static GltfAnimation Ramp(int node, float duration, float from, float to)
            => new()
            {
                Name = "rampe",
                Duration = duration,
                Channels = new[]
                {
                    new GltfChannel
                    {
                        Node = node,
                        Path = GltfPath.Translation,
                        Interpolation = GltfInterpolation.Linear,
                        Times = new[] { 0f, duration },
                        Values = new[] { new Vector4(from, 0f, 0f, 0f), new Vector4(to, 0f, 0f, 0f) },
                    },
                },
            };

        /// <summary>
        /// Squelette minimal : une racine et deux os, aucun maillage.
        /// L'Animator ne travaille que sur la hierarchie et les poses - pas
        /// besoin du vrai modele, et les tests tournent partout.
        ///
        /// Les os sont au repos en x = 5 et x = 7, valeurs choisies pour
        /// qu'un retour a la pose de repos se remarque.
        /// </summary>
        private static GltfModel Skeleton()
        {
            const string json = """
            {"asset":{"version":"2.0"},"scene":0,"scenes":[{"nodes":[0]}],
             "nodes":[{"name":"racine","children":[1,2]},
                      {"name":"os1","translation":[5,0,0]},
                      {"name":"os2","translation":[7,0,0]}]}
            """;

            byte[] payload = System.Text.Encoding.UTF8.GetBytes(json);

            using var stream = new System.IO.MemoryStream();
            using var writer = new System.IO.BinaryWriter(stream);

            writer.Write(0x46546C67u);              // "glTF"
            writer.Write(2u);                       // version
            writer.Write(0u);                       // longueur totale, remplie apres
            writer.Write((uint)payload.Length);
            writer.Write(0x4E4F534Au);              // "JSON"
            writer.Write(payload);

            byte[] bytes = stream.ToArray();
            BitConverter.GetBytes((uint)bytes.Length).CopyTo(bytes, 8);

            return GltfModel.FromGlb(Glb.Parse(bytes));
        }
    }
}

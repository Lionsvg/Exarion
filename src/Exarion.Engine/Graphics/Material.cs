// ===========================================================================
//  Exarion - Materiau
//
//  Modele PBR "metallic-roughness", celui de glTF et de la quasi-totalite
//  des moteurs actuels. Deux parametres suffisent a decrire la plupart des
//  surfaces :
//
//    RUGOSITE     0 = miroir, 1 = mat. Controle la TAILLE du reflet.
//    METALLICITE  0 = dielectrique (bois, plastique, beton)
//                 1 = metal (acier, or, cuivre)
//                 Les valeurs intermediaires n'ont pas de sens physique :
//                 une surface est metallique ou elle ne l'est pas. Elles ne
//                 servent qu'aux transitions dans une texture.
//
//  Difference physique reelle entre les deux :
//    - un dielectrique reflechit ~4 % de la lumiere sans la teinter, et
//      diffuse le reste avec sa couleur ;
//    - un metal ne diffuse RIEN, et teinte son reflet de sa couleur.
//
//  C'est pourquoi un metal parfait apparait noir sans lumiere a reflechir.
// ===========================================================================

using System.Numerics;

namespace Exarion.Engine.Graphics
{
    public struct Material
    {
        /// <summary>Teinte multipliee a la couleur du sommet et a la texture.</summary>
        public Vector3 Albedo;

        /// <summary>0 = miroir, 1 = totalement mat.</summary>
        public float Roughness;

        /// <summary>0 = dielectrique, 1 = metal.</summary>
        public float Metallic;

        /// <summary>
        /// Texture propre a ce materiau. null = celle de la scene.
        ///
        /// Le decor partage une seule texture projetee depuis le monde ; un
        /// personnage a la sienne, depliee sur ses UV. Les deux doivent
        /// coexister dans la meme image.
        /// </summary>
        public Texture? Map;

        /// <summary>
        /// Vrai : plaquer avec les UV du maillage. Faux : projection planaire
        /// depuis le monde. Voir le commentaire du shader, uUseMeshUv.
        /// </summary>
        public bool UseMeshUv;

        public Material(Vector3 albedo, float roughness, float metallic)
        {
            Albedo = albedo;
            Roughness = roughness;
            Metallic = metallic;
            Map = null;
            UseMeshUv = false;
        }

        /// <summary>Materiau d'un modele importe : sa texture, ses UV.</summary>
        public static Material Textured(Texture map, float roughness = 0.7f, float metallic = 0f)
            => new(Vector3.One, roughness, metallic) { Map = map, UseMeshUv = true };

        /// <summary>Beton, plastique mat, peinture : le cas courant.</summary>
        public static readonly Material Default = new(Vector3.One, 0.75f, 0f);

        /// <summary>Surface tres mate, sans reflet marque.</summary>
        public static Material Matte(Vector3 albedo) => new(albedo, 0.95f, 0f);

        /// <summary>Plastique brillant, carrelage, peinture laquee.</summary>
        public static Material Glossy(Vector3 albedo, float roughness = 0.25f) => new(albedo, roughness, 0f);

        /// <summary>Metal. Sa couleur teinte le reflet, pas la diffusion.</summary>
        public static Material Metal(Vector3 albedo, float roughness = 0.3f) => new(albedo, roughness, 1f);

        /// <summary>Bois, carton, tissu.</summary>
        public static readonly Material Wood = new(new Vector3(1f, 0.94f, 0.86f), 0.85f, 0f);
    }
}

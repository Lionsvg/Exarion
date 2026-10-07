// ===========================================================================
//  Exarion - Camera
//
//  Convention d'orientation, la meme que CharacterMotor :
//    yaw = 0     regarde vers -Z
//    yaw croit   tourne vers la gauche (repere main droite)
//    pitch > 0   regarde vers le haut
//
//  Il est essentiel que camera et controleur partagent cette convention,
//  sinon le joueur avance dans une direction et regarde dans une autre.
// ===========================================================================

using System;
using System.Numerics;

namespace Exarion.Engine.Graphics
{
    public sealed class Camera
    {
        public Vector3 Position;

        /// <summary>Rotation horizontale, en radians.</summary>
        public float Yaw;

        /// <summary>Rotation verticale, en radians. Bornee pour eviter de se retourner.</summary>
        public float Pitch;

        /// <summary>
        /// Champ de vision HORIZONTAL, en degres. 90 est la valeur de CS.
        ///
        /// On specifie l'horizontal, pas le vertical : c'est le comportement
        /// "Hor+" attendu d'un FPS. Un joueur en 21:9 doit voir PLUS sur les
        /// cotes, pas moins en hauteur. Specifier le vertical produirait
        /// l'inverse et desavantagerait les ecrans larges.
        /// </summary>
        public float HorizontalFovDegrees = 90f;

        /// <summary>Champ vertical derive de l'horizontal et du format d'image.</summary>
        public float VerticalFovDegrees
        {
            get
            {
                float h = HorizontalFovDegrees * MathF.PI / 180f;
                float v = 2f * MathF.Atan(MathF.Tan(h * 0.5f) / MathF.Max(0.0001f, AspectRatio));
                return v * 180f / MathF.PI;
            }
        }

        public float NearPlane = 0.02f;
        public float FarPlane = 500f;
        public float AspectRatio = 16f / 9f;

        public const float MaxPitch = 1.5533f;   // ~89 degres

        public void AddLook(float deltaYaw, float deltaPitch)
        {
            Yaw += deltaYaw;
            Pitch = Math.Clamp(Pitch + deltaPitch, -MaxPitch, MaxPitch);

            // On garde yaw dans [-pi, pi] : evite de perdre en precision
            // apres des heures de jeu.
            if (Yaw > MathF.PI) Yaw -= MathF.Tau;
            else if (Yaw < -MathF.PI) Yaw += MathF.Tau;
        }

        public Vector3 Forward
        {
            get
            {
                float cp = MathF.Cos(Pitch);
                return new Vector3(-MathF.Sin(Yaw) * cp, MathF.Sin(Pitch), -MathF.Cos(Yaw) * cp);
            }
        }

        /// <summary>Direction laterale, toujours horizontale.</summary>
        public Vector3 Right => new Vector3(MathF.Cos(Yaw), 0f, -MathF.Sin(Yaw));

        public Vector3 Up => Vector3.Cross(Right, Forward);

        /// <summary>
        /// Direction du rayon qui part de l'oeil et traverse un pixel.
        ///
        /// ⚠️ <paramref name="pixelY"/> compte depuis le HAUT, comme les
        /// positions de souris. Sert a viser avec un curseur libre plutot qu'au
        /// centre de l'ecran - c'est ce qui rend l'editeur utilisable.
        ///
        /// ⚠️ Passer la taille du TAMPON RENDU, pas celle de la fenetre : les
        /// deux different en DPI eleve, et le decalage grandit vers les bords.
        ///
        /// Le calcul lui-meme vit dans Core (ScreenRay), ou il se teste sans
        /// ouvrir de fenetre. Ceci n'est qu'une facade.
        /// </summary>
        public Vector3 RayThroughPixel(float pixelX, float pixelY, int width, int height)
            => Exarion.Core.ScreenRay.Through(pixelX, pixelY, width, height,
                                              Forward, Right, Up, HorizontalFovDegrees);

        public Matrix4x4 GetViewMatrix()
            => Matrix4x4.CreateLookAt(Position, Position + Forward, Vector3.UnitY);

        /// <summary>
        /// Projection perspective main droite pour OpenGL.
        ///
        /// ATTENTION : on n'utilise PAS Matrix4x4.CreatePerspectiveFieldOfView.
        /// System.Numerics produit une profondeur dans [0,1] (convention
        /// DirectX), alors qu'OpenGL 3.3 attend [-1,1]. Utiliser la version
        /// du framework ecrase toute la scene dans la moitie proche du tampon
        /// de profondeur, avec un z-fighting massif. On construit donc la
        /// matrice a la main, d'apres la definition classique de glFrustum.
        /// </summary>
        public Matrix4x4 GetProjectionMatrix()
        {
            float fovRadians = VerticalFovDegrees * MathF.PI / 180f;
            float f = 1f / MathF.Tan(fovRadians * 0.5f);
            float range = NearPlane - FarPlane;

            // Ecrite en convention vecteur-ligne (celle de System.Numerics).
            // ShaderProgram.SetUniform la transpose a l'envoi vers GLSL.
            var m = new Matrix4x4
            {
                M11 = f / AspectRatio,
                M22 = f,
                M33 = (FarPlane + NearPlane) / range,
                M34 = -1f,
                M43 = 2f * FarPlane * NearPlane / range,
                M44 = 0f,
            };

            return m;
        }

        public Matrix4x4 GetViewProjection() => GetViewMatrix() * GetProjectionMatrix();
    }
}

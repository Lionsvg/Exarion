// ===========================================================================
//  Exarion 1 - Vecteur 2D minimal
//
//  Exarion.Core est compile SANS reference au moteur (noEngineReferences),
//  ce qui interdit UnityEngine.Vector2. On definit donc le strict necessaire.
//  La conversion vers Unity se fait dans la couche jeu (VecExtensions).
// ===========================================================================

using System;

namespace Exarion.Core
{
    public struct Vec2 : IEquatable<Vec2>
    {
        public float X;
        public float Y;

        public Vec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public static readonly Vec2 Zero = new Vec2(0f, 0f);

        public float Magnitude => (float)Math.Sqrt(X * X + Y * Y);

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator *(Vec2 a, float s) => new Vec2(a.X * s, a.Y * s);

        public bool Equals(Vec2 other) => X.Equals(other.X) && Y.Equals(other.Y);
        public override bool Equals(object obj) => obj is Vec2 v && Equals(v);
        public override int GetHashCode() => (X.GetHashCode() * 397) ^ Y.GetHashCode();
        public override string ToString() => $"({X:0.###}, {Y:0.###})";
    }
}

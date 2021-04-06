using UnityEngine;

namespace Electroant
{
    public static class CommonExtensions
    {
        public static Vector3 ToXY_(this Vector2 v) => new Vector3(v.x, v.y, 0);
        public static Vector3 ToX_Y(this Vector2 v) => new Vector3(v.x, 0, v.y);
    }
}
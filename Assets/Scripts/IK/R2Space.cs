using UnityEngine;

namespace IK
{
    public struct R2Space
    {
        public readonly Plane Plane;
        public readonly Vector3 I;
        public readonly Vector3 J;

        public R2Space(Plane plane)
        {
            Plane = plane;
            var normRotated = Quaternion.Euler(12,24, 36) * plane.normal;
            I = Vector3.Cross(plane.normal, normRotated).normalized;
            J = Vector3.Cross(plane.normal, I)          .normalized;
        }

        private R2Space(Plane plane, Vector3 i, Vector3 j)
        {
            Plane = plane;
            I = i;
            J = j;
        }

        /// <summary>
        /// return v as a linear combination of R2Space's basis vectors
        /// </summary>
        /// <param name="v"></param>
        /// <returns></returns>
        public Vector2 GetPointInR2Space(Vector3 v)
        {
            Vector3 pointOnPlane = Plane.ClosestPointOnPlane(v);
            var xI = Vector3.Dot(I, pointOnPlane);
            var yJ = Vector3.Dot(J, pointOnPlane);
            return new Vector2(xI, yJ);
        }
        
       
        
        /// <summary>
        /// in degrees between 0 and 360
        /// </summary>
        /// <param name="v"></param>
        /// <returns></returns>
        public float GetAngleOnPlane(Vector3 v)
        {
            float planeX = Vector3.Dot(I, v);
            float planeY = Vector3.Dot(J, v);

            if (planeX == 0 && planeY == 0)
                return 0;
            
            float degreesNegToPos180 =  Mathf.Atan2(planeY, planeX) * Mathf.Rad2Deg;
            
            float degrees0To360;
            if (degreesNegToPos180 < 0)
                degrees0To360 = 360 + degreesNegToPos180;
            else
                degrees0To360 = degreesNegToPos180;
            return degrees0To360;
        }

        public static R2Space GetRotated(R2Space r2, Quaternion rotation)
        {
            return new R2Space(new Plane(rotation * r2.Plane.normal, 0), rotation * r2.I,rotation * r2.J);
        }
    }
}
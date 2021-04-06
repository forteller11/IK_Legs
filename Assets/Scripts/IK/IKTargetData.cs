using UnityEngine;

namespace Electroant.Visuals.IK
{
    public struct IKTargetData
    {
        public RaycastHit Hit;
        public float InitialYaw;
    }

    public struct IKInterpData
    {
        public readonly Transform BodyTransform;
        public readonly float BodyYaw;
        public readonly Vector3 Velocity;
        public readonly float VelocityMag;

        public readonly Vector2 MinMaxVelocity;
        //public float BodyYaw;
        public IKInterpData(Transform bodyTransform, float yaw, Vector3 velocity, float velMag, Vector2 minMaxVelocity)
        {
            BodyTransform = bodyTransform;
            BodyYaw = yaw;
            Velocity = velocity;
            VelocityMag = velMag;
            MinMaxVelocity = minMaxVelocity;
        }
    }

    public struct IKLegState
    {
        public float TimeSinceNewTarget;
        public float DistToTarget;
        public bool Planted;
        public bool DesiresNewTarget;
    }

    // public struct IKInterpResult
    // {
    //     public bool OccupiedByIK;
    //     public bool 
    // }
}
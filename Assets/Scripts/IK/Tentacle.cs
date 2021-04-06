using System;
using Electroant.Visuals.IK;
using Sirenix.OdinInspector;
using UnityEngine;

namespace IK
{
    public class Tentacle : MonoBehaviour
    {
        [SerializeField] private AnimationCurve InterpolationCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        [Required] public Transform TipTransform;
        public IKSegmentTarget IKTarget { get; private set; } //created by ikcontroller on creation, to mirror transform hierarchy of controller

        private float _reach = Single.NegativeInfinity; 
        public float Reach => _reach; //assumes seg start is at origin
        public GameObject Gameobject => gameObject;

        private Quaternion _localTPoseRotation;

        private void Awake()
        {
            _reach = Vector3.Distance(TipTransform.position, transform.position);
            if (InterpolationCurve.Evaluate(1) < 1)
                Debug.LogError($"Anime curve at {Gameobject.name} must end with x: 1; y: 1. But current ends at {InterpolationCurve.Evaluate(1)}");
        }

        public void Init(IKSegmentTarget iktarget)
        {
            _localTPoseRotation = transform.localRotation;
            IKTarget = iktarget;
        }

        public void IKOutOfReach(Vector3 goal, Vector3 localUp)
        {
            Vector3 endToTarget = IKTarget.SegmentEnd.position - goal;
            IKTarget.transform.rotation = Quaternion.LookRotation(endToTarget, localUp);
        }
        
        public void IKItterateInReach(Vector3 effector, Vector3 goal)
        {
            Vector3 toTarget   = goal - IKTarget.transform.position;
            Vector3 toEffector = effector - IKTarget.transform.position;
            Quaternion lookTarget = Quaternion.FromToRotation(toEffector, toTarget);
            
            IKTarget.transform.rotation = lookTarget * IKTarget.transform.rotation;
        }

        public void InterpolateTowardsTarget(float interpProgressNormalized)
        {
            var interpProgressScaled = InterpolationCurve.Evaluate(interpProgressNormalized);
            transform.rotation = Quaternion.Slerp(transform.rotation, IKTarget.transform.rotation, interpProgressScaled);

        }
        
        public void ResetToResting()
        {
            IKTarget.transform.localRotation = _localTPoseRotation;
        }
    }
}
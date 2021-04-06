using System;
using System.Collections;
using System.Collections.Generic;
using Electroant.Visuals.IK;
using IK;
using Sirenix.OdinInspector;
using Unity.Mathematics;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Rendering;


public class IKController : MonoBehaviour
{
    [ShowInInspector] [NonSerialized] public List<Tentacle> Segments;
    [SerializeField] [Required] Transform BodyTransform;
    
    [FoldoutGroup("IK")] [SerializeField] int _maxIKItterations = 5;
    [FoldoutGroup("IK")] [SerializeField] float _distanceToleranceToTarget = .1f;
    
    [FoldoutGroup("Retargeting")] [SerializeField] int _raycastRetargetMaxAttempts = 5;
    [FoldoutGroup("Retargeting")] [Range(0,180)] [SerializeField] float _maxYawChangeBeforeDesiresNewTarget = 45f;
    [FoldoutGroup("Retargeting")] [Range(.1f,5)] [SerializeField] float _newTargetMaxDistanceRelToReach = 1;
    
    [FoldoutGroup("Retargeting")] [SerializeField] private Vector3 _targetExtentCenter = Vector3.down;
    [FoldoutGroup("Retargeting")] [SerializeField] private Collider [] _noGoZones;

    [FoldoutGroup("Interpolation")] [SerializeField] float _totalTargetInterpTime = .3f;
    [FoldoutGroup("Interpolation")] [SerializeField] float _totalNoTargetInterpTime = 2f;
    [FoldoutGroup("Interpolation")] [ShowInInspector] float _interpolationProgressRawCache;
    public float Reach { get; private set; }
   // [Range(0,1)] [SerializeField] float _legLeftPercentHighPoint = .5f;

    [NonSerialized] public int LastRetargetAttemptIndex; //where 0 == just moved, and higher == hasmoved later
    
    
    private IKLegState _ikState;
    public IKLegState IKState => _ikState;
    public IKTargetData? TargetData { get; private set; }
    
    
    private static RaycastHit[] _hitsShared = new RaycastHit[100]; //this makes the solution NOT multi threadable per limb
    private static int _rayCastLayerMask = 1 << 8;

    

    public void Init()
    {
        Segments = new List<Tentacle>();
        
        if (GetComponent<Tentacle>() == null)
            Debug.LogError($"Need a root segment on {gameObject.name} IKController");
            
        GetIkSegmentsRecursivelyFromChildren(gameObject);
        GetApproximateLengthOfLeg();
        MirrorLimbTransformHierarchyAndInit();
        InitTargetExtents();

        void GetIkSegmentsRecursivelyFromChildren(GameObject segmentHolder)
        {
            
            var segs = segmentHolder.GetComponents<Tentacle>();
            if (segs.Length > 2)
                Debug.LogError($"There are {segs.Length} IKSegments at {segmentHolder} when there should only be 1!");
            var seg = segs[0];
            
            Segments.Add(seg);
            
            for (int i = 0; i < segmentHolder.transform.childCount; i++)
            {
                var child = segmentHolder.transform.GetChild(i);
                if (child.GetComponent<Tentacle>() != null)
                {
                    GetIkSegmentsRecursivelyFromChildren(child.gameObject);
                }
            }
            
        }
        
        void GetApproximateLengthOfLeg()
        {
            Reach = 0;
            for (int i = 0; i < Segments.Count; i++)
                Reach += Segments[i].Reach;
        }

        void InitTargetExtents()
        {
            _targetExtentCenter = _targetExtentCenter.normalized;
            var plane = new Plane(_targetExtentCenter, 0);
        }

        void MirrorLimbTransformHierarchyAndInit()
        {
            var IKSegmentTargets = new IKSegmentTarget[Segments.Count];
            for (int i = 0; i < Segments.Count; i++)
            {
                IKSegmentTargets[i] = new GameObject($"segment_mirror: {i}").AddComponent<IKSegmentTarget>();
                CopyPasteTransforms( Segments[i].transform, IKSegmentTargets[i].transform);

                //set transform
                if (i == 0)
                    IKSegmentTargets[i].transform.SetParent(transform.parent);
                else
                    IKSegmentTargets[i].transform.SetParent(IKSegmentTargets[i - 1].transform);
            
            }

            for (int i = 0; i < Segments.Count; i++)
            {
                //segment end
                if (i != Segments.Count - 1)
                    IKSegmentTargets[i].SegmentEnd = IKSegmentTargets[i + 1].transform;
                else
                {
                    var finalTip = new GameObject("segment_tip_tranform").transform;
                    finalTip.SetParent(IKSegmentTargets[i].transform);
                    CopyPasteTransforms(Segments[i].TipTransform, finalTip);
                    IKSegmentTargets[i].SegmentEnd = finalTip;
                }

                Segments[i].Init(IKSegmentTargets[i]);
            }

            void CopyPasteTransforms(Transform copyFrom, Transform pasteOnto)
            {
                pasteOnto.transform.position   = copyFrom.transform.position;
                pasteOnto.transform.rotation   = copyFrom.transform.rotation;
                pasteOnto.transform.localScale = copyFrom.transform.localScale;
            }
        }

    }

    public bool SetTarget(Vector2 minMaxVel, float deltaYaw, float yaw, Vector3 velocity, float velMag)
    {
        int hitNumber = -1;
        int closestHitIndex = -1;
        bool hasHitAtLeastOne = false;

  
        var targetExtentCenterRel = BodyTransform.rotation * _targetExtentCenter;

        float percentageBetweenMinMaxVelocity = Mathf.Clamp01(Mathf.InverseLerp(minMaxVel.x, minMaxVel.y, velMag));

        var velNorm = velocity / velMag;
        // Debug.Log(deltaYaw);
        // var velNormRotatedWithYaw = Quaternion.AngleAxis(deltaYaw, BodyTransform.up) * velNorm;
        
        Vector3 maxRayTarget = velNorm; //normalize
        Vector3 minRayTarget =  targetExtentCenterRel;



        DrawRays();

        void DrawRays()
        {

            Debug.DrawRay(transform.position, maxRayTarget, new Color(1,.6f,0), 10);
            Debug.DrawRay(transform.position, minRayTarget, Color.blue, 10);
        }
        
  
        
        for (int i = 0; i < _raycastRetargetMaxAttempts; i++)
        {
            
            #region get dir of ray
            
            float percentDoneAttempts =  1 - ((float) i / _raycastRetargetMaxAttempts); //where 1 == start, 0 === end
            float lerpAmount = percentageBetweenMinMaxVelocity * percentDoneAttempts;
            
            Vector3 rayCastDir = Vector3.Slerp(minRayTarget, maxRayTarget, lerpAmount);
            #endregion
            
            float maxTargetDistance = Reach * _newTargetMaxDistanceRelToReach;
            var ray = new Ray(transform.position, rayCastDir);
            
            Debug.DrawRay(ray.origin, ray.direction*3, Color.Lerp(Color.black, Color.red, percentDoneAttempts), 4);
            
            hitNumber = Physics.RaycastNonAlloc(ray, _hitsShared, maxTargetDistance, _rayCastLayerMask); //todo non alloc
            {
                closestHitIndex = -1;
                float closestHitDistance = float.PositiveInfinity;

                for (int j = 0; j < hitNumber; j++)
                {
                    var currentDist = Vector3.Distance(_hitsShared[j].point, transform.position);

                    if (currentDist <= maxTargetDistance &&
                        currentDist < closestHitDistance && 
                        !IsPointWithinNoGoZones(_hitsShared[j].point))
                    {
                        closestHitIndex = j;
                        closestHitDistance = currentDist;
                        hasHitAtLeastOne = true;
                    }
                }

                if (hasHitAtLeastOne)
                    break;

                if (i == _raycastRetargetMaxAttempts-1)
                    Debug.LogWarning("cudnt find point");
            }
        }
        
        if (hasHitAtLeastOne)
        {
            _ikState.DesiresNewTarget = false;
            _ikState.Planted = false;
            _interpolationProgressRawCache = 0;
            
            TargetData = new IKTargetData() //shouldn't really use the same struct for controller and tentacle
            {
                Hit = _hitsShared[closestHitIndex],
                InitialYaw = yaw,
            };
        }
        else //no target
        {
            TargetData = null;
            _ikState.DesiresNewTarget = true;
            _ikState.Planted = false;
            _ikState.DistToTarget = float.PositiveInfinity;
            _interpolationProgressRawCache = 0;
        }

        return hasHitAtLeastOne;
    }

    public void ManualUpdate(IKInterpData interpData)
    {

        SolveIKAndDetermineStates();
        InterpolateToTargetAndDetermineIfPlanted();
        InterpolateIncrement();
        
        //there are 3 basic states of operation: 1) no target data == interp to resting position 2) out of reach ik 3) and in reach leg ik
        void SolveIKAndDetermineStates()
        {
            if (TargetData == null)
            {
                for (int i = 0; i < Segments.Count; i++)
                    Segments[i].ResetToResting();
            }
            else
            {

                _ikState.DistToTarget = Vector3.Distance(Segments[0].transform.position, TargetData.Value.Hit.point);

                Vector3 goal = TargetData.Value.Hit.point;
                //Debug.DrawRay(legLeft, normal, Color.red, .1f);
                if (_ikState.DistToTarget < Reach)
                {
                    for (int i = Segments.Count - 1; i >= 0; i--)
                    {
                        Segments[i].ResetToResting();
                    }
                    
                    for (int solverCount = 0; solverCount < _maxIKItterations; solverCount++)
                    {
                        for (int i = Segments.Count - 1; i >= 0; i--)
                        {

                            //todo use abs distance
                            var tipDistFromTarget = Vector3.Distance(Segments[Segments.Count - 1].IKTarget.SegmentEnd.position, TargetData.Value.Hit.point);
                            if (tipDistFromTarget < _distanceToleranceToTarget)
                            {
                                break;
                            }

                            var currentSeg = Segments[i];
                            currentSeg.IKItterateInReach(Segments[Segments.Count - 1].IKTarget.SegmentEnd.position, goal);

                        }
                    }
                }
                else
                {
                    for (int i = Segments.Count - 1; i >= 0; i--)
                    {
                        Segments[i].IKItterateInReach(Segments[Segments.Count - 1].IKTarget.SegmentEnd.position, goal);
                    }
                }

                #region does desire new target?
                if (Mathf.Abs(TargetData.Value.InitialYaw - interpData.BodyYaw) >= _maxYawChangeBeforeDesiresNewTarget)
                {
                    _ikState.DesiresNewTarget = true;
                    return;
                }
             
                var shoulderDistFromTarget = Vector3.Distance(Segments[0].transform.position, TargetData.Value.Hit.point); //todo use abs distance
                if (shoulderDistFromTarget > Reach)
                {
                    _ikState.DesiresNewTarget = true;
                    return;
                }
 
                if (IsPointWithinNoGoZones(Segments[Segments.Count - 1].IKTarget.SegmentEnd.position))
                {
                    _ikState.DesiresNewTarget = true;
                    return;
                }
                #endregion
            }

        }

        void InterpolateToTargetAndDetermineIfPlanted()
        {
            float interpProgress;
            if (TargetData.HasValue)
            {
                interpProgress = Mathf.Clamp01(_interpolationProgressRawCache / _totalTargetInterpTime);
                var tipDistFromTarget = Vector3.Distance(Segments[Segments.Count - 1].IKTarget.SegmentEnd.position, TargetData.Value.Hit.point);
                _ikState.Planted = tipDistFromTarget < _distanceToleranceToTarget;
            }
            else
            {
                _ikState.Planted = false;
                interpProgress = Mathf.Clamp01(_interpolationProgressRawCache / _totalNoTargetInterpTime);
            }

            for (int i = 0; i < Segments.Count; i++)
            {
                Segments[i].InterpolateTowardsTarget(interpProgress);
            }
            
        }

        void InterpolateIncrement()
        {
            _interpolationProgressRawCache += Time.deltaTime;
            _ikState.TimeSinceNewTarget += Time.deltaTime;
        }
    }

    float GetInterpolationProgressLinear() => Mathf.Clamp01(_interpolationProgressRawCache / _totalTargetInterpTime);


    private void OnDrawGizmosSelected()
    {
        
        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(transform.position, BodyTransform.rotation * _targetExtentCenter*2);
    }

    bool IsPointWithinNoGoZones(Vector3 point)
    {
        for (int i = 0; i < _noGoZones.Length; i++)
        {
            if (_noGoZones[i].bounds.Contains(point))
                return true;
        }

        return false;
    }
    private void OnDrawGizmos()
    {

        
        if (Application.isPlaying)
        {
            if (TargetData.HasValue)
            {
                Gizmos.color = Color.blue;
                Gizmos.DrawSphere(TargetData.Value.Hit.point, .12f);
                Gizmos.DrawLine(transform.position, TargetData.Value.Hit.point);
            }

            if (TargetData.HasValue)
            {
                Gizmos.color = new Color(.8f, .4f, 0f);
                Gizmos.DrawWireSphere(TargetData.Value.Hit.point, _distanceToleranceToTarget);
            }
            
            if (_ikState.DesiresNewTarget)
            {
                Gizmos.color = new Color(.6f, .3f, .9f);
                Gizmos.DrawWireSphere(transform.position, .3f);
            }

            if (_ikState.Planted)
            {
                Gizmos.color = new Color(.5f, .3f, .1f);
                Gizmos.DrawWireSphere(Segments[Segments.Count - 1].TipTransform.position, .2f);
            }
        }
        else
        {
          
        }

    }
    
    public Vector3 GetTipPosition()
    {
        return Segments[Segments.Count - 1].TipTransform.position;
    }

    
    
}

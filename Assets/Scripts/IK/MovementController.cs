using System;
using System.Collections;
using System.Collections.Generic;
using Electroant.Visuals.IK;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.InputSystem;
using static Unity.Mathematics.math;

public class MovementController : MonoBehaviour
{
    #region members
    [Required] [SerializeField] Rigidbody _rigidbody;
    
    [FoldoutGroup("movement")] [SerializeField] float _sensitivity = 0.003f;
    [FoldoutGroup("movement")] [Range(0,1)] [SerializeField] float _decceleration = 0.90f;
    [FoldoutGroup("movement")] [SerializeField] float _rotationSensitivity = 1;
    [Tooltip("determined by how many legs are planted")]
    [FoldoutGroup("movement")] [SerializeField] Vector2 _minMaxVelocity = new Vector2(0.005f, 0.3f);

    [FoldoutGroup("lean")] [Range(0, 90)] [SerializeField] float _maxLeanAngle = 25;
    [FoldoutGroup("lean")] [Range(0,1)] [SerializeField] float _leanInterpSpeed = 0.03f;
    [FoldoutGroup("lean")] [Range(0,2)] [SerializeField] float _leanIntensity = .18f;
    
    [FoldoutGroup("retargeting")] [SerializeField] float MinIntervalBetweenLegRetarget = 0.1f;
    [FoldoutGroup("retargeting")] [SerializeField] int MinLegsOnGround = 1; //todo use this to determine if new target can be set
    
    [SerializeField] List<IKController> Limbs;
    
    private float _timeBetweenLastSucessfulLegRetarget;
    private Vector3 _velocity;
    private float _yawAngle = 0;
    private float _lastPhysicFramesYawAngle = 0;
    #endregion
    
    [Button] void AutoAssignLegs()
    {
        Limbs = new List<IKController>();
        var legs = GetComponentsInChildren<IKController>();

        Limbs = new List<IKController>(legs.Length);
        Limbs.AddRange(legs);
    }

    private void Start()
    {
        _timeBetweenLastSucessfulLegRetarget = 0;
        
        for (int i = 0; i < Limbs.Count; i++)
        {
            Limbs[i].Init();
            Limbs[i].SetTarget(_minMaxVelocity, Mathf.DeltaAngle(_yawAngle, _lastPhysicFramesYawAngle), _yawAngle, _velocity, _velocity.magnitude);
        }

    }

    private void FixedUpdate()
    {
        _rigidbody.velocity = Vector3.zero;
        _rigidbody.angularVelocity = Vector3.zero;
        
        #region input
        var board = Keyboard.current;
        var w = board.wKey.ReadValue();
        var s = board.sKey.ReadValue();
        var a = board.aKey.ReadValue();
        var d = board.dKey.ReadValue();
        var shift = board.shiftKey.ReadValue();
        var space = board.spaceKey.ReadValue();
        var q = board.qKey.ReadValue();
        var e = board.eKey.ReadValue();
        
        Vector3 inputAbsolute = new Vector3(d - a,space-shift, w - s);
        Vector3 inputRelative = transform.rotation * inputAbsolute;
        Vector3 movement = inputRelative * _sensitivity;
        movement.y *= .6f;
        #endregion

        #region max vel calculation

        GetLegsPlanted(out int numberOfLimbsPlanted, out float percentLimbsFromMinPlanted);
           
        //todo use to introduce gravity in physics
        float percentGravityLegs = 1;
        if (numberOfLimbsPlanted < MinLegsOnGround)
            percentGravityLegs = (float) numberOfLimbsPlanted / MinLegsOnGround;
        percentGravityLegs = 1 - percentGravityLegs;
        
        
        float velMagMax = Mathf.Lerp(_minMaxVelocity.x, _minMaxVelocity.y, percentLimbsFromMinPlanted);
       
        #endregion
        
        #region velocity calc
        float velMag = _velocity.magnitude;

        //if over max vel, or if not moving
        if (velMag > velMagMax
        || Mathf.Approximately(movement.sqrMagnitude, 0))
        {
            _velocity *= _decceleration;
        }
        else
        {
            _velocity += movement;
        }
        
        _rigidbody.MovePosition(transform.position + _velocity);
      
        #endregion

        #region rotation
        
        Vector3 avgLegPositionWorld = Vector3.zero;
        Vector3 avgNormalOfHits = Vector3.zero;
        int totalPlantedLegs = 0;
        int totalLegsWithTargets = 0;
        
        for (int i = 0; i < Limbs.Count; i++)
        {
            if (Limbs[i].TargetData.HasValue)
            {
                avgNormalOfHits += Limbs[i].TargetData.Value.Hit.normal; //todo dont 
                totalLegsWithTargets++;
                
                if (Limbs[i].IKState.Planted)
                {
                    avgLegPositionWorld += Limbs[i].TargetData.Value.Hit.point; //todo dont 
                    totalPlantedLegs++;
                }
            }
        }

        if (totalPlantedLegs != 0)
            avgLegPositionWorld /= totalPlantedLegs;
        else
            avgLegPositionWorld = transform.position;
        
        if (totalLegsWithTargets != 0)
            avgNormalOfHits /= totalLegsWithTargets;
        else
            avgNormalOfHits = Vector3.up; //will lead to cat like behavior of landing on feeting
        #region update yaw

        float rotateForce = (e-q) * _rotationSensitivity;
        _lastPhysicFramesYawAngle = _yawAngle; //so there's always a delta
        _yawAngle += rotateForce;
     
        var localUp = avgNormalOfHits;
        var yawRotation = Quaternion.AngleAxis(_yawAngle, localUp); //maybe vector3.up vs local  up;
        var wallRotation = Quaternion.FromToRotation(Vector3.up, localUp);

        #endregion
        
        #region calc lean

        Vector3 avgLegPositionRelative = avgLegPositionWorld - transform.position;
        Vector3 gravity = Vector3.down;

        Debug.DrawRay(transform.position, avgLegPositionRelative*2, Color.cyan);
        /*todo
         this causes Assertion failed on expression: 'fRoot >= Vector3f::epsilon' UnityEngine.Quaternion:FromToRotation (UnityEngine.Vector3,UnityEngine.Vector3)*/
        var leanRaw = avgLegPositionRelative == gravity ? Quaternion.identity : Quaternion.FromToRotation(gravity, avgLegPositionRelative); 
        leanRaw.ToAngleAxis(out var leanAngleRaw, out var leanAxisRaw);
        float leanAngleTuned = Mathf.Clamp(leanAngleRaw * _leanIntensity, -_maxLeanAngle, _maxLeanAngle);

        var leanTuned = Quaternion.AngleAxis(leanAngleTuned, leanAxisRaw);
        //TargetLean = leanTuned;
        #endregion
       
        var combinedRotation = leanTuned * yawRotation * wallRotation;
        
        var tryRot = Quaternion.Slerp(transform.rotation, combinedRotation, _leanInterpSpeed);
        _rigidbody.MoveRotation(tryRot);
        #endregion
    }

    private void Update()
    {
        float velMag = _velocity.magnitude;
        GetLegsPlanted(out int numberOfLimbsPlanted, out float percentLimbsFromMinPlanted);
        
        #region update ik controller
       
       var data = new IKInterpData(transform, _yawAngle, _velocity, velMag, _minMaxVelocity);
       for (int i = 0; i < Limbs.Count; i++)
       {
           Limbs[i].ManualUpdate(data);
       }

       #region sort limbs by last used index
       //insertion sort from largest to smallest
       for (int i = 0; i < Limbs.Count; i++)
       {
           int currentMax = -1;
           int currentMaxIndex = -1;
           
           for (int j = i; j < Limbs.Count; j++)
           {
               if (Limbs[j].LastRetargetAttemptIndex > currentMax)
               {
                   currentMax = Limbs[j].LastRetargetAttemptIndex;
                   currentMaxIndex = j;
               }
           }

           var cache1 = Limbs[i];
           var cache2 = Limbs[currentMaxIndex];

           Limbs[i] = cache2;
           Limbs[currentMaxIndex] = cache1;
       }
       #endregion
       
       
       #region retargeting
       
       _timeBetweenLastSucessfulLegRetarget += Time.deltaTime;
       
       //if haven't too recently retargeted a leg...
       //find new target if ik wants new target
       if (_timeBetweenLastSucessfulLegRetarget > MinIntervalBetweenLegRetarget)
       

           #region retarget if a leg desires a retarget
           for (int i = 0; i < Limbs.Count; i++)
           {
               if (numberOfLimbsPlanted < MinLegsOnGround) //if we need more legs to be planted, don't retarget planted legs
               {
                   if (Limbs[i].IKState.DesiresNewTarget && !Limbs[i].IKState.Planted)
                   {
                       if (SetNewTarget(Limbs[i]))
                           break;
                       else
                           Limbs[i].LastRetargetAttemptIndex = 0;
                   }
               }

               else if (Limbs[i].IKState.DesiresNewTarget)
               {
                   if (SetNewTarget(Limbs[i]))
                    break;
               }
           }
           #endregion
    

       bool SetNewTarget(IKController limb)
       {
           bool newTargetFound = limb.SetTarget( _minMaxVelocity, Mathf.DeltaAngle(_yawAngle, _lastPhysicFramesYawAngle), _yawAngle, _velocity, velMag);
           
           for (int j = 0; j < Limbs.Count; j++)
               Limbs[j].LastRetargetAttemptIndex++;
           
           limb.LastRetargetAttemptIndex = 0;

           if (newTargetFound)
               _timeBetweenLastSucessfulLegRetarget = 0;

           return newTargetFound;
       }
       #endregion
       #endregion

    }

    void GetLegsPlanted(out int numberOfLimbsPlanted, out float percentLimbsFromMinPlanted)
    {
        numberOfLimbsPlanted = 0;
        
        for (int i = 0; i < Limbs.Count; i++)
        {
            if (Limbs[i].IKState.Planted)
                numberOfLimbsPlanted++;
        }
        
        percentLimbsFromMinPlanted = Mathf.Max(numberOfLimbsPlanted - MinLegsOnGround, 0)/((float)Limbs.Count - MinLegsOnGround);
    }
    
}


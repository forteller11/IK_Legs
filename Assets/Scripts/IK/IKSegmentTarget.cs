using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Electroant.Visuals.IK
{
    public class IKSegmentTarget : MonoBehaviour
    {
        [ShowInInspector] [NonSerialized] public Transform SegmentEnd;
        [ShowInInspector] public Vector3 SegmentEndPosition => SegmentEnd.position;
    }
}
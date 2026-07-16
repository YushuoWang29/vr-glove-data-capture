using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRGloveDataCapture.FingerKinematics
{
    /// <summary>
    /// Optional constrained post-processor for a Hi5 hand rig. It couples DIP
    /// flexion to PIP flexion while preserving abduction/adduction and twist.
    /// The vendor animation is read in LateUpdate, after HI5_VIVEInstance.Update.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(32000)]
    public sealed class FingerJointCoupling : MonoBehaviour
    {
        [Serializable]
        public sealed class FingerChain
        {
            public string label;
            [Tooltip("PIP joint transform, e.g. Noitom_RightHandIndex2.")]
            public Transform pip;
            [Tooltip("DIP joint transform, e.g. Noitom_RightHandIndex3.")]
            public Transform dip;
            [Tooltip("Use -1 if this rig reports flexion with the opposite sign.")]
            public float flexionSign = 1.0f;
        }

        [SerializeField]
        private bool applyCoupling = true;

        [SerializeField]
        [Range(0.0f, 1.2f)]
        private float dipToPipRatio = 0.67f;

        [SerializeField]
        [Range(0.0f, 1.0f)]
        private float couplingWeight = 0.65f;

        [SerializeField]
        [Min(0.0f)]
        private float responseHz = 18.0f;

        [SerializeField]
        private Vector3 localFlexionAxis = Vector3.forward;

        [SerializeField]
        private Vector2 dipFlexionLimits = new Vector2(-5.0f, 95.0f);

        [SerializeField]
        private bool autoBindHi5Names = true;

        [SerializeField]
        private FingerChain[] fingers = new FingerChain[0];

        private Quaternion[] pipNeutral = new Quaternion[0];
        private Quaternion[] dipNeutral = new Quaternion[0];
        private float[] filteredDipAngles = new float[0];

        public bool ApplyCoupling
        {
            get { return applyCoupling; }
            set { applyCoupling = value; }
        }

        private void Reset()
        {
            CreateDefaultChains();
            AutoBindBones();
        }

        private void OnEnable()
        {
            if (autoBindHi5Names && !HasAnyBoundChain())
            {
                AutoBindBones();
            }

            CaptureNeutralPose();
        }

        private void OnValidate()
        {
            if (localFlexionAxis.sqrMagnitude < 0.0001f)
            {
                localFlexionAxis = Vector3.forward;
            }
            else
            {
                localFlexionAxis.Normalize();
            }

            dipFlexionLimits.y = Mathf.Max(dipFlexionLimits.x, dipFlexionLimits.y);
        }

        private void LateUpdate()
        {
            if (!applyCoupling || fingers == null || fingers.Length == 0)
            {
                return;
            }

            if (pipNeutral.Length != fingers.Length)
            {
                CaptureNeutralPose();
            }

            Vector3 axis = localFlexionAxis.normalized;
            float response = responseHz <= 0.0f
                ? 1.0f
                : 1.0f - Mathf.Exp(-2.0f * Mathf.PI * responseHz * Time.deltaTime);

            for (int i = 0; i < fingers.Length; i++)
            {
                FingerChain finger = fingers[i];
                if (finger == null || finger.pip == null || finger.dip == null)
                {
                    continue;
                }

                float sign = Mathf.Approximately(finger.flexionSign, 0.0f)
                    ? 1.0f
                    : Mathf.Sign(finger.flexionSign);
                Quaternion pipRelative = Quaternion.Inverse(pipNeutral[i]) * finger.pip.localRotation;
                Quaternion dipRelative = Quaternion.Inverse(dipNeutral[i]) * finger.dip.localRotation;

                float pipAngle = GetSignedTwistAngle(pipRelative, axis) * sign;
                float measuredDipAngle = GetSignedTwistAngle(dipRelative, axis) * sign;
                float coupledDipAngle = Mathf.Clamp(
                    pipAngle * dipToPipRatio,
                    dipFlexionLimits.x,
                    dipFlexionLimits.y);

                if (float.IsNaN(filteredDipAngles[i]))
                {
                    filteredDipAngles[i] = measuredDipAngle;
                }

                filteredDipAngles[i] = Mathf.LerpAngle(
                    filteredDipAngles[i],
                    coupledDipAngle,
                    response);
                float finalAngle = Mathf.LerpAngle(
                    measuredDipAngle,
                    filteredDipAngles[i],
                    couplingWeight) * sign;

                Quaternion coupledRelative = ReplaceTwist(dipRelative, axis, finalAngle);
                finger.dip.localRotation = dipNeutral[i] * coupledRelative;
            }
        }

        [ContextMenu("Capture current pose as coupling neutral")]
        public void CaptureNeutralPose()
        {
            int count = fingers == null ? 0 : fingers.Length;
            pipNeutral = new Quaternion[count];
            dipNeutral = new Quaternion[count];
            filteredDipAngles = new float[count];

            Vector3 axis = localFlexionAxis.sqrMagnitude < 0.0001f
                ? Vector3.forward
                : localFlexionAxis.normalized;

            for (int i = 0; i < count; i++)
            {
                FingerChain finger = fingers[i];
                pipNeutral[i] = finger != null && finger.pip != null
                    ? finger.pip.localRotation
                    : Quaternion.identity;
                dipNeutral[i] = finger != null && finger.dip != null
                    ? finger.dip.localRotation
                    : Quaternion.identity;
                filteredDipAngles[i] = finger != null && finger.dip != null
                    ? GetSignedTwistAngle(
                        Quaternion.Inverse(dipNeutral[i]) * finger.dip.localRotation,
                        axis)
                    : float.NaN;
            }
        }

        [ContextMenu("Auto-bind Noitom Hi5 finger bones")]
        public void AutoBindBones()
        {
            if (fingers == null || fingers.Length != 4)
            {
                CreateDefaultChains();
            }

            Transform[] descendants = GetComponentsInChildren<Transform>(true);
            Dictionary<string, Transform> byName = new Dictionary<string, Transform>();
            string side = null;
            for (int i = 0; i < descendants.Length; i++)
            {
                byName[descendants[i].name] = descendants[i];
                if (descendants[i].name.StartsWith("Noitom_LeftHand", StringComparison.Ordinal))
                {
                    side = "Left";
                }
                else if (descendants[i].name.StartsWith("Noitom_RightHand", StringComparison.Ordinal))
                {
                    side = "Right";
                }
            }

            if (side == null)
            {
                return;
            }

            string[] names = { "Index", "Middle", "Ring", "Pinky" };
            for (int i = 0; i < names.Length; i++)
            {
                FingerChain finger = fingers[i] ?? new FingerChain();
                finger.label = names[i];
                byName.TryGetValue("Noitom_" + side + "Hand" + names[i] + "2", out finger.pip);
                byName.TryGetValue("Noitom_" + side + "Hand" + names[i] + "3", out finger.dip);
                fingers[i] = finger;
            }

            CaptureNeutralPose();
        }

        private void CreateDefaultChains()
        {
            string[] names = { "Index", "Middle", "Ring", "Pinky" };
            fingers = new FingerChain[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                fingers[i] = new FingerChain { label = names[i], flexionSign = 1.0f };
            }
        }

        private bool HasAnyBoundChain()
        {
            if (fingers == null)
            {
                return false;
            }

            for (int i = 0; i < fingers.Length; i++)
            {
                if (fingers[i] != null && fingers[i].pip != null && fingers[i].dip != null)
                {
                    return true;
                }
            }

            return false;
        }

        internal static float GetSignedTwistAngle(Quaternion rotation, Vector3 axis)
        {
            axis.Normalize();
            Vector3 vector = new Vector3(rotation.x, rotation.y, rotation.z);
            float projected = Vector3.Dot(vector, axis);
            float angle = 2.0f * Mathf.Atan2(projected, rotation.w) * Mathf.Rad2Deg;
            return Mathf.DeltaAngle(0.0f, angle);
        }

        internal static Quaternion ReplaceTwist(Quaternion rotation, Vector3 axis, float twistAngle)
        {
            axis.Normalize();
            Vector3 vector = new Vector3(rotation.x, rotation.y, rotation.z);
            Vector3 projection = Vector3.Project(vector, axis);
            Quaternion currentTwist = Normalize(new Quaternion(
                projection.x,
                projection.y,
                projection.z,
                rotation.w));
            Quaternion swing = rotation * Quaternion.Inverse(currentTwist);
            return Normalize(swing * Quaternion.AngleAxis(twistAngle, axis));
        }

        private static Quaternion Normalize(Quaternion value)
        {
            float magnitude = Mathf.Sqrt(
                value.x * value.x + value.y * value.y +
                value.z * value.z + value.w * value.w);
            if (magnitude < 0.00001f)
            {
                return Quaternion.identity;
            }

            float inverse = 1.0f / magnitude;
            return new Quaternion(
                value.x * inverse,
                value.y * inverse,
                value.z * inverse,
                value.w * inverse);
        }
    }
}

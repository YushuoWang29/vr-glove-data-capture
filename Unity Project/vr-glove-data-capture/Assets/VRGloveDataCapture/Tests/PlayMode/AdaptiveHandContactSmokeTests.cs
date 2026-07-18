using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VRGloveDataCapture.HandInteraction;

namespace VRGloveDataCapture.Tests
{
    public sealed class AdaptiveHandContactSmokeTests
    {
        private const string VendorScenePath =
            "Assets/Hi5_Interaction_SDK/Scenes/Vive/TableScene_Vive.unity";

        [UnityTest]
        public IEnumerator SolverAutoInstallsOnBothVisibleHandsWithoutWritingSourceBones()
        {
            string absoluteScenePath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", VendorScenePath));
            if (!File.Exists(absoluteScenePath))
            {
                Assert.Ignore("Local Hi5 Interaction SDK scene is not installed.");
            }

            AsyncOperation loadOperation = EditorSceneManager.LoadSceneAsyncInPlayMode(
                VendorScenePath,
                new LoadSceneParameters(LoadSceneMode.Single));
            while (!loadOperation.isDone)
            {
                yield return null;
            }

            AdaptiveHandContactController[] controllers = new AdaptiveHandContactController[0];
            for (int frame = 0; frame < 300; frame++)
            {
                controllers = UnityEngine.Object.FindObjectsOfType<AdaptiveHandContactController>();
                if (controllers.Length == 2 && Array.TrueForAll(controllers, controller => controller.IsBound))
                {
                    break;
                }

                yield return null;
            }

            Assert.AreEqual(2, controllers.Length, "Expected one adaptive contact controller per visible hand.");

            FieldInfo sourceBonesField = typeof(AdaptiveHandContactController).GetField(
                "sourceBones",
                BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo lateUpdateMethod = typeof(AdaptiveHandContactController).GetMethod(
                "LateUpdate",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(sourceBonesField);
            Assert.IsNotNull(lateUpdateMethod);

            for (int controllerIndex = 0; controllerIndex < controllers.Length; controllerIndex++)
            {
                AdaptiveHandContactController controller = controllers[controllerIndex];
                Assert.IsTrue(controller.IsBound, controller.Status);
                Assert.AreNotSame(controller.VisualRigRoot, controller.SourceRigRoot,
                    "The visual solver must not be installed on the source/calibration rig.");

                Transform[] sourceBones = (Transform[])sourceBonesField.GetValue(controller);
                Quaternion[] before = new Quaternion[sourceBones.Length];
                for (int boneIndex = 0; boneIndex < sourceBones.Length; boneIndex++)
                {
                    before[boneIndex] = sourceBones[boneIndex] == null
                        ? Quaternion.identity
                        : sourceBones[boneIndex].localRotation;
                }

                GameObject contactObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                contactObject.name = "AdaptiveContactIsolationProbe";
                contactObject.transform.position = controller.HandCenter.position;
                contactObject.transform.localScale = Vector3.one * 0.12f;
                Rigidbody body = contactObject.AddComponent<Rigidbody>();
                body.isKinematic = true;
                Physics.SyncTransforms();

                lateUpdateMethod.Invoke(controller, null);

                for (int boneIndex = 0; boneIndex < sourceBones.Length; boneIndex++)
                {
                    if (sourceBones[boneIndex] == null)
                    {
                        continue;
                    }

                    Assert.Less(
                        Quaternion.Angle(before[boneIndex], sourceBones[boneIndex].localRotation),
                        0.0001f,
                        "Visual contact wrote into Hi5 source bone " + boneIndex + ".");
                }

                UnityEngine.Object.Destroy(contactObject);
            }

            VerifyVisibleFingerStopsAtProbeWithoutChangingGlovePose(
                controllers[0],
                sourceBonesField,
                lateUpdateMethod);
        }

        private static void VerifyVisibleFingerStopsAtProbeWithoutChangingGlovePose(
            AdaptiveHandContactController controller,
            FieldInfo sourceBonesField,
            MethodInfo lateUpdateMethod)
        {
            const int ProbeLayer = 30;
            FieldInfo contactLayersField = typeof(AdaptiveHandContactController).GetField(
                "contactLayers",
                BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo vendorHandField = typeof(AdaptiveHandContactController).GetField(
                "vendorVisibleHand",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(contactLayersField);
            Assert.IsNotNull(vendorHandField);

            LayerMask probeMask = 1 << ProbeLayer;
            contactLayersField.SetValue(controller, probeMask);
            lateUpdateMethod.Invoke(controller, null);

            Component vendorHand = (Component)vendorHandField.GetValue(controller);
            FieldInfo visibleIndexField = vendorHand.GetType().GetField(
                "m_IndexFingerTransforms",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            IList visibleIndex = (IList)visibleIndexField.GetValue(vendorHand);
            Transform[] sourceBones = (Transform[])sourceBonesField.GetValue(controller);

            Transform visibleMcp = (Transform)visibleIndex[0];
            Transform visiblePip = (Transform)visibleIndex[1];
            Quaternion originalSourceRotation = sourceBones[6].localRotation;
            Quaternion originalVisibleRotation = visibleMcp.localRotation;
            Vector3 baselineMidpoint = Vector3.Lerp(visibleMcp.position, visiblePip.position, 0.5f);

            Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
            Quaternion targetRotation = originalSourceRotation;
            Vector3 targetMidpoint = baselineMidpoint;
            float greatestDisplacement = 0.0f;
            for (int axisIndex = 0; axisIndex < axes.Length; axisIndex++)
            {
                Quaternion candidateRotation = originalSourceRotation *
                                               Quaternion.AngleAxis(70.0f, axes[axisIndex]);
                visibleMcp.localRotation = candidateRotation;
                Vector3 candidateMidpoint = Vector3.Lerp(
                    visibleMcp.position,
                    visiblePip.position,
                    0.5f);
                float displacement = Vector3.Distance(baselineMidpoint, candidateMidpoint);
                if (displacement > greatestDisplacement)
                {
                    greatestDisplacement = displacement;
                    targetRotation = candidateRotation;
                    targetMidpoint = candidateMidpoint;
                }
            }

            Assert.Greater(greatestDisplacement, 0.008f,
                "The test rig did not provide enough finger travel for a contact probe.");

            sourceBones[6].localRotation = targetRotation;
            visibleMcp.localRotation = originalVisibleRotation;

            GameObject probe = new GameObject("IndexFingerSurfaceProbe");
            probe.layer = ProbeLayer;
            probe.transform.position = targetMidpoint;
            SphereCollider probeCollider = probe.AddComponent<SphereCollider>();
            probeCollider.radius = 0.0015f;
            Rigidbody probeBody = probe.AddComponent<Rigidbody>();
            probeBody.isKinematic = true;
            Physics.SyncTransforms();

            lateUpdateMethod.Invoke(controller, null);

            Assert.Less(
                Quaternion.Angle(targetRotation, sourceBones[6].localRotation),
                0.0001f,
                "The contact solver changed the glove/source joint target.");
            Assert.Greater(
                Quaternion.Angle(targetRotation, visibleMcp.localRotation),
                0.05f,
                "The visible joint was not clamped before entering the probe collider.");

            sourceBones[6].localRotation = originalSourceRotation;
            visibleMcp.localRotation = originalVisibleRotation;
            UnityEngine.Object.Destroy(probe);
        }
    }
}

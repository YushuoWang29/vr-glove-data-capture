using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VRGloveDataCapture.MixedReality;
using VRGloveDataCapture.UserInterface;

namespace VRGloveDataCapture.Tests
{
    public sealed class GazeControlPanelSmokeTests
    {
        private const string VendorScenePath =
            "Assets/Hi5_Interaction_SDK/Scenes/Vive/TableScene_Vive.unity";

        [UnityTest]
        public IEnumerator PanelPreservesCalibrationAndRoutesVendorGazeToProjectControls()
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

            GazeFunctionPanelController controller = null;
            GameObject panelRoot = null;
            FieldInfo panelRootField = typeof(GazeFunctionPanelController).GetField(
                "panelRoot",
                BindingFlags.NonPublic | BindingFlags.Instance);
            for (int frame = 0; frame < 300; frame++)
            {
                GazeFunctionPanelController[] controllers =
                    Resources.FindObjectsOfTypeAll<GazeFunctionPanelController>();
                controller = null;
                panelRoot = null;
                for (int index = 0; index < controllers.Length; index++)
                {
                    GameObject candidatePanel =
                        panelRootField.GetValue(controllers[index]) as GameObject;
                    if (candidatePanel != null)
                    {
                        controller = controllers[index];
                        panelRoot = candidatePanel;
                        break;
                    }
                }
                if (controller != null && panelRoot != null)
                {
                    break;
                }
                yield return null;
            }

            Assert.IsNotNull(controller, "The scene-independent gaze panel installer did not start.");
            Assert.IsNotNull(panelRoot, "The gaze panel did not bind to the vendor main menu.");
            Assert.IsNotNull(FindSceneObject("Calibration"), "The vendor calibration state was removed.");
            Assert.IsNotNull(FindSceneObject("Btn_Calibrate"), "The original gaze calibration entry was removed.");

            FieldInfo mainRootField = typeof(GazeFunctionPanelController).GetField(
                "mainStateRoot",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Transform mainRoot = mainRootField.GetValue(controller) as Transform;
            Assert.IsNotNull(mainRoot, "The project did not bind to the vendor Main menu state.");
            Transform reconnectEntry = mainRoot.Find("Btn_Reconnect");
            Transform interactionEntry = mainRoot.Find("Btn_Help");
            Transform closeEntry = mainRoot.Find("Btn_Close");
            Assert.IsNotNull(reconnectEntry, "The vendor Reconnect entry is missing.");
            Assert.IsNotNull(interactionEntry, "The vendor Interaction entry is missing.");
            Assert.IsNotNull(closeEntry, "The vendor Close entry is missing.");
            Assert.IsTrue(interactionEntry.gameObject.activeSelf,
                "The adapted Interaction entry is not available before calibration.");
            Assert.IsFalse(closeEntry.gameObject.activeSelf,
                "The old Close entry was not replaced by Interaction.");
            Assert.That(interactionEntry.localPosition.x,
                Is.EqualTo(closeEntry.localPosition.x).Within(0.001f),
                "Interaction was not moved into the right-hand menu slot.");
            BoxCollider reconnectEntryCollider = reconnectEntry.GetComponent<BoxCollider>();
            BoxCollider interactionEntryCollider = interactionEntry.GetComponent<BoxCollider>();
            Assert.IsNotNull(reconnectEntryCollider, "Reconnect has no gaze collider.");
            Assert.IsNotNull(interactionEntryCollider, "Interaction has no gaze collider.");
            float entryCenterDistance = Mathf.Abs(
                interactionEntry.localPosition.x - reconnectEntry.localPosition.x);
            float requiredSeparation =
                (reconnectEntryCollider.size.x + interactionEntryCollider.size.x) * 0.5f;
            Assert.GreaterOrEqual(entryCenterDistance, requiredSeparation,
                "Reconnect and Interaction gaze colliders overlap.");
            Assert.AreEqual("Exit", ReadEnumField(interactionEntry.gameObject, "EnterState"),
                "Gazing Interaction still invokes ReConnect instead of entering interaction mode.");

            // Hardware-free test: drive the real production completion detector
            // through the vendor manager field instead of directly forcing UI state.
            Type managerType = FindType("HI5.HI5_Manager_Thread");
            Assert.IsNotNull(managerType, "The namespaced Hi5 calibration manager type is unavailable.");
            MethodInfo managerInstance = managerType.GetMethod(
                "Instance",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            object manager = managerInstance == null ? null : managerInstance.Invoke(null, null);
            Assert.IsNotNull(manager, "The Hi5 calibration manager instance is unavailable.");
            FieldInfo completionField = managerType.GetField(
                "IsCalibrationComplete",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(completionField, "The Hi5 calibration completion field is unavailable.");
            bool originalCompletion = (bool)completionField.GetValue(manager);
            completionField.SetValue(manager, true);
            MethodInfo updateCalibration = typeof(GazeFunctionPanelController).GetMethod(
                "UpdateCalibrationTransition",
                BindingFlags.NonPublic | BindingFlags.Instance);
            updateCalibration.Invoke(controller, null);
            yield return null;
            Assert.IsTrue(controller.IsFeaturePanelVisible,
                "The function panel did not unlock after real Hi5 calibration completion.");

            for (int frame = 0; frame < 12; frame++)
            {
                yield return null;
            }

            Transform background = panelRoot.transform.Find("Background");
            Assert.IsNotNull(background, "The light control-panel background was not created.");
            MeshRenderer backgroundRenderer = background.GetComponent<MeshRenderer>();
            Assert.IsNotNull(backgroundRenderer, "The control-panel background has no renderer.");
            Color backgroundColor = backgroundRenderer.sharedMaterial.color;
            Assert.Greater(ColorLuminance(backgroundColor), 0.85f,
                "The redesigned panel background is not gray-white.");
            MeshFilter backgroundMesh = background.GetComponent<MeshFilter>();
            Assert.IsNotNull(backgroundMesh, "The panel background has no rounded mesh.");
            Assert.Greater(backgroundMesh.sharedMesh.vertexCount, 8,
                "The panel background is still a square four-corner quad.");

            GazeDwellButton[] gazeButtons = panelRoot.GetComponentsInChildren<GazeDwellButton>(true);
            Assert.AreEqual(6, gazeButtons.Length, "Expected six gaze-operated function controls.");
            HashSet<string> expectedIds = new HashSet<string>
            {
                "passthrough", "video", "trial", "marker", "reset", "recalibrate"
            };
            Type interactiveItemType = FindType("HI5.VRInteraction.VRInteractiveItem");
            Assert.IsNotNull(interactiveItemType, "The vendor VRInteractiveItem type is unavailable.");
            for (int index = 0; index < gazeButtons.Length; index++)
            {
                GazeDwellButton button = gazeButtons[index];
                Assert.IsTrue(expectedIds.Remove(button.ActionId), "Unexpected or duplicate action " + button.ActionId);
                Assert.IsNotNull(button.GetComponent<BoxCollider>(), button.ActionId + " has no gaze collider.");
                Assert.IsNotNull(button.GetComponent(interactiveItemType),
                    button.ActionId + " is not connected to the vendor eye raycaster.");

                Transform surfaceTransform = button.transform.Find("Surface");
                Assert.IsNotNull(surfaceTransform, button.ActionId + " has no visible surface.");
                MeshRenderer surface = surfaceTransform.GetComponent<MeshRenderer>();
                MeshFilter mesh = surfaceTransform.GetComponent<MeshFilter>();
                Assert.IsNotNull(surface, button.ActionId + " has no surface renderer.");
                Assert.IsNotNull(mesh, button.ActionId + " has no rounded surface mesh.");
                Assert.Greater(mesh.sharedMesh.vertexCount, 8,
                    button.ActionId + " still uses a sharp-cornered quad.");

                TextMesh[] labels = button.GetComponentsInChildren<TextMesh>(true);
                Assert.AreEqual(2, labels.Length,
                    button.ActionId + " should separate its title and compact status line.");
                for (int labelIndex = 0; labelIndex < labels.Length; labelIndex++)
                {
                    TextMesh label = labels[labelIndex];
                    Assert.Less(ColorLuminance(label.color), 0.55f,
                        button.ActionId + " text is not gray-black.");
                    Renderer labelRenderer = label.GetComponent<Renderer>();
                    Assert.LessOrEqual(
                        labelRenderer.bounds.size.x,
                        surface.bounds.size.x * 0.9f + 0.01f,
                        button.ActionId + " text overflows the rounded button.");
                }
            }
            Assert.AreEqual(0, expectedIds.Count, "One or more function controls were not created.");

            SteamVrPassthroughEffect passthrough = null;
            for (int frame = 0; frame < 180; frame++)
            {
                passthrough = UnityEngine.Object.FindObjectOfType<SteamVrPassthroughEffect>();
                if (passthrough != null)
                {
                    break;
                }
                yield return null;
            }
            Assert.IsNotNull(passthrough, "The passthrough installer did not bind to the stereo camera.");
            passthrough.SetPassthroughEnabled(false);

            FieldInfo materialField = typeof(SteamVrPassthroughEffect).GetField(
                "backgroundMaterial",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Material backgroundMaterial = materialField.GetValue(passthrough) as Material;
            Assert.IsNotNull(backgroundMaterial, "The passthrough background material was not created.");
            Assert.AreEqual(
                "Hidden/VRGloveDataCapture/PassthroughBackground",
                backgroundMaterial.shader.name,
                "Passthrough is still using the one-eye post-processing shader.");

            Texture2D syntheticStereoFrame = new Texture2D(16, 32, TextureFormat.RGBA32, false);
            MethodInfo detectFrameLayout = typeof(SteamVrPassthroughEffect).GetMethod(
                "DetectFrameLayout",
                BindingFlags.NonPublic | BindingFlags.Instance);
            detectFrameLayout.Invoke(passthrough, new object[] { syntheticStereoFrame });
            Assert.AreEqual(2, passthrough.DetectedCameraCount,
                "A top/bottom stereo camera frame was not detected as two cameras.");
            Assert.AreEqual("VerticalStereo", passthrough.DetectedFrameLayout,
                "A top/bottom OpenVR frame will not be split per eye.");
            UnityEngine.Object.Destroy(syntheticStereoFrame);

            Vector2 negativeVScale = new Vector2(1.0f, -1.0f);
            Vector2 leftPackedBottom = SteamVrPassthroughEffect.CalculateStereoLayoutUv(
                Vector2.zero,
                0,
                SteamVrPassthroughEffect.StereoFrameLayout.VerticalStereo,
                false,
                SteamVrPassthroughEffect.PerEyeOrientation.Normal,
                negativeVScale);
            Vector2 rightPackedBottom = SteamVrPassthroughEffect.CalculateStereoLayoutUv(
                Vector2.zero,
                1,
                SteamVrPassthroughEffect.StereoFrameLayout.VerticalStereo,
                false,
                SteamVrPassthroughEffect.PerEyeOrientation.Normal,
                negativeVScale);
            Assert.That(leftPackedBottom.y, Is.EqualTo(0.5f).Within(0.0001f),
                "The left eye does not select OpenVR's top/left camera after the Valve V flip.");
            Assert.That(rightPackedBottom.y, Is.EqualTo(0.0f).Within(0.0001f),
                "The right eye does not select OpenVR's bottom/right camera after the Valve V flip.");
            Vector2 rotatedLeft = SteamVrPassthroughEffect.CalculateStereoLayoutUv(
                new Vector2(0.2f, 0.3f),
                0,
                SteamVrPassthroughEffect.StereoFrameLayout.VerticalStereo,
                false,
                SteamVrPassthroughEffect.PerEyeOrientation.Rotate180,
                negativeVScale);
            Assert.GreaterOrEqual(rotatedLeft.y, 0.5f,
                "Per-eye rotation exchanged the two camera regions.");
            Assert.LessOrEqual(rotatedLeft.y, 1.0f,
                "Per-eye rotation escaped the left camera region.");
            Assert.IsTrue(
                SteamVrPassthroughEffect.RequiresInstancedStereoDraw(
                    UnityEngine.XR.XRSettings.StereoRenderingMode.SinglePassInstanced),
                "Single Pass Instanced would still submit only one background instance.");
            Assert.IsFalse(
                SteamVrPassthroughEffect.RequiresInstancedStereoDraw(
                    UnityEngine.XR.XRSettings.StereoRenderingMode.MultiPass),
                "Multi Pass must not receive a doubled background draw.");

            Assert.IsNotNull(
                FindType("VRGloveDataCapture.EditorTools.Hi5EditorPlayModeShutdownGuard"),
                "The Hi5 safe Play Mode shutdown guard is not loaded.");

            GazeDwellButton passthroughButton = Array.Find(
                gazeButtons,
                candidate => candidate.ActionId == "passthrough");
            Component interactiveItem = passthroughButton.GetComponent(interactiveItemType);
            interactiveItemType.GetMethod("Over").Invoke(interactiveItem, null);
            MethodInfo complete = typeof(GazeDwellButton).GetMethod(
                "HandleSelectionComplete",
                BindingFlags.NonPublic | BindingFlags.Instance);
            complete.Invoke(passthroughButton, null);
            Assert.IsTrue(passthrough.IsPassthroughRequested,
                "A completed vendor gaze did not invoke the passthrough command.");
            interactiveItemType.GetMethod("Out").Invoke(interactiveItem, null);
            passthrough.SetPassthroughEnabled(false);

            MethodInfo recalibrate = typeof(GazeFunctionPanelController).GetMethod(
                "StartRecalibration",
                BindingFlags.NonPublic | BindingFlags.Instance);
            recalibrate.Invoke(controller, null);
            yield return null;
            Assert.IsFalse(panelRoot.activeInHierarchy,
                "The function panel remained visible over the original calibration flow.");
            Assert.IsTrue(FindSceneObject("Calibration").activeInHierarchy,
                "Recalibration did not restore the original vendor calibration panel.");
            Assert.IsTrue(interactionEntry.gameObject.activeSelf,
                "Interaction was not restored when the feature panel was hidden.");
            Assert.IsFalse(closeEntry.gameObject.activeSelf,
                "The replaced Close entry was incorrectly restored.");
            completionField.SetValue(manager, originalCompletion);
        }

        private static string ReadEnumField(GameObject target, string fieldName)
        {
            MonoBehaviour[] behaviours = target.GetComponents<MonoBehaviour>();
            for (int index = 0; index < behaviours.Length; index++)
            {
                MonoBehaviour behaviour = behaviours[index];
                if (behaviour == null)
                {
                    continue;
                }

                FieldInfo field = behaviour.GetType().GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null && field.FieldType.IsEnum)
                {
                    object value = field.GetValue(behaviour);
                    return value == null ? string.Empty : value.ToString();
                }
            }

            return string.Empty;
        }

        private static GameObject FindSceneObject(string objectName)
        {
            Transform[] transforms = Resources.FindObjectsOfTypeAll<Transform>();
            for (int index = 0; index < transforms.Length; index++)
            {
                Transform candidate = transforms[index];
                if (candidate.gameObject.scene.IsValid() && candidate.name == objectName)
                {
                    return candidate.gameObject;
                }
            }
            return null;
        }

        private static Type FindType(string fullName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int index = 0; index < assemblies.Length; index++)
            {
                Type type = assemblies[index].GetType(fullName, false);
                if (type != null)
                {
                    return type;
                }
            }
            return null;
        }

        private static float ColorLuminance(Color color)
        {
            return color.r * 0.2126f + color.g * 0.7152f + color.b * 0.0722f;
        }
    }
}

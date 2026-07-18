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
            completionField.SetValue(manager, originalCompletion);
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
    }
}

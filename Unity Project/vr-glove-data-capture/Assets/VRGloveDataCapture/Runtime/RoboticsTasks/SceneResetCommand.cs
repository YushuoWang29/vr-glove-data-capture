namespace VRGloveDataCapture.RoboticsTasks
{
    /// <summary>
    /// Public command surface for the Hi5 message-based full-scene reset.
    /// Keyboard, gaze UI and task controllers all use this path.
    /// </summary>
    public static class SceneResetCommand
    {
        public static bool TryResetAll()
        {
            return Hi5RuntimeBridge.DispatchReset();
        }
    }
}

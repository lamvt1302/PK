using UnityEngine;

namespace PK
{
    public static class PkRuntimeEntry
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Object.FindAnyObjectByType<PkBootstrap>() == null)
            {
                var bootstrapGo = new GameObject("PKBootstrap");
                bootstrapGo.AddComponent<PkBootstrap>();
                Object.DontDestroyOnLoad(bootstrapGo);
            }

            // TODO: Rebuild HUD as Unity prefabs (Phase 2)
            // Original: if (Object.FindAnyObjectByType<PkRuntimeHud>() == null)
            // Original: {
            // Original:     var hudGo = new GameObject("PKRuntimeHud");
            // Original:     hudGo.AddComponent<PkRuntimeHud>();
            // Original:     Object.DontDestroyOnLoad(hudGo);
            // Original: }
        }
    }
}
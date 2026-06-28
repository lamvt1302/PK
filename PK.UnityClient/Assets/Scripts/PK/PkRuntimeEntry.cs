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

            // Phase 2: Auto-create HUD if not present in scene
            if (Object.FindAnyObjectByType<PK.UI.PkHudManager>() == null)
            {
                var hudGo = new GameObject("PK_HUD");
                var hud = hudGo.AddComponent<PK.UI.PkHudManager>();
                // Auto-find bootstrap reference
                var bootstrap = Object.FindAnyObjectByType<PkBootstrap>();
                if (bootstrap != null)
                {
                    // Use reflection to set Bootstrap field (since HUD panels need manual prefab setup)
                    var field = typeof(PK.UI.PkHudManager).GetField("Bootstrap");
                    if (field != null) field.SetValue(hud, bootstrap);
                }
                Object.DontDestroyOnLoad(hudGo);
                Debug.Log("[PkRuntimeEntry] Auto-created PkHudManager (panels need manual prefab setup in Editor)");
            }
        }
    }
}
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PK.Editor
{
    /// <summary>
    /// Idempotent editor tool that creates / refreshes the PKDemo scene so the
    /// game can be played in Play mode without manual setup.
    /// Menu: PK / Setup Demo Scene
    ///
    /// Signed: agent-15-unity-lead
    /// </summary>
    public static class PkSceneSetup
    {
        private const string ScenePath = "Assets/Scenes/PKDemo.unity";
        private const string ApiConfigPath = "Assets/Scripts/PK/ApiConfig.asset";

        [MenuItem("PK/Setup Demo Scene")]
        public static void SetupScene()
        {
            EnsureApiConfigAsset();

            var scene = OpenOrCreateScene();

            EnsureMainCamera();
            EnsureDirectionalLight();
            EnsurePkBootstrap();
            EnsurePkHudManager();

            EditorSceneManager.SaveScene(scene, ScenePath, true);
            AddSceneToBuildSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[PkSceneSetup] PKDemo scene ready at " + ScenePath);
        }

        // ---- API config asset -------------------------------------------------

        private static ApiConfig EnsureApiConfigAsset()
        {
            var existing = AssetDatabase.LoadAssetAtPath<ApiConfig>(ApiConfigPath);
            if (existing != null)
            {
                return existing;
            }

            var config = ScriptableObject.CreateInstance<ApiConfig>();
            SetServerUrl(config, "http://localhost:8080");

            EnsureDirectoryForAsset(ApiConfigPath);
            AssetDatabase.CreateAsset(config, ApiConfigPath);
            AssetDatabase.SaveAssets();
            return config;
        }

        private static void SetServerUrl(ApiConfig config, string url)
        {
            // The ApiConfig shipped on main exposes `BaseUrl`. Keep a fallback so
            // the editor tool keeps working even if the field is renamed later.
            var baseUrlField = typeof(ApiConfig).GetField("BaseUrl");
            if (baseUrlField != null)
            {
                baseUrlField.SetValue(config, url);
                return;
            }

            var serverUrlField = typeof(ApiConfig).GetField("ServerUrl");
            if (serverUrlField != null)
            {
                serverUrlField.SetValue(config, url);
            }
        }

        // ---- Scene ------------------------------------------------------------

        private static Scene OpenOrCreateScene()
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i).name == "PKDemo")
                {
                    return SceneManager.GetSceneAt(i);
                }
            }

            if (File.Exists(ScenePath))
            {
                return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            var newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EnsureDirectoryForAsset(ScenePath);
            return newScene;
        }

        // ---- GameObjects ------------------------------------------------------

        private static void EnsureMainCamera()
        {
            var cam = UnityEngine.Object.FindAnyObjectByType<Camera>();
            if (cam == null)
            {
                var go = new GameObject("Main Camera");
                cam = go.AddComponent<Camera>();
            }

            cam.gameObject.name = "Main Camera";
            cam.gameObject.tag = "MainCamera";
            cam.transform.position = new Vector3(0f, 1f, -10f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.08f, 0.18f, 1f); // dark blue
        }

        private static void EnsureDirectionalLight()
        {
            var existing = GameObject.Find("Directional Light");
            if (existing != null && existing.GetComponent<Light>() != null)
            {
                return;
            }

            var go = new GameObject("Directional Light");
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            go.transform.position = new Vector3(0f, 3f, 0f);
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        private static void EnsurePkBootstrap()
        {
            var go = GameObject.Find("PK_Bootstrap");
            if (go == null)
            {
                go = new GameObject("PK_Bootstrap");
            }

            if (go.GetComponent<PkBootstrap>() == null)
            {
                go.AddComponent<PkBootstrap>();
            }

            var config = AssetDatabase.LoadAssetAtPath<ApiConfig>(ApiConfigPath);
            if (config != null)
            {
                var bootstrap = go.GetComponent<PkBootstrap>();
                // PkBootstrap.Config is public; assign via serialized field.
                var configField = typeof(PkBootstrap).GetField("Config");
                if (configField != null)
                {
                    configField.SetValue(bootstrap, config);
                }
            }
        }

        private static void EnsurePkHudManager()
        {
            var go = GameObject.Find("PK_HUD");
            if (go == null)
            {
                go = new GameObject("PK_HUD");
            }

            if (go.GetComponent<PK.UI.PkHudManager>() == null)
            {
                var hud = go.AddComponent<PK.UI.PkHudManager>();
                var bootstrap = Object.FindObjectOfType<PkBootstrap>();
                if (bootstrap != null)
                {
                    var field = typeof(PK.UI.PkHudManager).GetField("Bootstrap");
                    if (field != null) field.SetValue(hud, bootstrap);
                }
            }
        }

        // ---- Build settings ---------------------------------------------------

        private static void AddSceneToBuildSettings()
        {
            var existing = EditorBuildSettings.scenes;
            foreach (var s in existing)
            {
                if (s.path == ScenePath)
                {
                    if (!s.enabled)
                    {
                        s.enabled = true;
                        EditorBuildSettings.scenes = existing;
                    }
                    return;
                }
            }

            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(existing)
            {
                new EditorBuildSettingsScene(ScenePath, true)
            };
            EditorBuildSettings.scenes = list.ToArray();
        }

        // ---- Helpers ----------------------------------------------------------

        private static void EnsureDirectoryForAsset(string assetPath)
        {
            var dir = Path.GetDirectoryName(assetPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }
    }
}
#endif
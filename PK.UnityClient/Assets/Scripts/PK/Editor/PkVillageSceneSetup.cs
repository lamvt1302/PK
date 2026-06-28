#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PK.Editor
{
    /// <summary>
    /// Editor script tự động setup 3D isometric village scene cho Pirate Kings.
    /// Tham chiếu Coin Master: village 3D isometric, camera 45°, 5 buildings/island,
    /// slot machine overlay 2D phía trước.
    ///
    /// Menu: "PK / Setup Village Scene" — tạo scene Main.unity với:
    ///   - Isometric camera (orthographic, 45°)
    ///   - Directional light có shadow soft
    ///   - IslandBase (Cylinder) làm đảo
    ///   - 5 Building_Slot_* (Cube) gắn PkBuildingSlot
    ///   - SlotMachinePlatform (Cube) phía trước camera
    ///   - Canvas (Screen Space Overlay) + HUD rỗng gắn PkHudManager
    ///
    /// Lưu scene vào Assets/Scenes/Main.unity và add vào Build Settings.
    /// Idempotent: chạy lại sẽ refresh các object theo tên.
    /// </summary>
    public static class PkVillageSceneSetup
    {
        private const string ScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("PK/Setup Village Scene")]
        public static void SetupScene()
        {
            // 1. Tạo / mở scene Main.
            var scene = OpenOrCreateScene();

            // 2. Isometric camera.
            EnsureIsometricCamera();

            // 3. Directional light.
            EnsureDirectionalLight();

            // 4. Island base.
            EnsureIslandBase();

            // 5. 5 building slots.
            EnsureBuildingSlots();

            // 6. Slot machine platform.
            EnsureSlotMachinePlatform();

            // 7. Canvas + HUD.
            EnsureCanvasAndHud();

            // 8. Save scene.
            EnsureDirectoryForAsset(ScenePath);
            EditorSceneManager.SaveScene(scene, ScenePath, true);
            AddSceneToBuildSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[PkVillageSceneSetup] Main.unity ready at " + ScenePath);
        }

        // ---- Scene ------------------------------------------------------------

        private static Scene OpenOrCreateScene()
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i).name == "Main")
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

        // ---- 2. Isometric Camera ----------------------------------------------

        private static void EnsureIsometricCamera()
        {
            var cam = UnityEngine.Object.FindAnyObjectByType<Camera>();
            if (cam == null)
            {
                var go = new GameObject("Main Camera");
                cam = go.AddComponent<Camera>();
            }

            cam.gameObject.name = "Main Camera";
            cam.gameObject.tag = "MainCamera";

            // Isometric perspective: nhìn xuống 45°, orthographic.
            cam.transform.position = new Vector3(0f, 10f, -8f);
            cam.transform.rotation = Quaternion.Euler(45f, 0f, 0f);
            cam.orthographic = true;
            cam.orthographicSize = 6f;

            // Background sky blue (0.5, 0.7, 1.0).
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.5f, 0.7f, 1.0f, 1f);
        }

        // ---- 3. Directional Light ---------------------------------------------

        private static void EnsureDirectionalLight()
        {
            var go = GameObject.Find("Directional Light");
            Light light;
            if (go == null || (light = go.GetComponent<Light>()) == null)
            {
                if (go == null) go = new GameObject("Directional Light");
                light = go.AddComponent<Light>();
                light.type = LightType.Directional;
            }

            // Rotation (50, -30, 0), intensity 1.2, shadow Soft.
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;
        }

        // ---- 4. Island Base ---------------------------------------------------

        private static void EnsureIslandBase()
        {
            var go = GameObject.Find("IslandBase");
            if (go == null)
            {
                // Dùng Cylinder làm đảo (primitive 3D).
                var proto = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                proto.name = "IslandBase";
                go = proto;
            }

            // position (0,0,0), scale (4,0.3,4), rotation (0,0,0).
            go.transform.position = new Vector3(0f, 0f, 0f);
            go.transform.localScale = new Vector3(4f, 0.3f, 4f);
            go.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
            go.tag = "Untagged";

            // Material xanh lá (0.4, 0.6, 0.3).
            ApplyColorMaterial(go, new Color(0.4f, 0.6f, 0.3f, 1f), "IslandBaseMat");
        }

        // ---- 5. 5 Building Slots ----------------------------------------------

        private static readonly Vector3[] SlotPositions =
        {
            new Vector3(-2.5f, 0.5f, 1.5f),
            new Vector3(-1.2f, 0.5f, 2.5f),
            new Vector3(0f,   0.5f, 2.8f),
            new Vector3(1.2f,  0.5f, 2.5f),
            new Vector3(2.5f,  0.5f, 1.5f)
        };

        private static readonly string[] SlotNames =
        {
            "Building_Slot_0",
            "Building_Slot_1",
            "Building_Slot_2",
            "Building_Slot_3",
            "Building_Slot_4"
        };

        private static void EnsureBuildingSlots()
        {
            var brown = new Color(0.6f, 0.4f, 0.2f, 1f);

            for (var i = 0; i < SlotPositions.Length; i++)
            {
                var name = SlotNames[i];
                var pos = SlotPositions[i];

                var go = GameObject.Find(name);
                if (go == null)
                {
                    var proto = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    proto.name = name;
                    go = proto;
                }

                go.transform.position = pos;
                go.transform.localScale = new Vector3(0.8f, 1f, 0.8f);
                go.transform.rotation = Quaternion.Euler(0f, 0f, 0f);

                ApplyColorMaterial(go, brown, "BuildingSlotMat_" + i);

                // Gán PkBuildingSlot (nếu chưa có) + set SlotIndex.
                var slot = go.GetComponent<PkBuildingSlot>();
                if (slot == null)
                {
                    slot = go.AddComponent<PkBuildingSlot>();
                }
                slot.SlotIndex = i;
            }
        }

        // ---- 6. Slot Machine Platform ----------------------------------------

        private static void EnsureSlotMachinePlatform()
        {
            var go = GameObject.Find("SlotMachinePlatform");
            if (go == null)
            {
                var proto = GameObject.CreatePrimitive(PrimitiveType.Cube);
                proto.name = "SlotMachinePlatform";
                go = proto;
            }

            // position (0, 0.5, -1), scale (2, 1, 0.5).
            go.transform.position = new Vector3(0f, 0.5f, -1f);
            go.transform.localScale = new Vector3(2f, 1f, 0.5f);
            go.transform.rotation = Quaternion.Euler(0f, 0f, 0f);

            // Material vàng (0.8, 0.7, 0.2).
            ApplyColorMaterial(go, new Color(0.8f, 0.7f, 0.2f, 1f), "SlotMachinePlatformMat");
        }

        // ---- 7. Canvas + HUD --------------------------------------------------

        private static void EnsureCanvasAndHud()
        {
            // Canvas (Screen Space - Overlay).
            var canvasGo = GameObject.Find("Canvas");
            Canvas canvas;
            if (canvasGo == null || (canvas = canvasGo.GetComponent<Canvas>()) == null)
            {
                if (canvasGo == null) canvasGo = new GameObject("Canvas");
                canvas = canvasGo.AddComponent<Canvas>();
                canvasGo.AddComponent<CanvasScaler>();
                canvasGo.AddComponent<GraphicRaycaster>();
            }

            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // CanvasScaler: reference 1080x1920, match 0.5.
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = canvasGo.AddComponent<CanvasScaler>();
            }
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            // HUD empty GameObject gắn PkHudManager.
            var hudGo = GameObject.Find("HUD");
            if (hudGo == null)
            {
                hudGo = new GameObject("HUD");
            }
            hudGo.transform.SetParent(canvasGo.transform, false);

            // Gán PkHudManager (nếu chưa có). Dùng reflection-agnostic AddComponent
            // vì PkHudManager nằm trong namespace PK.UI.
            var hudType = System.Type.GetType("PK.UI.PkHudManager, Assembly-CSharp");
            if (hudType != null && hudGo.GetComponent(hudType) == null)
            {
                hudGo.AddComponent(hudType);
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

        private static void ApplyColorMaterial(GameObject go, Color color, string matName)
        {
            var path = "Assets/Scenes/Materials/" + matName + ".mat";
            EnsureDirectoryForAsset(path);

            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit")
                                   ?? Shader.Find("Standard")
                                   ?? Shader.Find("Mobile/Diffuse"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.color = color;

            var r = go.GetComponent<Renderer>();
            if (r == null) r = go.GetComponent<MeshRenderer>();
            if (r != null) r.sharedMaterial = mat;
        }
    }
}
#endif
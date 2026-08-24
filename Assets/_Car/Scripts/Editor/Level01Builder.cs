using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DriveMad.EditorTools
{
    public static class Level01Builder
    {
        const float CarScale = 0.22f;
        const float WheelRadius = 1.5465f * CarScale;
        const float FrontZ = 2.85f * CarScale;
        const float RearZ = -3.95f * CarScale;
        const float AxleHalf = 2.05f * CarScale;
        const int GroundLayer = 8;
        const int VehicleLayer = 9;

        [MenuItem("Drive Mad/Rebuild Level 01")]
        public static string Rebuild()
        {
            EnsureLayers();
            Material hillMat = EnsureMaterial("HillDirt", new Color(0.76f, 0.58f, 0.34f));
            Material markerMat = EnsureMaterial("Marker", new Color(0.92f, 0.92f, 0.92f));
            PhysicsMaterial grip = EnsurePhysicsMaterial();

            ClearRoots();

            SmoothHill hill = CreateHill(hillMat, grip);
            GameObject gameRoot = new GameObject("GameSession");
            LevelSession session = gameRoot.AddComponent<LevelSession>();
            DriveInput input = gameRoot.AddComponent<DriveInput>();

            DriveMadCarController car = CreateCar();
            CreateMarkers(hill, markerMat);
            CreateFinish(hill, session);
            CreateKillZone(session);
            Camera cam = CreateCamera(car.Chassis);
            CreateLight();
            CreateUi(input, session);

            input.SetCar(car);
            input.SetSession(session);
            session.SetCar(car);

            SerializedObject sessionSo = new SerializedObject(session);
            sessionSo.FindProperty("car").objectReferenceValue = car;
            sessionSo.ApplyModifiedProperties();

            SerializedObject inputSo = new SerializedObject(input);
            inputSo.FindProperty("car").objectReferenceValue = car;
            inputSo.FindProperty("session").objectReferenceValue = session;
            inputSo.ApplyModifiedProperties();

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            ConfigureBuildSettings();
            return "Level 01 rebuilt. Car=" + car.name + " Hill=" + hill.name + " Camera=" + cam.name;
        }

        static void EnsureLayers()
        {
            SerializedObject tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            layers.GetArrayElementAtIndex(GroundLayer).stringValue = "Ground";
            layers.GetArrayElementAtIndex(VehicleLayer).stringValue = "Vehicle";
            tagManager.ApplyModifiedProperties();
        }

        static Material EnsureMaterial(string name, Color color)
        {
            string path = "Assets/_Car/Materials/" + name + ".mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    shader = Shader.Find("Sprites/Default");
                }

                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color"))
            {
                mat.SetColor("_Color", color);
            }

            EditorUtility.SetDirty(mat);
            return mat;
        }

        static PhysicsMaterial EnsurePhysicsMaterial()
        {
            string path = "Assets/_Car/Materials/HillGrip.asset";
            PhysicsMaterial mat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (mat == null)
            {
                mat = new PhysicsMaterial("HillGrip");
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.staticFriction = 0.95f;
            mat.dynamicFriction = 0.85f;
            mat.bounciness = 0f;
            mat.frictionCombine = PhysicsMaterialCombine.Maximum;
            mat.bounceCombine = PhysicsMaterialCombine.Minimum;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void ClearRoots()
        {
            GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                Object.DestroyImmediate(roots[i]);
            }
        }

        static SmoothHill CreateHill(Material hillMat, PhysicsMaterial grip)
        {
            GameObject go = new GameObject("Track_Hill");
            go.layer = GroundLayer;
            MeshFilter filter = go.AddComponent<MeshFilter>();
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            MeshCollider collider = go.AddComponent<MeshCollider>();
            SmoothHill hill = go.AddComponent<SmoothHill>();
            renderer.sharedMaterial = hillMat;
            collider.material = grip;
            hill.Build();
            filter = go.GetComponent<MeshFilter>();
            collider.sharedMesh = filter.sharedMesh;
            return hill;
        }

        static DriveMadCarController CreateCar()
        {
            GameObject root = new GameObject("PlayerCar");
            root.layer = VehicleLayer;
            root.transform.position = new Vector3(0f, 0.22f, 4.2f);

            DriveMadCarController car = root.AddComponent<DriveMadCarController>();

            GameObject visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            visual.layer = VehicleLayer;
            visual.AddComponent<Rigidbody>();
            BoxCollider box = visual.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.48f, -0.12f);
            box.size = new Vector3(0.7f, 0.5f, 1.85f);

            GameObject bodyMesh = PlaceModel("Assets/_Car/Import/VoxelCar/Body.fbx", visual.transform, "Body", Vector3.zero, Vector3.one * CarScale);
            GameObject susF = PlaceModel("Assets/_Car/Import/VoxelCar/Suspension.fbx", visual.transform, "Suspension_Front", new Vector3(0f, WheelRadius, FrontZ), Vector3.one * CarScale);
            GameObject susR = PlaceModel("Assets/_Car/Import/VoxelCar/Suspension.fbx", visual.transform, "Suspension_Rear", new Vector3(0f, WheelRadius, RearZ), Vector3.one * CarScale);

            Transform wheels = new GameObject("Wheels").transform;
            wheels.SetParent(root.transform, false);

            Transform fl = PlaceModel("Assets/_Car/Import/VoxelCar/Wheel.fbx", wheels, "Wheel_FL", new Vector3(-AxleHalf, WheelRadius, FrontZ), Vector3.one * CarScale).transform;
            Transform fr = PlaceModel("Assets/_Car/Import/VoxelCar/Wheel.fbx", wheels, "Wheel_FR", new Vector3(AxleHalf, WheelRadius, FrontZ), Vector3.one * CarScale).transform;
            Transform rl = PlaceModel("Assets/_Car/Import/VoxelCar/Wheel.fbx", wheels, "Wheel_RL", new Vector3(-AxleHalf, WheelRadius, RearZ), Vector3.one * CarScale).transform;
            Transform rr = PlaceModel("Assets/_Car/Import/VoxelCar/Wheel.fbx", wheels, "Wheel_RR", new Vector3(AxleHalf, WheelRadius, RearZ), Vector3.one * CarScale).transform;

            car.SetVisuals(fl, fr, rl, rr, susF.transform, susR.transform);

            car.SetChassis(visual.transform);

            SerializedObject so = new SerializedObject(car);
            so.FindProperty("chassis").objectReferenceValue = visual.transform;
            so.FindProperty("groundMask").intValue = 1 << GroundLayer;
            so.FindProperty("front").FindPropertyRelative("leftVisual").objectReferenceValue = fl;
            so.FindProperty("front").FindPropertyRelative("rightVisual").objectReferenceValue = fr;
            so.FindProperty("front").FindPropertyRelative("suspensionVisual").objectReferenceValue = susF.transform;
            so.FindProperty("front").FindPropertyRelative("localZ").floatValue = FrontZ;
            so.FindProperty("rear").FindPropertyRelative("leftVisual").objectReferenceValue = rl;
            so.FindProperty("rear").FindPropertyRelative("rightVisual").objectReferenceValue = rr;
            so.FindProperty("rear").FindPropertyRelative("suspensionVisual").objectReferenceValue = susR.transform;
            so.FindProperty("rear").FindPropertyRelative("localZ").floatValue = RearZ;
            so.FindProperty("wheelRadius").floatValue = WheelRadius;
            so.FindProperty("axleHalfWidth").floatValue = AxleHalf;
            so.FindProperty("centerOfMass").vector3Value = new Vector3(0f, -0.2f, 0.1f);
            so.FindProperty("motorForce").floatValue = 1850f;
            so.FindProperty("reverseForce").floatValue = 1500f;
            so.FindProperty("groundPitchTorque").floatValue = 7f;
            so.FindProperty("airPitchTorque").floatValue = 20f;
            so.FindProperty("spring").floatValue = 52000f;
            so.FindProperty("damper").floatValue = 3500f;
            so.FindProperty("suspensionRestLength").floatValue = 0.12f;
            so.ApplyModifiedProperties();

            SetLayerRecursively(root, VehicleLayer);
            return car;
        }

        static GameObject PlaceModel(string path, Transform parent, string name, Vector3 localPos, Vector3 localScale)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = name;
            instance.transform.SetParent(parent, false);
            instance.transform.localPosition = localPos;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = localScale;
            Collider[] colliders = instance.GetComponentsInChildren<Collider>();
            for (int i = 0; i < colliders.Length; i++)
            {
                Object.DestroyImmediate(colliders[i]);
            }

            return instance;
        }

        static void CreateMarkers(SmoothHill hill, Material markerMat)
        {
            GameObject start = PlaceModel(
                "Assets/_Car/Import/VoxelCar/Start_Finish.fbx",
                null,
                "StartGate",
                Vector3.zero,
                Vector3.one * 0.32f);
            start.transform.position = new Vector3(0f, hill.SampleHeight(2.2f), 2.2f);

            GameObject finish = PlaceModel(
                "Assets/_Car/Import/VoxelCar/Start_Finish.fbx",
                null,
                "FinishGate",
                Vector3.zero,
                Vector3.one * 0.32f);
            float finishZ = hill.Length - 5f;
            finish.transform.position = new Vector3(0f, hill.SampleHeight(finishZ), finishZ);
        }

        static void CreateFinish(SmoothHill hill, LevelSession session)
        {
            GameObject go = new GameObject("FinishTrigger");
            go.transform.position = new Vector3(0f, hill.Height + 1.6f, hill.Length - 4.2f);
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(hill.Width + 1f, 4f, 2.4f);
            FinishTrigger trigger = go.AddComponent<FinishTrigger>();
            trigger.SetSession(session);
            SerializedObject so = new SerializedObject(trigger);
            so.FindProperty("session").objectReferenceValue = session;
            so.ApplyModifiedProperties();
        }

        static void CreateKillZone(LevelSession session)
        {
            GameObject go = new GameObject("KillZone");
            go.transform.position = new Vector3(0f, -8f, 20f);
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(80f, 4f, 120f);
            KillZone zone = go.AddComponent<KillZone>();
            SerializedObject so = new SerializedObject(zone);
            so.FindProperty("session").objectReferenceValue = session;
            so.ApplyModifiedProperties();
        }

        static Camera CreateCamera(Transform target)
        {
            GameObject go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            Camera cam = go.AddComponent<Camera>();
            go.AddComponent<AudioListener>();
            go.AddComponent<UniversalAdditionalCameraData>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.78f, 0.93f);
            cam.fieldOfView = 42f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 120f;
            go.transform.position = new Vector3(11.5f, 5.2f, 7.2f);
            SideViewFollowCamera follow = go.AddComponent<SideViewFollowCamera>();
            follow.SetTarget(target);
            SerializedObject so = new SerializedObject(follow);
            so.FindProperty("target").objectReferenceValue = target;
            so.ApplyModifiedProperties();
            return cam;
        }

        static void CreateLight()
        {
            GameObject go = new GameObject("Directional Light");
            Light light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.05f;
            light.color = new Color(1f, 0.97f, 0.9f);
            go.transform.rotation = Quaternion.Euler(42f, -30f, 0f);
            go.AddComponent<UniversalAdditionalLightData>();
        }

        static void CreateUi(DriveInput input, LevelSession session)
        {
            GameObject canvasGo = new GameObject("HUD_Canvas");
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.AddComponent<CanvasScaler>();
            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
            {
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            Text status = CreateText(canvasGo.transform, "StatusText", Vector2.zero, new Vector2(900f, 140f), 64, TextAnchor.MiddleCenter, font);
            SerializedObject sessionSo = new SerializedObject(session);
            sessionSo.FindProperty("statusText").objectReferenceValue = status;
            sessionSo.ApplyModifiedProperties();

            GameObject reverse = CreateHoldButton(canvasGo.transform, "Button_Reverse", new Vector2(160f, 140f), new Vector2(280f, 160f), "<  BACK", font, input, OnScreenHoldButton.ActionType.Reverse);
            GameObject forward = CreateHoldButton(canvasGo.transform, "Button_Forward", new Vector2(-160f, 140f), new Vector2(280f, 160f), "FWD  >", font, input, OnScreenHoldButton.ActionType.Forward);
            Pin(reverse.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f));
            Pin(forward.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(1f, 0f));

            GameObject restart = CreateButton(canvasGo.transform, "Button_Restart", new Vector2(-90f, -70f), new Vector2(160f, 80f), "R", font);
            Pin(restart.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f));
            UiRestartButton restartHook = restart.AddComponent<UiRestartButton>();
            restartHook.SetInput(input);
            SerializedObject restartSo = new SerializedObject(restartHook);
            restartSo.FindProperty("input").objectReferenceValue = input;
            restartSo.ApplyModifiedProperties();

            GameObject es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();
        }

        static GameObject CreateHoldButton(Transform parent, string name, Vector2 anchored, Vector2 size, string label, Font font, DriveInput input, OnScreenHoldButton.ActionType action)
        {
            GameObject go = CreateButton(parent, name, anchored, size, label, font);
            OnScreenHoldButton hold = go.AddComponent<OnScreenHoldButton>();
            SerializedObject so = new SerializedObject(hold);
            so.FindProperty("input").objectReferenceValue = input;
            so.FindProperty("action").enumValueIndex = (int)action;
            so.ApplyModifiedProperties();
            return go;
        }

        static GameObject CreateButton(Transform parent, string name, Vector2 anchored, Vector2 size, string label, Font font)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rect = go.AddComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = anchored;
            Image image = go.AddComponent<Image>();
            image.color = new Color(0.08f, 0.08f, 0.1f, 0.55f);
            go.AddComponent<Button>();
            CreateText(go.transform, "Label", Vector2.zero, size, 36, TextAnchor.MiddleCenter, font).text = label;
            return go;
        }

        static Text CreateText(Transform parent, string name, Vector2 anchored, Vector2 size, int fontSize, TextAnchor anchor, Font font)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rect = go.AddComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = anchored;
            Text text = go.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        static void Pin(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.pivot = min;
        }

        static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }

        static void ConfigureBuildSettings()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene("Assets/_Car/Scenes/Level01.unity", true)
            };
        }
    }
}

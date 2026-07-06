using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Slime
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Slime/Interaction Test Scene Builder")]
    public sealed class SlimeInteractionTestSceneBuilder : MonoBehaviour
    {
        const string GeneratedRootName = "Generated Interaction Test Root";

        [SerializeField] bool autoBuildOnPlay = true;
        [SerializeField] bool buildInEditMode = true;
        [SerializeField] bool rebuildWhenPlayStarts = true;

        Material _floorMaterial;
        Material _triggerMaterial;
        Material _slimePreviewMaterial;
        Material _doorMaterial;
        Material _rotateMaterial;
        Material _blinkMaterial;
        Material _sequenceMaterial;
        Material _dangerMaterial;
        Material _checkpointMaterial;

        void Awake()
        {
            if (!Application.isPlaying || !autoBuildOnPlay)
                return;

            if (rebuildWhenPlayStarts)
                RebuildScene();
            else
                EnsureSceneBuilt();
        }

        void OnEnable()
        {
            if (Application.isPlaying || !buildInEditMode)
                return;

            RebuildScene();
        }

        [ContextMenu("Rebuild Interaction Test Scene")]
        public void RebuildScene()
        {
            if (!ShouldBuildInCurrentScene())
                return;

            GameObject existingRoot = FindGeneratedRoot();
            if (existingRoot != null)
                DestroyAny(existingRoot);

            BuildScene();
        }

        public void EnsureSceneBuilt()
        {
            if (!ShouldBuildInCurrentScene())
                return;

            if (FindGeneratedRoot() != null)
                return;

            BuildScene();
        }

        void BuildScene()
        {
            PrepareMaterials();

            GameObject root = new GameObject(GeneratedRootName);
            MoveToBuilderScene(root);
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            ConfigureLighting();
            CreateFloor(root.transform);

            SlimeControllerMotor motor = CreateGeometryPlayer(root.transform);
            Camera sceneCamera = ConfigureCamera();
            if (motor != null && sceneCamera != null)
                motor.facingCamera = sceneCamera.transform;

            CreateCheckpointAndRespawnStation(root.transform, motor);
            CreateDoorVisibilityStation(root.transform);
            CreateRotateStation(root.transform);
            CreatePermanentDoorStation(root.transform);
            CreateBlinkAppearStation(root.transform);
            CreateBlinkSequenceStation(root.transform);
            CreateInstructionLabel(root.transform);
        }

        bool ShouldBuildInCurrentScene()
        {
            string sceneName = gameObject.scene.name;
            return string.IsNullOrEmpty(sceneName) || sceneName == "Interaction Test";
        }

        void PrepareMaterials()
        {
            _floorMaterial = CreateMaterial("Interaction Test Floor", new Color(0.28f, 0.32f, 0.34f, 1f));
            _triggerMaterial = CreateMaterial("Interaction Test Trigger", new Color(0.05f, 0.55f, 1f, 0.78f));
            _slimePreviewMaterial = CreateMaterial("Interaction Test Slime", new Color(0f, 0.95f, 0.8f, 1f));
            _doorMaterial = CreateMaterial("Interaction Test Door", new Color(0.9f, 0.7f, 0.22f, 1f));
            _rotateMaterial = CreateMaterial("Interaction Test Rotate", new Color(0.86f, 0.25f, 0.95f, 1f));
            _blinkMaterial = CreateMaterial("Interaction Test Blink", new Color(0.25f, 0.95f, 0.45f, 1f));
            _sequenceMaterial = CreateMaterial("Interaction Test Sequence", new Color(0.95f, 0.38f, 0.25f, 1f));
            _dangerMaterial = CreateMaterial("Interaction Test Danger", new Color(1f, 0.12f, 0.1f, 0.8f));
            _checkpointMaterial = CreateMaterial("Interaction Test Checkpoint", new Color(0.1f, 0.95f, 0.95f, 0.8f));
        }

        void ConfigureLighting()
        {
            Light light = FindSceneObject<Light>();
            if (light == null)
            {
                GameObject lightObject = new GameObject("Directional Light");
                MoveToBuilderScene(lightObject);
                light = lightObject.AddComponent<Light>();
            }

            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.transform.SetPositionAndRotation(new Vector3(0f, 7f, -4f), Quaternion.Euler(50f, -30f, 0f));
        }

        Camera ConfigureCamera()
        {
            Camera camera = FindSceneMainCamera();
            if (camera == null)
                camera = FindSceneObject<Camera>();

            if (camera == null)
            {
                GameObject cameraObject = new GameObject("Main Camera");
                MoveToBuilderScene(cameraObject);
                cameraObject.tag = "MainCamera";
                camera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
            }

            camera.tag = "MainCamera";
            camera.fieldOfView = 70f;
            camera.transform.SetPositionAndRotation(new Vector3(0f, 8.6f, -10.2f), Quaternion.Euler(38f, 0f, 0f));

            CameraFollow_Slime follow = camera.GetComponent<CameraFollow_Slime>();
            if (follow != null)
                DestroyAny(follow);

            return camera;
        }

        void CreateFloor(Transform root)
        {
            GameObject floor = CreateCube("Test Floor", root, new Vector3(0f, -0.08f, 4f), new Vector3(18f, 0.16f, 18f), _floorMaterial);
            floor.isStatic = true;
        }

        SlimeControllerMotor CreateGeometryPlayer(Transform root)
        {
            GameObject player = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            player.name = "Geometry Player Body";
            player.transform.SetParent(root, false);
            player.transform.position = new Vector3(0f, 0.65f, -4f);
            player.transform.localScale = Vector3.one * 1.25f;

            Renderer renderer = player.GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = _slimePreviewMaterial;

            Rigidbody body = player.AddComponent<Rigidbody>();
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            SlimeControllerMotor motor = player.AddComponent<SlimeControllerMotor>();
            motor.moveSpeed = 3.2f;
            motor.jumpSpeed = 4f;
            motor.groundY = 0.65f;

            return motor;
        }

        void CreateDoorVisibilityStation(Transform root)
        {
            Vector3 basePosition = new Vector3(-7.2f, 0.45f, 2.2f);
            GameObject door = CreateCube("Temporary Hide Door Target", root, basePosition + new Vector3(0f, 1.05f, 2.25f), new Vector3(1.1f, 2.1f, 0.28f), _doorMaterial);
            GameObject trigger = CreateTriggerPad("01 Trigger - Temporary Hide Door", root, basePosition, "Hide door while touching");

            SlimeDoorVisibilityContactEvent handler = trigger.AddComponent<SlimeDoorVisibilityContactEvent>();
            SetSerializedField(handler, "objectToHide", door);
            SetTriggerMode(handler, SlimeContactEventTriggerMode.RepeatableStart);
            SetSerializedField(handler, "restoreObjectWhenContactEnds", true);
            handler.ResetCapturedState();

            ConfigureContactSwitch(trigger, handler);
        }

        void CreateRotateStation(Transform root)
        {
            Vector3 basePosition = new Vector3(-4.2f, 0.45f, 2.2f);
            GameObject rotor = CreateCube("Rotating Gate Target", root, basePosition + new Vector3(0f, 1.05f, 2.25f), new Vector3(0.35f, 2f, 1.8f), _rotateMaterial);
            GameObject trigger = CreateTriggerPad("02 Trigger - Rotate Gate", root, basePosition, "Rotate target 90 degrees");

            SlimeRotateContactEvent handler = trigger.AddComponent<SlimeRotateContactEvent>();
            SetSerializedField(handler, "target", rotor.transform);
            SetSerializedField(handler, "rotationAxis", Vector3.up);
            SetSerializedField(handler, "rotationAngle", 90f);
            SetTriggerMode(handler, SlimeContactEventTriggerMode.RepeatableStart);
            SetSerializedField(handler, "restoreWhenContactEnds", false);
            handler.ResetRotationState();

            ConfigureContactSwitch(trigger, handler);
        }

        void CreatePermanentDoorStation(Transform root)
        {
            Vector3 basePosition = new Vector3(-1.4f, 0.45f, 2.2f);
            GameObject door = CreateCube("Permanent Door Target", root, basePosition + new Vector3(0f, 1.05f, 2.25f), new Vector3(1.25f, 2.1f, 0.32f), _doorMaterial);
            GameObject trigger = CreateTriggerPad("03 Trigger - Permanent Open Door", root, basePosition, "Door model and collider disappear");

            SlimePermanentDoorOpenContactEvent handler = trigger.AddComponent<SlimePermanentDoorOpenContactEvent>();
            SetSerializedField(handler, "doorModel", door);
            SetSerializedField(handler, "doorColliders", door.GetComponentsInChildren<Collider>(true));
            SetTriggerMode(handler, SlimeContactEventTriggerMode.Once);

            ConfigureContactSwitch(trigger, handler);
        }

        void CreateCheckpointAndRespawnStation(Transform root, SlimeControllerMotor player)
        {
            GameObject managerObject = new GameObject("Checkpoint Manager");
            managerObject.transform.SetParent(root, false);
            managerObject.transform.position = new Vector3(1.2f, 0.25f, -3.6f);

            GameObject startMarker = CreateCube("Initial Checkpoint Marker", root, new Vector3(0f, 0.12f, -4f), new Vector3(0.8f, 0.18f, 0.8f), _checkpointMaterial);
            Transform startPoint = CreatePoint("Initial Checkpoint Point", root, new Vector3(0f, 0.65f, -4f));
            SlimeGeometryCheckpointManager manager = managerObject.AddComponent<SlimeGeometryCheckpointManager>();
            manager.Configure(player != null ? player.transform : null, startPoint);

            GameObject checkpointPad = CreateCube("Checkpoint Refresh Zone", root, new Vector3(1.4f, 0.25f, 2.2f), new Vector3(1.6f, 0.25f, 1.6f), _checkpointMaterial);
            BoxCollider checkpointCollider = checkpointPad.GetComponent<BoxCollider>();
            checkpointCollider.isTrigger = true;

            Transform checkpointPoint = CreatePoint("Checkpoint Refresh Point", root, new Vector3(1.4f, 0.65f, 2.2f));
            SlimeGeometryCheckpointZone checkpointZone = checkpointPad.AddComponent<SlimeGeometryCheckpointZone>();
            checkpointZone.Configure(manager, checkpointPoint, false);
            CreateLabel("Checkpoint Label", root, "Checkpoint refresh", checkpointPad.transform.position + new Vector3(0f, 1.1f, 0f), Color.cyan);

            _ = startMarker;

            GameObject deathTrigger = CreateCube("04 Trigger - Respawn At Checkpoint", root, new Vector3(1.4f, 0.35f, 6.2f), new Vector3(1.8f, 0.35f, 1.8f), _dangerMaterial);
            BoxCollider deathCollider = deathTrigger.GetComponent<BoxCollider>();
            deathCollider.isTrigger = true;
            CreateLabel("Respawn Label", root, "Respawn at checkpoint", deathTrigger.transform.position + new Vector3(0f, 1.1f, 0f), Color.red);

            SlimeGeometryRespawnAtCheckpointContactEvent handler = deathTrigger.AddComponent<SlimeGeometryRespawnAtCheckpointContactEvent>();
            handler.Configure(manager);
            SetTriggerMode(handler, SlimeContactEventTriggerMode.RepeatableStart);

            ConfigureContactSwitch(deathTrigger, handler);
        }

        void CreateBlinkAppearStation(Transform root)
        {
            Vector3 basePosition = new Vector3(4.2f, 0.45f, 2.2f);
            GameObject target = CreateCube("Blink Appear Target", root, basePosition + new Vector3(0f, 1f, 2.25f), new Vector3(1.1f, 1.1f, 1.1f), _blinkMaterial);
            target.SetActive(false);
            GameObject trigger = CreateTriggerPad("05 Trigger - Blink Appear", root, basePosition, "Blink target into view");

            SlimeBlinkAppearContactEvent handler = trigger.AddComponent<SlimeBlinkAppearContactEvent>();
            SetSerializedField(handler, "target", target);
            SetSerializedField(handler, "blinkFrequencyHz", 5f);
            SetSerializedField(handler, "blinkCount", 8);
            SetSerializedField(handler, "finalVisible", true);
            SetTriggerMode(handler, SlimeContactEventTriggerMode.RepeatableStart);

            ConfigureContactSwitch(trigger, handler);
        }

        void CreateBlinkSequenceStation(Transform root)
        {
            Vector3 basePosition = new Vector3(7.2f, 0.45f, 2.2f);
            GameObject[] targets =
            {
                CreateCube("Blink Sequence Target A", root, basePosition + new Vector3(-0.9f, 1f, 2.2f), new Vector3(0.65f, 0.65f, 0.65f), _sequenceMaterial),
                CreateCube("Blink Sequence Target B", root, basePosition + new Vector3(0f, 1f, 2.2f), new Vector3(0.65f, 0.65f, 0.65f), _sequenceMaterial),
                CreateCube("Blink Sequence Target C", root, basePosition + new Vector3(0.9f, 1f, 2.2f), new Vector3(0.65f, 0.65f, 0.65f), _sequenceMaterial),
            };

            for (int i = 0; i < targets.Length; i++)
                targets[i].SetActive(false);

            GameObject trigger = CreateTriggerPad("06 Trigger - Blink Sequence", root, basePosition, "Blink targets in order");

            SlimeBlinkSequenceControllerContactEvent handler = trigger.AddComponent<SlimeBlinkSequenceControllerContactEvent>();
            var blinkTargets = new SlimeBlinkSequenceControllerContactEvent.BlinkTarget[targets.Length];
            for (int i = 0; i < targets.Length; i++)
                blinkTargets[i] = new SlimeBlinkSequenceControllerContactEvent.BlinkTarget { target = targets[i], visibleDuringStep = true };

            SetSerializedField(handler, "blinkTargets", blinkTargets);
            SetSerializedField(handler, "stepInterval", 0.25f);
            SetSerializedField(handler, "cycles", 2);
            SetSerializedField(handler, "hideAllBeforeSequence", true);
            SetSerializedField(handler, "restoreInitialVisibilityAfterSequence", false);
            SetSerializedField(handler, "finalVisible", true);
            SetTriggerMode(handler, SlimeContactEventTriggerMode.RepeatableStart);
            handler.ResetSequenceState();

            ConfigureContactSwitch(trigger, handler);
        }

        GameObject CreateTriggerPad(string objectName, Transform root, Vector3 position, string label)
        {
            GameObject trigger = CreateCube(objectName, root, position, new Vector3(1.55f, 0.28f, 1.55f), _triggerMaterial);
            BoxCollider collider = trigger.GetComponent<BoxCollider>();
            collider.isTrigger = true;
            CreateLabel(objectName + " Label", root, label, position + new Vector3(0f, 1.1f, 0f), Color.white);
            return trigger;
        }

        void ConfigureContactSwitch(GameObject triggerObject, params SlimeContactEventHandler[] handlers)
        {
            SlimeDoorSwitchOnContact contactSwitch = triggerObject.AddComponent<SlimeDoorSwitchOnContact>();
            SetSerializedField(contactSwitch, "slimeSimulator", null);
            SetSerializedField(contactSwitch, "slimeSurface", null);
            SetSerializedField(contactSwitch, "autoFindSlimeSurface", false);
            SetSerializedField(contactSwitch, "useSlimeSurfaceContact", false);
            SetSerializedField(contactSwitch, "usePhysicsCallbacks", true);
            SetSerializedField(contactSwitch, "onlyPhysicsContactFromSlime", true);
            SetSerializedField(contactSwitch, "includeChildColliders", true);
            SetSerializedField(contactSwitch, "contactPadding", 0.05f);
            SetSerializedField(contactSwitch, "maxSurfaceVertexChecks", 1);
            SetSerializedField(contactSwitch, "eventHandlers", handlers);
            contactSwitch.RefreshContactColliders();
        }

        GameObject CreateCube(string objectName, Transform root, Vector3 position, Vector3 scale, Material material)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = objectName;
            cube.transform.SetParent(root, false);
            cube.transform.position = position;
            cube.transform.localScale = scale;
            Renderer renderer = cube.GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = material;
            return cube;
        }

        Transform CreatePoint(string objectName, Transform root, Vector3 position)
        {
            GameObject point = new GameObject(objectName);
            point.transform.SetParent(root, false);
            point.transform.position = position;
            return point.transform;
        }

        void CreateInstructionLabel(Transform root)
        {
            CreateLabel(
                "Interaction Test Instructions",
                root,
                "WASD move geometry proxy. Touch colored pads to test: hide, rotate, open, respawn, blink, sequence.",
                new Vector3(0f, 2.35f, -5.2f),
                Color.white);
        }

        static TextMesh CreateLabel(string objectName, Transform root, string text, Vector3 position, Color color)
        {
            GameObject labelObject = new GameObject(objectName);
            labelObject.transform.SetParent(root, false);
            labelObject.transform.position = position;
            labelObject.transform.rotation = Quaternion.Euler(55f, 0f, 0f);

            TextMesh textMesh = labelObject.AddComponent<TextMesh>();
            textMesh.text = text;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.characterSize = 0.14f;
            textMesh.fontSize = 32;
            textMesh.color = color;
            return textMesh;
        }

        GameObject FindGeneratedRoot()
        {
            Scene scene = gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded)
                return GameObject.Find(GeneratedRootName);

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] != null && roots[i].name == GeneratedRootName)
                    return roots[i];
            }

            return null;
        }

        T FindSceneObject<T>() where T : Component
        {
            Scene scene = gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded)
                return UnityEngine.Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] == null)
                    continue;

                T component = roots[i].GetComponentInChildren<T>(true);
                if (component != null)
                    return component;
            }

            return null;
        }

        Camera FindSceneMainCamera()
        {
            Scene scene = gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded)
                return Camera.main;

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] == null)
                    continue;

                Camera[] cameras = roots[i].GetComponentsInChildren<Camera>(true);
                for (int cameraIndex = 0; cameraIndex < cameras.Length; cameraIndex++)
                {
                    Camera camera = cameras[cameraIndex];
                    if (camera != null && camera.CompareTag("MainCamera"))
                        return camera;
                }
            }

            return null;
        }

        void MoveToBuilderScene(GameObject target)
        {
            Scene scene = gameObject.scene;
            if (target != null && scene.IsValid() && scene.isLoaded)
                SceneManager.MoveGameObjectToScene(target, scene);
        }

        static Material CreateMaterial(string materialName, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");

            Material material = new Material(shader) { name = materialName };
            SetColorIfPresent(material, "_BaseColor", color);
            SetColorIfPresent(material, "_UnlitColor", color);
            SetColorIfPresent(material, "_Color", color);
            SetColorIfPresent(material, "_MainColor", color);
            SetFloatIfPresent(material, "_SurfaceType", 0f);
            SetFloatIfPresent(material, "_AlphaCutoffEnable", 0f);
            SetFloatIfPresent(material, "_UseShadowThreshold", 0f);

            return material;
        }

        static void SetSerializedField(object target, string fieldName, object value)
        {
            if (target == null)
                return;

            Type type = target.GetType();
            while (type != null)
            {
                FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    field.SetValue(target, value);
                    return;
                }

                type = type.BaseType;
            }

            Debug.LogWarning($"SlimeInteractionTestSceneBuilder: field '{fieldName}' not found on {target.GetType().Name}.");
        }

        static void SetTriggerMode(SlimeContactEventHandler handler, SlimeContactEventTriggerMode triggerMode)
        {
            SetSerializedField(handler, "triggerMode", triggerMode);
            SetSerializedField(handler, "triggerModeMigrated", true);
        }

        static void SetColorIfPresent(Material material, string propertyName, Color value)
        {
            if (material != null && material.HasProperty(propertyName))
                material.SetColor(propertyName, value);
        }

        static void SetFloatIfPresent(Material material, string propertyName, float value)
        {
            if (material != null && material.HasProperty(propertyName))
                material.SetFloat(propertyName, value);
        }

        static void DestroyAny(UnityEngine.Object target)
        {
            if (target == null)
                return;

            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }
    }
}

#if UNITY_EDITOR
using System;
using Supernova.Gameplay;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;
using UnityAnimatorController = UnityEditor.Animations.AnimatorController;

namespace Supernova.Editor.Gameplay
{
    /// <summary>
    /// Builds the pickaxe's right-click throw assets: the spin clip that tumbles the
    /// pickaxe about its centre of mass, the controller that hands off from spin to
    /// the authored pinned clip, the thrown projectile prefab, and the tool wiring.
    /// Every location resolves through <see cref="ProjectAssetPaths"/>.
    /// </summary>
    public static class ThrownPickaxeAssetBuilder
    {
        private const string SessionKey =
            "Supernova.ThrownPickaxeAssetBuilder.Ensured.V1";
        private const string SpinStateName = "Spin";
        private const string PinnedStateName = "Pinned";
        private const float SpinClipDuration = 1f;
        private const int SpinKeyCount = 5;
        private const float HeadBarBandHeight = 0.12f;
        private const float HeadBarBandHalfWidth = 0.15f;

        [InitializeOnLoadMethod]
        private static void ScheduleEnsureConfiguration()
        {
            if (SessionState.GetBool(SessionKey, false))
                return;

            SessionState.SetBool(SessionKey, true);
            EditorApplication.delayCall += EnsureWhenReady;
        }

        [MenuItem("Tools/Supernova/Gameplay/Rebuild Thrown Pickaxe Assets")]
        public static void Rebuild()
        {
            PlayerToolDefinition definition = EnsureConfiguration(true);
            Selection.activeObject = definition;
            EditorGUIUtility.PingObject(definition);
            Debug.Log(
                "Rebuilt the pickaxe spin clip, thrown projectile, and throw wiring.",
                definition);
        }

        private static void EnsureWhenReady()
        {
            if (EditorApplication.isCompiling
                || EditorApplication.isUpdating
                || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.delayCall += EnsureWhenReady;
                return;
            }

            try
            {
                EnsureConfiguration(false);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static PlayerToolDefinition EnsureConfiguration(bool rebuild)
        {
            EnsureAssetFolder(ProjectAssetPaths.Folders.PickaxePrefabs);
            AnimationClip pinnedClip =
                AssetDatabase.LoadAssetAtPath<AnimationClip>(
                    ProjectAssetPaths.Animations.PickaxeThrown);
            if (pinnedClip == null)
            {
                throw new InvalidOperationException(
                    "Cannot configure the thrown pickaxe because its authored "
                    + "pinned clip is missing: "
                    + ProjectAssetPaths.Animations.PickaxeThrown);
            }

            AnimationClip spinClip = EnsureSpinClip();
            UnityAnimatorController controller = EnsureController(
                spinClip,
                pinnedClip);
            GameObject projectile = EnsureProjectilePrefab(controller, rebuild);
            PlayerToolDefinition definition = EnsureDefinition(projectile);
            EnsurePlayerRegistration();
            AssetDatabase.SaveAssets();
            return definition;
        }

        /// <summary>
        /// A looping clip that spins the pivot a full turn about its local Z. The
        /// prefab offsets the model so this pivot sits on the pickaxe's centre of
        /// mass, which makes the tumble read as rotation about its balance point.
        /// </summary>
        private static AnimationClip EnsureSpinClip()
        {
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                ProjectAssetPaths.Animations.PickaxeSpin);
            if (clip == null)
            {
                clip = new AnimationClip();
                AssetDatabase.CreateAsset(
                    clip,
                    ProjectAssetPaths.Animations.PickaxeSpin);
            }
            else
            {
                AnimationClip empty = new AnimationClip();
                EditorUtility.CopySerialized(empty, clip);
                Object.DestroyImmediate(empty);
            }

            clip.name = "pickaxe_spin";
            clip.frameRate = 60f;

            // Linear keys spaced across the turn keep the angular rate constant, so
            // the animator's speed multiplier maps directly to revolutions/second.
            Keyframe[] keys = new Keyframe[SpinKeyCount];
            for (int i = 0; i < SpinKeyCount; i++)
            {
                float t = i / (float)(SpinKeyCount - 1);
                keys[i] = new Keyframe(
                    t * SpinClipDuration,
                    t * 360f)
                {
                    inTangent = 360f / SpinClipDuration,
                    outTangent = 360f / SpinClipDuration,
                };
            }

            var curve = new AnimationCurve(keys);
            AnimationUtility.SetEditorCurve(
                clip,
                EditorCurveBinding.FloatCurve(
                    string.Empty,
                    typeof(Transform),
                    "localEulerAnglesRaw.z"),
                curve);
            AnimationUtility.SetEditorCurve(
                clip,
                EditorCurveBinding.FloatCurve(
                    string.Empty,
                    typeof(Transform),
                    "localEulerAnglesRaw.x"),
                AnimationCurve.Constant(0f, SpinClipDuration, 0f));
            AnimationUtility.SetEditorCurve(
                clip,
                EditorCurveBinding.FloatCurve(
                    string.Empty,
                    typeof(Transform),
                    "localEulerAnglesRaw.y"),
                AnimationCurve.Constant(0f, SpinClipDuration, 0f));

            AnimationClipSettings settings =
                AnimationUtility.GetAnimationClipSettings(clip);
            settings.startTime = 0f;
            settings.stopTime = SpinClipDuration;
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            return clip;
        }

        private static UnityAnimatorController EnsureController(
            AnimationClip spinClip,
            AnimationClip pinnedClip)
        {
            UnityAnimatorController controller =
                AssetDatabase.LoadAssetAtPath<UnityAnimatorController>(
                    ProjectAssetPaths.Animations.ThrownPickaxeController);
            if (controller == null)
            {
                controller = UnityAnimatorController.CreateAnimatorControllerAtPath(
                    ProjectAssetPaths.Animations.ThrownPickaxeController);
            }

            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorState spin = EnsureState(machine, SpinStateName);
            AnimatorState pinned = EnsureState(machine, PinnedStateName);
            spin.motion = spinClip;
            pinned.motion = pinnedClip;
            // The projectile plays each state by name, so no transitions are needed.
            machine.defaultState = spin;
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static AnimatorState EnsureState(
            AnimatorStateMachine machine,
            string name)
        {
            ChildAnimatorState[] states = machine.states;
            for (int i = 0; i < states.Length; i++)
            {
                if (states[i].state != null && states[i].state.name == name)
                    return states[i].state;
            }
            return machine.AddState(name);
        }

        private static GameObject EnsureProjectilePrefab(
            UnityAnimatorController controller,
            bool rebuild)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProjectAssetPaths.Prefabs.ThrownPickaxe);
            if (existing != null && !rebuild)
                return existing;

            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProjectAssetPaths.ThirdParty.StylizedPickaxePrefab);
            if (model == null)
            {
                throw new InvalidOperationException(
                    "Missing the pickaxe source model: "
                    + ProjectAssetPaths.ThirdParty.StylizedPickaxePrefab);
            }

            Mesh mesh = ResolveMesh(model);
            Vector3 centreOfMass = CalculateCentreOfMass(mesh);
            Vector3 headTip = CalculateHeadTip(mesh);

            var root = new GameObject("ThrownPickaxe");
            try
            {
                // Pivot at the centre of mass, model shifted back by the same
                // amount, so spinning the pivot spins the pickaxe about its balance
                // point without moving the projectile's origin.
                var pivot = new GameObject("Spin Pivot");
                pivot.transform.SetParent(root.transform, false);
                Animator animator = pivot.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.updateMode = AnimatorUpdateMode.Normal;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(
                    model,
                    pivot.transform);
                visual.name = model.name;
                visual.transform.localPosition = -centreOfMass;
                visual.transform.localRotation = Quaternion.identity;

                Rigidbody body = root.AddComponent<Rigidbody>();
                body.mass = 2.2f;
                body.drag = 0.02f;
                body.angularDrag = 0.05f;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode =
                    CollisionDetectionMode.ContinuousDynamic;

                // A capsule spanning head to handle keeps the pickaxe from passing
                // through thin geometry while it tumbles.
                CapsuleCollider collider = root.AddComponent<CapsuleCollider>();
                collider.direction = 1;
                collider.center = Vector3.zero;
                collider.height = Mathf.Max(0.2f, mesh.bounds.size.y);
                collider.radius = 0.09f;

                ThrownPickaxe pickaxe = root.AddComponent<ThrownPickaxe>();
                var serialized = new SerializedObject(pickaxe);
                SetReference(serialized, "body", body);
                SetReference(serialized, "spinPivot", pivot.transform);
                SetReference(serialized, "spinAnimator", animator);
                SetVector(
                    serialized,
                    "headTipLocalPosition",
                    headTip - centreOfMass);
                SetVector(
                    serialized,
                    "headTipLocalDirection",
                    CalculateHeadSpikeDirection(mesh));
                // Sink most of the spike so the head visibly bites into the surface.
                // A shallow depth only grazes it and reads as the pickaxe resting on
                // top rather than being driven in.
                SetFloat(serialized, "pinDepth", 0.34f);
                SetFloat(serialized, "minimumBiteAngle", 45f);
                // Covers the authored wobble's impact beat, which is the only thing
                // softening the contact now that the pose snaps on the first frame.
                SetFloat(serialized, "pinSettleDuration", 0.4f);
                SetFloat(serialized, "pickupDistance", 1.6f);
                SetFloat(serialized, "recallSpeed", 9f);
                SetFloat(serialized, "recallAcceleration", 26f);
                SetFloat(serialized, "recallAbsorbDistance", 0.45f);
                SetFloat(serialized, "recallSpinRevolutions", 3.2f);
                SetFloat(serialized, "recallTimeout", 4f);
                serialized.ApplyModifiedPropertiesWithoutUndo();

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(
                    root,
                    ProjectAssetPaths.Prefabs.ThrownPickaxe);
                if (saved == null)
                {
                    throw new InvalidOperationException(
                        "Failed to save the thrown pickaxe prefab: "
                        + ProjectAssetPaths.Prefabs.ThrownPickaxe);
                }
                return saved;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static Mesh ResolveMesh(GameObject model)
        {
            MeshFilter filter = model.GetComponentInChildren<MeshFilter>(true);
            if (filter == null || filter.sharedMesh == null)
            {
                throw new InvalidOperationException(
                    "The pickaxe source model has no mesh: " + model.name);
            }
            return filter.sharedMesh;
        }

        /// <summary>
        /// Volume centroid via signed tetrahedra. This is the pickaxe's balance
        /// point, which is what the spin has to rotate about; the bounds centre sits
        /// noticeably lower because the heavy head is near the top.
        /// </summary>
        public static Vector3 CalculateCentreOfMass(Mesh mesh)
        {
            if (mesh == null) return Vector3.zero;

            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            double volume = 0.0;
            Vector3 accumulated = Vector3.zero;
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]];
                Vector3 b = vertices[triangles[i + 1]];
                Vector3 c = vertices[triangles[i + 2]];
                double signedVolume =
                    Vector3.Dot(a, Vector3.Cross(b, c)) / 6.0;
                volume += signedVolume;
                accumulated += (a + b + c) * (float)(signedVolume / 4.0);
            }

            return Mathf.Abs((float)volume) > 1e-6f
                ? accumulated / (float)volume
                : mesh.bounds.center;
        }

        /// <summary>
        /// The pick end: the vertex furthest along the head bar, which is the part
        /// that has to end up buried in the surface.
        /// </summary>
        public static Vector3 CalculateHeadTip(Mesh mesh)
        {
            if (mesh == null) return Vector3.zero;

            Vector3[] vertices = mesh.vertices;
            if (vertices.Length == 0) return Vector3.zero;

            Vector3 tip = vertices[0];
            for (int i = 1; i < vertices.Length; i++)
            {
                if (vertices[i].x < tip.x) tip = vertices[i];
            }
            return tip;
        }

        /// <summary>
        /// The direction the pick spike actually points, taken from the head bar
        /// rather than from the centre of mass. The centre of mass sits down the
        /// shaft, so a centre-of-mass-to-tip vector is tilted ~30 degrees off the
        /// spike and would bury the pickaxe at the wrong angle.
        /// </summary>
        public static Vector3 CalculateHeadSpikeDirection(Mesh mesh)
        {
            Vector3 tip = CalculateHeadTip(mesh);
            Vector3 barMiddle = CalculateHeadBarMiddle(mesh, tip);
            Vector3 direction = tip - barMiddle;
            return direction.sqrMagnitude > 0.0001f
                ? direction.normalized
                : Vector3.left;
        }

        /// <summary>
        /// Centroid of the head bar around the shaft, level with the spike. The
        /// spike axis runs from here out to the tip.
        /// </summary>
        private static Vector3 CalculateHeadBarMiddle(Mesh mesh, Vector3 tip)
        {
            Vector3[] vertices = mesh.vertices;
            Vector3 accumulated = Vector3.zero;
            int count = 0;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 vertex = vertices[i];
                if (Mathf.Abs(vertex.y - tip.y) < HeadBarBandHeight
                    && Mathf.Abs(vertex.x) < HeadBarBandHalfWidth)
                {
                    accumulated += vertex;
                    count++;
                }
            }

            return count > 0
                ? accumulated / count
                : new Vector3(0f, tip.y, 0f);
        }

        private static Vector3 ResolveHeadDirection(
            Vector3 headTip,
            Vector3 centreOfMass)
        {
            Vector3 direction = headTip - centreOfMass;
            return direction.sqrMagnitude > 0.0001f
                ? direction.normalized
                : Vector3.left;
        }

        private static PlayerToolDefinition EnsureDefinition(
            GameObject projectilePrefab)
        {
            PlayerToolDefinition definition =
                AssetDatabase.LoadAssetAtPath<PlayerToolDefinition>(
                    ProjectAssetPaths.Config.PickaxeTool);
            if (definition == null)
            {
                throw new InvalidOperationException(
                    "Missing the pickaxe tool definition: "
                    + ProjectAssetPaths.Config.PickaxeTool);
            }

            ThrownPickaxe projectile = projectilePrefab != null
                ? projectilePrefab.GetComponent<ThrownPickaxe>()
                : null;
            if (projectile == null)
            {
                throw new InvalidOperationException(
                    "The thrown pickaxe prefab has no ThrownPickaxe component: "
                    + ProjectAssetPaths.Prefabs.ThrownPickaxe);
            }

            var serialized = new SerializedObject(definition);
            SetReference(
                serialized,
                "magnetHoldAnimation",
                LoadMagnetHoldAnimation());
            SetFloat(serialized, "magnetHoldLoopStartNormalized", 0.7f);
            SetFloat(serialized, "magnetHoldLoopEndNormalized", 1f);

            SetReference(serialized, "thrownPickaxePrefab", projectile);
            SetFloat(serialized, "pickaxeThrowSpeed", 22f);
            SetFloat(serialized, "pickaxeSpinRevolutions", 2.4f);
            SetFloat(serialized, "pickaxePickupDistance", 1.6f);
            SetFloat(serialized, "pickaxeMagnetPullAcceleration", 34f);
            SetFloat(serialized, "pickaxeMagnetMaximumPullSpeed", 16f);
            // A level-aimed throw travels ~13m, so this covers a normal throw plus
            // some slack for a lobbed one, without letting the player reel themselves
            // across a whole cave. The grab hook, by comparison, reaches 30m.
            SetFloat(serialized, "pickaxeMagnetRange", 25f);
            // Deliberate aim, not "anything on screen". The old hemisphere test made
            // every visible pickaxe a valid target.
            SetFloat(serialized, "pickaxeMagnetAimAngle", 20f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
            return definition;
        }

        /// <summary>
        /// The looping two-handed cast pose the magnet tool used before it moved to
        /// right click. It lives inside an FBX, so the clip is picked out by name.
        /// </summary>
        private static AnimationClip LoadMagnetHoldAnimation()
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(
                ProjectAssetPaths.ThirdParty.SuriyunMagnetHold);
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is AnimationClip clip
                    && !clip.name.StartsWith("__preview__"))
                {
                    return clip;
                }
            }

            throw new InvalidOperationException(
                "Missing the magnet hold animation clip inside: "
                + ProjectAssetPaths.ThirdParty.SuriyunMagnetHold);
        }

        /// <summary>
        /// The throw needs its own component on the player, and the deleted magnet
        /// tool has to be dropped from the registered definitions.
        /// </summary>
        private static void EnsurePlayerRegistration()
        {
            GameObject player = PrefabUtility.LoadPrefabContents(
                ProjectAssetPaths.Prefabs.Player);
            if (player == null)
            {
                throw new InvalidOperationException(
                    "Cannot wire the pickaxe throw because the player prefab is "
                    + "missing: " + ProjectAssetPaths.Prefabs.Player);
            }

            try
            {
                PlayerToolController controller =
                    player.GetComponent<PlayerToolController>();
                if (controller == null)
                {
                    throw new InvalidOperationException(
                        "The player prefab has no PlayerToolController.");
                }

                if (player.GetComponent<PickaxeThrowController>() == null)
                    player.AddComponent<PickaxeThrowController>();

                var serialized = new SerializedObject(controller);
                RemoveMissingDefinitions(
                    serialized.FindProperty("toolDefinitions"));
                serialized.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(
                    player,
                    ProjectAssetPaths.Prefabs.Player);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(player);
            }
        }

        private static void RemoveMissingDefinitions(
            SerializedProperty definitions)
        {
            if (definitions == null) return;
            for (int i = definitions.arraySize - 1; i >= 0; i--)
            {
                if (definitions.GetArrayElementAtIndex(i).objectReferenceValue
                    == null)
                {
                    definitions.DeleteArrayElementAtIndex(i);
                }
            }
        }

        private static void EnsureAssetFolder(string folderPath)
        {
            string[] parts = folderPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static void SetReference(
            SerializedObject serialized,
            string propertyName,
            Object value)
        {
            serialized.FindProperty(propertyName).objectReferenceValue = value;
        }

        private static void SetFloat(
            SerializedObject serialized,
            string propertyName,
            float value)
        {
            serialized.FindProperty(propertyName).floatValue = value;
        }

        private static void SetVector(
            SerializedObject serialized,
            string propertyName,
            Vector3 value)
        {
            serialized.FindProperty(propertyName).vector3Value = value;
        }
    }
}
#endif

using System;
using System.Reflection;
using NUnit.Framework;
using Supernova.Audio;
using Supernova.Gameplay;
using Supernova.Infrastructure;
using Supernova.MinecraftCaves.Creatures;
using Supernova.Voxels;
using UnityEditor;
using UnityEngine;

namespace Supernova.Tests.Editor
{
    public sealed class AudioManagerTests
    {
        [Test]
        public void RequestPlay_ValidCue_BroadcastsImmutableRequest()
        {
            SoundEffectCue cue =
                ScriptableObject.CreateInstance<SoundEffectCue>();
            Vector3 position = new Vector3(2f, 3f, 4f);
            SoundEffectPlaybackRequest received = default;
            int notificationCount = 0;
            Action<SoundEffectPlaybackRequest> observer = request =>
            {
                received = request;
                notificationCount++;
            };

            SoundEffectEvents.PlaybackRequested += observer;
            try
            {
                bool accepted =
                    SoundEffectEvents.RequestPlay(cue, position, 0.4f);

                Assert.That(accepted, Is.True);
                Assert.That(notificationCount, Is.EqualTo(1));
                Assert.That(received.Cue, Is.SameAs(cue));
                Assert.That(received.Position, Is.EqualTo(position));
                Assert.That(received.VolumeScale, Is.EqualTo(0.4f));
            }
            finally
            {
                SoundEffectEvents.PlaybackRequested -= observer;
                UnityEngine.Object.DestroyImmediate(cue);
            }
        }

        [Test]
        public void RequestPlay_NullCue_DoesNotNotifyObservers()
        {
            int notificationCount = 0;
            Action<SoundEffectPlaybackRequest> observer =
                _ => notificationCount++;

            SoundEffectEvents.PlaybackRequested += observer;
            try
            {
                bool accepted = SoundEffectEvents.RequestPlay(
                    null,
                    Vector3.zero);

                Assert.That(accepted, Is.False);
                Assert.That(notificationCount, Is.Zero);
            }
            finally
            {
                SoundEffectEvents.PlaybackRequested -= observer;
            }
        }

        [Test]
        public void AudioManager_PlaybackRequest_ConfiguresPooledVoice()
        {
            GameObject managerObject = new GameObject("Audio Manager Test");
            AudioManager manager =
                managerObject.AddComponent<AudioManager>();
            AudioClip clip = AudioClip.Create(
                "Audio Manager Test Clip",
                128,
                1,
                44100,
                false);
            SoundEffectCue cue = CreateCue(
                clip,
                0.5f,
                0.7f,
                2f,
                15f,
                AudioRolloffMode.Linear);
            Vector3 position = new Vector3(5f, 6f, 7f);

            try
            {
                InvokeLifecycle(manager, "OnEnable");
                SoundEffectEvents.RequestPlay(cue, position, 0.4f);

                Assert.That(manager.VoiceCount, Is.EqualTo(1));
                AudioSource source =
                    manager.GetComponentInChildren<AudioSource>();
                Assert.That(source, Is.Not.Null);
                Assert.That(source.clip, Is.SameAs(clip));
                Assert.That(source.transform.position, Is.EqualTo(position));
                Assert.That(source.volume, Is.EqualTo(0.2f).Within(0.0001f));
                Assert.That(source.pitch, Is.EqualTo(1f));
                Assert.That(source.spatialBlend, Is.EqualTo(0.7f));
                Assert.That(source.minDistance, Is.EqualTo(2f));
                Assert.That(source.maxDistance, Is.EqualTo(15f));
                Assert.That(source.rolloffMode, Is.EqualTo(
                    AudioRolloffMode.Linear));
            }
            finally
            {
                InvokeLifecycle(manager, "OnDisable");
                UnityEngine.Object.DestroyImmediate(managerObject);
                UnityEngine.Object.DestroyImmediate(cue);
                UnityEngine.Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void AudioManager_Disabled_DoesNotObservePlaybackRequests()
        {
            GameObject managerObject = new GameObject("Disabled Audio Manager");
            AudioManager manager =
                managerObject.AddComponent<AudioManager>();
            AudioClip clip = AudioClip.Create(
                "Disabled Manager Clip",
                64,
                1,
                22050,
                false);
            SoundEffectCue cue = CreateCue(
                clip,
                1f,
                0f,
                1f,
                20f,
                AudioRolloffMode.Logarithmic);

            try
            {
                InvokeLifecycle(manager, "OnEnable");
                InvokeLifecycle(manager, "OnDisable");
                SoundEffectEvents.RequestPlay(cue, Vector3.zero);

                Assert.That(manager.VoiceCount, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(managerObject);
                UnityEngine.Object.DestroyImmediate(cue);
                UnityEngine.Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void AudioManager_LoopRequest_UpdatesFollowsStopsAndResumes()
        {
            GameObject managerObject = new GameObject("Loop Audio Manager");
            AudioManager manager =
                managerObject.AddComponent<AudioManager>();
            GameObject followTarget = new GameObject("Loop Follow Target");
            AudioClip clip = AudioClip.Create(
                "Loop Manager Clip",
                128,
                1,
                44100,
                false);
            SoundEffectCue cue = CreateCue(
                clip,
                0.5f,
                0f,
                1f,
                20f,
                AudioRolloffMode.Logarithmic);
            int loopId = SoundEffectEvents.CreateLoopId();

            try
            {
                InvokeLifecycle(manager, "OnEnable");
                followTarget.transform.position = new Vector3(1f, 2f, 3f);

                bool accepted = SoundEffectEvents.RequestLoop(
                    loopId,
                    cue,
                    followTarget.transform,
                    0.4f,
                    1.5f);

                Assert.That(accepted, Is.True);
                Assert.That(manager.LoopCount, Is.EqualTo(1));
                AudioSource source =
                    manager.GetComponentInChildren<AudioSource>();
                Assert.That(source, Is.Not.Null);
                Assert.That(source.loop, Is.True);
                Assert.That(source.clip, Is.SameAs(clip));
                Assert.That(source.volume, Is.EqualTo(0.2f).Within(0.0001f));
                Assert.That(source.pitch, Is.EqualTo(1.5f).Within(0.0001f));
                Assert.That(
                    source.transform.position,
                    Is.EqualTo(followTarget.transform.position));

                followTarget.transform.position = new Vector3(4f, 5f, 6f);
                InvokeLifecycle(manager, "Update");
                Assert.That(
                    source.transform.position,
                    Is.EqualTo(followTarget.transform.position));

                SoundEffectEvents.RequestLoop(
                    loopId,
                    cue,
                    followTarget.transform,
                    0.8f,
                    1f);
                Assert.That(manager.LoopCount, Is.EqualTo(1));
                Assert.That(
                    manager.GetComponentInChildren<AudioSource>(),
                    Is.SameAs(source));
                Assert.That(source.volume, Is.EqualTo(0.4f).Within(0.0001f));
                Assert.That(source.pitch, Is.EqualTo(1f).Within(0.0001f));

                source.timeSamples = 64;
                SoundEffectEvents.RequestStopLoop(loopId);
                Assert.That(manager.LoopCount, Is.Zero);

                int resumedLoopId = SoundEffectEvents.CreateLoopId();
                SoundEffectEvents.RequestLoop(
                    resumedLoopId,
                    cue,
                    followTarget.transform,
                    1f,
                    1f);
                AudioSource resumedSource =
                    manager.GetComponentInChildren<AudioSource>();
                Assert.That(resumedSource, Is.Not.Null);
                Assert.That(resumedSource.timeSamples, Is.EqualTo(64));
                SoundEffectEvents.RequestStopLoop(resumedLoopId);
                Assert.That(manager.LoopCount, Is.Zero);
            }
            finally
            {
                InvokeLifecycle(manager, "OnDisable");
                UnityEngine.Object.DestroyImmediate(managerObject);
                UnityEngine.Object.DestroyImmediate(followTarget);
                UnityEngine.Object.DestroyImmediate(cue);
                UnityEngine.Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void AudioManager_LoopFade_InterpolatesThenStopsAndResumesOffset()
        {
            GameObject managerObject = new GameObject("Fading Loop Manager");
            AudioManager manager = managerObject.AddComponent<AudioManager>();
            GameObject followTarget = new GameObject("Fading Loop Target");
            AudioClip clip = AudioClip.Create(
                "Fading Loop Clip",
                128,
                1,
                44100,
                false);
            SoundEffectCue cue = CreateCue(
                clip,
                0.5f,
                0f,
                1f,
                20f,
                AudioRolloffMode.Logarithmic);
            int loopId = SoundEffectEvents.CreateLoopId();

            try
            {
                InvokeLifecycle(manager, "OnEnable");
                SoundEffectEvents.RequestLoop(
                    loopId,
                    cue,
                    followTarget.transform,
                    1f,
                    1f,
                    0.2f);

                AudioSource source =
                    manager.GetComponentInChildren<AudioSource>();
                Assert.That(source, Is.Not.Null);
                Assert.That(
                    source.volume,
                    Is.EqualTo(0f).Within(0.0001f));

                InvokeLoopUpdate(manager, 0.1f);
                Assert.That(source.volume, Is.EqualTo(0.25f).Within(0.0001f));
                InvokeLoopUpdate(manager, 0.1f);
                Assert.That(source.volume, Is.EqualTo(0.5f).Within(0.0001f));

                source.timeSamples = 64;
                SoundEffectEvents.RequestStopLoop(loopId, 0.2f);
                InvokeLoopUpdate(manager, 0.1f);
                Assert.That(manager.LoopCount, Is.EqualTo(1));
                Assert.That(source.volume, Is.EqualTo(0.25f).Within(0.0001f));
                InvokeLoopUpdate(manager, 0.1f);
                Assert.That(manager.LoopCount, Is.Zero);

                int resumedLoopId = SoundEffectEvents.CreateLoopId();
                SoundEffectEvents.RequestLoop(
                    resumedLoopId,
                    cue,
                    followTarget.transform);
                AudioSource resumedSource =
                    manager.GetComponentInChildren<AudioSource>();
                Assert.That(resumedSource, Is.Not.Null);
                Assert.That(resumedSource.timeSamples, Is.EqualTo(64));
                SoundEffectEvents.RequestStopLoop(resumedLoopId);
            }
            finally
            {
                InvokeLifecycle(manager, "OnDisable");
                UnityEngine.Object.DestroyImmediate(managerObject);
                UnityEngine.Object.DestroyImmediate(followTarget);
                UnityEngine.Object.DestroyImmediate(cue);
                UnityEngine.Object.DestroyImmediate(clip);
            }
        }

        [TestCase(PlayerCharacterState.Move, true, 1.75f)]
        [TestCase(PlayerCharacterState.CrouchMove, true, 1f)]
        [TestCase(PlayerCharacterState.Idle, false, 0f)]
        public void PlayerMovementSound_MapsStateToExpectedPitch(
            PlayerCharacterState state,
            bool expectedPlaying,
            float expectedPitch)
        {
            MethodInfo method = typeof(VoxelPlayerController).GetMethod(
                "TryGetMovementSoundPitch",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            object[] arguments = { state, 0f };

            bool playing = (bool)method.Invoke(null, arguments);

            Assert.That(playing, Is.EqualTo(expectedPlaying));
            Assert.That(
                (float)arguments[1],
                Is.EqualTo(expectedPitch).Within(0.0001f));
        }

        [TestCase(true, true, false, true, false, true, 1.75f)]
        [TestCase(true, true, true, true, false, true, 1f)]
        [TestCase(false, true, false, true, false, false, 0f)]
        [TestCase(true, false, false, true, false, false, 0f)]
        [TestCase(true, true, false, false, false, false, 0f)]
        [TestCase(true, true, false, true, true, false, 0f)]
        public void PlayerMovementSound_ToolActionKeepsFootstepsWhileWalking(
            bool toolAllowsMovement,
            bool grounded,
            bool crouching,
            bool movementInputActive,
            bool swinging,
            bool expectedPlaying,
            float expectedPitch)
        {
            MethodInfo method = typeof(VoxelPlayerController).GetMethod(
                "TryGetToolActionMovementSoundPitch",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            object[] arguments =
            {
                toolAllowsMovement,
                grounded,
                crouching,
                movementInputActive,
                swinging,
                0f,
            };

            bool playing = (bool)method.Invoke(null, arguments);

            Assert.That(playing, Is.EqualTo(expectedPlaying));
            Assert.That(
                (float)arguments[5],
                Is.EqualTo(expectedPitch).Within(0.0001f));
        }


        [Test]
        public void PlayerPrefab_UsesRunClipForMovementSound()
        {
            SoundEffectCue cue = AssetDatabase.LoadAssetAtPath<SoundEffectCue>(
                ProjectAssetPaths.Config.RunMovementSound);
            AudioClip runClip = AssetDatabase.LoadAssetAtPath<AudioClip>(
                ProjectAssetPaths.Audio.Run);
            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProjectAssetPaths.Prefabs.Player);

            Assert.That(cue, Is.Not.Null);
            Assert.That(runClip, Is.Not.Null);
            Assert.That(playerPrefab, Is.Not.Null);

            VoxelPlayerController player =
                playerPrefab.GetComponentInChildren<VoxelPlayerController>(true);
            Assert.That(player, Is.Not.Null);
            SerializedObject serializedPlayer = new SerializedObject(player);
            Assert.That(
                serializedPlayer.FindProperty("movementSound")
                    .objectReferenceValue,
                Is.SameAs(cue));

            SerializedObject serializedCue = new SerializedObject(cue);
            SerializedProperty clips = serializedCue.FindProperty("clips");
            Assert.That(clips.arraySize, Is.EqualTo(1));
            Assert.That(
                clips.GetArrayElementAtIndex(0).objectReferenceValue,
                Is.SameAs(runClip));
        }

        [Test]
        public void PlayerPrefab_UsesHomeFootstepClipForHomeSceneMovement()
        {
            SoundEffectCue cue = AssetDatabase.LoadAssetAtPath<SoundEffectCue>(
                ProjectAssetPaths.Config.HomeCellMovementSound);
            AudioClip homeFootstep = AssetDatabase.LoadAssetAtPath<AudioClip>(
                ProjectAssetPaths.Audio.HomeFootstep);
            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProjectAssetPaths.Prefabs.Player);

            Assert.That(cue, Is.Not.Null);
            Assert.That(homeFootstep, Is.Not.Null);
            Assert.That(playerPrefab, Is.Not.Null);

            VoxelPlayerController player =
                playerPrefab.GetComponentInChildren<VoxelPlayerController>(true);
            Assert.That(player, Is.Not.Null);
            SerializedObject serializedPlayer = new SerializedObject(player);
            Assert.That(
                serializedPlayer.FindProperty("homeCellMovementSound")
                    .objectReferenceValue,
                Is.SameAs(cue));

            SerializedObject serializedCue = new SerializedObject(cue);
            SerializedProperty clips = serializedCue.FindProperty("clips");
            Assert.That(clips.arraySize, Is.EqualTo(1));
            Assert.That(
                clips.GetArrayElementAtIndex(0).objectReferenceValue,
                Is.SameAs(homeFootstep));
        }

        [Test]
        public void PlayerMovementSound_HomeSceneOverridesDefaultCue()
        {
            SoundEffectCue defaultCue =
                ScriptableObject.CreateInstance<SoundEffectCue>();
            SoundEffectCue homeCue =
                ScriptableObject.CreateInstance<SoundEffectCue>();
            MethodInfo method = typeof(VoxelPlayerController).GetMethod(
                "SelectMovementSound",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);

            try
            {
                Assert.That(
                    method.Invoke(
                        null,
                        new object[] { defaultCue, homeCue, false }),
                    Is.SameAs(defaultCue));
                Assert.That(
                    method.Invoke(
                        null,
                        new object[] { defaultCue, homeCue, true }),
                    Is.SameAs(homeCue));
                Assert.That(
                    method.Invoke(
                        null,
                        new object[] { defaultCue, null, true }),
                    Is.SameAs(defaultCue));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(defaultCue);
                UnityEngine.Object.DestroyImmediate(homeCue);
            }
        }

        [Test]
        public void PlayerMovementSound_RecognizesConfiguredHomeScene()
        {
            MethodInfo method = typeof(VoxelPlayerController).GetMethod(
                "IsHomeScene",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);

            Assert.That(
                method.Invoke(null, new object[] { "Home", "Home" }),
                Is.True);
            Assert.That(
                method.Invoke(null, new object[] { "InfiniteCaves", "Home" }),
                Is.False);
            Assert.That(
                method.Invoke(null, new object[] { "Home", string.Empty }),
                Is.False);
        }

        [Test]
        public void PlayerPrefab_UsesMagnetClipForMagnetInteractionSound()
        {
            SoundEffectCue cue = AssetDatabase.LoadAssetAtPath<SoundEffectCue>(
                ProjectAssetPaths.Config.MagnetInteractionSound);
            AudioClip magnetClip = AssetDatabase.LoadAssetAtPath<AudioClip>(
                ProjectAssetPaths.Audio.Magnet);
            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProjectAssetPaths.Prefabs.Player);

            Assert.That(cue, Is.Not.Null);
            Assert.That(magnetClip, Is.Not.Null);
            Assert.That(playerPrefab, Is.Not.Null);

            VoxelPlayerController player =
                playerPrefab.GetComponentInChildren<VoxelPlayerController>(true);
            Assert.That(player, Is.Not.Null);
            SerializedObject serializedPlayer = new SerializedObject(player);
            Assert.That(
                serializedPlayer.FindProperty("magnetSound")
                    .objectReferenceValue,
                Is.SameAs(cue));

            SerializedObject serializedCue = new SerializedObject(cue);
            SerializedProperty clips = serializedCue.FindProperty("clips");
            Assert.That(clips.arraySize, Is.EqualTo(1));
            Assert.That(
                clips.GetArrayElementAtIndex(0).objectReferenceValue,
                Is.SameAs(magnetClip));
        }

        [Test]
        public void PlayerMagnetSound_StartsAndStopsOneStableLoop()
        {
            GameObject playerObject = new GameObject("Magnet Sound Player");
            VoxelPlayerController player =
                playerObject.AddComponent<VoxelPlayerController>();
            SoundEffectCue cue =
                ScriptableObject.CreateInstance<SoundEffectCue>();
            SerializedObject serializedPlayer = new SerializedObject(player);
            serializedPlayer.FindProperty("magnetSound").objectReferenceValue =
                cue;
            serializedPlayer.ApplyModifiedPropertiesWithoutUndo();
            MethodInfo updateSound = typeof(VoxelPlayerController).GetMethod(
                "UpdateMagnetSound",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(updateSound, Is.Not.Null);

            int startCount = 0;
            int stopCount = 0;
            int startedLoopId = 0;
            int stoppedLoopId = 0;
            SoundEffectLoopRequest startedRequest = default;
            SoundEffectLoopStopRequest stoppedRequest = default;
            Action<SoundEffectLoopRequest> startObserver = request =>
            {
                startCount++;
                startedRequest = request;
                startedLoopId = request.LoopId;
            };
            Action<SoundEffectLoopStopRequest> stopObserver = request =>
            {
                stopCount++;
                stoppedRequest = request;
                stoppedLoopId = request.LoopId;
            };

            SoundEffectEvents.LoopRequested += startObserver;
            SoundEffectEvents.LoopStopRequested += stopObserver;
            try
            {
                updateSound.Invoke(player, new object[] { true });
                updateSound.Invoke(player, new object[] { true });
                Assert.That(startCount, Is.EqualTo(1));
                Assert.That(startedLoopId, Is.Not.Zero);
                Assert.That(
                    startedRequest.VolumeScale,
                    Is.EqualTo(0.5f).Within(0.0001f));
                Assert.That(
                    startedRequest.FadeInSeconds,
                    Is.EqualTo(0.2f).Within(0.0001f));

                updateSound.Invoke(player, new object[] { false });
                updateSound.Invoke(player, new object[] { false });
                Assert.That(stopCount, Is.EqualTo(1));
                Assert.That(stoppedLoopId, Is.EqualTo(startedLoopId));
                Assert.That(
                    stoppedRequest.FadeOutSeconds,
                    Is.EqualTo(0.2f).Within(0.0001f));

                updateSound.Invoke(player, new object[] { true });
                Assert.That(startCount, Is.EqualTo(2));
                Assert.That(startedLoopId, Is.EqualTo(stoppedLoopId));
            }
            finally
            {
                SoundEffectEvents.LoopRequested -= startObserver;
                SoundEffectEvents.LoopStopRequested -= stopObserver;
                UnityEngine.Object.DestroyImmediate(playerObject);
                UnityEngine.Object.DestroyImmediate(cue);
            }
        }

        [Test]
        public void MagnetInteractor_ReportsAttractionOnlyWithAnActiveTarget()
        {
            GameObject interactorObject = new GameObject("Magnet Interactor");
            FirstPersonMagnetInteractor interactor =
                interactorObject.AddComponent<FirstPersonMagnetInteractor>();
            GameObject targetObject = new GameObject("Magnet Target");
            Rigidbody targetBody = targetObject.AddComponent<Rigidbody>();
            FieldInfo actionActiveField =
                typeof(FirstPersonMagnetInteractor).GetField(
                    "magnetActionActive",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo heldBodyField =
                typeof(FirstPersonMagnetInteractor).GetField(
                    "heldBody",
                    BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(actionActiveField, Is.Not.Null);
            Assert.That(heldBodyField, Is.Not.Null);
            try
            {
                Assert.That(interactor.IsAttractingTarget, Is.False);

                actionActiveField.SetValue(interactor, true);
                Assert.That(
                    interactor.IsAttractingTarget,
                    Is.False,
                    "Holding magnet input without a target must stay silent.");

                heldBodyField.SetValue(interactor, targetBody);
                Assert.That(interactor.IsAttractingTarget, Is.True);

                actionActiveField.SetValue(interactor, false);
                Assert.That(interactor.IsAttractingTarget, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(interactorObject);
                UnityEngine.Object.DestroyImmediate(targetObject);
            }
        }

        [Test]
        public void PlayerToolDefinition_PrimaryActionSound_IsConfigurable()
        {
            PlayerToolDefinition definition =
                ScriptableObject.CreateInstance<PlayerToolDefinition>();
            SoundEffectCue cue =
                ScriptableObject.CreateInstance<SoundEffectCue>();

            try
            {
                SerializedObject serializedDefinition =
                    new SerializedObject(definition);
                serializedDefinition
                    .FindProperty("primaryActionSound")
                    .objectReferenceValue = cue;
                serializedDefinition.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(
                    definition.PrimaryActionSound,
                    Is.SameAs(cue));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(definition);
                UnityEngine.Object.DestroyImmediate(cue);
            }
        }

        private static SoundEffectCue CreateCue(
            AudioClip clip,
            float volume,
            float spatialBlend,
            float minimumDistance,
            float maximumDistance,
            AudioRolloffMode rolloffMode)
        {
            SoundEffectCue cue =
                ScriptableObject.CreateInstance<SoundEffectCue>();
            SerializedObject serializedCue = new SerializedObject(cue);
            SerializedProperty clips = serializedCue.FindProperty("clips");
            clips.arraySize = 1;
            clips.GetArrayElementAtIndex(0).objectReferenceValue = clip;
            serializedCue.FindProperty("volume").floatValue = volume;
            serializedCue.FindProperty("pitchRange").vector2Value =
                Vector2.one;
            serializedCue.FindProperty("spatialBlend").floatValue =
                spatialBlend;
            serializedCue.FindProperty("minimumDistance").floatValue =
                minimumDistance;
            serializedCue.FindProperty("maximumDistance").floatValue =
                maximumDistance;
            serializedCue.FindProperty("rolloffMode").enumValueIndex =
                (int)rolloffMode;
            serializedCue.ApplyModifiedPropertiesWithoutUndo();
            return cue;
        }

        private static void InvokeLifecycle(
            AudioManager manager,
            string methodName)
        {
            MethodInfo method = typeof(AudioManager).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(manager, null);
        }

        private static void InvokeLoopUpdate(
            AudioManager manager,
            float deltaTime)
        {
            MethodInfo method = typeof(AudioManager).GetMethod(
                "UpdateLoops",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(manager, new object[] { deltaTime });
        }
    

        [Test]
        public void PlayerLandingSound_SelectsSmallAndBigByDownwardYAxisSpeed()
        {
            SoundEffectCue small =
                ScriptableObject.CreateInstance<SoundEffectCue>();
            SoundEffectCue big =
                ScriptableObject.CreateInstance<SoundEffectCue>();
            MethodInfo method = typeof(VoxelPlayerController).GetMethod(
                "SelectLandingSound",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);

            try
            {
                Assert.That(
                    method.Invoke(null, new object[] { 7.99f, small, big }),
                    Is.Null);
                Assert.That(
                    method.Invoke(null, new object[] { 8f, small, big }),
                    Is.SameAs(small));
                Assert.That(
                    method.Invoke(null, new object[] { 15.99f, small, big }),
                    Is.SameAs(small));
                Assert.That(
                    method.Invoke(null, new object[] { 16f, small, big }),
                    Is.SameAs(big));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(small);
                UnityEngine.Object.DestroyImmediate(big);
            }
        }

        [Test]
        public void PlayerLandingSound_IgnoresHorizontalVelocity()
        {
            MethodInfo method = typeof(VoxelPlayerController).GetMethod(
                "GetDownwardYAxisSpeed",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);

            Assert.That(
                method.Invoke(
                    null,
                    new object[] { new Vector3(1000f, -9f, -1000f) }),
                Is.EqualTo(9f));
            Assert.That(
                method.Invoke(
                    null,
                    new object[] { new Vector3(1000f, 3f, -1000f) }),
                Is.EqualTo(0f));
        }


        [TestCase(CreatureBehaviorState.Wander, true, true, true)]
        [TestCase(CreatureBehaviorState.Pursue, true, true, true)]
        [TestCase(CreatureBehaviorState.Attack, true, true, false)]
        [TestCase(CreatureBehaviorState.Pursue, false, true, false)]
        [TestCase(CreatureBehaviorState.Pursue, true, false, false)]
        public void CreatureMovementSound_RequiresLiveMovingTravelState(
            CreatureBehaviorState state,
            bool moving,
            bool alive,
            bool expected)
        {
            MethodInfo method = typeof(CreatureBehaviorAgent).GetMethod(
                "ShouldPlayMovementSound",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            Assert.That(
                method.Invoke(null, new object[] { state, moving, alive }),
                Is.EqualTo(expected));
        }

        [Test]
        public void GameplaySoundCatalog_UsesRequestedAudioClips()
        {
            GameAssetCatalog catalog =
                AssetDatabase.LoadAssetAtPath<GameAssetCatalog>(
                    ProjectAssetPaths.Config.GameAssetCatalog);
            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.Audio, Is.Not.Null);
            Assert.That(catalog.Audio.IsComplete, Is.True);

            AssertCueUsesClips(
                catalog.Audio.CreatureRun,
                ProjectAssetPaths.Audio.Run);
            AssertCueUsesClips(
                catalog.Audio.CreatureAttack,
                ProjectAssetPaths.Audio.Woosh);
            AssertCueUsesClips(
                catalog.Audio.CreatureHitPlayer,
                ProjectAssetPaths.Audio.Hit1,
                ProjectAssetPaths.Audio.Hit2,
                ProjectAssetPaths.Audio.Hit3);
            AssertCueUsesClips(
                catalog.Audio.PlayerFallSmall,
                ProjectAssetPaths.Audio.FallSmall);
            AssertCueUsesClips(
                catalog.Audio.PlayerFallBig,
                ProjectAssetPaths.Audio.FallBig);
            AssertCueUsesClips(
                catalog.Audio.BombFuse,
                ProjectAssetPaths.Audio.Fuse);
            AssertCueUsesClips(
                catalog.Audio.BombExplosion,
                ProjectAssetPaths.Audio.Explode1,
                ProjectAssetPaths.Audio.Explode2,
                ProjectAssetPaths.Audio.Explode3,
                ProjectAssetPaths.Audio.Explode4);
        }

        private static void AssertCueUsesClips(
            SoundEffectCue cue,
            params string[] expectedClipPaths)
        {
            Assert.That(cue, Is.Not.Null);
            SerializedObject serialized = new SerializedObject(cue);
            SerializedProperty clips = serialized.FindProperty("clips");
            Assert.That(clips.arraySize, Is.EqualTo(expectedClipPaths.Length));
            for (int i = 0; i < expectedClipPaths.Length; i++)
            {
                AudioClip expected =
                    AssetDatabase.LoadAssetAtPath<AudioClip>(
                        expectedClipPaths[i]);
                Assert.That(expected, Is.Not.Null, expectedClipPaths[i]);
                Assert.That(
                    clips.GetArrayElementAtIndex(i).objectReferenceValue,
                    Is.SameAs(expected));
            }
        }
}
}

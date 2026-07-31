using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Supernova.MinecraftCaves;
using Supernova.MinecraftCaves.Creatures;
using Supernova.Voxels;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityAnimatorController = UnityEditor.Animations.AnimatorController;

namespace Supernova.Tests
{
    public sealed class MonsterSpawnConfigurationTests
    {
        private const string TablePath =
            ProjectAssetPaths.Config.MonsterSpawnTable;

        [Test]
        public void DefaultTable_ContainsBehaviorReadyMonsterPrefabs()
        {
            MonsterSpawnTable table =
                AssetDatabase.LoadAssetAtPath<MonsterSpawnTable>(TablePath);
            Assert.That(table, Is.Not.Null);
            Assert.That(table.MaximumActiveMonsters, Is.GreaterThan(0));
            Assert.That(table.MaximumMonsterSpawnsPerFrame, Is.EqualTo(1));
            Assert.That(table.SecondsBetweenMonsterGroups, Is.EqualTo(0.75f));
            Assert.That(table.Monsters, Is.Not.Empty);
            CollectionAssert.AreEquivalent(
                new[] { "Cactus Mob", "Skeleton", "Skeleton Giant" },
                table.Monsters.Select(definition => definition.Prefab.name));

            foreach (MonsterSpawnDefinition definition in table.Monsters)
            {
                Assert.That(definition, Is.Not.Null);
                Assert.That(definition.Prefab, Is.Not.Null);
                Assert.That(definition.AttemptsPerChunk, Is.EqualTo(2));
                Assert.That(definition.MinimumGroupSize, Is.GreaterThanOrEqualTo(2));
                Assert.That(
                    definition.MaximumGroupSize,
                    Is.GreaterThanOrEqualTo(definition.MinimumGroupSize));
                Assert.That(definition.GroupRadiusInVoxels, Is.GreaterThan(0f));
                Assert.That(
                    definition.Prefab.GetComponent<CreatureBehaviorAgent>(),
                    Is.Not.Null,
                    definition.name + " needs a CreatureBehaviorAgent on its root.");
                Assert.That(
                    definition.Prefab.GetComponent<Rigidbody>(),
                    Is.Not.Null,
                    definition.name + " needs a Rigidbody on its root.");
                Assert.That(
                    definition.Prefab.GetComponent<Collider>(),
                    Is.Not.Null,
                    definition.name + " needs a root Collider.");
                CreatureVoxelShapeAuthoring shapeAuthoring =
                    definition.Prefab.GetComponent<CreatureVoxelShapeAuthoring>();
                Assert.That(
                    shapeAuthoring,
                    Is.Not.Null,
                    definition.name + " needs voxel navigation authoring.");
                Assert.That(
                    shapeAuthoring.Shape,
                    Is.Not.Null,
                    definition.name + " needs a baked voxel shape.");
                Assert.That(
                    shapeAuthoring.Shape.IsEmpty,
                    Is.False,
                    definition.name + " has an empty voxel shape.");
                Assert.That(
                    shapeAuthoring.Shape.BakedVoxelSize,
                    Is.EqualTo(0.42f).Within(0.0001f));
                Assert.That(
                    definition.Prefab.GetComponent<CreatureBehaviorAnimator>(),
                    Is.Not.Null,
                    definition.name + " needs the behavior animation bridge.");

                Animator animator =
                    definition.Prefab.GetComponentInChildren<Animator>(true);
                Assert.That(animator, Is.Not.Null);
                Assert.That(
                    animator.applyRootMotion,
                    Is.False,
                    definition.name + " must let the voxel motor drive movement.");
                UnityAnimatorController controller =
                    AssetDatabase.LoadAssetAtPath<UnityAnimatorController>(
                        AssetDatabase.GetAssetPath(
                            animator.runtimeAnimatorController));
                Assert.That(
                    controller,
                    Is.Not.Null,
                    definition.name + " needs an AnimatorController.");
                Assert.That(
                    controller.parameters.Any(parameter =>
                        parameter.name
                            == CreatureBehaviorAnimator.BehaviorStateParameter
                        && parameter.type == AnimatorControllerParameterType.Int),
                    Is.True,
                    definition.name + " needs the BehaviorState parameter.");
                CollectionAssert.AreEquivalent(
                    new[] { "Idle", "Wander", "Pursue", "Attack", "Hurt", "Dead" },
                    controller.layers[0].stateMachine.states
                        .Select(state => state.state.name));
                Assert.That(
                    controller.layers[0].stateMachine.states.All(
                        state => state.state.motion != null),
                    Is.True,
                    definition.name + " has an animation state without a clip.");

                AnimatorStateTransition[] transitions =
                    controller.layers[0].stateMachine.anyStateTransitions;
                Assert.That(transitions, Has.Length.EqualTo(6));
                string[] stateNames =
                    { "Idle", "Wander", "Pursue", "Attack", "Hurt", "Dead" };
                for (int stateIndex = 0;
                    stateIndex < stateNames.Length;
                    stateIndex++)
                {
                    int expectedState = stateIndex;
                    Assert.That(
                        transitions.Any(transition =>
                            transition.destinationState != null
                            && transition.destinationState.name
                                == stateNames[expectedState]
                            && transition.conditions.Any(condition =>
                                condition.parameter
                                    == CreatureBehaviorAnimator
                                        .BehaviorStateParameter
                                && condition.mode
                                    == AnimatorConditionMode.Equals
                                && Mathf.Approximately(
                                    condition.threshold,
                                    expectedState))),
                        Is.True,
                        definition.name + " does not transition to "
                            + stateNames[expectedState] + ".");
                }
            }
        }

        [TestCase(0, 0, true)]
        [TestCase(1, 0, true)]
        [TestCase(-1, 1, true)]
        [TestCase(2, 0, false)]
        [TestCase(0, -2, false)]
        public void MonsterSpawnChunkExclusion_CoversSpawnChunkAndNeighbors(
            int xOffset,
            int zOffset,
            bool expected)
        {
            MethodInfo method = typeof(MinecraftCaveInfiniteWorld).GetMethod(
                "IsMonsterSpawnChunkExcluded",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            var playerSpawnChunk = new Vector3Int(10, 0, -4);
            var candidateChunk = playerSpawnChunk
                + new Vector3Int(xOffset, 0, zOffset);

            Assert.That(
                method.Invoke(
                    null,
                    new object[] { candidateChunk, playerSpawnChunk }),
                Is.EqualTo(expected));
        }

        [TestCase(2, -3)]
        [TestCase(-2, 3)]
        public void NaturalSpawnSampling_UsesRequestedChunkInterior(
            int chunkX,
            int chunkZ)
        {
            MethodInfo createRandom =
                typeof(MinecraftCaveInfiniteWorld).GetMethod(
                    "CreateNaturalSpawnRandom",
                    BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo sampleAttempt =
                typeof(MinecraftCaveInfiniteWorld).GetMethod(
                    "SampleNaturalSpawnAttempt",
                    BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(createRandom, Is.Not.Null);
            Assert.That(sampleAttempt, Is.Not.Null);

            var column = new Vector3Int(chunkX, 0, chunkZ);
            var random = (System.Random)createRandom.Invoke(
                null,
                new object[] { 18731, column, 1, 0 });
            for (int index = 0; index < 32; index++)
            {
                object attempt = sampleAttempt.Invoke(
                    null,
                    new object[] { random, column });
                System.Type attemptType = attempt.GetType();
                int x = (int)attemptType.GetProperty("X").GetValue(attempt);
                int z = (int)attemptType.GetProperty("Z").GetValue(attempt);
                int startY = (int)attemptType
                    .GetProperty("StartY")
                    .GetValue(attempt);
                double spawnRoll = (double)attemptType
                    .GetProperty("SpawnRoll")
                    .GetValue(attempt);

                Assert.That(
                    x,
                    Is.InRange(
                        chunkX * VoxelColumnChunkData.Width + 1,
                        (chunkX + 1) * VoxelColumnChunkData.Width - 2));
                Assert.That(
                    z,
                    Is.InRange(
                        chunkZ * VoxelColumnChunkData.Depth + 1,
                        (chunkZ + 1) * VoxelColumnChunkData.Depth - 2));
                Assert.That(
                    startY,
                    Is.InRange(2, VoxelColumnChunkData.Height - 4));
                Assert.That(spawnRoll, Is.InRange(0d, 1d));
            }
        }

        [Test]
        public void LivingMonsterCount_IgnoresDisabledAgents()
        {
            var worldObject = new GameObject("World");
            var activeObject = new GameObject("Active Monster");
            var disabledComponentObject =
                new GameObject("Disabled Component Monster");
            var inactiveObject = new GameObject("Inactive Monster");
            try
            {
                MinecraftCaveInfiniteWorld world =
                    worldObject.AddComponent<MinecraftCaveInfiniteWorld>();
                CreatureBehaviorAgent active =
                    activeObject.AddComponent<CreatureBehaviorAgent>();
                CreatureBehaviorAgent disabledComponent =
                    disabledComponentObject.AddComponent<CreatureBehaviorAgent>();
                CreatureBehaviorAgent inactive =
                    inactiveObject.AddComponent<CreatureBehaviorAgent>();
                disabledComponent.enabled = false;
                inactiveObject.SetActive(false);

                FieldInfo activeMonstersField =
                    typeof(MinecraftCaveInfiniteWorld).GetField(
                        "activeMonsters",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo countLivingMonsters =
                    typeof(MinecraftCaveInfiniteWorld).GetMethod(
                        "CountLivingMonsters",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(activeMonstersField, Is.Not.Null);
                Assert.That(countLivingMonsters, Is.Not.Null);

                var monsters =
                    (List<CreatureBehaviorAgent>)activeMonstersField.GetValue(world);
                monsters.Add(active);
                monsters.Add(disabledComponent);
                monsters.Add(inactive);

                Assert.That(countLivingMonsters.Invoke(world, null), Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(inactiveObject);
                Object.DestroyImmediate(disabledComponentObject);
                Object.DestroyImmediate(activeObject);
                Object.DestroyImmediate(worldObject);
            }
        }

        [Test]
        public void GeneratedMonster_CanBindWorldAndPlayerContext()
        {
            var worldObject = new GameObject("World");
            var playerObject = new GameObject("Player Foot");
            var monsterObject = new GameObject("Monster");
            try
            {
                MinecraftCaveInfiniteWorld world =
                    worldObject.AddComponent<MinecraftCaveInfiniteWorld>();
                CreatureBehaviorAgent agent =
                    monsterObject.AddComponent<CreatureBehaviorAgent>();

                agent.BindWorldContext(world, playerObject.transform);

                Assert.That(agent.CaveWorld, Is.SameAs(world));
                Assert.That(agent.PlayerFoot, Is.SameAs(playerObject.transform));
            }
            finally
            {
                Object.DestroyImmediate(monsterObject);
                Object.DestroyImmediate(playerObject);
                Object.DestroyImmediate(worldObject);
            }
        }

    }
}

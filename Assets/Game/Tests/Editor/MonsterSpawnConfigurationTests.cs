using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Supernova.MinecraftCaves;
using Supernova.MinecraftCaves.Creatures;
using Supernova.Missions;
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
            Assert.That(table.SpawnCellSizeInChunks, Is.GreaterThanOrEqualTo(3));
            Assert.That(table.Monsters, Is.Not.Empty);
            CollectionAssert.AreEquivalent(
                new[] { "Cactus Mob", "Skeleton", "Skeleton Giant" },
                table.Monsters.Select(definition => definition.Prefab.name));

            foreach (MonsterSpawnDefinition definition in table.Monsters)
            {
                Assert.That(definition, Is.Not.Null);
                Assert.That(definition.Prefab, Is.Not.Null);
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

        [TestCase(0, 6, 0)]
        [TestCase(5, 6, 0)]
        [TestCase(6, 6, 1)]
        [TestCase(-1, 6, -1)]
        [TestCase(-6, 6, -1)]
        [TestCase(-7, 6, -2)]
        public void SpawnCellCoordinates_UseFloorDivision(
            int value,
            int divisor,
            int expected)
        {
            MethodInfo method = typeof(MinecraftCaveInfiniteWorld).GetMethod(
                "FloorDivide",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            Assert.That(
                method.Invoke(null, new object[] { value, divisor }),
                Is.EqualTo(expected));
        }

        [Test]
        public void SpawnExclusion_UsesConfiguredMonsterRadius()
        {
            MonsterSpawnTable table =
                AssetDatabase.LoadAssetAtPath<MonsterSpawnTable>(TablePath);
            LevelConfiguration level =
                AssetDatabase.LoadAssetAtPath<LevelConfiguration>(
                    ProjectAssetPaths.Config.FirstLevel);
            Assert.That(table, Is.Not.Null);
            Assert.That(level, Is.Not.Null);

            var worldObject = new GameObject("World");
            try
            {
                MinecraftCaveInfiniteWorld world =
                    worldObject.AddComponent<MinecraftCaveInfiniteWorld>();
                Assert.That(world.ApplyLevelConfiguration(level), Is.True);
                Assert.That(level.MonsterGeneration, Is.SameAs(table));
                SetField(
                    world,
                    "targetSpawnWorldPosition",
                    new Vector3(10f, 4f, 10f));

                Assert.That(
                    IsExcluded(world, new Vector3(15f, 100f, 10f)),
                    Is.True);
                Assert.That(
                    IsExcluded(world, new Vector3(30f, 4f, 10f)),
                    Is.False);
            }
            finally
            {
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

        private static bool IsExcluded(
            MinecraftCaveInfiniteWorld world,
            Vector3 position)
        {
            MethodInfo method = typeof(MinecraftCaveInfiniteWorld).GetMethod(
                "IsInsideMonsterSpawnExclusion",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (bool)method.Invoke(world, new object[] { position });
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }
    }
}

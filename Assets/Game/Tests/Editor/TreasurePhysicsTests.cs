using NUnit.Framework;
using Supernova.Gameplay;
using UnityEditor;
using UnityEngine;

namespace Supernova.Tests
{
    public sealed class TreasurePhysicsTests
    {
        [Test]
        public void ConfiguredTreasures_UseDynamicRigidbodyCompatibleColliders()
        {
            TreasureSpawnTable table =
                AssetDatabase.LoadAssetAtPath<TreasureSpawnTable>(
                    "Assets/Game/Config/TreasureSpawnTable.asset");
            Assert.That(table, Is.Not.Null);

            foreach (TreasureDefinition definition in table.Treasures)
            {
                Assert.That(definition, Is.Not.Null);
                Assert.That(definition.Prefab, Is.Not.Null);
                Collider[] colliders =
                    definition.Prefab.GetComponentsInChildren<Collider>(true);
                Assert.That(colliders, Is.Not.Empty,
                    definition.name + " needs a physical collider.");
                for (int i = 0; i < colliders.Length; i++)
                {
                    MeshCollider meshCollider = colliders[i] as MeshCollider;
                    if (meshCollider == null) continue;
                    Assert.That(meshCollider.convex, Is.True,
                        definition.name
                        + " cannot combine a non-convex MeshCollider "
                        + "with its runtime dynamic Rigidbody.");
                }
                if (definition.name == "HolyBookTreasure")
                {
                    BoxCollider bookCollider =
                        definition.Prefab.GetComponent<BoxCollider>();
                    Assert.That(bookCollider, Is.Not.Null,
                        "The thin holy book needs a stable box collider.");
                    Assert.That(bookCollider.size.y, Is.GreaterThanOrEqualTo(0.1f));
                }
            }
        }
    }
}

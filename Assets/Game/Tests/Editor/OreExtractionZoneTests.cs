using System.Reflection;
using NUnit.Framework;
using Supernova.Gameplay;
using Supernova.Missions;
using Supernova.Voxels;
using UnityEngine;

namespace Supernova.Tests
{
    public sealed class OreExtractionZoneTests
    {
        private GameObject zoneObject;
        private GameObject treasureObject;
        private TreasureDefinition definition;

        [TearDown]
        public void TearDown()
        {
            if (zoneObject != null) Object.DestroyImmediate(zoneObject);
            if (treasureObject != null) Object.DestroyImmediate(treasureObject);
            if (definition != null) Object.DestroyImmediate(definition);
        }

        [Test]
        public void Treasure_RemainsStoredAndValueTracksZonePresence()
        {
            zoneObject = new GameObject("Extraction");
            OreExtractionZone zone = zoneObject.AddComponent<OreExtractionZone>();
            zone.Configure(null);

            definition = ScriptableObject.CreateInstance<TreasureDefinition>();
            definition.Configure(null, 75, 2f, 1f, 1);
            treasureObject = new GameObject("Treasure");
            treasureObject.AddComponent<Rigidbody>().isKinematic = true;
            BoxCollider collider = treasureObject.AddComponent<BoxCollider>();
            TreasurePickup treasure =
                treasureObject.AddComponent<TreasurePickup>();
            treasure.Configure(definition);

            InvokeTrigger(zone, "OnTriggerEnter", collider);

            Assert.That(zone.CurrentStoredValue, Is.EqualTo(75));
            Assert.That(treasureObject, Is.Not.Null,
                "Stored resources must remain physically present.");

            InvokeTrigger(zone, "OnTriggerExit", collider);
            Assert.That(zone.CurrentStoredValue, Is.Zero);
        }

        [Test]
        public void MinedOre_UsesValueStoredOnItsDrop()
        {
            zoneObject = new GameObject("Extraction");
            OreExtractionZone zone = zoneObject.AddComponent<OreExtractionZone>();
            zone.Configure(null);

            treasureObject = new GameObject("Recovered Ore");
            treasureObject.AddComponent<Rigidbody>().isKinematic = true;
            BoxCollider collider = treasureObject.AddComponent<BoxCollider>();
            MinedOreDrop drop = treasureObject.AddComponent<MinedOreDrop>();
            drop.Configure(
                new VoxelTypeId(3),
                4,
                new Mesh(),
                valuePerVoxel: 25);

            InvokeTrigger(zone, "OnTriggerEnter", collider);

            Assert.That(zone.CurrentStoredValue, Is.EqualTo(100));
        }

        private static void InvokeTrigger(
            OreExtractionZone zone,
            string methodName,
            Collider collider)
        {
            MethodInfo method = typeof(OreExtractionZone).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(zone, new object[] { collider });
        }
    }
}

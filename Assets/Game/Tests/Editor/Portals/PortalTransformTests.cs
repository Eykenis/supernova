using NUnit.Framework;
using Supernova.Portals;

using UnityEngine.TestTools.Utils;
using UnityEngine;

namespace Supernova.Tests.Editor.Portals
{
    public sealed class PortalTransformTests
    {
        [Test]
        public void MappingMatrix_MapsSourceCenterToDestinationCenter()
        {
            GameObject sourceObject = new GameObject("Source");
            GameObject destinationObject = new GameObject("Destination");
            Portal source = sourceObject.AddComponent<Portal>();
            Portal destination = destinationObject.AddComponent<Portal>();
            sourceObject.transform.position = new Vector3(0f, 2f, 3f);
            destinationObject.transform.position = new Vector3(7f, 1f, -4f);
            destinationObject.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

            Vector3 mapped = source.GetMappingMatrix(destination)
                .MultiplyPoint3x4(sourceObject.transform.position);

            Assert.That(mapped, Is.EqualTo(destinationObject.transform.position)
                .Using(Vector3ComparerWithEqualsOperator.Instance));
            Object.DestroyImmediate(sourceObject);
            Object.DestroyImmediate(destinationObject);
        }

        [Test]
        public void TransformDirection_PreservesSpeed()
        {
            GameObject sourceObject = new GameObject("Source");
            GameObject destinationObject = new GameObject("Destination");
            Portal source = sourceObject.AddComponent<Portal>();
            Portal destination = destinationObject.AddComponent<Portal>();
            destinationObject.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            Vector3 velocity = new Vector3(1f, -4f, 7f);

            Vector3 mapped = Portal.TransformDirection(
                source,
                destination,
                velocity);

            Assert.That(mapped.magnitude, Is.EqualTo(velocity.magnitude).Within(0.0001f));
            Object.DestroyImmediate(sourceObject);
            Object.DestroyImmediate(destinationObject);
        }
    }
}

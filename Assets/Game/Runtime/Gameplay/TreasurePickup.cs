using UnityEngine;
using System.Collections;

namespace Supernova.Gameplay
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class TreasurePickup : MonoBehaviour
    {
        [SerializeField] private TreasureDefinition definition;
        public TreasureDefinition Definition => definition;
        public int Value => definition != null ? definition.Value : 0;

        public void Configure(TreasureDefinition value)
        {
            definition = value;
            Rigidbody body = GetComponent<Rigidbody>();
            if (definition != null) body.mass = definition.Weight;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.solverIterations = Mathf.Max(body.solverIterations, 12);
            body.solverVelocityIterations =
                Mathf.Max(body.solverVelocityIterations, 6);
            body.maxAngularVelocity = Mathf.Min(body.maxAngularVelocity, 12f);
            body.drag = Mathf.Max(body.drag, 0.15f);
            body.angularDrag = Mathf.Max(body.angularDrag, 0.4f);
            body.isKinematic = true;
            if (Application.isPlaying)
            {
                StartCoroutine(ArmPhysicsAfterTerrainSettles(body));
            }
        }

        private static IEnumerator ArmPhysicsAfterTerrainSettles(Rigidbody body)
        {
            // The terrain mesh/collider is committed during Update. Waiting for a
            // physics step prevents a newly spawned treasure from integrating
            // against the previous PhysicsScene and falling through the cave.
            yield return new WaitForFixedUpdate();
            Physics.SyncTransforms();
            if (body != null)
            {
                body.isKinematic = false;
                body.WakeUp();
            }
        }
    }
}

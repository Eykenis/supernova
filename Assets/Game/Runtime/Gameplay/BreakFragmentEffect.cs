using System;
using System.Collections.Generic;
using UnityEngine;

namespace Supernova.Gameplay
{
    /// <summary>
    /// Owns one spawned fracture group, gives each direct child independent
    /// physics, and removes the entire effect after five seconds.
    /// </summary>
    public sealed class BreakFragmentEffect : MonoBehaviour
    {
        public const float DefaultLifetime = 5f;

        private readonly List<Rigidbody> fragmentBodies =
            new List<Rigidbody>();
        private readonly List<Mesh> ownedMeshes = new List<Mesh>();
        private readonly List<Transform> fragmentTransforms =
            new List<Transform>();
        private readonly List<Vector3> initialFragmentScales =
            new List<Vector3>();

        private Material ownedMaterial;
        private Texture ownedTexture;
        private float lifetime = DefaultLifetime;
        private float age;

        public IReadOnlyList<Rigidbody> FragmentBodies => fragmentBodies;
        public float Lifetime => lifetime;
        public float NormalizedAge =>
            lifetime > 0f ? Mathf.Clamp01(age / lifetime) : 1f;

        public static BreakFragmentEffect SpawnPrefab(
            GameObject variantPrefab,
            ValuableObject.BreakContext context)
        {
            if (variantPrefab == null)
            {
                return null;
            }

            GameObject root = Instantiate(
                variantPrefab,
                context.Position,
                context.Rotation);
            root.name = variantPrefab.name;
            root.transform.localScale = context.Scale;
            SetLayerRecursively(root, context.Layer);

            BreakFragmentEffect effect =
                root.GetComponent<BreakFragmentEffect>();
            if (effect == null)
            {
                effect = root.AddComponent<BreakFragmentEffect>();
            }

            if (!effect.Initialize(context, null, null))
            {
                DestroySafely(root);
                return null;
            }

            return effect;
        }

        public static BreakFragmentEffect SpawnMeshes(
            string effectName,
            IReadOnlyList<MeshFragmentBuilder.Fragment> fragments,
            Material[] materials,
            ValuableObject.BreakContext context,
            Material ownedMaterial = null,
            Texture ownedTexture = null)
        {
            if (fragments == null || fragments.Count == 0)
            {
                return null;
            }

            var root = new GameObject(effectName);
            root.hideFlags = HideFlags.DontSave;
            root.transform.SetPositionAndRotation(
                context.Position,
                context.Rotation);
            root.transform.localScale = context.Scale;
            root.layer = context.Layer;

            for (int i = 0; i < fragments.Count; i++)
            {
                MeshFragmentBuilder.Fragment fragment = fragments[i];
                var fragmentObject = new GameObject(
                    $"Fragment {i + 1}");
                fragmentObject.layer = context.Layer;
                fragmentObject.transform.SetParent(root.transform, false);
                fragmentObject.transform.localPosition =
                    fragment.LocalPosition;

                MeshFilter filter =
                    fragmentObject.AddComponent<MeshFilter>();
                filter.sharedMesh = fragment.Mesh;
                MeshRenderer renderer =
                    fragmentObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode =
                    UnityEngine.Rendering.ShadowCastingMode.Off;

                BoxCollider collider =
                    fragmentObject.AddComponent<BoxCollider>();
                collider.center = fragment.Mesh.bounds.center;
                collider.size = Vector3.Max(
                    fragment.Mesh.bounds.size,
                    Vector3.one * 0.04f);
            }

            BreakFragmentEffect effect =
                root.AddComponent<BreakFragmentEffect>();
            for (int i = 0; i < fragments.Count; i++)
            {
                effect.ownedMeshes.Add(fragments[i].Mesh);
            }

            effect.ownedMaterial = ownedMaterial;
            effect.ownedTexture = ownedTexture;
            if (!effect.Initialize(
                    context,
                    ownedMaterial,
                    ownedTexture))
            {
                DestroySafely(root);
                return null;
            }

            return effect;
        }

        public void Tick(float deltaTime)
        {
            age += Mathf.Max(0f, deltaTime);
            float shrinkStart = Mathf.Max(0f, lifetime - 0.6f);
            if (age >= shrinkStart)
            {
                float scale = 1f - Mathf.InverseLerp(
                    shrinkStart,
                    lifetime,
                    age);
                for (int i = 0; i < fragmentTransforms.Count; i++)
                {
                    Transform fragment = fragmentTransforms[i];
                    if (fragment != null)
                    {
                        fragment.localScale =
                            initialFragmentScales[i] * scale;
                    }
                }
            }

            if (age >= lifetime && Application.isPlaying)
            {
                Destroy(gameObject);
            }
        }

        private bool Initialize(
            ValuableObject.BreakContext context,
            Material materialToOwn,
            Texture textureToOwn)
        {
            ownedMaterial = materialToOwn;
            ownedTexture = textureToOwn;
            fragmentBodies.Clear();
            fragmentTransforms.Clear();
            initialFragmentScales.Clear();

            int fragmentCount = transform.childCount;
            if (fragmentCount == 0)
            {
                return false;
            }

            var random = new System.Random(context.RandomSeed);
            float fragmentMass =
                Mathf.Max(0.01f, context.Mass / fragmentCount);
            float impactSpeed = Mathf.Clamp(
                context.ImpactStrength * 0.16f,
                1.4f,
                4.5f);

            for (int i = 0; i < fragmentCount; i++)
            {
                Transform fragment = transform.GetChild(i);
                fragment.gameObject.SetActive(true);
                SetLayerRecursively(fragment.gameObject, context.Layer);
                fragmentTransforms.Add(fragment);
                initialFragmentScales.Add(fragment.localScale);

                Collider collider =
                    fragment.GetComponentInChildren<Collider>(true);
                if (collider == null)
                {
                    AddBoundsCollider(fragment);
                }

                Rigidbody body = fragment.GetComponent<Rigidbody>();
                if (body == null)
                {
                    body = fragment.gameObject.AddComponent<Rigidbody>();
                }

                body.mass = fragmentMass;
                body.collisionDetectionMode =
                    CollisionDetectionMode.ContinuousDynamic;
                body.interpolation = RigidbodyInterpolation.Interpolate;

                Vector3 centre = ResolveWorldCentre(fragment);
                Vector3 outward = centre - context.ImpactPoint;
                if (outward.sqrMagnitude < 0.0001f)
                {
                    outward = RandomDirection(random);
                }
                outward.Normalize();

                Vector3 inheritedAngularVelocity =
                    Vector3.Cross(
                        context.AngularVelocity,
                        centre - context.Position);
                float speedVariation = Mathf.Lerp(
                    0.75f,
                    1.25f,
                    (float)random.NextDouble());
                body.velocity = context.Velocity
                    + inheritedAngularVelocity
                    + outward * (impactSpeed * speedVariation);
                body.angularVelocity = context.AngularVelocity
                    + RandomDirection(random)
                    * Mathf.Lerp(
                        2.5f,
                        6f,
                        (float)random.NextDouble());
                fragmentBodies.Add(body);
            }

            return true;
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        private void OnDestroy()
        {
            for (int i = 0; i < ownedMeshes.Count; i++)
            {
                DestroySafely(ownedMeshes[i]);
            }
            ownedMeshes.Clear();

            DestroySafely(ownedMaterial);
            DestroySafely(ownedTexture);
            ownedMaterial = null;
            ownedTexture = null;
        }

        private static void AddBoundsCollider(Transform fragment)
        {
            Renderer[] renderers =
                fragment.GetComponentsInChildren<Renderer>(true);
            Bounds worldBounds = new Bounds(fragment.position, Vector3.zero);
            bool found = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!found)
                {
                    worldBounds = renderers[i].bounds;
                    found = true;
                }
                else
                {
                    worldBounds.Encapsulate(renderers[i].bounds);
                }
            }

            BoxCollider collider =
                fragment.gameObject.AddComponent<BoxCollider>();
            if (!found)
            {
                collider.size = Vector3.one * 0.1f;
                return;
            }

            collider.center = fragment.InverseTransformPoint(
                worldBounds.center);
            Vector3 lossyScale = fragment.lossyScale;
            collider.size = new Vector3(
                SafeDivide(worldBounds.size.x, lossyScale.x),
                SafeDivide(worldBounds.size.y, lossyScale.y),
                SafeDivide(worldBounds.size.z, lossyScale.z));
        }

        private static float SafeDivide(float value, float divisor)
        {
            return Mathf.Abs(divisor) > 0.0001f
                ? value / Mathf.Abs(divisor)
                : value;
        }

        private static Vector3 ResolveWorldCentre(Transform fragment)
        {
            Renderer renderer =
                fragment.GetComponentInChildren<Renderer>(true);
            return renderer != null
                ? renderer.bounds.center
                : fragment.position;
        }

        private static Vector3 RandomDirection(System.Random random)
        {
            var direction = new Vector3(
                (float)random.NextDouble() * 2f - 1f,
                (float)random.NextDouble() * 2f - 1f,
                (float)random.NextDouble() * 2f - 1f);
            return direction.sqrMagnitude > 0.0001f
                ? direction.normalized
                : Vector3.up;
        }

        private static void SetLayerRecursively(GameObject target, int layer)
        {
            target.layer = layer;
            Transform targetTransform = target.transform;
            for (int i = 0; i < targetTransform.childCount; i++)
            {
                SetLayerRecursively(
                    targetTransform.GetChild(i).gameObject,
                    layer);
            }
        }

        private static void DestroySafely(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }
    }
}

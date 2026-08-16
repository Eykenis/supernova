using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Supernova.PortalExample
{
    /// <summary>
    /// Maintains the two clipped visual halves of a rigidbody while its
    /// physical authority moves through a linked portal pair.
    /// </summary>
    internal sealed class PortalExampleSeamlessVisual
    {
        private static readonly int ClipPlaneId =
            Shader.PropertyToID("_PortalClipPlane");
        private static readonly int ApertureId =
            Shader.PropertyToID("_PortalApertureCenterRadius");
        private static readonly int ApertureRightId =
            Shader.PropertyToID("_PortalApertureRight");
        private static readonly int ApertureUpId =
            Shader.PropertyToID("_PortalApertureUp");
        private static readonly int LimitApertureId =
            Shader.PropertyToID("_PortalLimitAperture");

        private sealed class RendererBinding
        {
            public MeshRenderer Source;
            public MeshRenderer Proxy;
            public Transform ProxyTransform;
            public Material[] OriginalMaterials;
            public MaterialPropertyBlock OriginalPropertyBlock;
            public MaterialPropertyBlock SourcePropertyBlock;
            public MaterialPropertyBlock ProxyPropertyBlock;
        }

        private struct IgnoredCollisionPair
        {
            public Collider Traveller;
            public Collider Obstacle;
        }

        private readonly PortalExampleTraveller traveller;
        private readonly Rigidbody body;
        private readonly List<RendererBinding> rendererBindings =
            new List<RendererBinding>();
        private readonly List<Material> runtimeMaterials =
            new List<Material>();
        private readonly Dictionary<Material, Material> materialLookup =
            new Dictionary<Material, Material>();
        private readonly List<IgnoredCollisionPair> ignoredCollisions =
            new List<IgnoredCollisionPair>();
        private readonly List<Collider> portalObstacles =
            new List<Collider>();

        private GameObject proxyRoot;
        private PortalExampleGate currentGate;
        private PortalExampleGate oppositeGate;
        private bool originalIsEmergedHalf;

        public bool IsActive { get; private set; }

        public PortalExampleSeamlessVisual(
            PortalExampleTraveller configuredTraveller,
            Rigidbody configuredBody)
        {
            traveller = configuredTraveller;
            body = configuredBody;
        }

        public void Begin(
            PortalExampleGate source,
            PortalExampleGate destination,
            Shader clipShader)
        {
            if (source == null || destination == null)
            {
                return;
            }
            if (IsActive && currentGate == source
                && oppositeGate == destination)
            {
                return;
            }

            End();
            currentGate = source;
            oppositeGate = destination;
            originalIsEmergedHalf = false;
            IsActive = true;
            if (clipShader != null)
            {
                BuildRendererBindings(clipShader);
            }
            IgnorePortalObstacles(source);
            IgnorePortalObstacles(destination);
            UpdateVisuals();
        }

        public void CommitPhysicalTransfer(
            PortalExampleGate source,
            PortalExampleGate destination)
        {
            if (!IsActive)
            {
                return;
            }

            currentGate = destination;
            oppositeGate = source;
            originalIsEmergedHalf = true;
            IgnorePortalObstacles(source);
            IgnorePortalObstacles(destination);
            UpdateVisuals();
        }

        public bool UsesGate(PortalExampleGate gate)
        {
            return IsActive
                && (currentGate == gate || oppositeGate == gate);
        }

        public void UpdateVisuals()
        {
            if (!IsActive || currentGate == null || oppositeGate == null)
            {
                return;
            }

            Matrix4x4 mapping = PortalExampleSpace.BuildMapping(
                currentGate.transform,
                oppositeGate.transform);
            for (int index = 0; index < rendererBindings.Count; index++)
            {
                RendererBinding binding = rendererBindings[index];
                if (binding.Source == null || binding.Proxy == null)
                {
                    continue;
                }

                Transform sourceTransform = binding.Source.transform;
                binding.ProxyTransform.SetPositionAndRotation(
                    mapping.MultiplyPoint3x4(sourceTransform.position),
                    PortalExampleSpace.MapRotation(
                        mapping,
                        sourceTransform.rotation));
                binding.ProxyTransform.localScale =
                    sourceTransform.lossyScale;
                binding.Proxy.enabled = binding.Source.enabled;

                ApplyClipPlane(
                    binding.Source,
                    binding.SourcePropertyBlock,
                    currentGate,
                    originalIsEmergedHalf);
                ApplyClipPlane(
                    binding.Proxy,
                    binding.ProxyPropertyBlock,
                    oppositeGate,
                    !originalIsEmergedHalf);
            }
        }

        public void End()
        {
            RestoreCollisionPairs();
            for (int index = 0; index < rendererBindings.Count; index++)
            {
                RendererBinding binding = rendererBindings[index];
                if (binding.Source == null)
                {
                    continue;
                }
                binding.Source.sharedMaterials = binding.OriginalMaterials;
                binding.Source.SetPropertyBlock(
                    binding.OriginalPropertyBlock);
            }
            rendererBindings.Clear();

            if (proxyRoot != null)
            {
                proxyRoot.SetActive(false);
                DestroyRuntimeObject(proxyRoot);
                proxyRoot = null;
            }
            for (int index = 0; index < runtimeMaterials.Count; index++)
            {
                DestroyRuntimeObject(runtimeMaterials[index]);
            }
            runtimeMaterials.Clear();
            materialLookup.Clear();
            currentGate = null;
            oppositeGate = null;
            originalIsEmergedHalf = false;
            IsActive = false;
        }

        private void BuildRendererBindings(Shader clipShader)
        {
            MeshRenderer[] sourceRenderers =
                traveller.GetComponentsInChildren<MeshRenderer>(true);
            if (sourceRenderers.Length == 0)
            {
                return;
            }

            proxyRoot = new GameObject(traveller.name + " Portal Visual Proxy")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            for (int index = 0; index < sourceRenderers.Length; index++)
            {
                MeshRenderer sourceRenderer = sourceRenderers[index];
                MeshFilter sourceFilter = sourceRenderer != null
                    ? sourceRenderer.GetComponent<MeshFilter>()
                    : null;
                if (sourceFilter == null || sourceFilter.sharedMesh == null)
                {
                    continue;
                }

                GameObject proxyObject =
                    new GameObject(sourceRenderer.name + " Portal Proxy")
                    {
                        hideFlags = HideFlags.HideAndDontSave,
                        layer = sourceRenderer.gameObject.layer
                    };
                proxyObject.transform.SetParent(proxyRoot.transform, false);
                MeshFilter proxyFilter = proxyObject.AddComponent<MeshFilter>();
                proxyFilter.sharedMesh = sourceFilter.sharedMesh;
                MeshRenderer proxyRenderer =
                    proxyObject.AddComponent<MeshRenderer>();
                proxyRenderer.shadowCastingMode = ShadowCastingMode.Off;
                proxyRenderer.receiveShadows = sourceRenderer.receiveShadows;
                proxyRenderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                proxyRenderer.reflectionProbeUsage =
                    sourceRenderer.reflectionProbeUsage;
                proxyRenderer.sortingLayerID = sourceRenderer.sortingLayerID;
                proxyRenderer.sortingOrder = sourceRenderer.sortingOrder;

                Material[] originalMaterials =
                    sourceRenderer.sharedMaterials;
                Material[] clippedMaterials =
                    BuildClippedMaterials(originalMaterials, clipShader);
                sourceRenderer.sharedMaterials = clippedMaterials;
                proxyRenderer.sharedMaterials = clippedMaterials;

                var originalBlock = new MaterialPropertyBlock();
                sourceRenderer.GetPropertyBlock(originalBlock);
                var sourceBlock = new MaterialPropertyBlock();
                sourceRenderer.GetPropertyBlock(sourceBlock);
                rendererBindings.Add(new RendererBinding
                {
                    Source = sourceRenderer,
                    Proxy = proxyRenderer,
                    ProxyTransform = proxyObject.transform,
                    OriginalMaterials = originalMaterials,
                    OriginalPropertyBlock = originalBlock,
                    SourcePropertyBlock = sourceBlock,
                    ProxyPropertyBlock = new MaterialPropertyBlock()
                });
            }
        }

        private Material[] BuildClippedMaterials(
            Material[] originals,
            Shader clipShader)
        {
            Material[] clipped = new Material[originals.Length];
            for (int index = 0; index < originals.Length; index++)
            {
                Material original = originals[index];
                if (original == null)
                {
                    continue;
                }
                if (!materialLookup.TryGetValue(original, out Material runtime))
                {
                    runtime = CreateClippedMaterial(original, clipShader);
                    materialLookup[original] = runtime;
                    runtimeMaterials.Add(runtime);
                }
                clipped[index] = runtime;
            }
            return clipped;
        }

        private static Material CreateClippedMaterial(
            Material source,
            Shader clipShader)
        {
            int sourceRenderQueue = source.renderQueue;
            string[] sourceKeywords = source.shaderKeywords;
            var material = new Material(source)
            {
                name = source.name + " (Portal Clipped)",
                hideFlags = HideFlags.HideAndDontSave,
                shader = clipShader
            };
            material.renderQueue = sourceRenderQueue;
            material.shaderKeywords = sourceKeywords;

            if (source.HasProperty("_MainTex")
                && !source.HasProperty("_BaseMap"))
            {
                material.SetTexture(
                    "_BaseMap",
                    source.GetTexture("_MainTex"));
                material.SetTextureScale(
                    "_BaseMap",
                    source.GetTextureScale("_MainTex"));
                material.SetTextureOffset(
                    "_BaseMap",
                    source.GetTextureOffset("_MainTex"));
            }
            if (source.HasProperty("_Color")
                && !source.HasProperty("_BaseColor"))
            {
                material.SetColor(
                    "_BaseColor",
                    source.GetColor("_Color"));
            }
            return material;
        }

        private static void ApplyClipPlane(
            Renderer renderer,
            MaterialPropertyBlock block,
            PortalExampleGate gate,
            bool limitToAperture)
        {
            Vector3 normal = gate.transform.forward.normalized;
            Vector3 position = gate.transform.position;
            block.SetVector(
                ClipPlaneId,
                new Vector4(
                    normal.x,
                    normal.y,
                    normal.z,
                    -Vector3.Dot(normal, position)));
            float radius = gate.WorldApertureRadius;
            block.SetVector(
                ApertureId,
                new Vector4(position.x, position.y, position.z, radius));
            block.SetVector(ApertureRightId, gate.transform.right.normalized);
            block.SetVector(ApertureUpId, gate.transform.up.normalized);
            block.SetFloat(LimitApertureId, limitToAperture ? 1f : 0f);
            renderer.SetPropertyBlock(block);
        }

        private void IgnorePortalObstacles(PortalExampleGate gate)
        {
            if (gate == null)
            {
                return;
            }

            portalObstacles.Clear();
            gate.CollectPortalPlaneObstacles(
                portalObstacles,
                CalculateTunnelDepth(gate));
            Collider[] travellerColliders =
                traveller.GetComponentsInChildren<Collider>(true);
            bool ignoredAnyCollision = false;
            for (int travellerIndex = 0;
                travellerIndex < travellerColliders.Length;
                travellerIndex++)
            {
                Collider travellerCollider =
                    travellerColliders[travellerIndex];
                if (travellerCollider == null || !travellerCollider.enabled
                    || travellerCollider.attachedRigidbody != body)
                {
                    continue;
                }
                for (int obstacleIndex = 0;
                    obstacleIndex < portalObstacles.Count;
                    obstacleIndex++)
                {
                    Collider obstacle = portalObstacles[obstacleIndex];
                    if (obstacle == null || obstacle == travellerCollider
                        || obstacle.attachedRigidbody == body
                        || ContainsCollisionPair(
                            travellerCollider,
                            obstacle)
                        || Physics.GetIgnoreCollision(
                            travellerCollider,
                            obstacle))
                    {
                        continue;
                    }

                    Physics.IgnoreCollision(
                        travellerCollider,
                        obstacle,
                        true);
                    ignoredCollisions.Add(new IgnoredCollisionPair
                    {
                        Traveller = travellerCollider,
                        Obstacle = obstacle
                    });
                    ignoredAnyCollision = true;
                }
            }
            if (ignoredAnyCollision && body != null)
            {
                body.WakeUp();
            }
        }

        private float CalculateTunnelDepth(PortalExampleGate gate)
        {
            float maximumExtent = 0f;
            Vector3 normal = gate.transform.forward.normalized;
            Collider[] colliders =
                traveller.GetComponentsInChildren<Collider>(true);
            for (int index = 0; index < colliders.Length; index++)
            {
                Collider collider = colliders[index];
                if (collider == null || !collider.enabled
                    || !collider.gameObject.activeInHierarchy
                    || collider.attachedRigidbody != body)
                {
                    continue;
                }

                Vector3 extent = collider.bounds.extents;
                float projectedExtent = Mathf.Abs(normal.x) * extent.x
                    + Mathf.Abs(normal.y) * extent.y
                    + Mathf.Abs(normal.z) * extent.z;
                maximumExtent = Mathf.Max(maximumExtent, projectedExtent);
            }

            return maximumExtent + gate.WorldApertureRadius * 0.2f;
        }

        private bool ContainsCollisionPair(
            Collider travellerCollider,
            Collider obstacle)
        {
            for (int index = 0; index < ignoredCollisions.Count; index++)
            {
                IgnoredCollisionPair pair = ignoredCollisions[index];
                if (pair.Traveller == travellerCollider
                    && pair.Obstacle == obstacle)
                {
                    return true;
                }
            }
            return false;
        }

        private void RestoreCollisionPairs()
        {
            for (int index = 0; index < ignoredCollisions.Count; index++)
            {
                IgnoredCollisionPair pair = ignoredCollisions[index];
                if (pair.Traveller != null && pair.Obstacle != null)
                {
                    Physics.IgnoreCollision(
                        pair.Traveller,
                        pair.Obstacle,
                        false);
                }
            }
            ignoredCollisions.Clear();
            portalObstacles.Clear();
        }

        private static void DestroyRuntimeObject(Object target)
        {
            if (target == null)
            {
                return;
            }
            if (Application.isPlaying)
            {
                Object.Destroy(target);
            }
            else
            {
                Object.DestroyImmediate(target);
            }
        }
    }
}

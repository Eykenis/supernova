using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;

namespace Supernova.Gameplay
{
    /// <summary>
    /// Rebinds copied skinned equipment to the equipped character and owns its interaction VFX.
    /// </summary>
    public sealed class PlayerEquipmentVisual : MonoBehaviour
    {
        [Serializable]
        public sealed class SkinnedRendererBinding
        {
            [SerializeField] private SkinnedMeshRenderer renderer;
            [SerializeField] private string rootBoneName;
            [SerializeField] private string[] boneNames = Array.Empty<string>();

            public SkinnedMeshRenderer Renderer => renderer;

            public SkinnedRendererBinding(
                SkinnedMeshRenderer renderer,
                string rootBoneName,
                string[] boneNames)
            {
                this.renderer = renderer;
                this.rootBoneName = rootBoneName;
                this.boneNames = boneNames ?? Array.Empty<string>();
            }

            public void Bind(IReadOnlyDictionary<string, Transform> bonesByName)
            {
                if (renderer == null)
                    return;

                Transform[] resolvedBones = new Transform[boneNames.Length];
                for (int i = 0; i < boneNames.Length; i++)
                {
                    bonesByName.TryGetValue(boneNames[i], out resolvedBones[i]);
                }

                renderer.bones = resolvedBones;
                if (!string.IsNullOrEmpty(rootBoneName)
                    && bonesByName.TryGetValue(rootBoneName, out Transform rootBone))
                {
                    renderer.rootBone = rootBone;
                }
            }
        }

        [SerializeField] private bool mountAtCharacterRoot = true;
        [SerializeField] private SkinnedRendererBinding[] skinnedRenderers =
            Array.Empty<SkinnedRendererBinding>();
        [SerializeField] private Transform equipmentBoneRoot;
        [SerializeField] private GameObject interactionVfxRoot;

        public bool MountAtCharacterRoot => mountAtCharacterRoot;

        private void Awake()
        {
            SetInteractionActive(false);
        }

        public void Bind(Animator animator)
        {
            if (animator == null)
                return;

            if (equipmentBoneRoot != null)
            {
                Transform chest = animator.GetBoneTransform(HumanBodyBones.Chest);
                if (chest != null)
                    equipmentBoneRoot.SetParent(chest, false);
            }

            Transform[] transforms = animator.GetComponentsInChildren<Transform>(true);
            Dictionary<string, Transform> bonesByName =
                new Dictionary<string, Transform>(transforms.Length);
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform bone = transforms[i];
                if (!bonesByName.ContainsKey(bone.name))
                    bonesByName.Add(bone.name, bone);
            }

            for (int i = 0; i < skinnedRenderers.Length; i++)
                skinnedRenderers[i]?.Bind(bonesByName);

            BindThrusterConstraints(bonesByName);
        }

        private void OnDestroy()
        {
            if (equipmentBoneRoot == null || equipmentBoneRoot.IsChildOf(transform))
                return;

            if (Application.isPlaying)
                Destroy(equipmentBoneRoot.gameObject);
            else
                DestroyImmediate(equipmentBoneRoot.gameObject);
        }

        public void SetInteractionActive(bool active)
        {
            if (interactionVfxRoot == null)
                return;

            if (active)
                interactionVfxRoot.SetActive(true);
            ParticleSystem[] particles =
                interactionVfxRoot.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particles.Length; i++)
            {
                if (active)
                {
                    particles[i].gameObject.SetActive(true);
                    particles[i].Play(true);
                }
                else
                    particles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            if (!active)
                interactionVfxRoot.SetActive(false);
        }

        private void BindThrusterConstraints(
            IReadOnlyDictionary<string, Transform> bonesByName)
        {
            if (interactionVfxRoot == null)
                return;

            ParentConstraint[] constraints =
                interactionVfxRoot.GetComponentsInChildren<ParentConstraint>(true);
            for (int i = 0; i < constraints.Length; i++)
            {
                ParentConstraint constraint = constraints[i];
                string boneName = GetThrusterBoneName(constraint.gameObject.name);
                if (string.IsNullOrEmpty(boneName)
                    || !bonesByName.TryGetValue(boneName, out Transform bone))
                {
                    continue;
                }

                ConstraintSource source = new ConstraintSource
                {
                    sourceTransform = bone,
                    weight = 1f,
                };
                if (constraint.sourceCount > 0)
                    constraint.SetSource(0, source);
                else
                    constraint.AddSource(source);
                constraint.constraintActive = true;
            }
        }

        private static string GetThrusterBoneName(string connectorName)
        {
            switch (connectorName)
            {
                case "Main_Thruster_Conect":
                    return "BackPack_Main";
                case "Sub_Thruster1_Conect":
                    return "Vernier_Ball_02_L";
                case "Sub_Thruster2_Conect":
                    return "Vernier_Ball_02_R";
                default:
                    return string.Empty;
            }
        }

#if UNITY_EDITOR
        public void Configure(
            SkinnedRendererBinding[] bindings,
            Transform boneRoot,
            GameObject vfxRoot,
            bool useCharacterRoot)
        {
            skinnedRenderers = bindings ?? Array.Empty<SkinnedRendererBinding>();
            equipmentBoneRoot = boneRoot;
            interactionVfxRoot = vfxRoot;
            mountAtCharacterRoot = useCharacterRoot;
        }
#endif
    }
}

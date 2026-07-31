#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Supernova.EditorTools.Animation
{
    /// <summary>
    /// The rifle animation library uses the Motus skeleton, so copying P05's HumanDescription leaves
    /// bone names and the transform hierarchy incompatible. Auto-mapping the FBX's own
    /// skeleton and creating an Avatar per animation keeps the P05 Avatar untouched while
    /// still allowing normal Humanoid retargeting at runtime.
    /// </summary>
    public sealed class RifleAnimationImporter : AssetPostprocessor
    {
        private static readonly string[] ProductionClipPaths =
        {
            ProjectAssetPaths.ThirdParty.RifleIdle,
            ProjectAssetPaths.ThirdParty.RifleMove,
            ProjectAssetPaths.ThirdParty.RifleFire,
        };

        private void OnPreprocessModel()
        {
            if (!IsRifleAnimationPath(assetPath)) return;
            ConfigureImporter((ModelImporter)assetImporter, assetPath);
        }

        [MenuItem("Tools/Supernova/Animation/Repair Production Rifle Animations")]
        public static void RepairProductionAnimations()
        {
            int repaired = 0;
            foreach (string path in ProductionClipPaths)
                if (EnsureImporterSettings(path)) repaired++;

            AssetDatabase.SaveAssets();
            Debug.Log(
                $"Verified the production rifle clips; reimported {repaired} whose Humanoid settings were stale.");
        }

        [MenuItem("Tools/Supernova/Animation/Repair Entire Rifle Animation Library")]
        public static void RepairEntireLibrary()
        {
            string[] modelGuids = AssetDatabase.FindAssets(
                "t:Model",
                new[] { ProjectAssetPaths.ThirdParty.RifleAnimationFolder });
            int repaired = 0;
            foreach (string guid in modelGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!IsRifleAnimationPath(path)) continue;
                if (EnsureImporterSettings(path)) repaired++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log(
                $"Verified {modelGuids.Length} rifle models; reimported {repaired} whose Humanoid settings were stale.");
        }

        private static bool EnsureImporterSettings(string path)
        {
            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError("Missing rifle animation FBX: " + path);
                return false;
            }

            bool changed = ConfigureImporter(importer, path);
            if (changed) importer.SaveAndReimport();

            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<Avatar>()
                .FirstOrDefault();
            if (avatar == null || !avatar.isHuman || !avatar.isValid)
            {
                Debug.LogError(
                    "Rifle animation did not produce a valid independent Humanoid Avatar: "
                    + path);
            }
            return changed;
        }

        private static bool ConfigureImporter(ModelImporter importer, string path)
        {
            bool changed = false;
            if (importer.animationType != ModelImporterAnimationType.Human)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                changed = true;
            }
            if (importer.avatarSetup
                != ModelImporterAvatarSetup.CreateFromThisModel)
            {
                importer.avatarSetup =
                    ModelImporterAvatarSetup.CreateFromThisModel;
                changed = true;
            }
            if (!importer.autoGenerateAvatarMappingIfUnspecified)
            {
                importer.autoGenerateAvatarMappingIfUnspecified = true;
                changed = true;
            }
            if (importer.sourceAvatar != null)
            {
                importer.sourceAvatar = null;
                changed = true;
            }
            HumanDescription description = importer.humanDescription;
            if (!UsesMotusSkeleton(description))
            {
                description.human = Array.Empty<HumanBone>();
                description.skeleton = Array.Empty<SkeletonBone>();
                importer.humanDescription = description;
                changed = true;
            }
            if (!importer.importAnimation)
            {
                importer.importAnimation = true;
                changed = true;
            }

            bool loop = IsLoopingClip(path);
            ModelImporterClipAnimation[] clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0)
                clips = importer.defaultClipAnimations;
            for (int i = 0; i < clips.Length; i++)
            {
                ModelImporterClipAnimation clip = clips[i];
                bool clipChanged = clip.loopTime != loop
                    || clip.loopPose != loop
                    || !clip.lockRootRotation
                    || !clip.lockRootHeightY
                    || !clip.lockRootPositionXZ
                    || !clip.keepOriginalOrientation
                    || !clip.keepOriginalPositionY
                    || !clip.keepOriginalPositionXZ;
                clip.loopTime = loop;
                clip.loopPose = loop;
                clip.lockRootRotation = true;
                clip.lockRootHeightY = true;
                clip.lockRootPositionXZ = true;
                clip.keepOriginalOrientation = true;
                clip.keepOriginalPositionY = true;
                clip.keepOriginalPositionXZ = true;
                clips[i] = clip;
                changed |= clipChanged;
            }
            importer.clipAnimations = clips;
            return changed;
        }

        private static bool UsesMotusSkeleton(HumanDescription description)
        {
            if (description.human == null) return false;
            for (int i = 0; i < description.human.Length; i++)
            {
                HumanBone bone = description.human[i];
                if (bone.humanName == "LeftUpperLeg"
                    && bone.boneName == "LeftUpLeg")
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsRifleAnimationPath(string path)
        {
            return !string.IsNullOrEmpty(path)
                && path.StartsWith(
                    ProjectAssetPaths.ThirdParty.RifleAnimationFolder + "/",
                    StringComparison.Ordinal)
                && path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLoopingClip(string path)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            return name.IndexOf("_Idle", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("_Loop", StringComparison.OrdinalIgnoreCase) >= 0;
        }

    }
}
#endif

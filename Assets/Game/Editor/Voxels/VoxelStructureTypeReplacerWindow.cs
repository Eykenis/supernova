using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Supernova.Voxels.Editor
{
    /// <summary>
    /// Bulk-swaps one voxel type for another across every sample of a saved
    /// structure template. Authoring a template voxel by voxel is impractical
    /// once a palette changes, and the stored type field is a packed blob that
    /// cannot be retargeted by hand.
    /// </summary>
    public sealed class VoxelStructureTypeReplacerWindow : EditorWindow
    {
        [SerializeField] private VoxelStructureAsset structure;
        [SerializeField] private VoxelTypeDefinition sourceType;
        [SerializeField] private VoxelTypeDefinition replacementType;

        [MenuItem("Tools/Supernova/Voxels/Replace Structure Voxel Types")]
        public static void OpenFromMenu()
        {
            VoxelStructureTypeReplacerWindow window = GetWindow<
                VoxelStructureTypeReplacerWindow>("Voxel Type Replace");
            window.TryAdoptSelectionStructure();
            window.Show();
        }

        private void OnEnable()
        {
            TryAdoptSelectionStructure();
        }

        private void OnSelectionChange()
        {
            TryAdoptSelectionStructure();
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField(
                "Bulk Structure Voxel Type Replace",
                EditorStyles.boldLabel);
            structure = (VoxelStructureAsset)EditorGUILayout.ObjectField(
                "Structure",
                structure,
                typeof(VoxelStructureAsset),
                false);
            if (structure == null)
            {
                EditorGUILayout.HelpBox(
                    "Select a Voxel Structure asset to inspect its type usage.",
                    MessageType.Info);
                return;
            }

            DrawTypeHistogram(out Dictionary<ushort, int> counts);

            EditorGUILayout.Space();
            sourceType = (VoxelTypeDefinition)EditorGUILayout.ObjectField(
                "Replace Type",
                sourceType,
                typeof(VoxelTypeDefinition),
                false);
            replacementType = (VoxelTypeDefinition)EditorGUILayout.ObjectField(
                "With Type",
                replacementType,
                typeof(VoxelTypeDefinition),
                false);

            if (sourceType == null || replacementType == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign both a source and a replacement voxel type.",
                    MessageType.Info);
                return;
            }

            ushort from = sourceType.TypeId.Value;
            ushort to = replacementType.TypeId.Value;
            if (from == to)
            {
                EditorGUILayout.HelpBox(
                    $"'{sourceType.name}' and '{replacementType.name}' share type "
                    + $"id {from}, so replacing them would change nothing. Voxel "
                    + "type ids are assigned per asset, not per file name.",
                    MessageType.Warning);
                return;
            }

            counts.TryGetValue(from, out int affected);
            EditorGUILayout.LabelField(
                "Affected samples",
                $"{affected} voxel(s) of type {from} -> {to}");
            if (affected == 0)
            {
                EditorGUILayout.HelpBox(
                    $"'{structure.name}' contains no voxel of type {from} "
                    + $"('{sourceType.DisplayName}'). Nothing to replace.",
                    MessageType.Warning);
                return;
            }

            // Air is implied by a negative density, so retargeting it would
            // desynchronise the density and type fields.
            if (from == VoxelTypeId.Air.Value || to == VoxelTypeId.Air.Value)
            {
                EditorGUILayout.HelpBox(
                    "Air is derived from density and cannot take part in a type "
                    + "replacement. Remove voxels through the authoring scene instead.",
                    MessageType.Error);
                return;
            }

            EditorGUILayout.Space();
            if (GUILayout.Button($"Replace {affected} Voxel(s)"))
            {
                Replace(from, to, affected);
            }
        }

        private void DrawTypeHistogram(out Dictionary<ushort, int> counts)
        {
            structure.CopyData(out _, out VoxelTypeId[] types);
            counts = new Dictionary<ushort, int>();
            for (int i = 0; i < types.Length; i++)
            {
                ushort value = types[i].Value;
                counts.TryGetValue(value, out int current);
                counts[value] = current + 1;
            }

            EditorGUILayout.LabelField(
                "Size",
                $"{structure.Size.x} x {structure.Size.y} x {structure.Size.z}"
                + $" ({structure.SampleCount} samples)");
            EditorGUILayout.LabelField("Solid voxels by type", EditorStyles.boldLabel);
            VoxelTypeCatalog catalog = LoadCatalog();
            using (new EditorGUI.IndentLevelScope())
            {
                foreach (KeyValuePair<ushort, int> entry in counts.OrderBy(item => item.Key))
                {
                    if (entry.Key == VoxelTypeId.Air.Value)
                    {
                        continue;
                    }

                    VoxelTypeDefinition definition = catalog != null
                        ? catalog.Find(new VoxelTypeId(entry.Key))
                        : null;
                    string label = definition != null
                        ? $"{entry.Key} ({definition.name})"
                        : $"{entry.Key} (unregistered)";
                    EditorGUILayout.LabelField(label, $"{entry.Value}");
                }
            }
        }

        private void Replace(ushort from, ushort to, int expected)
        {
            structure.CopyData(
                out float[] densities,
                out VoxelTypeId[] types);
            var replaced = new ushort[types.Length];
            int changed = 0;
            for (int i = 0; i < types.Length; i++)
            {
                ushort value = types[i].Value;
                if (value == from)
                {
                    value = to;
                    changed++;
                }
                replaced[i] = value;
            }

            Undo.RecordObject(structure, "Replace Structure Voxel Types");
            structure.SetData(
                structure.Size,
                structure.Anchor,
                structure.PlayerSpawnOffset,
                densities,
                replaced);
            EditorUtility.SetDirty(structure);
            AssetDatabase.SaveAssets();

            Debug.Log(
                $"Replaced {changed} voxel(s) of type {from} with type {to} in "
                + $"'{structure.name}' ({AssetDatabase.GetAssetPath(structure)}).",
                structure);
            if (changed != expected)
            {
                Debug.LogWarning(
                    $"Expected to replace {expected} voxel(s) but changed {changed}.",
                    structure);
            }
        }

        private static VoxelTypeCatalog LoadCatalog()
        {
            return AssetDatabase.LoadAssetAtPath<VoxelTypeCatalog>(
                ProjectAssetPaths.Config.VoxelCatalog);
        }

        private void TryAdoptSelectionStructure()
        {
            var selected = Selection.activeObject as VoxelStructureAsset;
            if (selected != null)
            {
                structure = selected;
            }
        }
    }
}

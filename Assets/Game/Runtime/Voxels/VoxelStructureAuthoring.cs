using System;
using System.Collections.Generic;
using Supernova.MinecraftCaves;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Supernova.Voxels
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class VoxelStructureAuthoring : MonoBehaviour
    {
        [SerializeField] private VoxelStructureAsset structureToEdit;
        [Tooltip("Optional random-world feature that consumes Structure To Edit as its template.")]
        [SerializeField] private VoxelStructureFeatureDefinition structureFeatureToEdit;
        [SerializeField] private VoxelTypeCatalog voxelTypeCatalog;
        [SerializeField] private Vector3Int size = new Vector3Int(13, 7, 13);
        [SerializeField] private Vector3Int anchor = new Vector3Int(6, 1, 6);
        [SerializeField] private Vector3 playerSpawnOffset = new Vector3(0f, 1.25f, 0f);
        [SerializeField, Min(1)] private ushort paintVoxelType = 1;
        [SerializeField, Min(0.001f)] private float paintDensity = 1f;
        [SerializeField] private string defaultAssetName = "VoxelStructure";

        public VoxelStructureAsset StructureToEdit => structureToEdit;
        public VoxelStructureFeatureDefinition StructureFeatureToEdit =>
            structureFeatureToEdit;
        public VoxelTypeCatalog TypeCatalog => voxelTypeCatalog;
        public Vector3Int Size => size;
        public Vector3Int Anchor => anchor;
        public Vector3 PlayerSpawnOffset => playerSpawnOffset;
        public VoxelTypeId PaintType => new VoxelTypeId(Math.Max((ushort)1, paintVoxelType));
        public float PaintDensity => Mathf.Max(0.001f, paintDensity);
        public string DefaultAssetName => string.IsNullOrWhiteSpace(defaultAssetName)
            ? "VoxelStructure"
            : defaultAssetName;

        public void Configure(
            VoxelStructureAsset asset,
            VoxelTypeCatalog catalog,
            Vector3Int dimensions,
            Vector3Int assetAnchor,
            Vector3 spawnOffset)
        {
            structureToEdit = asset;
            voxelTypeCatalog = catalog;
            size = dimensions;
            anchor = assetAnchor;
            playerSpawnOffset = spawnOffset;
            if (asset != null) defaultAssetName = asset.name;
            ClampConfiguration();
        }

        public void ConfigureFeature(
            VoxelStructureFeatureDefinition feature,
            VoxelTypeCatalog catalog)
        {
            structureFeatureToEdit = feature;
            VoxelStructureAsset template = feature != null
                ? feature.StructureTemplate
                : null;
            if (template != null)
            {
                Configure(
                    template,
                    catalog,
                    template.Size,
                    template.Anchor,
                    template.PlayerSpawnOffset);
            }
            else
            {
                voxelTypeCatalog = catalog;
            }
        }

        public bool TryBuildData(
            out float[] densities,
            out ushort[] types,
            out string error)
        {
            ClampConfiguration();
            int count = size.x * size.y * size.z;
            densities = new float[count];
            types = new ushort[count];
            for (int i = 0; i < count; i++) densities[i] = -1f;

            var occupied = new HashSet<Vector3Int>();
            VoxelStructureCellAuthoring[] cells =
                GetComponentsInChildren<VoxelStructureCellAuthoring>(false);
            foreach (VoxelStructureCellAuthoring cell in cells)
            {
                if (cell == null || !cell.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Vector3 local = cell.transform.localPosition;
                var coordinate = new Vector3Int(
                    Mathf.RoundToInt(local.x),
                    Mathf.RoundToInt(local.y),
                    Mathf.RoundToInt(local.z));
                if (!IsInBounds(coordinate))
                {
                    error = $"Cell '{cell.name}' at {coordinate} is outside structure bounds {size}.";
                    return false;
                }
                if (!occupied.Add(coordinate))
                {
                    error = $"Multiple authored cells occupy {coordinate}.";
                    return false;
                }

                int index = ToIndex(coordinate);
                densities[index] = cell.Density;
                types[index] = cell.Type.Value;
            }

            error = null;
            return true;
        }

        public VoxelStructureCellAuthoring FindCell(Vector3Int coordinate)
        {
            VoxelStructureCellAuthoring[] cells =
                GetComponentsInChildren<VoxelStructureCellAuthoring>(false);
            foreach (VoxelStructureCellAuthoring cell in cells)
            {
                if (cell != null
                    && Vector3Int.RoundToInt(cell.transform.localPosition) == coordinate)
                {
                    return cell;
                }
            }
            return null;
        }

        public bool TryCreatePaintCell(
            Vector3Int coordinate,
            out VoxelStructureCellAuthoring cell)
        {
            cell = null;
            if (!IsInBounds(coordinate) || FindCell(coordinate) != null)
            {
                return false;
            }

            cell = CreatePaintCell(coordinate);
            return true;
        }

        /// <summary>
        /// Paints every coordinate in the inclusive box between two corners.
        /// Existing cells are repainted and missing cells are created.
        /// </summary>
        public bool TryFillPaintBox(
            Vector3Int firstCorner,
            Vector3Int secondCorner,
            out int changedCellCount)
        {
            changedCellCount = 0;
            if (!IsInBounds(firstCorner) || !IsInBounds(secondCorner))
            {
                return false;
            }

            Vector3Int minimum = Vector3Int.Min(firstCorner, secondCorner);
            Vector3Int maximum = Vector3Int.Max(firstCorner, secondCorner);
            var cellsByCoordinate = new Dictionary<
                Vector3Int,
                VoxelStructureCellAuthoring>();
            VoxelStructureCellAuthoring[] cells =
                GetComponentsInChildren<VoxelStructureCellAuthoring>(false);
            foreach (VoxelStructureCellAuthoring cell in cells)
            {
                if (cell == null || !cell.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Vector3Int coordinate = Vector3Int.RoundToInt(
                    cell.transform.localPosition);
                if (!cellsByCoordinate.ContainsKey(coordinate))
                {
                    cellsByCoordinate.Add(coordinate, cell);
                }
            }

            VoxelTypeId type = PaintType;
            float density = PaintDensity;
            for (int z = minimum.z; z <= maximum.z; z++)
            {
                for (int y = minimum.y; y <= maximum.y; y++)
                {
                    for (int x = minimum.x; x <= maximum.x; x++)
                    {
                        var coordinate = new Vector3Int(x, y, z);
                        if (cellsByCoordinate.TryGetValue(
                                coordinate,
                                out VoxelStructureCellAuthoring cell))
                        {
                            if (cell.Type == type
                                && Mathf.Approximately(cell.Density, density))
                            {
                                continue;
                            }

                            cell.Configure(density, type);
                            ApplyCellMaterial(cell.gameObject, type);
                        }
                        else
                        {
                            cell = CreatePaintCell(coordinate);
                            cellsByCoordinate.Add(coordinate, cell);
                        }

                        changedCellCount++;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Removes every authored cell inside the inclusive box between two corners.
        /// </summary>
        public bool TryClearBox(
            Vector3Int firstCorner,
            Vector3Int secondCorner,
            out int removedCellCount)
        {
            removedCellCount = 0;
            if (!IsInBounds(firstCorner) || !IsInBounds(secondCorner))
            {
                return false;
            }

            Vector3Int minimum = Vector3Int.Min(firstCorner, secondCorner);
            Vector3Int maximum = Vector3Int.Max(firstCorner, secondCorner);
            VoxelStructureCellAuthoring[] cells =
                GetComponentsInChildren<VoxelStructureCellAuthoring>(false);
            foreach (VoxelStructureCellAuthoring cell in cells)
            {
                if (cell == null || !cell.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Vector3Int coordinate = Vector3Int.RoundToInt(
                    cell.transform.localPosition);
                if (coordinate.x < minimum.x || coordinate.x > maximum.x
                    || coordinate.y < minimum.y || coordinate.y > maximum.y
                    || coordinate.z < minimum.z || coordinate.z > maximum.z)
                {
                    continue;
                }

                if (TryRemoveCell(cell))
                {
                    removedCellCount++;
                }
            }

            return true;
        }

        private VoxelStructureCellAuthoring CreatePaintCell(
            Vector3Int coordinate)
        {
            GameObject cellObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cellObject.name = $"Voxel_{coordinate.x}_{coordinate.y}_{coordinate.z}";
            cellObject.transform.SetParent(transform, false);
            cellObject.transform.localPosition = coordinate;
            cellObject.transform.localScale = Vector3.one * 0.92f;
            VoxelStructureCellAuthoring cell =
                cellObject.AddComponent<VoxelStructureCellAuthoring>();
            cell.Configure(PaintDensity, PaintType);
            ApplyCellMaterial(cellObject, PaintType);
            return cell;
        }

        public bool TryRemoveCell(VoxelStructureCellAuthoring cell)
        {
            if (cell == null || !cell.transform.IsChildOf(transform))
            {
                return false;
            }

            cell.gameObject.SetActive(false);
            if (Application.isPlaying)
            {
                Destroy(cell.gameObject);
            }
            else
            {
                DestroyImmediate(cell.gameObject);
            }
            return true;
        }

        /// <summary>
        /// Moves a group of authored cells as one atomic edit. Every destination
        /// is validated before any Transform changes, so a failed box-selection
        /// move cannot leave half of the selection behind.
        /// </summary>
        public bool TryOffsetCells(
            IEnumerable<VoxelStructureCellAuthoring> cellsToOffset,
            Vector3Int offset,
            out string error)
        {
            if (cellsToOffset == null)
            {
                error = "No voxel cells were supplied.";
                return false;
            }

            var selected = new HashSet<VoxelStructureCellAuthoring>();
            foreach (VoxelStructureCellAuthoring cell in cellsToOffset)
            {
                if (cell == null)
                {
                    continue;
                }
                if (!cell.transform.IsChildOf(transform)
                    || !cell.gameObject.activeInHierarchy)
                {
                    error = $"Cell '{cell.name}' is not an active child of this structure.";
                    return false;
                }
                selected.Add(cell);
            }
            if (selected.Count == 0)
            {
                error = "No voxel cells are selected.";
                return false;
            }
            if (offset == Vector3Int.zero)
            {
                error = null;
                return true;
            }

            var occupiedByUnselected = new HashSet<Vector3Int>();
            VoxelStructureCellAuthoring[] allCells =
                GetComponentsInChildren<VoxelStructureCellAuthoring>(false);
            foreach (VoxelStructureCellAuthoring cell in allCells)
            {
                if (cell == null || !cell.gameObject.activeInHierarchy
                    || selected.Contains(cell))
                {
                    continue;
                }
                Vector3Int coordinate = Vector3Int.RoundToInt(
                    cell.transform.localPosition);
                if (!occupiedByUnselected.Add(coordinate))
                {
                    error = $"Multiple unselected cells already occupy {coordinate}.";
                    return false;
                }
            }

            var destinations = new Dictionary<
                VoxelStructureCellAuthoring,
                Vector3Int>();
            var destinationCoordinates = new HashSet<Vector3Int>();
            foreach (VoxelStructureCellAuthoring cell in selected)
            {
                Vector3Int source = Vector3Int.RoundToInt(
                    cell.transform.localPosition);
                Vector3Int destination = source + offset;
                if (!IsInBounds(destination))
                {
                    error = $"Offset moves cell '{cell.name}' from {source} "
                        + $"to {destination}, outside structure bounds {size}.";
                    return false;
                }
                if (!destinationCoordinates.Add(destination))
                {
                    error = $"Multiple selected cells would occupy {destination}.";
                    return false;
                }
                if (occupiedByUnselected.Contains(destination))
                {
                    error = $"Offset destination {destination} is occupied by "
                        + "an unselected voxel.";
                    return false;
                }
                destinations.Add(cell, destination);
            }

            foreach (KeyValuePair<VoxelStructureCellAuthoring, Vector3Int> pair
                     in destinations)
            {
                pair.Key.transform.localPosition = pair.Value;
            }
            error = null;
            return true;
        }

        /// <summary>
        /// Offsets the complete template relative to its placement point without
        /// touching any cell Transform. Moving geometry by +D is equivalent to
        /// moving Anchor by -D.
        /// </summary>
        public bool TryOffsetWholeStructure(
            Vector3Int relativeOffset,
            out string error)
        {
            ClampConfiguration();
            Vector3Int shiftedAnchor = anchor - relativeOffset;
            if (!IsInBounds(shiftedAnchor))
            {
                error = $"Whole-structure offset {relativeOffset} would move "
                    + $"Anchor from {anchor} to {shiftedAnchor}, outside {size}.";
                return false;
            }

            anchor = shiftedAnchor;
            error = null;
            return true;
        }

        public void ReloadFromAssignedAsset()
        {
            if (structureToEdit == null)
            {
                return;
            }

            VoxelStructureCellAuthoring[] existing =
                GetComponentsInChildren<VoxelStructureCellAuthoring>(true);
            foreach (VoxelStructureCellAuthoring cell in existing)
            {
                if (cell == null) continue;
                cell.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(cell.gameObject);
                else DestroyImmediate(cell.gameObject);
            }

            size = structureToEdit.Size;
            anchor = structureToEdit.Anchor;
            playerSpawnOffset = structureToEdit.PlayerSpawnOffset;
            for (int z = 0; z < size.z; z++)
            {
                for (int y = 0; y < size.y; y++)
                {
                    for (int x = 0; x < size.x; x++)
                    {
                        VoxelSample sample = structureToEdit.GetSample(x, y, z);
                        if (sample.Density < 0f) continue;

                        VoxelTypeId previousType = PaintType;
                        float previousDensity = paintDensity;
                        paintVoxelType = sample.Type.Value;
                        paintDensity = sample.Density;
                        TryCreatePaintCell(new Vector3Int(x, y, z), out _);
                        paintVoxelType = previousType.Value;
                        paintDensity = previousDensity;
                    }
                }
            }
        }

        public bool TrySaveAssignedAsset(out string error)
        {
            if (structureToEdit == null)
            {
                error = "No VoxelStructureAsset is assigned.";
                return false;
            }
            if (!TryBuildData(out float[] densities, out ushort[] types, out error))
            {
                return false;
            }

            structureToEdit.SetData(
                size,
                anchor,
                playerSpawnOffset,
                densities,
                types);
#if UNITY_EDITOR
            EditorUtility.SetDirty(structureToEdit);
            AssetDatabase.SaveAssets();
#endif
            return true;
        }

        public bool IsInBounds(Vector3Int coordinate)
        {
            return (uint)coordinate.x < size.x
                && (uint)coordinate.y < size.y
                && (uint)coordinate.z < size.z;
        }

        private int ToIndex(Vector3Int coordinate)
        {
            return coordinate.x + size.x * (coordinate.y + size.y * coordinate.z);
        }

        private void ApplyCellMaterial(GameObject cellObject, VoxelTypeId type)
        {
            VoxelTypeDefinition definition =
                voxelTypeCatalog != null ? voxelTypeCatalog.Find(type) : null;
            if (definition != null && definition.Material != null)
            {
                cellObject.GetComponent<MeshRenderer>().sharedMaterial = definition.Material;
            }
        }

        private void OnValidate()
        {
            ClampConfiguration();
        }

        private void ClampConfiguration()
        {
            size = new Vector3Int(
                Mathf.Clamp(size.x, 1, 128),
                Mathf.Clamp(size.y, 1, 128),
                Mathf.Clamp(size.z, 1, 128));
            anchor = new Vector3Int(
                Mathf.Clamp(anchor.x, 0, size.x - 1),
                Mathf.Clamp(anchor.y, 0, size.y - 1),
                Mathf.Clamp(anchor.z, 0, size.z - 1));
            paintVoxelType = Math.Max((ushort)1, paintVoxelType);
            paintDensity = Mathf.Max(0.001f, paintDensity);
        }

        private void OnDrawGizmosSelected()
        {
            Matrix4x4 previous = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.15f, 0.8f, 1f, 0.8f);
            Gizmos.DrawWireCube(
                ((Vector3)size - Vector3.one) * 0.5f,
                (Vector3)size);
            Gizmos.color = new Color(1f, 0.35f, 0.1f, 0.95f);
            Gizmos.DrawWireSphere(anchor, 0.3f);
            Gizmos.matrix = previous;
        }
    }
}

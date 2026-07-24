using System;
using System.Collections.Generic;
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
        [SerializeField] private VoxelTypeCatalog voxelTypeCatalog;
        [SerializeField] private Vector3Int size = new Vector3Int(13, 7, 13);
        [SerializeField] private Vector3Int anchor = new Vector3Int(6, 1, 6);
        [SerializeField] private Vector3 playerSpawnOffset = new Vector3(0f, 1.25f, 0f);
        [SerializeField, Min(1)] private ushort paintVoxelType = 1;
        [SerializeField, Min(0.001f)] private float paintDensity = 1f;
        [SerializeField] private string defaultAssetName = "VoxelStructure";

        public VoxelStructureAsset StructureToEdit => structureToEdit;
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

            GameObject cellObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cellObject.name = $"Voxel_{coordinate.x}_{coordinate.y}_{coordinate.z}";
            cellObject.transform.SetParent(transform, false);
            cellObject.transform.localPosition = coordinate;
            cellObject.transform.localScale = Vector3.one * 0.92f;
            cell = cellObject.AddComponent<VoxelStructureCellAuthoring>();
            cell.Configure(PaintDensity, PaintType);
            ApplyCellMaterial(cellObject, PaintType);
            return true;
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

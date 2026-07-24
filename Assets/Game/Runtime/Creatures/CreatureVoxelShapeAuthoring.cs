using System.Collections.Generic;
using UnityEngine;

namespace Supernova.MinecraftCaves.Creatures
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class CreatureVoxelShapeAuthoring : MonoBehaviour
    {
        [SerializeField] private MeshCollider sourceCollider;
        [SerializeField] private CreatureVoxelShape shape;
        [SerializeField, Min(0.0001f)] private float bakeVoxelSize = 1f;
        [SerializeField] private bool showOccupiedVoxels = true;
        [SerializeField] private Color previewColor = new Color(0.1f, 0.85f, 1f, 0.22f);

        public MeshCollider SourceCollider => sourceCollider;
        public CreatureVoxelShape Shape => shape;
        public float BakeVoxelSize => bakeVoxelSize;

        public void SetShape(CreatureVoxelShape value)
        {
            shape = value;
        }

        public void Configure(
            MeshCollider collider,
            CreatureVoxelShape voxelShape,
            float voxelSize)
        {
            sourceCollider = collider;
            shape = voxelShape;
            bakeVoxelSize = Mathf.Max(0.0001f, voxelSize);
        }

        private void Reset()
        {
            sourceCollider = GetComponentInChildren<MeshCollider>();
        }

        private void OnValidate()
        {
            bakeVoxelSize = Mathf.Max(0.0001f, bakeVoxelSize);
            if (sourceCollider == null)
            {
                sourceCollider = GetComponentInChildren<MeshCollider>();
            }
        }

        private void OnDrawGizmos()
        {
            if (!showOccupiedVoxels || shape == null || shape.IsEmpty)
            {
                return;
            }

            float size = shape.BakedVoxelSize;
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousColor = Gizmos.color;
            Gizmos.matrix = transform.localToWorldMatrix;

            IReadOnlyList<Vector3Int> voxels = shape.OccupiedVoxels;
            for (int i = 0; i < voxels.Count; i++)
            {
                Vector3 centre = ((Vector3)voxels[i] + Vector3.one * 0.5f) * size;
                Gizmos.color = previewColor;
                Gizmos.DrawCube(centre, Vector3.one * size * 0.94f);
                Gizmos.color = new Color(
                    previewColor.r,
                    previewColor.g,
                    previewColor.b,
                    Mathf.Max(0.65f, previewColor.a));
                Gizmos.DrawWireCube(centre, Vector3.one * size);
            }

            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Supernova.Voxels
{
    public enum MarchingCubesVertexPlacement
    {
        EdgeMidpoint,
        DensityInterpolated,
    }

    /// <summary>
    /// Extracts an isosurface with the fixed 256-case topology table. Vertex placement
    /// can preserve the legacy edge midpoints or interpolate the sampled density.
    /// </summary>
    public static class MarchingCubesMesher
    {
        private static readonly Vector3Int[] CornerOffsets =
        {
            new Vector3Int(0, 0, 0),
            new Vector3Int(1, 0, 0),
            new Vector3Int(1, 0, 1),
            new Vector3Int(0, 0, 1),
            new Vector3Int(0, 1, 0),
            new Vector3Int(1, 1, 0),
            new Vector3Int(1, 1, 1),
            new Vector3Int(0, 1, 1),
        };

        private static readonly Vector3[] EdgeMidpoints =
        {
            new Vector3(0.5f, 0f, 0f),
            new Vector3(1f, 0f, 0.5f),
            new Vector3(0.5f, 0f, 1f),
            new Vector3(0f, 0f, 0.5f),
            new Vector3(0.5f, 1f, 0f),
            new Vector3(1f, 1f, 0.5f),
            new Vector3(0.5f, 1f, 1f),
            new Vector3(0f, 1f, 0.5f),
            new Vector3(0f, 0.5f, 0f),
            new Vector3(1f, 0.5f, 0f),
            new Vector3(1f, 0.5f, 1f),
            new Vector3(0f, 0.5f, 1f),
        };

        private static readonly sbyte[,] TriangleTable =
        {
            { -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 8, 3, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 1, 9, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 1, 8, 3, 1, 9, 8, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 1, 2, 10, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 10, 1, 0, 2, 10, 0, 3, 2, 0, 8, 3, -1, -1, -1, -1 },
            { 0, 2, 10, 0, 10, 9, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 2, 8, 3, 2, 9, 8, 2, 10, 9, -1, -1, -1, -1, -1, -1, -1 },
            { 2, 3, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 11, 2, 0, 8, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 3, 11, 0, 11, 2, 0, 2, 1, 0, 1, 9, -1, -1, -1, -1 },
            { 1, 11, 2, 1, 8, 11, 1, 9, 8, -1, -1, -1, -1, -1, -1, -1 },
            { 1, 3, 11, 1, 11, 10, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 10, 1, 0, 11, 10, 0, 8, 11, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 3, 11, 0, 11, 10, 0, 10, 9, -1, -1, -1, -1, -1, -1, -1 },
            { 8, 10, 9, 8, 11, 10, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 4, 7, 8, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 7, 3, 0, 4, 7, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 1, 9, 0, 9, 4, 0, 4, 7, 0, 7, 8, -1, -1, -1, -1 },
            { 1, 7, 3, 1, 4, 7, 1, 9, 4, -1, -1, -1, -1, -1, -1, -1 },
            { 1, 2, 10, 4, 7, 8, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 10, 1, 0, 2, 10, 0, 3, 2, 0, 7, 3, 0, 4, 7, -1 },
            { 0, 2, 10, 0, 10, 9, 0, 9, 4, 0, 4, 7, 0, 7, 8, -1 },
            { 2, 7, 3, 2, 4, 7, 2, 9, 4, 2, 10, 9, -1, -1, -1, -1 },
            { 2, 3, 8, 2, 8, 4, 2, 4, 7, 2, 7, 11, -1, -1, -1, -1 },
            { 0, 11, 2, 0, 7, 11, 0, 4, 7, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 3, 8, 1, 11, 2, 1, 7, 11, 1, 4, 7, 1, 9, 4, -1 },
            { 1, 11, 2, 1, 7, 11, 1, 4, 7, 1, 9, 4, -1, -1, -1, -1 },
            { 1, 3, 8, 1, 8, 4, 1, 4, 7, 1, 7, 11, 1, 11, 10, -1 },
            { 0, 10, 1, 0, 11, 10, 0, 7, 11, 0, 4, 7, -1, -1, -1, -1 },
            { 0, 3, 8, 4, 7, 11, 4, 11, 10, 4, 10, 9, -1, -1, -1, -1 },
            { 4, 7, 11, 4, 11, 10, 4, 10, 9, -1, -1, -1, -1, -1, -1, -1 },
            { 4, 9, 5, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 8, 3, 0, 4, 8, 0, 5, 4, 0, 9, 5, -1, -1, -1, -1 },
            { 0, 1, 5, 0, 5, 4, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 1, 8, 3, 1, 4, 8, 1, 5, 4, -1, -1, -1, -1, -1, -1, -1 },
            { 1, 2, 10, 1, 10, 5, 1, 5, 4, 1, 4, 9, -1, -1, -1, -1 },
            { 0, 9, 1, 2, 8, 3, 2, 4, 8, 2, 5, 4, 2, 10, 5, -1 },
            { 0, 2, 10, 0, 10, 5, 0, 5, 4, -1, -1, -1, -1, -1, -1, -1 },
            { 2, 8, 3, 2, 4, 8, 2, 5, 4, 2, 10, 5, -1, -1, -1, -1 },
            { 2, 3, 11, 4, 9, 5, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 11, 2, 0, 8, 11, 0, 4, 8, 0, 5, 4, 0, 9, 5, -1 },
            { 0, 3, 11, 0, 11, 2, 0, 2, 1, 0, 1, 5, 0, 5, 4, -1 },
            { 1, 11, 2, 1, 8, 11, 1, 4, 8, 1, 5, 4, -1, -1, -1, -1 },
            { 1, 3, 11, 1, 11, 10, 1, 10, 5, 1, 5, 4, 1, 4, 9, -1 },
            { 0, 9, 1, 4, 10, 5, 4, 11, 10, 4, 8, 11, -1, -1, -1, -1 },
            { 0, 3, 11, 0, 11, 10, 0, 10, 5, 0, 5, 4, -1, -1, -1, -1 },
            { 4, 10, 5, 4, 11, 10, 4, 8, 11, -1, -1, -1, -1, -1, -1, -1 },
            { 5, 7, 8, 5, 8, 9, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 7, 3, 0, 5, 7, 0, 9, 5, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 1, 5, 0, 5, 7, 0, 7, 8, -1, -1, -1, -1, -1, -1, -1 },
            { 1, 7, 3, 1, 5, 7, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 1, 2, 10, 1, 10, 5, 1, 5, 7, 1, 7, 8, 1, 8, 9, -1 },
            { 0, 9, 1, 2, 7, 3, 2, 5, 7, 2, 10, 5, -1, -1, -1, -1 },
            { 0, 2, 10, 0, 10, 5, 0, 5, 7, 0, 7, 8, -1, -1, -1, -1 },
            { 2, 7, 3, 2, 5, 7, 2, 10, 5, -1, -1, -1, -1, -1, -1, -1 },
            { 2, 3, 8, 2, 8, 9, 2, 9, 5, 2, 5, 7, 2, 7, 11, -1 },
            { 0, 11, 2, 0, 7, 11, 0, 5, 7, 0, 9, 5, -1, -1, -1, -1 },
            { 0, 3, 8, 1, 11, 2, 1, 7, 11, 1, 5, 7, -1, -1, -1, -1 },
            { 1, 11, 2, 1, 7, 11, 1, 5, 7, -1, -1, -1, -1, -1, -1, -1 },
            { 1, 3, 8, 1, 8, 9, 5, 7, 11, 5, 11, 10, -1, -1, -1, -1 },
            { 0, 9, 1, 5, 7, 11, 5, 11, 10, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 3, 8, 5, 7, 11, 5, 11, 10, -1, -1, -1, -1, -1, -1, -1 },
            { 5, 7, 11, 5, 11, 10, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 5, 10, 6, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 8, 3, 5, 10, 6, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 1, 10, 0, 10, 6, 0, 6, 5, 0, 5, 9, -1, -1, -1, -1 },
            { 1, 8, 3, 1, 9, 8, 1, 5, 9, 1, 6, 5, 1, 10, 6, -1 },
            { 1, 2, 6, 1, 6, 5, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 5, 1, 0, 6, 5, 0, 2, 6, 0, 3, 2, 0, 8, 3, -1 },
            { 0, 2, 6, 0, 6, 5, 0, 5, 9, -1, -1, -1, -1, -1, -1, -1 },
            { 2, 8, 3, 2, 9, 8, 2, 5, 9, 2, 6, 5, -1, -1, -1, -1 },
            { 2, 3, 11, 2, 11, 6, 2, 6, 5, 2, 5, 10, -1, -1, -1, -1 },
            { 0, 10, 2, 0, 5, 10, 0, 6, 5, 0, 11, 6, 0, 8, 11, -1 },
            { 0, 3, 11, 0, 11, 6, 0, 6, 5, 0, 5, 9, 1, 10, 2, -1 },
            { 1, 10, 2, 5, 11, 6, 5, 8, 11, 5, 9, 8, -1, -1, -1, -1 },
            { 1, 3, 11, 1, 11, 6, 1, 6, 5, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 5, 1, 0, 6, 5, 0, 11, 6, 0, 8, 11, -1, -1, -1, -1 },
            { 0, 3, 11, 0, 11, 6, 0, 6, 5, 0, 5, 9, -1, -1, -1, -1 },
            { 5, 11, 6, 5, 8, 11, 5, 9, 8, -1, -1, -1, -1, -1, -1, -1 },
            { 4, 5, 10, 4, 10, 6, 4, 6, 7, 4, 7, 8, -1, -1, -1, -1 },
            { 0, 7, 3, 0, 6, 7, 0, 10, 6, 0, 5, 10, 0, 4, 5, -1 },
            { 0, 1, 10, 0, 10, 6, 0, 6, 7, 0, 7, 8, 4, 5, 9, -1 },
            { 1, 7, 3, 1, 6, 7, 1, 10, 6, 4, 5, 9, -1, -1, -1, -1 },
            { 1, 2, 6, 1, 6, 7, 1, 7, 8, 1, 8, 4, 1, 4, 5, -1 },
            { 0, 5, 1, 0, 4, 5, 2, 7, 3, 2, 6, 7, -1, -1, -1, -1 },
            { 0, 2, 6, 0, 6, 7, 0, 7, 8, 4, 5, 9, -1, -1, -1, -1 },
            { 2, 7, 3, 2, 6, 7, 4, 5, 9, -1, -1, -1, -1, -1, -1, -1 },
            { 2, 3, 8, 2, 8, 4, 2, 4, 5, 2, 5, 10, 6, 7, 11, -1 },
            { 0, 10, 2, 0, 5, 10, 0, 4, 5, 6, 7, 11, -1, -1, -1, -1 },
            { 0, 3, 8, 1, 10, 2, 4, 5, 9, 6, 7, 11, -1, -1, -1, -1 },
            { 1, 10, 2, 4, 5, 9, 6, 7, 11, -1, -1, -1, -1, -1, -1, -1 },
            { 1, 3, 8, 1, 8, 4, 1, 4, 5, 6, 7, 11, -1, -1, -1, -1 },
            { 0, 5, 1, 0, 4, 5, 6, 7, 11, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 3, 8, 4, 5, 9, 6, 7, 11, -1, -1, -1, -1, -1, -1, -1 },
            { 4, 5, 9, 6, 7, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 4, 10, 6, 4, 9, 10, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 8, 3, 0, 4, 8, 0, 6, 4, 0, 10, 6, 0, 9, 10, -1 },
            { 0, 1, 10, 0, 10, 6, 0, 6, 4, -1, -1, -1, -1, -1, -1, -1 },
            { 1, 8, 3, 1, 4, 8, 1, 6, 4, 1, 10, 6, -1, -1, -1, -1 },
            { 1, 2, 6, 1, 6, 4, 1, 4, 9, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 9, 1, 2, 8, 3, 2, 4, 8, 2, 6, 4, -1, -1, -1, -1 },
            { 0, 2, 6, 0, 6, 4, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 2, 8, 3, 2, 4, 8, 2, 6, 4, -1, -1, -1, -1, -1, -1, -1 },
            { 2, 3, 11, 2, 11, 6, 2, 6, 4, 2, 4, 9, 2, 9, 10, -1 },
            { 0, 10, 2, 0, 9, 10, 4, 11, 6, 4, 8, 11, -1, -1, -1, -1 },
            { 0, 3, 11, 0, 11, 6, 0, 6, 4, 1, 10, 2, -1, -1, -1, -1 },
            { 1, 10, 2, 4, 11, 6, 4, 8, 11, -1, -1, -1, -1, -1, -1, -1 },
            { 1, 3, 11, 1, 11, 6, 1, 6, 4, 1, 4, 9, -1, -1, -1, -1 },
            { 0, 9, 1, 4, 11, 6, 4, 8, 11, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 3, 11, 0, 11, 6, 0, 6, 4, -1, -1, -1, -1, -1, -1, -1 },
            { 4, 11, 6, 4, 8, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 6, 7, 8, 6, 8, 9, 6, 9, 10, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 7, 3, 0, 6, 7, 0, 10, 6, 0, 9, 10, -1, -1, -1, -1 },
            { 0, 1, 10, 0, 10, 6, 0, 6, 7, 0, 7, 8, -1, -1, -1, -1 },
            { 1, 7, 3, 1, 6, 7, 1, 10, 6, -1, -1, -1, -1, -1, -1, -1 },
            { 1, 2, 6, 1, 6, 7, 1, 7, 8, 1, 8, 9, -1, -1, -1, -1 },
            { 0, 9, 1, 2, 7, 3, 2, 6, 7, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 2, 6, 0, 6, 7, 0, 7, 8, -1, -1, -1, -1, -1, -1, -1 },
            { 2, 7, 3, 2, 6, 7, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 2, 3, 8, 2, 8, 9, 2, 9, 10, 6, 7, 11, -1, -1, -1, -1 },
            { 0, 10, 2, 0, 9, 10, 6, 7, 11, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 3, 8, 1, 10, 2, 6, 7, 11, -1, -1, -1, -1, -1, -1, -1 },
            { 1, 10, 2, 6, 7, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 1, 3, 8, 1, 8, 9, 6, 7, 11, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 9, 1, 6, 7, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 3, 8, 6, 7, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 6, 7, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 6, 11, 7, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 11, 3, 0, 6, 11, 0, 7, 6, 0, 8, 7, -1, -1, -1, -1 },
            { 0, 1, 9, 6, 11, 7, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 1, 11, 3, 1, 6, 11, 1, 7, 6, 1, 8, 7, 1, 9, 8, -1 },
            { 1, 2, 11, 1, 11, 7, 1, 7, 6, 1, 6, 10, -1, -1, -1, -1 },
            { 0, 10, 1, 0, 6, 10, 0, 7, 6, 0, 8, 7, 2, 11, 3, -1 },
            { 0, 2, 11, 0, 11, 7, 0, 7, 6, 0, 6, 10, 0, 10, 9, -1 },
            { 2, 11, 3, 6, 8, 7, 6, 9, 8, 6, 10, 9, -1, -1, -1, -1 },
            { 2, 3, 7, 2, 7, 6, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 6, 2, 0, 7, 6, 0, 8, 7, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 3, 7, 0, 7, 6, 0, 6, 2, 0, 2, 1, 0, 1, 9, -1 },
            { 1, 6, 2, 1, 7, 6, 1, 8, 7, 1, 9, 8, -1, -1, -1, -1 },
            { 1, 3, 7, 1, 7, 6, 1, 6, 10, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 10, 1, 0, 6, 10, 0, 7, 6, 0, 8, 7, -1, -1, -1, -1 },
            { 0, 3, 7, 0, 7, 6, 0, 6, 10, 0, 10, 9, -1, -1, -1, -1 },
            { 6, 8, 7, 6, 9, 8, 6, 10, 9, -1, -1, -1, -1, -1, -1, -1 },
            { 4, 6, 11, 4, 11, 8, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 11, 3, 0, 6, 11, 0, 4, 6, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 1, 9, 0, 9, 4, 0, 4, 6, 0, 6, 11, 0, 11, 8, -1 },
            { 1, 11, 3, 1, 6, 11, 1, 4, 6, 1, 9, 4, -1, -1, -1, -1 },
            { 1, 2, 11, 1, 11, 8, 1, 8, 4, 1, 4, 6, 1, 6, 10, -1 },
            { 0, 10, 1, 0, 6, 10, 0, 4, 6, 2, 11, 3, -1, -1, -1, -1 },
            { 0, 2, 11, 0, 11, 8, 4, 6, 10, 4, 10, 9, -1, -1, -1, -1 },
            { 2, 11, 3, 4, 6, 10, 4, 10, 9, -1, -1, -1, -1, -1, -1, -1 },
            { 2, 3, 8, 2, 8, 4, 2, 4, 6, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 6, 2, 0, 4, 6, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 3, 8, 1, 6, 2, 1, 4, 6, 1, 9, 4, -1, -1, -1, -1 },
            { 1, 6, 2, 1, 4, 6, 1, 9, 4, -1, -1, -1, -1, -1, -1, -1 },
            { 1, 3, 8, 1, 8, 4, 1, 4, 6, 1, 6, 10, -1, -1, -1, -1 },
            { 0, 10, 1, 0, 6, 10, 0, 4, 6, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 3, 8, 4, 6, 10, 4, 10, 9, -1, -1, -1, -1, -1, -1, -1 },
            { 4, 6, 10, 4, 10, 9, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 4, 11, 7, 4, 6, 11, 4, 5, 6, 4, 9, 5, -1, -1, -1, -1 },
            { 0, 11, 3, 0, 6, 11, 0, 5, 6, 0, 9, 5, 4, 8, 7, -1 },
            { 0, 1, 5, 0, 5, 6, 0, 6, 11, 0, 11, 7, 0, 7, 4, -1 },
            { 1, 11, 3, 1, 6, 11, 1, 5, 6, 4, 8, 7, -1, -1, -1, -1 },
            { 1, 2, 11, 1, 11, 7, 1, 7, 4, 1, 4, 9, 5, 6, 10, -1 },
            { 0, 9, 1, 2, 11, 3, 4, 8, 7, 5, 6, 10, -1, -1, -1, -1 },
            { 0, 2, 11, 0, 11, 7, 0, 7, 4, 5, 6, 10, -1, -1, -1, -1 },
            { 2, 11, 3, 4, 8, 7, 5, 6, 10, -1, -1, -1, -1, -1, -1, -1 },
            { 2, 3, 7, 2, 7, 4, 2, 4, 9, 2, 9, 5, 2, 5, 6, -1 },
            { 0, 6, 2, 0, 5, 6, 0, 9, 5, 4, 8, 7, -1, -1, -1, -1 },
            { 0, 3, 7, 0, 7, 4, 1, 6, 2, 1, 5, 6, -1, -1, -1, -1 },
            { 1, 6, 2, 1, 5, 6, 4, 8, 7, -1, -1, -1, -1, -1, -1, -1 },
            { 1, 3, 7, 1, 7, 4, 1, 4, 9, 5, 6, 10, -1, -1, -1, -1 },
            { 0, 9, 1, 4, 8, 7, 5, 6, 10, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 3, 7, 0, 7, 4, 5, 6, 10, -1, -1, -1, -1, -1, -1, -1 },
            { 4, 8, 7, 5, 6, 10, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 5, 6, 11, 5, 11, 8, 5, 8, 9, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 11, 3, 0, 6, 11, 0, 5, 6, 0, 9, 5, -1, -1, -1, -1 },
            { 0, 1, 5, 0, 5, 6, 0, 6, 11, 0, 11, 8, -1, -1, -1, -1 },
            { 1, 11, 3, 1, 6, 11, 1, 5, 6, -1, -1, -1, -1, -1, -1, -1 },
            { 1, 2, 11, 1, 11, 8, 1, 8, 9, 5, 6, 10, -1, -1, -1, -1 },
            { 0, 9, 1, 2, 11, 3, 5, 6, 10, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 2, 11, 0, 11, 8, 5, 6, 10, -1, -1, -1, -1, -1, -1, -1 },
            { 2, 11, 3, 5, 6, 10, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 2, 3, 8, 2, 8, 9, 2, 9, 5, 2, 5, 6, -1, -1, -1, -1 },
            { 0, 6, 2, 0, 5, 6, 0, 9, 5, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 3, 8, 1, 6, 2, 1, 5, 6, -1, -1, -1, -1, -1, -1, -1 },
            { 1, 6, 2, 1, 5, 6, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 1, 3, 8, 1, 8, 9, 5, 6, 10, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 9, 1, 5, 6, 10, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 3, 8, 5, 6, 10, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 5, 6, 10, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 5, 11, 7, 5, 10, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 11, 3, 0, 10, 11, 0, 5, 10, 0, 7, 5, 0, 8, 7, -1 },
            { 0, 1, 10, 0, 10, 11, 0, 11, 7, 0, 7, 5, 0, 5, 9, -1 },
            { 1, 11, 3, 1, 10, 11, 5, 8, 7, 5, 9, 8, -1, -1, -1, -1 },
            { 1, 2, 11, 1, 11, 7, 1, 7, 5, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 5, 1, 0, 7, 5, 0, 8, 7, 2, 11, 3, -1, -1, -1, -1 },
            { 0, 2, 11, 0, 11, 7, 0, 7, 5, 0, 5, 9, -1, -1, -1, -1 },
            { 2, 11, 3, 5, 8, 7, 5, 9, 8, -1, -1, -1, -1, -1, -1, -1 },
            { 2, 3, 7, 2, 7, 5, 2, 5, 10, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 10, 2, 0, 5, 10, 0, 7, 5, 0, 8, 7, -1, -1, -1, -1 },
            { 0, 3, 7, 0, 7, 5, 0, 5, 9, 1, 10, 2, -1, -1, -1, -1 },
            { 1, 10, 2, 5, 8, 7, 5, 9, 8, -1, -1, -1, -1, -1, -1, -1 },
            { 1, 3, 7, 1, 7, 5, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 5, 1, 0, 7, 5, 0, 8, 7, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 3, 7, 0, 7, 5, 0, 5, 9, -1, -1, -1, -1, -1, -1, -1 },
            { 5, 8, 7, 5, 9, 8, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 4, 5, 10, 4, 10, 11, 4, 11, 8, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 11, 3, 0, 10, 11, 0, 5, 10, 0, 4, 5, -1, -1, -1, -1 },
            { 0, 1, 10, 0, 10, 11, 0, 11, 8, 4, 5, 9, -1, -1, -1, -1 },
            { 1, 11, 3, 1, 10, 11, 4, 5, 9, -1, -1, -1, -1, -1, -1, -1 },
            { 1, 2, 11, 1, 11, 8, 1, 8, 4, 1, 4, 5, -1, -1, -1, -1 },
            { 0, 5, 1, 0, 4, 5, 2, 11, 3, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 2, 11, 0, 11, 8, 4, 5, 9, -1, -1, -1, -1, -1, -1, -1 },
            { 2, 11, 3, 4, 5, 9, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 2, 3, 8, 2, 8, 4, 2, 4, 5, 2, 5, 10, -1, -1, -1, -1 },
            { 0, 10, 2, 0, 5, 10, 0, 4, 5, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 3, 8, 1, 10, 2, 4, 5, 9, -1, -1, -1, -1, -1, -1, -1 },
            { 1, 10, 2, 4, 5, 9, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 1, 3, 8, 1, 8, 4, 1, 4, 5, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 5, 1, 0, 4, 5, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 3, 8, 4, 5, 9, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 4, 5, 9, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 4, 11, 7, 4, 10, 11, 4, 9, 10, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 11, 3, 0, 10, 11, 0, 9, 10, 4, 8, 7, -1, -1, -1, -1 },
            { 0, 1, 10, 0, 10, 11, 0, 11, 7, 0, 7, 4, -1, -1, -1, -1 },
            { 1, 11, 3, 1, 10, 11, 4, 8, 7, -1, -1, -1, -1, -1, -1, -1 },
            { 1, 2, 11, 1, 11, 7, 1, 7, 4, 1, 4, 9, -1, -1, -1, -1 },
            { 0, 9, 1, 2, 11, 3, 4, 8, 7, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 2, 11, 0, 11, 7, 0, 7, 4, -1, -1, -1, -1, -1, -1, -1 },
            { 2, 11, 3, 4, 8, 7, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 2, 3, 7, 2, 7, 4, 2, 4, 9, 2, 9, 10, -1, -1, -1, -1 },
            { 0, 10, 2, 0, 9, 10, 4, 8, 7, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 3, 7, 0, 7, 4, 1, 10, 2, -1, -1, -1, -1, -1, -1, -1 },
            { 1, 10, 2, 4, 8, 7, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 1, 3, 7, 1, 7, 4, 1, 4, 9, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 9, 1, 4, 8, 7, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 3, 7, 0, 7, 4, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 4, 8, 7, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 8, 9, 10, 8, 10, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 11, 3, 0, 10, 11, 0, 9, 10, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 1, 10, 0, 10, 11, 0, 11, 8, -1, -1, -1, -1, -1, -1, -1 },
            { 1, 11, 3, 1, 10, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 1, 2, 11, 1, 11, 8, 1, 8, 9, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 9, 1, 2, 11, 3, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 2, 11, 0, 11, 8, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 2, 11, 3, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 2, 3, 8, 2, 8, 9, 2, 9, 10, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 10, 2, 0, 9, 10, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 3, 8, 1, 10, 2, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 1, 10, 2, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 1, 3, 8, 1, 8, 9, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 9, 1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { 0, 3, 8, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
            { -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        };
        private static readonly int[,] EdgeCorners =
        {
            { 0, 1 }, { 1, 2 }, { 2, 3 }, { 3, 0 },
            { 4, 5 }, { 5, 6 }, { 6, 7 }, { 7, 4 },
            { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 },
        };

        // Each surface is polygonised as its own binary field. At a solid/solid
        // boundary between two surfaces, the two are moved slightly toward their
        // owning samples, leaving a small seam instead of two coincident,
        // z-fighting surfaces. Voxel types sharing a group form ONE surface, so
        // no seam is produced between them.
        private const float TypeBoundaryInset = 0.05f;
        [ThreadStatic] private static VoxelSample[] sampleCache;
        [ThreadStatic] private static List<int> activeSurfaceKeys;
        [ThreadStatic] private static HashSet<int> activeSurfaceKeySet;
        [ThreadStatic] private static List<VoxelTypeId> cellMemberTypes;
        [ThreadStatic] private static List<int> cellMemberCounts;
        [ThreadStatic] private static Vector3[] edgePositionCache;
        [ThreadStatic] private static int[] edgeSmoothingGroupCache;
        [ThreadStatic] private static int[] projectedEdgeVertexIndexCache;


        public static VoxelMeshData Build(
            VoxelVolume volume,
            float isoLevel = 0f,
            float voxelSize = 1f,
            MarchingCubesVertexPlacement vertexPlacement =
                MarchingCubesVertexPlacement.EdgeMidpoint,
            VoxelGroupMap groupMap = default)
        {
            if (volume == null)
            {
                throw new ArgumentNullException(nameof(volume));
            }

            return BuildGrid(
                (x, y, z) => volume.GetSample(x, y, z),
                VoxelVolume.Size - 1,
                VoxelVolume.Size - 1,
                VoxelVolume.Size - 1,
                isoLevel,
                voxelSize,
                vertexPlacement,
                null,
                default,
                groupMap);
        }

        public static VoxelMeshData BuildChunk(
            VoxelChunkRegion region,
            int chunkX,
            int chunkZ,
            float isoLevel = 0f,
            float voxelSize = 1f,
            MarchingCubesVertexPlacement vertexPlacement =
                MarchingCubesVertexPlacement.EdgeMidpoint)
        {
            if (region == null)
            {
                throw new ArgumentNullException(nameof(region));
            }

            if (!region.IsChunkInBounds(chunkX, chunkZ))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(chunkX),
                    $"Chunk coordinate ({chunkX}, {chunkZ}) is outside the region.");
            }

            int originX = chunkX * VoxelVolume.Size;
            int originZ = chunkZ * VoxelVolume.Size;
            float outsideDensity = isoLevel - 1f;
            return BuildGrid(
                (x, y, z) => region.GetWorldSampleOrDefault(
                    originX + x,
                    y,
                    originZ + z,
                    outsideDensity,
                    VoxelTypeId.Air),
                VoxelVolume.Size,
                VoxelVolume.Size,
                VoxelVolume.Size,
                isoLevel,
                voxelSize,
                vertexPlacement);
        }

        public static VoxelMeshData BuildChunk(
            InfiniteVoxelWorld world,
            Vector3Int chunkCoordinate,
            float isoLevel = 0f,
            float voxelSize = 1f,
            MarchingCubesVertexPlacement vertexPlacement =
                MarchingCubesVertexPlacement.EdgeMidpoint,
            VoxelTypeId? outsideType = null,
            VoxelTypeId? verticalOutsideType = null,
            VoxelGroupMap groupMap = default)
        {
            return BuildColumnRange(
                world,
                chunkCoordinate,
                0,
                VoxelColumnChunkData.Height,
                isoLevel,
                voxelSize,
                vertexPlacement,
                outsideType,
                verticalOutsideType,
                groupMap);
        }

        public static VoxelMeshData BuildColumnSection(
            InfiniteVoxelWorld world,
            Vector3Int columnCoordinate,
            int startY,
            int height,
            float isoLevel = 0f,
            float voxelSize = 1f,
            MarchingCubesVertexPlacement vertexPlacement =
                MarchingCubesVertexPlacement.EdgeMidpoint,
            VoxelTypeId? outsideType = null,
            VoxelTypeId? verticalOutsideType = null,
            VoxelGroupMap groupMap = default)
        {
            if (startY < 0
                || height <= 0
                || startY + height > VoxelColumnChunkData.Height)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(startY),
                    $"Mesh section {startY}..{startY + height} is outside "
                    + $"0..{VoxelColumnChunkData.Height}.");
            }

            return BuildColumnRange(
                world,
                columnCoordinate,
                startY,
                height,
                isoLevel,
                voxelSize,
                vertexPlacement,
                outsideType,
                verticalOutsideType,
                groupMap);
        }

        private static VoxelMeshData BuildColumnRange(
            InfiniteVoxelWorld world,
            Vector3Int columnCoordinate,
            int startY,
            int height,
            float isoLevel,
            float voxelSize,
            MarchingCubesVertexPlacement vertexPlacement,
            VoxelTypeId? outsideType,
            VoxelTypeId? verticalOutsideType,
            VoxelGroupMap groupMap = default)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            var origin = new Vector3Int(
                columnCoordinate.x * VoxelColumnChunkData.Width,
                startY,
                columnCoordinate.z * VoxelColumnChunkData.Depth);
            // Ungenerated neighbours remain assumed solid, matching the existing
            // infinite-world streaming behavior.
            float outsideDensity = isoLevel + 1f;
            return BuildGrid(
                (x, y, z) =>
                {
                    int worldY = origin.y + y;
                    VoxelTypeId fallbackType = InfiniteVoxelWorld.IsWorldYInBounds(worldY)
                        ? outsideType ?? VoxelTypeId.Default
                        : verticalOutsideType ?? outsideType ?? VoxelTypeId.Default;
                    return world.GetSampleOrDefault(
                        origin.x + x,
                        worldY,
                        origin.z + z,
                        outsideDensity,
                        fallbackType);
                },
                VoxelColumnChunkData.Width,
                height,
                VoxelColumnChunkData.Depth,
                isoLevel,
                voxelSize,
                vertexPlacement,
                null,
                default,
                groupMap);
        }

        internal static int GetCapturedColumnSectionSampleCount(int height)
        {
            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            return (VoxelColumnChunkData.Width + 1)
                * (height + 1)
                * (VoxelColumnChunkData.Depth + 1);
        }

        internal static void CaptureColumnSectionSamples(
            InfiniteVoxelWorld world,
            Vector3Int columnCoordinate,
            int startY,
            int height,
            float isoLevel,
            VoxelSample[] samples,
            VoxelTypeId? outsideType = null,
            VoxelTypeId? verticalOutsideType = null)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }
            ValidateColumnSection(startY, height);
            int requiredSampleCount = GetCapturedColumnSectionSampleCount(height);
            if (samples == null || samples.Length < requiredSampleCount)
            {
                throw new ArgumentException(
                    $"Sample buffer must contain at least {requiredSampleCount} entries.",
                    nameof(samples));
            }

            int sampleCountX = VoxelColumnChunkData.Width + 1;
            int sampleCountY = height + 1;
            int sampleCountZ = VoxelColumnChunkData.Depth + 1;
            float outsideDensity = isoLevel + 1f;
            VoxelColumnChunkData current = GetColumnDataOrNull(
                world,
                columnCoordinate.x,
                columnCoordinate.z);
            VoxelColumnChunkData positiveX = GetColumnDataOrNull(
                world,
                columnCoordinate.x + 1,
                columnCoordinate.z);
            VoxelColumnChunkData positiveZ = GetColumnDataOrNull(
                world,
                columnCoordinate.x,
                columnCoordinate.z + 1);
            VoxelColumnChunkData positiveXPositiveZ = GetColumnDataOrNull(
                world,
                columnCoordinate.x + 1,
                columnCoordinate.z + 1);

            for (int z = 0; z < sampleCountZ; z++)
            {
                bool crossesPositiveZBoundary = z == VoxelColumnChunkData.Depth;
                int localZ = crossesPositiveZBoundary ? 0 : z;
                VoxelColumnChunkData rowColumn = crossesPositiveZBoundary
                    ? positiveZ
                    : current;
                VoxelColumnChunkData boundaryColumn = crossesPositiveZBoundary
                    ? positiveXPositiveZ
                    : positiveX;
                for (int y = 0; y < sampleCountY; y++)
                {
                    int worldY = startY + y;
                    bool worldYInBounds = InfiniteVoxelWorld.IsWorldYInBounds(worldY);
                    VoxelTypeId fallbackType = worldYInBounds
                        ? outsideType ?? VoxelTypeId.Default
                        : verticalOutsideType ?? outsideType ?? VoxelTypeId.Default;
                    var outsideSample = new VoxelSample(
                        outsideDensity,
                        fallbackType);
                    int rowStart = sampleCountX * (y + sampleCountY * z);
                    if (!worldYInBounds)
                    {
                        FillSampleRange(
                            samples,
                            rowStart,
                            sampleCountX,
                            outsideSample);
                        continue;
                    }

                    if (rowColumn != null)
                    {
                        rowColumn.CopySampleRowTo(
                            worldY,
                            localZ,
                            samples,
                            rowStart);
                    }
                    else
                    {
                        FillSampleRange(
                            samples,
                            rowStart,
                            VoxelColumnChunkData.Width,
                            outsideSample);
                    }

                    samples[rowStart + VoxelColumnChunkData.Width] =
                        boundaryColumn != null
                            ? boundaryColumn.GetSampleUnchecked(0, worldY, localZ)
                            : outsideSample;
                }
            }
        }

        private static VoxelColumnChunkData GetColumnDataOrNull(
            InfiniteVoxelWorld world,
            int columnX,
            int columnZ)
        {
            return world.TryGetChunk(
                new Vector2Int(columnX, columnZ),
                out InfiniteVoxelChunk chunk)
                    ? chunk.Data
                    : null;
        }

        private static void FillSampleRange(
            VoxelSample[] samples,
            int startIndex,
            int count,
            VoxelSample sample)
        {
            int endIndex = startIndex + count;
            for (int index = startIndex; index < endIndex; index++)
            {
                samples[index] = sample;
            }
        }

        internal static VoxelMeshData BuildCapturedColumnSection(
            VoxelSample[] samples,
            int height,
            float isoLevel,
            float voxelSize,
            MarchingCubesVertexPlacement vertexPlacement,
            VoxelGroupMap groupMap = default)
        {
            return BuildCapturedColumnSectionInto(
                samples,
                height,
                isoLevel,
                voxelSize,
                vertexPlacement,
                groupMap,
                null);
        }

        internal static VoxelMeshData BuildCapturedColumnSectionPooled(
            VoxelSample[] samples,
            int height,
            float isoLevel,
            float voxelSize,
            MarchingCubesVertexPlacement vertexPlacement,
            VoxelGroupMap groupMap = default)
        {
            VoxelMeshData output = VoxelMeshData.RentPooled();
            try
            {
                return BuildCapturedColumnSectionInto(
                    samples,
                    height,
                    isoLevel,
                    voxelSize,
                    vertexPlacement,
                    groupMap,
                    output);
            }
            catch
            {
                output.Dispose();
                throw;
            }
        }

        private static VoxelMeshData BuildCapturedColumnSectionInto(
            VoxelSample[] samples,
            int height,
            float isoLevel,
            float voxelSize,
            MarchingCubesVertexPlacement vertexPlacement,
            VoxelGroupMap groupMap,
            VoxelMeshData output)
        {
            if (samples == null)
            {
                throw new ArgumentNullException(nameof(samples));
            }
            if (voxelSize <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(voxelSize),
                    "Voxel size must be positive.");
            }

            int expectedSampleCount = GetCapturedColumnSectionSampleCount(height);
            if (samples.Length < expectedSampleCount)
            {
                throw new ArgumentException(
                    "Captured sample count does not match the requested section height.",
                    nameof(samples));
            }

            return BuildSampledGrid(
                samples,
                VoxelColumnChunkData.Width,
                height,
                VoxelColumnChunkData.Depth,
                isoLevel,
                voxelSize,
                vertexPlacement,
                null,
                default,
                groupMap,
                output);
        }


        private static void ValidateColumnSection(int startY, int height)
        {
            if (startY < 0
                || height <= 0
                || startY + height > VoxelColumnChunkData.Height)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(startY),
                    $"Mesh section {startY}..{startY + height} is outside "
                    + $"0..{VoxelColumnChunkData.Height}.");
            }
        }

        /// <summary>
        /// Rebuilds one connected component of a voxel type in terrain-local world
        /// coordinates. The same polygonisation and vertex placement as Chunk meshes
        /// are used, so the extracted geometry matches the component before removal.
        /// </summary>
        public static VoxelMeshData BuildTypeComponent(
            InfiniteVoxelWorld world,
            HashSet<Vector3Int> component,
            VoxelTypeId targetType,
            float isoLevel = 0f,
            float voxelSize = 1f,
            MarchingCubesVertexPlacement vertexPlacement =
                MarchingCubesVertexPlacement.EdgeMidpoint)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }
            if (component == null)
            {
                throw new ArgumentNullException(nameof(component));
            }
            if (component.Count == 0)
            {
                throw new ArgumentException(
                    "A type component must contain at least one sample.",
                    nameof(component));
            }
            if (targetType.IsAir)
            {
                throw new ArgumentException(
                    "Air cannot be extracted as a mesh component.",
                    nameof(targetType));
            }

            Vector3Int minimum = new Vector3Int(
                int.MaxValue,
                int.MaxValue,
                int.MaxValue);
            Vector3Int maximum = new Vector3Int(
                int.MinValue,
                int.MinValue,
                int.MinValue);
            foreach (Vector3Int coordinate in component)
            {
                minimum = Vector3Int.Min(minimum, coordinate);
                maximum = Vector3Int.Max(maximum, coordinate);
            }

            Vector3Int cellOrigin = minimum - Vector3Int.one;
            Vector3Int cellCounts = maximum - minimum
                + Vector3Int.one * 2;
            VoxelTypeId excludedType = targetType != VoxelTypeId.Default
                ? VoxelTypeId.Default
                : new VoxelTypeId(ushort.MaxValue);
            float outsideDensity = isoLevel + 1f;

            return BuildGrid(
                (x, y, z) =>
                {
                    Vector3Int coordinate = cellOrigin
                        + new Vector3Int(x, y, z);
                    VoxelSample sample = world.GetSampleOrDefault(
                        coordinate.x,
                        coordinate.y,
                        coordinate.z,
                        outsideDensity,
                        VoxelTypeId.Default);
                    return sample.Type == targetType
                        && !component.Contains(coordinate)
                            ? new VoxelSample(sample.Density, excludedType)
                            : sample;
                },
                cellCounts.x,
                cellCounts.y,
                cellCounts.z,
                isoLevel,
                voxelSize,
                vertexPlacement,
                targetType,
                (Vector3)cellOrigin * voxelSize);
        }

        /// <summary>
        /// Rebuilds one connected type component from a detached body's sample
        /// snapshot. Samples outside the requested ore component still contribute
        /// their captured densities, matching the live-world extraction surface.
        /// </summary>
        public static VoxelMeshData BuildCapturedTypeComponent(
            HashSet<Vector3Int> component,
            IReadOnlyDictionary<Vector3Int, VoxelSample> samples,
            VoxelTypeId targetType,
            float isoLevel = 0f,
            float voxelSize = 1f,
            MarchingCubesVertexPlacement vertexPlacement =
                MarchingCubesVertexPlacement.EdgeMidpoint)
        {
            if (component == null)
            {
                throw new ArgumentNullException(nameof(component));
            }
            if (samples == null)
            {
                throw new ArgumentNullException(nameof(samples));
            }
            if (component.Count == 0)
            {
                throw new ArgumentException(
                    "A captured type component must contain at least one sample.",
                    nameof(component));
            }
            if (targetType.IsAir)
            {
                throw new ArgumentException(
                    "Air cannot be extracted as a mesh component.",
                    nameof(targetType));
            }

            Vector3Int minimum = new Vector3Int(
                int.MaxValue,
                int.MaxValue,
                int.MaxValue);
            Vector3Int maximum = new Vector3Int(
                int.MinValue,
                int.MinValue,
                int.MinValue);
            foreach (Vector3Int coordinate in component)
            {
                minimum = Vector3Int.Min(minimum, coordinate);
                maximum = Vector3Int.Max(maximum, coordinate);
            }

            Vector3Int cellOrigin = minimum - Vector3Int.one;
            Vector3Int cellCounts = maximum - minimum
                + Vector3Int.one * 2;
            VoxelTypeId excludedType = targetType != VoxelTypeId.Default
                ? VoxelTypeId.Default
                : new VoxelTypeId(ushort.MaxValue);
            float outsideDensity = isoLevel + 1f;

            return BuildGrid(
                (x, y, z) =>
                {
                    Vector3Int coordinate = cellOrigin
                        + new Vector3Int(x, y, z);
                    VoxelSample sample = samples.TryGetValue(
                        coordinate,
                        out VoxelSample captured)
                            ? captured
                            : new VoxelSample(
                                outsideDensity,
                                VoxelTypeId.Default);
                    return sample.Type == targetType
                        && !component.Contains(coordinate)
                            ? new VoxelSample(sample.Density, excludedType)
                            : sample;
                },
                cellCounts.x,
                cellCounts.y,
                cellCounts.z,
                isoLevel,
                voxelSize,
                vertexPlacement,
                targetType,
                (Vector3)cellOrigin * voxelSize);
        }

        /// <summary>
        /// Rebuilds a detached, potentially multi-type solid component in
        /// terrain-local world coordinates. Samples outside the component are
        /// treated as air, which closes the newly cut faces while preserving the
        /// world's density interpolation, surface grouping, and submesh types.
        /// </summary>
        public static VoxelMeshData BuildComponent(
            InfiniteVoxelWorld world,
            HashSet<Vector3Int> component,
            float isoLevel = 0f,
            float voxelSize = 1f,
            MarchingCubesVertexPlacement vertexPlacement =
                MarchingCubesVertexPlacement.EdgeMidpoint,
            VoxelGroupMap groupMap = default)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }
            if (component == null)
            {
                throw new ArgumentNullException(nameof(component));
            }
            if (component.Count == 0)
            {
                throw new ArgumentException(
                    "A component must contain at least one sample.",
                    nameof(component));
            }

            Vector3Int minimum = new Vector3Int(
                int.MaxValue,
                int.MaxValue,
                int.MaxValue);
            Vector3Int maximum = new Vector3Int(
                int.MinValue,
                int.MinValue,
                int.MinValue);
            foreach (Vector3Int coordinate in component)
            {
                minimum = Vector3Int.Min(minimum, coordinate);
                maximum = Vector3Int.Max(maximum, coordinate);
            }

            Vector3Int cellOrigin = minimum - Vector3Int.one;
            Vector3Int cellCounts = maximum - minimum
                + Vector3Int.one * 2;
            float airDensity = isoLevel - 1f;
            return BuildGrid(
                (x, y, z) =>
                {
                    Vector3Int coordinate = cellOrigin
                        + new Vector3Int(x, y, z);
                    if (!component.Contains(coordinate)
                        || !world.TryGetSample(
                            coordinate.x,
                            coordinate.y,
                            coordinate.z,
                            out VoxelSample sample)
                        || !sample.IsSolid(isoLevel))
                    {
                        return new VoxelSample(
                            airDensity,
                            VoxelTypeId.Air);
                    }

                    return sample;
                },
                cellCounts.x,
                cellCounts.y,
                cellCounts.z,
                isoLevel,
                voxelSize,
                vertexPlacement,
                null,
                (Vector3)cellOrigin * voxelSize,
                groupMap);
        }

        /// <summary>
        /// Builds a detached component from an immutable sample snapshot. The
        /// caller may run this method on a worker thread as long as neither
        /// collection is mutated until the method returns.
        /// </summary>
        public static VoxelMeshData BuildCapturedComponent(
            HashSet<Vector3Int> component,
            IReadOnlyDictionary<Vector3Int, VoxelSample> samples,
            float isoLevel = 0f,
            float voxelSize = 1f,
            MarchingCubesVertexPlacement vertexPlacement =
                MarchingCubesVertexPlacement.EdgeMidpoint,
            VoxelGroupMap groupMap = default)
        {
            if (component == null)
            {
                throw new ArgumentNullException(nameof(component));
            }
            if (samples == null)
            {
                throw new ArgumentNullException(nameof(samples));
            }
            if (component.Count == 0)
            {
                throw new ArgumentException(
                    "A captured component must contain at least one sample.",
                    nameof(component));
            }

            Vector3Int minimum = new Vector3Int(
                int.MaxValue,
                int.MaxValue,
                int.MaxValue);
            Vector3Int maximum = new Vector3Int(
                int.MinValue,
                int.MinValue,
                int.MinValue);
            foreach (Vector3Int coordinate in component)
            {
                minimum = Vector3Int.Min(minimum, coordinate);
                maximum = Vector3Int.Max(maximum, coordinate);
            }

            Vector3Int cellOrigin = minimum - Vector3Int.one;
            Vector3Int cellCounts = maximum - minimum
                + Vector3Int.one * 2;
            float airDensity = isoLevel - 1f;
            return BuildGrid(
                (x, y, z) =>
                {
                    Vector3Int coordinate = cellOrigin
                        + new Vector3Int(x, y, z);
                    if (!component.Contains(coordinate)
                        || !samples.TryGetValue(
                            coordinate,
                            out VoxelSample sample)
                        || !sample.IsSolid(isoLevel))
                    {
                        return new VoxelSample(
                            airDensity,
                            VoxelTypeId.Air);
                    }

                    return sample;
                },
                cellCounts.x,
                cellCounts.y,
                cellCounts.z,
                isoLevel,
                voxelSize,
                vertexPlacement,
                null,
                (Vector3)cellOrigin * voxelSize,
                groupMap);
        }



        private static VoxelMeshData BuildGrid(
            SampleSampler sample,
            int cellCountX,
            int cellCountY,
            int cellCountZ,
            float isoLevel,
            float voxelSize,
            MarchingCubesVertexPlacement vertexPlacement,
            VoxelTypeId? requestedType = null,
            Vector3 vertexOffset = default,
            VoxelGroupMap groupMap = default)
        {
            if (voxelSize <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(voxelSize),
                    "Voxel size must be positive.");
            }

            int sampleCountX = cellCountX + 1;
            int sampleCountY = cellCountY + 1;
            int sampleCountZ = cellCountZ + 1;
            int sampleCount = sampleCountX * sampleCountY * sampleCountZ;
            if (sampleCache == null || sampleCache.Length < sampleCount)
            {
                sampleCache = new VoxelSample[sampleCount];
            }

            for (int z = 0; z < sampleCountZ; z++)
            {
                for (int y = 0; y < sampleCountY; y++)
                {
                    int rowStart = sampleCountX * (y + sampleCountY * z);
                    for (int x = 0; x < sampleCountX; x++)
                    {
                        sampleCache[rowStart + x] = sample(x, y, z);
                    }
                }
            }

            return BuildSampledGrid(
                sampleCache,
                cellCountX,
                cellCountY,
                cellCountZ,
                isoLevel,
                voxelSize,
                vertexPlacement,
                requestedType,
                vertexOffset,
                groupMap);
        }

        private static VoxelMeshData BuildSampledGrid(
            VoxelSample[] samples,
            int cellCountX,
            int cellCountY,
            int cellCountZ,
            float isoLevel,
            float voxelSize,
            MarchingCubesVertexPlacement vertexPlacement,
            VoxelTypeId? requestedType = null,
            Vector3 vertexOffset = default,
            VoxelGroupMap groupMap = default,
            VoxelMeshData output = null)
        {
            VoxelMeshData meshData = output ?? new VoxelMeshData();
            if (edgePositionCache == null)
            {
                edgePositionCache = new Vector3[12];
            }
            if (edgeSmoothingGroupCache == null)
            {
                edgeSmoothingGroupCache = new int[12];
            }
            if (projectedEdgeVertexIndexCache == null)
            {
                projectedEdgeVertexIndexCache =
                    new int[VoxelMeshData.ProjectedEdgeCacheSize];
            }
            Vector3[] edgePositions = edgePositionCache;
            int[] edgeSmoothingGroups = edgeSmoothingGroupCache;
            int[] projectedEdgeVertexIndices = projectedEdgeVertexIndexCache;
            int sampleCountX = cellCountX + 1;
            int sampleCountY = cellCountY + 1;
            int sampleCountZ = cellCountZ + 1;

            if (activeSurfaceKeys == null)
            {
                activeSurfaceKeys = new List<int>();
            }
            if (activeSurfaceKeySet == null)
            {
                activeSurfaceKeySet = new HashSet<int>();
            }
            if (cellMemberTypes == null)
            {
                cellMemberTypes = new List<VoxelTypeId>();
            }
            if (cellMemberCounts == null)
            {
                cellMemberCounts = new List<int>();
            }
            activeSurfaceKeys.Clear();
            activeSurfaceKeySet.Clear();

            if (requestedType.HasValue)
            {
                // A single requested type is its own surface regardless of groups:
                // callers use this to extract one component in isolation.
                for (int z = 0; z < cellCountZ; z++)
                {
                    for (int y = 0; y < cellCountY; y++)
                    {
                        for (int x = 0; x < cellCountX; x++)
                        {
                            PolygoniseCell(
                                samples,
                                sampleCountX,
                                sampleCountY,
                                x,
                                y,
                                z,
                                groupMap.GetGroupKey(requestedType.Value),
                                requestedType.Value,
                                groupMap,
                                isoLevel,
                                voxelSize,
                                vertexPlacement,
                                vertexOffset,
                                edgePositions,
                                edgeSmoothingGroups,
                                projectedEdgeVertexIndices,
                                meshData);
                        }
                    }
                }
                meshData.PrepareForUpload();
                return meshData;
            }

            int sampleCount = sampleCountX * sampleCountY * sampleCountZ;
            for (int i = 0; i < sampleCount; i++)
            {
                VoxelSample voxel = samples[i];
                if (!voxel.IsSolid(isoLevel))
                {
                    continue;
                }
                int key = groupMap.GetGroupKey(voxel.Type);
                if (activeSurfaceKeySet.Add(key))
                {
                    activeSurfaceKeys.Add(key);
                }
            }

            activeSurfaceKeys.Sort();
            for (int keyIndex = 0; keyIndex < activeSurfaceKeys.Count; keyIndex++)
            {
                int surfaceKey = activeSurfaceKeys[keyIndex];
                for (int z = 0; z < cellCountZ; z++)
                {
                    for (int y = 0; y < cellCountY; y++)
                    {
                        for (int x = 0; x < cellCountX; x++)
                        {
                            PolygoniseCell(
                                samples,
                                sampleCountX,
                                sampleCountY,
                                x,
                                y,
                                z,
                                surfaceKey,
                                null,
                                groupMap,
                                isoLevel,
                                voxelSize,
                                vertexPlacement,
                                vertexOffset,
                                edgePositions,
                                edgeSmoothingGroups,
                                projectedEdgeVertexIndices,
                                meshData);
                        }
                    }
                }
            }

            meshData.PrepareForUpload();
            return meshData;
        }

        private delegate VoxelSample SampleSampler(int x, int y, int z);

        private static void PolygoniseCell(
            VoxelSample[] samples,
            int sampleCountX,
            int sampleCountY,
            int cellX,
            int cellY,
            int cellZ,
            int surfaceKey,
            VoxelTypeId? forcedType,
            VoxelGroupMap groupMap,
            float isoLevel,
            float voxelSize,
            MarchingCubesVertexPlacement vertexPlacement,
            Vector3 vertexOffset,
            Vector3[] edgePositions,
            int[] edgeSmoothingGroups,
            int[] projectedEdgeVertexIndices,
            VoxelMeshData output)
        {
            int cubeIndex = 0;
            cellMemberTypes.Clear();
            cellMemberCounts.Clear();
            for (int corner = 0; corner < 8; corner++)
            {
                VoxelSample voxel = GetCornerSample(
                    samples,
                    sampleCountX,
                    sampleCountY,
                    cellX,
                    cellY,
                    cellZ,
                    corner);
                bool belongs = voxel.IsSolid(isoLevel)
                    && (forcedType.HasValue
                        ? voxel.Type == forcedType.Value
                        : groupMap.GetGroupKey(voxel.Type) == surfaceKey);
                if (!belongs)
                {
                    continue;
                }
                cubeIndex |= 1 << corner;
                TallyMemberType(voxel.Type);
            }

            if (cubeIndex == 0 || cubeIndex == 255)
            {
                return;
            }

            // Submeshes stay per voxel type so each palette keeps its material,
            // even though the surface itself spans the whole group. The type that
            // owns most of the cell's member corners wins its triangles.
            VoxelTypeId triangleType = forcedType ?? GetDominantMemberType();

            for (int edge = 0; edge < edgeSmoothingGroups.Length; edge++)
            {
                edgeSmoothingGroups[edge] = -1;
            }
            for (int vertex = 0; vertex < projectedEdgeVertexIndices.Length; vertex++)
            {
                projectedEdgeVertexIndices[vertex] = -1;
            }

            Vector3 cellOrigin = vertexOffset
                + new Vector3(cellX, cellY, cellZ) * voxelSize;
            for (int tableIndex = 0; tableIndex < 16; tableIndex += 3)
            {
                int firstEdge = TriangleTable[cubeIndex, tableIndex];
                if (firstEdge < 0)
                {
                    break;
                }

                int secondEdge = TriangleTable[cubeIndex, tableIndex + 1];
                int thirdEdge = TriangleTable[cubeIndex, tableIndex + 2];
                PrepareEdge(
                    samples,
                    sampleCountX,
                    sampleCountY,
                    cellX,
                    cellY,
                    cellZ,
                    firstEdge,
                    surfaceKey,
                    forcedType,
                    groupMap,
                    isoLevel,
                    voxelSize,
                    vertexPlacement,
                    cellOrigin,
                    edgePositions,
                    edgeSmoothingGroups,
                    output);
                PrepareEdge(
                    samples,
                    sampleCountX,
                    sampleCountY,
                    cellX,
                    cellY,
                    cellZ,
                    secondEdge,
                    surfaceKey,
                    forcedType,
                    groupMap,
                    isoLevel,
                    voxelSize,
                    vertexPlacement,
                    cellOrigin,
                    edgePositions,
                    edgeSmoothingGroups,
                    output);
                PrepareEdge(
                    samples,
                    sampleCountX,
                    sampleCountY,
                    cellX,
                    cellY,
                    cellZ,
                    thirdEdge,
                    surfaceKey,
                    forcedType,
                    groupMap,
                    isoLevel,
                    voxelSize,
                    vertexPlacement,
                    cellOrigin,
                    edgePositions,
                    edgeSmoothingGroups,
                    output);

                output.AddFaceProjectedTriangle(
                    triangleType,
                    firstEdge,
                    secondEdge,
                    thirdEdge,
                    edgePositions,
                    edgeSmoothingGroups,
                    projectedEdgeVertexIndices);
            }
        }

        private static void PrepareEdge(
            VoxelSample[] samples,
            int sampleCountX,
            int sampleCountY,
            int cellX,
            int cellY,
            int cellZ,
            int edge,
            int surfaceKey,
            VoxelTypeId? forcedType,
            VoxelGroupMap groupMap,
            float isoLevel,
            float voxelSize,
            MarchingCubesVertexPlacement vertexPlacement,
            Vector3 cellOrigin,
            Vector3[] edgePositions,
            int[] edgeSmoothingGroups,
            VoxelMeshData output)
        {
            if (edgeSmoothingGroups[edge] >= 0)
            {
                return;
            }

            edgePositions[edge] = cellOrigin + GetEdgePosition(
                samples,
                sampleCountX,
                sampleCountY,
                cellX,
                cellY,
                cellZ,
                edge,
                surfaceKey,
                forcedType,
                groupMap,
                isoLevel,
                vertexPlacement) * voxelSize;
            edgeSmoothingGroups[edge] = output.CreateSmoothingGroup();
        }

        private static Vector3 GetEdgePosition(
            VoxelSample[] samples,
            int sampleCountX,
            int sampleCountY,
            int cellX,
            int cellY,
            int cellZ,
            int edge,
            int surfaceKey,
            VoxelTypeId? forcedType,
            VoxelGroupMap groupMap,
            float isoLevel,
            MarchingCubesVertexPlacement vertexPlacement)
        {
            int cornerA = EdgeCorners[edge, 0];
            int cornerB = EdgeCorners[edge, 1];
            VoxelSample sampleA = GetCornerSample(
                samples, sampleCountX, sampleCountY, cellX, cellY, cellZ, cornerA);
            VoxelSample sampleB = GetCornerSample(
                samples, sampleCountX, sampleCountY, cellX, cellY, cellZ, cornerB);

            bool solidA = sampleA.IsSolid(isoLevel);
            bool solidB = sampleB.IsSolid(isoLevel);
            bool aIsTarget = solidA
                && BelongsToSurface(
                    sampleA.Type,
                    surfaceKey,
                    forcedType,
                    groupMap);
            bool bIsTarget = solidB
                && BelongsToSurface(
                    sampleB.Type,
                    surfaceKey,
                    forcedType,
                    groupMap);
            // Only inset where two DIFFERENT surfaces meet. Types sharing a group
            // resolve to the same key, so their shared edges interpolate normally
            // and the group reads as one continuous solid.
            bool isDifferentSurfaceBoundary = solidA
                && solidB
                && SurfaceKeyOf(sampleA.Type, forcedType, groupMap)
                    != SurfaceKeyOf(sampleB.Type, forcedType, groupMap);
            // Ore is harvested as one connected rigid body, so exposing it by
            // removing adjacent terrain must not reshape it. Keep an ore edge at
            // the same inset position whether the non-ore endpoint is another
            // solid group or air. Natural stone still uses density interpolation
            // against air, preserving the smooth cave surface.
            bool isStableOreBoundary = groupMap.IsConfigured
                && surfaceKey == (int)VoxelGroup.Ore
                && aIsTarget != bIsTarget;

            float t = 0.5f;
            if ((isDifferentSurfaceBoundary || isStableOreBoundary)
                && aIsTarget != bIsTarget)
            {
                t = aIsTarget
                    ? 0.5f - TypeBoundaryInset
                    : 0.5f + TypeBoundaryInset;
            }
            else if (vertexPlacement == MarchingCubesVertexPlacement.DensityInterpolated)
            {
                float densityRange = sampleB.Density - sampleA.Density;
                if (Mathf.Abs(densityRange) > Mathf.Epsilon)
                {
                    t = Mathf.Clamp01((isoLevel - sampleA.Density) / densityRange);
                }
            }

            return Vector3.Lerp(
                (Vector3)CornerOffsets[cornerA],
                (Vector3)CornerOffsets[cornerB],
                t);
        }

        private static bool BelongsToSurface(
            VoxelTypeId type,
            int surfaceKey,
            VoxelTypeId? forcedType,
            VoxelGroupMap groupMap)
        {
            return forcedType.HasValue
                ? type == forcedType.Value
                : groupMap.GetGroupKey(type) == surfaceKey;
        }

        /// <summary>
        /// Surface identity of one sample. When a single type was requested the
        /// world is only ever "that type" versus "everything else", so all other
        /// types collapse to one foreign key.
        /// </summary>
        private static int SurfaceKeyOf(
            VoxelTypeId type,
            VoxelTypeId? forcedType,
            VoxelGroupMap groupMap)
        {
            if (!forcedType.HasValue)
            {
                return groupMap.GetGroupKey(type);
            }
            return type == forcedType.Value ? 0 : 1;
        }

        private static void TallyMemberType(VoxelTypeId type)
        {
            for (int i = 0; i < cellMemberTypes.Count; i++)
            {
                if (cellMemberTypes[i] == type)
                {
                    cellMemberCounts[i]++;
                    return;
                }
            }
            cellMemberTypes.Add(type);
            cellMemberCounts.Add(1);
        }

        /// <summary>
        /// Type owning the most member corners of the current cell. Ties resolve to
        /// the lowest type id so the choice is stable regardless of corner order.
        /// </summary>
        private static VoxelTypeId GetDominantMemberType()
        {
            VoxelTypeId best = cellMemberTypes[0];
            int bestCount = cellMemberCounts[0];
            for (int i = 1; i < cellMemberTypes.Count; i++)
            {
                int count = cellMemberCounts[i];
                if (count > bestCount
                    || (count == bestCount
                        && cellMemberTypes[i].Value < best.Value))
                {
                    best = cellMemberTypes[i];
                    bestCount = count;
                }
            }
            return best;
        }

        private static VoxelSample GetCornerSample(
            VoxelSample[] samples,
            int sampleCountX,
            int sampleCountY,
            int cellX,
            int cellY,
            int cellZ,
            int corner)
        {
            Vector3Int offset = CornerOffsets[corner];
            int sampleX = cellX + offset.x;
            int sampleY = cellY + offset.y;
            int sampleZ = cellZ + offset.z;
            return samples[
                sampleX + sampleCountX * (sampleY + sampleCountY * sampleZ)];
        }
    }
}

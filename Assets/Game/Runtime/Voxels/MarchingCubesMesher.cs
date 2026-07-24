using System;
using System.Collections.Generic;
using UnityEngine;

namespace Supernova.Voxels
{
    /// <summary>
    /// Extracts a binary isosurface with a fixed 256-case lookup table and edge midpoints.
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

        // Each type is polygonised as its own binary field. At a solid/solid type
        // boundary, the two surfaces are moved slightly toward their owning samples,
        // leaving a small seam instead of two coincident, z-fighting surfaces.
        private const float TypeBoundaryInset = 0.05f;
        private static VoxelSample[] sampleCache;
        private static readonly List<VoxelTypeId> ActiveTypes = new List<VoxelTypeId>();
        private static readonly HashSet<VoxelTypeId> ActiveTypeSet = new HashSet<VoxelTypeId>();

        public static VoxelMeshData Build(
            VoxelVolume volume,
            float isoLevel = 0f,
            float voxelSize = 1f)
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
                voxelSize);
        }

        public static VoxelMeshData BuildChunk(
            VoxelChunkRegion region,
            int chunkX,
            int chunkZ,
            float isoLevel = 0f,
            float voxelSize = 1f)
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
                voxelSize);
        }

        public static VoxelMeshData BuildChunk(
            InfiniteVoxelWorld world,
            Vector3Int chunkCoordinate,
            float isoLevel = 0f,
            float voxelSize = 1f)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            Vector3Int origin = chunkCoordinate * VoxelVolume.Size;
            // Ungenerated neighbours remain assumed solid, matching the existing
            // infinite-world streaming behavior. Their fallback type is Default.
            float outsideDensity = isoLevel + 1f;
            return BuildGrid(
                (x, y, z) => world.GetSampleOrDefault(
                    origin.x + x,
                    origin.y + y,
                    origin.z + z,
                    outsideDensity,
                    VoxelTypeId.Default),
                VoxelVolume.Size,
                VoxelVolume.Size,
                VoxelVolume.Size,
                isoLevel,
                voxelSize);
        }

        private static VoxelMeshData BuildGrid(
            SampleSampler sample,
            int cellCountX,
            int cellCountY,
            int cellCountZ,
            float isoLevel,
            float voxelSize)
        {
            if (voxelSize <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(voxelSize),
                    "Voxel size must be positive.");
            }

            var meshData = new VoxelMeshData();
            var edgeVertexIndices = new int[12];
            int sampleCountX = cellCountX + 1;
            int sampleCountY = cellCountY + 1;
            int sampleCountZ = cellCountZ + 1;
            int sampleCount = sampleCountX * sampleCountY * sampleCountZ;
            if (sampleCache == null || sampleCache.Length < sampleCount)
            {
                sampleCache = new VoxelSample[sampleCount];
            }

            ActiveTypes.Clear();
            ActiveTypeSet.Clear();
            for (int z = 0; z < sampleCountZ; z++)
            {
                for (int y = 0; y < sampleCountY; y++)
                {
                    int rowStart = sampleCountX * (y + sampleCountY * z);
                    for (int x = 0; x < sampleCountX; x++)
                    {
                        VoxelSample voxel = sample(x, y, z);
                        sampleCache[rowStart + x] = voxel;
                        if (voxel.IsSolid(isoLevel) && ActiveTypeSet.Add(voxel.Type))
                        {
                            ActiveTypes.Add(voxel.Type);
                        }
                    }
                }
            }

            ActiveTypes.Sort();
            foreach (VoxelTypeId type in ActiveTypes)
            {
                for (int z = 0; z < cellCountZ; z++)
                {
                    for (int y = 0; y < cellCountY; y++)
                    {
                        for (int x = 0; x < cellCountX; x++)
                        {
                            PolygoniseCell(
                                sampleCache,
                                sampleCountX,
                                sampleCountY,
                                x,
                                y,
                                z,
                                type,
                                isoLevel,
                                voxelSize,
                                edgeVertexIndices,
                                meshData);
                        }
                    }
                }
            }

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
            VoxelTypeId targetType,
            float isoLevel,
            float voxelSize,
            int[] edgeVertexIndices,
            VoxelMeshData output)
        {
            int cubeIndex = 0;
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
                if (voxel.IsSolid(isoLevel) && voxel.Type == targetType)
                {
                    cubeIndex |= 1 << corner;
                }
            }

            if (cubeIndex == 0 || cubeIndex == 255)
            {
                return;
            }

            for (int edge = 0; edge < edgeVertexIndices.Length; edge++)
            {
                edgeVertexIndices[edge] = -1;
            }

            Vector3 cellOrigin = new Vector3(cellX, cellY, cellZ) * voxelSize;
            for (int tableIndex = 0; tableIndex < 16; tableIndex++)
            {
                int edge = TriangleTable[cubeIndex, tableIndex];
                if (edge < 0)
                {
                    break;
                }

                int vertexIndex = edgeVertexIndices[edge];
                if (vertexIndex < 0)
                {
                    vertexIndex = output.Vertices.Count;
                    output.Vertices.Add(
                        cellOrigin + GetEdgePosition(
                            samples,
                            sampleCountX,
                            sampleCountY,
                            cellX,
                            cellY,
                            cellZ,
                            edge,
                            targetType,
                            isoLevel) * voxelSize);
                    edgeVertexIndices[edge] = vertexIndex;
                }

                output.AddTriangleIndex(targetType, vertexIndex);
            }
        }

        private static Vector3 GetEdgePosition(
            VoxelSample[] samples,
            int sampleCountX,
            int sampleCountY,
            int cellX,
            int cellY,
            int cellZ,
            int edge,
            VoxelTypeId targetType,
            float isoLevel)
        {
            int cornerA = EdgeCorners[edge, 0];
            int cornerB = EdgeCorners[edge, 1];
            VoxelSample sampleA = GetCornerSample(
                samples, sampleCountX, sampleCountY, cellX, cellY, cellZ, cornerA);
            VoxelSample sampleB = GetCornerSample(
                samples, sampleCountX, sampleCountY, cellX, cellY, cellZ, cornerB);

            bool aIsTarget = sampleA.IsSolid(isoLevel) && sampleA.Type == targetType;
            bool bIsTarget = sampleB.IsSolid(isoLevel) && sampleB.Type == targetType;
            bool isDifferentSolidTypeBoundary =
                sampleA.IsSolid(isoLevel)
                && sampleB.IsSolid(isoLevel)
                && sampleA.Type != sampleB.Type;

            float t = 0.5f;
            if (isDifferentSolidTypeBoundary && aIsTarget != bIsTarget)
            {
                t = aIsTarget
                    ? 0.5f - TypeBoundaryInset
                    : 0.5f + TypeBoundaryInset;
            }

            return Vector3.Lerp(
                (Vector3)CornerOffsets[cornerA],
                (Vector3)CornerOffsets[cornerB],
                t);
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

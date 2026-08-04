using System;
using System.Collections.Generic;
using System.Linq;
using Supernova.MinecraftCaves;
using Supernova.Voxels;
using UnityEditor;
using UnityEngine;

public static class JigsawStructureAssetBuilder
{
    [MenuItem("Tools/Supernova/World Generation/Create Default Jigsaw Structures")]
    public static void CreateDefaultStructures()
    {
        EnsureFolder(ProjectAssetPaths.Folders.JigsawStructureFeatures);
        VoxelTypeDefinition structureBrick =
            AssetDatabase.LoadAssetAtPath<VoxelTypeDefinition>(
                ProjectAssetPaths.Config.StructureBrickVoxel);
        if (structureBrick == null)
        {
            Debug.LogError(
                $"Missing voxel type at {ProjectAssetPaths.Config.StructureBrickVoxel}.");
            return;
        }
        VoxelTypeDefinition fortressBrick =
            AssetDatabase.LoadAssetAtPath<VoxelTypeDefinition>(
                ProjectAssetPaths.Config.FortressBrickVoxel);
        if (fortressBrick == null)
        {
            Debug.LogError(
                $"Missing voxel type at {ProjectAssetPaths.Config.FortressBrickVoxel}.");
            return;
        }

        JigsawStructureFeatureDefinition mineshaft = EnsureDefinition(
            ProjectAssetPaths.Config.AbandonedMineshaftJigsaw);
        mineshaft.Configure(
            true,
            "abandoned_mineshaft",
            structureBrick,
            structureBrick,
            104729,
            10,
            1f,
            48,
            168,
            40,
            7,
            120,
            "mineshaft_corridor",
            BuildMineshaftPieces());
        mineshaft.ConfigureLayoutPolicy(6, 8, 1);
        EditorUtility.SetDirty(mineshaft);

        JigsawStructureFeatureDefinition fortress = EnsureDefinition(
            ProjectAssetPaths.Config.FortressJigsaw);
        fortress.Configure(
            true,
            "fortress",
            structureBrick,
            fortressBrick,
            161803,
            12,
            0.35f,
            40,
            150,
            36,
            7,
            120,
            "fortress_hall",
            BuildFortressPieces());
        fortress.ConfigureLayoutPolicy(8, 8, 1);
        EditorUtility.SetDirty(fortress);

        MinecraftWorldGenerationConfiguration world =
            AssetDatabase.LoadAssetAtPath<MinecraftWorldGenerationConfiguration>(
                ProjectAssetPaths.Config.WorldGeneration);
        if (world != null)
        {
            List<JigsawStructureFeatureDefinition> structures =
                world.JigsawStructures
                    .Where(item => item != null
                        && item.StableId != mineshaft.StableId
                        && item.StableId != fortress.StableId)
                    .ToList();
            structures.Add(mineshaft);
            structures.Add(fortress);
            world.SetJigsawStructures(structures);
            EditorUtility.SetDirty(world);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeObject = fortress;
        Debug.Log(
            "Created and bound the abandoned mineshaft and fortress jigsaw definitions.");
    }

    private static IEnumerable<JigsawPieceDefinition> BuildMineshaftPieces()
    {
        var room = new JigsawPieceDefinition();
        room.ConfigureBox(
            "mineshaft_room",
            "Start Room",
            JigsawPieceDefinition.Shape.Room,
            JigsawPieceDefinition.BuildStyle.Excavated,
            JigsawPieceDefinition.ConnectorPattern.FourWay,
            JigsawPieceDefinition.Decoration.None,
            true,
            0,
            0,
            0,
            13,
            19,
            13,
            19,
            6,
            8);
        room.ConfigureSelectionConstraints(0, 1, false);
        AddOutputs(room, "mine", 3, 1f, true);

        var corridor = new JigsawPieceDefinition();
        corridor.ConfigurePassage(
            "mineshaft_corridor",
            "Corridor",
            JigsawPieceDefinition.Shape.Corridor,
            JigsawPieceDefinition.BuildStyle.Excavated,
            JigsawPieceDefinition.ConnectorPattern.ForwardAndSides,
            JigsawPieceDefinition.Decoration.SupportFrames,
            70,
            1,
            7,
            10,
            24,
            5,
            5,
            4,
            0.32f,
            0.65f,
            4);
        corridor.ConfigureSelectionConstraints(6, 0, true, 3);
        AddInput(corridor, "mine", 3);
        AddOutput(corridor, "forward", JigsawConnectorDefinition.Face.Forward,
            "mine", 3, 1f);
        AddOutput(corridor, "left_branch", JigsawConnectorDefinition.Face.Left,
            "mine", 3, 0.32f);
        AddOutput(corridor, "right_branch", JigsawConnectorDefinition.Face.Right,
            "mine", 3, 0.32f);

        var crossing = new JigsawPieceDefinition();
        crossing.ConfigureBox(
            "mineshaft_crossing",
            "Crossing",
            JigsawPieceDefinition.Shape.Crossing,
            JigsawPieceDefinition.BuildStyle.Excavated,
            JigsawPieceDefinition.ConnectorPattern.ThreeWay,
            JigsawPieceDefinition.Decoration.SupportFrames,
            false,
            20,
            2,
            7,
            7,
            7,
            7,
            7,
            5,
            5);
        crossing.ConfigureSelectionConstraints(2, 8, false, 5);
        AddInput(crossing, "mine", 3);
        AddOutput(crossing, "forward", JigsawConnectorDefinition.Face.Forward,
            "mine", 3, 1f);
        AddOutput(crossing, "left", JigsawConnectorDefinition.Face.Left,
            "mine", 3, 1f);
        AddOutput(crossing, "right", JigsawConnectorDefinition.Face.Right,
            "mine", 3, 1f);

        var stairs = new JigsawPieceDefinition();
        stairs.ConfigurePassage(
            "mineshaft_stairs",
            "Stairs",
            JigsawPieceDefinition.Shape.Stairs,
            JigsawPieceDefinition.BuildStyle.Excavated,
            JigsawPieceDefinition.ConnectorPattern.Forward,
            JigsawPieceDefinition.Decoration.SupportFrames,
            10,
            2,
            7,
            12,
            12,
            5,
            5,
            4,
            0f,
            0.65f,
            4);
        stairs.ConfigureSelectionConstraints(1, 6, false, 6);
        AddInput(stairs, "mine", 3);
        AddOutput(stairs, "forward", JigsawConnectorDefinition.Face.Forward,
            "mine", 3, 1f);

        var storage = new JigsawPieceDefinition();
        storage.ConfigureBox(
            "mineshaft_storage",
            "Timber Storage",
            JigsawPieceDefinition.Shape.Room,
            JigsawPieceDefinition.BuildStyle.Excavated,
            JigsawPieceDefinition.ConnectorPattern.None,
            JigsawPieceDefinition.Decoration.SupportFrames,
            false,
            12,
            3,
            7,
            9,
            13,
            11,
            15,
            6,
            8);
        storage.ConfigureSelectionConstraints(0, 4, false);
        AddInput(storage, "mine", 3);
        AddProcessor(
            storage,
            "storage_footings",
            JigsawProcessorDefinition.Kind.SupportToGround,
            16);

        var deadEnd = new JigsawPieceDefinition();
        deadEnd.ConfigurePassage(
            "mineshaft_dead_end",
            "Collapsed End",
            JigsawPieceDefinition.Shape.Corridor,
            JigsawPieceDefinition.BuildStyle.Excavated,
            JigsawPieceDefinition.ConnectorPattern.None,
            JigsawPieceDefinition.Decoration.SupportFrames,
            1,
            1,
            16,
            3,
            7,
            5,
            5,
            1,
            0f,
            0.5f,
            3,
            "terminators",
            "terminators");
        AddInput(deadEnd, "mine", 3, "terminators");
        return new[] { room, corridor, crossing, stairs, storage, deadEnd };
    }

    private static IEnumerable<JigsawPieceDefinition> BuildFortressPieces()
    {
        var lobby = new JigsawPieceDefinition();
        lobby.ConfigureBox(
            "fortress_lobby",
            "Lobby",
            JigsawPieceDefinition.Shape.Room,
            JigsawPieceDefinition.BuildStyle.Masonry,
            JigsawPieceDefinition.ConnectorPattern.FourWay,
            JigsawPieceDefinition.Decoration.Pillars,
            true,
            0,
            0,
            0,
            15,
            15,
            15,
            15,
            8,
            8);
        lobby.ConfigureSelectionConstraints(0, 1, false);
        AddOutputs(lobby, "fortress", 5, 1f, true);
        AddProcessor(
            lobby,
            "lobby_footings",
            JigsawProcessorDefinition.Kind.SupportToGround,
            20);
        AddProcessor(
            lobby,
            "lobby_weathering",
            JigsawProcessorDefinition.Kind.Weathering,
            1,
            0.22f,
            JigsawProcessorDefinition.Palette.Accent);

        var hall = new JigsawPieceDefinition();
        hall.ConfigurePassage(
            "fortress_hall",
            "Hall",
            JigsawPieceDefinition.Shape.Corridor,
            JigsawPieceDefinition.BuildStyle.Masonry,
            JigsawPieceDefinition.ConnectorPattern.ForwardAndSides,
            JigsawPieceDefinition.Decoration.None,
            60,
            1,
            7,
            10,
            18,
            5,
            6,
            4,
            0.3f,
            0.65f,
            4);
        hall.ConfigureSelectionConstraints(4, 0, true, 3);
        AddInput(hall, "fortress", 5);
        AddOutput(hall, "forward", JigsawConnectorDefinition.Face.Forward,
            "fortress", 5, 1f);
        AddOutput(hall, "left_branch", JigsawConnectorDefinition.Face.Left,
            "fortress", 5, 0.34f);
        AddOutput(hall, "right_branch", JigsawConnectorDefinition.Face.Right,
            "fortress", 5, 0.34f);
        AddProcessor(
            hall,
            "hall_pillars",
            JigsawProcessorDefinition.Kind.SupportToGround,
            24);
        AddProcessor(
            hall,
            "hall_weathering",
            JigsawProcessorDefinition.Kind.Weathering,
            1,
            0.18f,
            JigsawProcessorDefinition.Palette.Accent);

        var library = new JigsawPieceDefinition();
        library.ConfigureBox(
            "fortress_library",
            "Library",
            JigsawPieceDefinition.Shape.Room,
            JigsawPieceDefinition.BuildStyle.Masonry,
            JigsawPieceDefinition.ConnectorPattern.None,
            JigsawPieceDefinition.Decoration.LibraryShelves,
            false,
            20,
            2,
            7,
            13,
            17,
            13,
            17,
            8,
            9);
        library.ConfigureSelectionConstraints(1, 2, false, 5);
        AddInput(library, "fortress", 5);
        AddProcessor(
            library,
            "library_foundation",
            JigsawProcessorDefinition.Kind.FoundationFill,
            3,
            1f,
            JigsawProcessorDefinition.Palette.Primary,
            0,
            false);
        AddProcessor(
            library,
            "library_weathering",
            JigsawProcessorDefinition.Kind.Weathering,
            1,
            0.25f,
            JigsawProcessorDefinition.Palette.Accent);

        var crossing = new JigsawPieceDefinition();
        crossing.ConfigureBox(
            "fortress_crossing",
            "Cross Hall",
            JigsawPieceDefinition.Shape.Crossing,
            JigsawPieceDefinition.BuildStyle.Masonry,
            JigsawPieceDefinition.ConnectorPattern.ThreeWay,
            JigsawPieceDefinition.Decoration.Pillars,
            false,
            15,
            2,
            7,
            9,
            9,
            9,
            9,
            7,
            7);
        crossing.ConfigureSelectionConstraints(1, 6, false, 4);
        AddInput(crossing, "fortress", 5);
        AddOutput(crossing, "forward", JigsawConnectorDefinition.Face.Forward,
            "fortress", 5, 1f);
        AddOutput(crossing, "left", JigsawConnectorDefinition.Face.Left,
            "fortress", 5, 1f);
        AddOutput(crossing, "right", JigsawConnectorDefinition.Face.Right,
            "fortress", 5, 1f);

        var stairs = new JigsawPieceDefinition();
        stairs.ConfigurePassage(
            "fortress_stairs",
            "Stair Hall",
            JigsawPieceDefinition.Shape.Stairs,
            JigsawPieceDefinition.BuildStyle.Masonry,
            JigsawPieceDefinition.ConnectorPattern.Forward,
            JigsawPieceDefinition.Decoration.None,
            5,
            2,
            7,
            10,
            10,
            5,
            6,
            4,
            0f,
            0.7f,
            4);
        stairs.ConfigureSelectionConstraints(1, 5, false, 5);
        AddInput(stairs, "fortress", 5);
        AddOutput(stairs, "forward", JigsawConnectorDefinition.Face.Forward,
            "fortress", 5, 1f);

        var portalRoom = new JigsawPieceDefinition();
        portalRoom.ConfigureBox(
            "fortress_portal_room",
            "Portal Chamber",
            JigsawPieceDefinition.Shape.Room,
            JigsawPieceDefinition.BuildStyle.Masonry,
            JigsawPieceDefinition.ConnectorPattern.None,
            JigsawPieceDefinition.Decoration.PortalFrame,
            false,
            8,
            4,
            7,
            15,
            19,
            17,
            21,
            9,
            11);
        portalRoom.ConfigureSelectionConstraints(1, 1, false, 6);
        AddInput(portalRoom, "fortress", 5);
        AddProcessor(
            portalRoom,
            "portal_footings",
            JigsawProcessorDefinition.Kind.SupportToGround,
            24);
        AddProcessor(
            portalRoom,
            "portal_headroom",
            JigsawProcessorDefinition.Kind.ClearAbove,
            2);
        AddProcessor(
            portalRoom,
            "portal_weathering",
            JigsawProcessorDefinition.Kind.Weathering,
            1,
            0.3f,
            JigsawProcessorDefinition.Palette.Accent);

        var prison = new JigsawPieceDefinition();
        prison.ConfigureBox(
            "fortress_prison",
            "Prison Block",
            JigsawPieceDefinition.Shape.Room,
            JigsawPieceDefinition.BuildStyle.Masonry,
            JigsawPieceDefinition.ConnectorPattern.None,
            JigsawPieceDefinition.Decoration.PrisonCells,
            false,
            12,
            3,
            7,
            13,
            17,
            15,
            19,
            8,
            10);
        prison.ConfigureSelectionConstraints(0, 2, false);
        AddInput(prison, "fortress", 5);

        var deadEnd = new JigsawPieceDefinition();
        deadEnd.ConfigurePassage(
            "fortress_dead_end",
            "Sealed End",
            JigsawPieceDefinition.Shape.Corridor,
            JigsawPieceDefinition.BuildStyle.Masonry,
            JigsawPieceDefinition.ConnectorPattern.None,
            JigsawPieceDefinition.Decoration.None,
            1,
            1,
            16,
            4,
            6,
            5,
            6,
            1,
            0f,
            0.5f,
            4,
            "terminators",
            "terminators");
        AddInput(deadEnd, "fortress", 5, "terminators");
        return new[]
        {
            lobby,
            hall,
            library,
            crossing,
            stairs,
            prison,
            portalRoom,
            deadEnd,
        };
    }

    private static void AddOutputs(
        JigsawPieceDefinition piece,
        string family,
        int openingWidth,
        float chance,
        bool allFaces)
    {
        AddOutput(piece, "north", JigsawConnectorDefinition.Face.Forward,
            family, openingWidth, chance);
        if (!allFaces)
        {
            return;
        }
        AddOutput(piece, "east", JigsawConnectorDefinition.Face.Right,
            family, openingWidth, chance);
        AddOutput(piece, "south", JigsawConnectorDefinition.Face.Back,
            family, openingWidth, chance);
        AddOutput(piece, "west", JigsawConnectorDefinition.Face.Left,
            family, openingWidth, chance);
    }

    private static void AddInput(
        JigsawPieceDefinition piece,
        string family,
        int openingWidth,
        string pool = "main")
    {
        var connector = new JigsawConnectorDefinition();
        connector.Configure(
            "entrance",
            JigsawConnectorDefinition.Role.Input,
            JigsawConnectorDefinition.Face.Back,
            family + "_entry",
            family + "_branch",
            pool,
            -1,
            0,
            1,
            openingWidth,
            Math.Min(5, openingWidth));
        piece.AddConnector(connector);
    }

    private static void AddOutput(
        JigsawPieceDefinition piece,
        string id,
        JigsawConnectorDefinition.Face face,
        string family,
        int openingWidth,
        float chance)
    {
        var connector = new JigsawConnectorDefinition();
        connector.Configure(
            id,
            JigsawConnectorDefinition.Role.Output,
            face,
            family + "_branch",
            family + "_entry",
            "main",
            -1,
            0,
            1,
            openingWidth,
            Math.Min(5, openingWidth),
            chance,
            "terminators");
        piece.AddConnector(connector);
    }

    private static void AddProcessor(
        JigsawPieceDefinition piece,
        string id,
        JigsawProcessorDefinition.Kind kind,
        int distance,
        float chance = 1f,
        JigsawProcessorDefinition.Palette palette =
            JigsawProcessorDefinition.Palette.Primary,
        int inset = 0,
        bool perimeterOnly = true)
    {
        var processor = new JigsawProcessorDefinition();
        processor.Configure(
            id,
            kind,
            distance,
            chance,
            palette,
            inset,
            perimeterOnly);
        piece.AddProcessor(processor);
    }

    private static JigsawStructureFeatureDefinition EnsureDefinition(
        string assetPath)
    {
        JigsawStructureFeatureDefinition definition =
            AssetDatabase.LoadAssetAtPath<JigsawStructureFeatureDefinition>(
                assetPath);
        if (definition != null)
        {
            return definition;
        }
        definition = ScriptableObject.CreateInstance<
            JigsawStructureFeatureDefinition>();
        AssetDatabase.CreateAsset(definition, assetPath);
        return definition;
    }

    private static void EnsureFolder(string folderPath)
    {
        string[] segments = folderPath.Split('/');
        string current = segments[0];
        for (int i = 1; i < segments.Length; i++)
        {
            string next = current + "/" + segments[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, segments[i]);
            }
            current = next;
        }
    }
}

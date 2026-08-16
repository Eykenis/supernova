using System.Collections.Generic;
using Supernova.MinecraftCaves;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(JigsawStructureFeatureDefinition))]
public sealed class JigsawStructureFeatureDefinitionEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();
        serializedObject.ApplyModifiedProperties();

        var definition = (JigsawStructureFeatureDefinition)target;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Piece Module Editor", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Each module joins a named pool. The start module creates sockets; "
            + "weighted modules from the matching pool are then selected by depth.",
            MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Add Room"))
            {
                AddBox(definition, JigsawPieceDefinition.Shape.Room);
            }
            if (GUILayout.Button("Add Corridor"))
            {
                AddPassage(definition, JigsawPieceDefinition.Shape.Corridor);
            }
            if (GUILayout.Button("Add Crossing"))
            {
                AddBox(definition, JigsawPieceDefinition.Shape.Crossing);
            }
            if (GUILayout.Button("Add Stairs"))
            {
                AddPassage(definition, JigsawPieceDefinition.Shape.Stairs);
            }
        }
        if (GUILayout.Button("Add Vertical Shaft"))
        {
            AddVerticalShaft(definition);
        }

        if (!definition.Enabled)
        {
            EditorGUILayout.HelpBox("This jigsaw structure is disabled.", MessageType.Info);
        }
        else if (definition.TryCreateSettings(
            out JigsawStructureFeatureSettings settings,
            out string error))
        {
            int explicitSocketCount = 0;
            int requiredPieceCount = 0;
            for (int i = 0; i < settings.Pieces.Count; i++)
            {
                explicitSocketCount += settings.Pieces[i].Connectors.Count;
                requiredPieceCount += settings.Pieces[i].MinimumCount;
            }
            EditorGUILayout.HelpBox(
                $"Valid structure: {definition.Pieces.Count} modules, "
                + $"{explicitSocketCount} explicit sockets, "
                + $"{requiredPieceCount} required placements.",
                MessageType.Info);
            IReadOnlyList<JigsawStructureValidator.Issue> issues =
                JigsawStructureValidator.Validate(settings);
            for (int i = 0; i < issues.Count; i++)
            {
                EditorGUILayout.HelpBox(
                    issues[i].Message,
                    issues[i].Severity == JigsawStructureValidator.Severity.Error
                        ? MessageType.Error
                        : MessageType.Warning);
            }
        }
        else
        {
            EditorGUILayout.HelpBox(error, MessageType.Error);
        }
    }

    private static void AddBox(
        JigsawStructureFeatureDefinition definition,
        JigsawPieceDefinition.Shape shape)
    {
        var piece = new JigsawPieceDefinition();
        piece.ConfigureBox(
            "new_" + shape.ToString().ToLowerInvariant(),
            "New " + shape,
            shape,
            JigsawPieceDefinition.BuildStyle.Masonry,
            JigsawPieceDefinition.ConnectorPattern.Forward,
            JigsawPieceDefinition.Decoration.None,
            false,
            10,
            1,
            12,
            7,
            7,
            7,
            7,
            5,
            5);
        Add(definition, piece);
    }

    private static void AddPassage(
        JigsawStructureFeatureDefinition definition,
        JigsawPieceDefinition.Shape shape)
    {
        var piece = new JigsawPieceDefinition();
        piece.ConfigurePassage(
            "new_" + shape.ToString().ToLowerInvariant(),
            "New " + shape,
            shape,
            JigsawPieceDefinition.BuildStyle.Masonry,
            JigsawPieceDefinition.ConnectorPattern.Forward,
            JigsawPieceDefinition.Decoration.None,
            10,
            1,
            12,
            8,
            12,
            5,
            5);
        Add(definition, piece);
    }

    private static void AddVerticalShaft(
        JigsawStructureFeatureDefinition definition)
    {
        var piece = new JigsawPieceDefinition();
        piece.ConfigureBox(
            "new_vertical_shaft",
            "New Vertical Shaft",
            JigsawPieceDefinition.Shape.VerticalShaft,
            JigsawPieceDefinition.BuildStyle.Masonry,
            JigsawPieceDefinition.ConnectorPattern.None,
            JigsawPieceDefinition.Decoration.SpiralStairs,
            false,
            10,
            1,
            12,
            21,
            21,
            21,
            21,
            12,
            14);
        Add(definition, piece);
    }

    private static void Add(
        JigsawStructureFeatureDefinition definition,
        JigsawPieceDefinition piece)
    {
        Undo.RecordObject(definition, "Add Jigsaw Piece Module");
        definition.AddPiece(piece);
        EditorUtility.SetDirty(definition);
    }
}

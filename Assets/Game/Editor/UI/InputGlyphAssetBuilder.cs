#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;

public static class InputGlyphAssetBuilder
{
    private const int AtlasWidth = 1024;
    private const int AtlasHeight = 1024;
    private const int GlyphHeight = 58;
    private const int PackingGap = 5;
    private const float GlyphBaselineRatio = 0.8f;

    private static readonly Color32 Transparent =
        new Color32(0, 0, 0, 0);
    private static readonly Color32 Border =
        new Color32(151, 197, 218, 255);
    private static readonly Color32 MouseInterior =
        new Color32(24, 34, 45, 255);
    private static readonly Color32 Ink =
        new Color32(237, 247, 250, 255);
    private static readonly Color32 Accent =
        new Color32(92, 221, 237, 255);

    private static readonly Dictionary<char, string[]> GlyphPatterns =
        new Dictionary<char, string[]>
        {
            { 'A', Rows("01110", "10001", "10001", "11111", "10001", "10001", "10001") },
            { 'B', Rows("11110", "10001", "10001", "11110", "10001", "10001", "11110") },
            { 'C', Rows("01111", "10000", "10000", "10000", "10000", "10000", "01111") },
            { 'D', Rows("11110", "10001", "10001", "10001", "10001", "10001", "11110") },
            { 'E', Rows("11111", "10000", "10000", "11110", "10000", "10000", "11111") },
            { 'F', Rows("11111", "10000", "10000", "11110", "10000", "10000", "10000") },
            { 'G', Rows("01111", "10000", "10000", "10111", "10001", "10001", "01111") },
            { 'H', Rows("10001", "10001", "10001", "11111", "10001", "10001", "10001") },
            { 'I', Rows("11111", "00100", "00100", "00100", "00100", "00100", "11111") },
            { 'J', Rows("00111", "00010", "00010", "00010", "10010", "10010", "01100") },
            { 'K', Rows("10001", "10010", "10100", "11000", "10100", "10010", "10001") },
            { 'L', Rows("10000", "10000", "10000", "10000", "10000", "10000", "11111") },
            { 'M', Rows("10001", "11011", "10101", "10101", "10001", "10001", "10001") },
            { 'N', Rows("10001", "11001", "10101", "10011", "10001", "10001", "10001") },
            { 'O', Rows("01110", "10001", "10001", "10001", "10001", "10001", "01110") },
            { 'P', Rows("11110", "10001", "10001", "11110", "10000", "10000", "10000") },
            { 'Q', Rows("01110", "10001", "10001", "10001", "10101", "10010", "01101") },
            { 'R', Rows("11110", "10001", "10001", "11110", "10100", "10010", "10001") },
            { 'S', Rows("01111", "10000", "10000", "01110", "00001", "00001", "11110") },
            { 'T', Rows("11111", "00100", "00100", "00100", "00100", "00100", "00100") },
            { 'U', Rows("10001", "10001", "10001", "10001", "10001", "10001", "01110") },
            { 'V', Rows("10001", "10001", "10001", "10001", "10001", "01010", "00100") },
            { 'W', Rows("10001", "10001", "10001", "10101", "10101", "10101", "01010") },
            { 'X', Rows("10001", "10001", "01010", "00100", "01010", "10001", "10001") },
            { 'Y', Rows("10001", "10001", "01010", "00100", "00100", "00100", "00100") },
            { 'Z', Rows("11111", "00001", "00010", "00100", "01000", "10000", "11111") },
            { '0', Rows("01110", "10001", "10011", "10101", "11001", "10001", "01110") },
            { '1', Rows("00100", "01100", "00100", "00100", "00100", "00100", "01110") },
            { '2', Rows("01110", "10001", "00001", "00010", "00100", "01000", "11111") },
            { '3', Rows("11110", "00001", "00001", "01110", "00001", "00001", "11110") },
            { '4', Rows("00010", "00110", "01010", "10010", "11111", "00010", "00010") },
            { '5', Rows("11111", "10000", "10000", "11110", "00001", "00001", "11110") },
            { '6', Rows("01110", "10000", "10000", "11110", "10001", "10001", "01110") },
            { '7', Rows("11111", "00001", "00010", "00100", "01000", "01000", "01000") },
            { '8', Rows("01110", "10001", "10001", "01110", "10001", "10001", "01110") },
            { '9', Rows("01110", "10001", "10001", "01111", "00001", "00001", "01110") },
            { '-', Rows("00000", "00000", "00000", "11111", "00000", "00000", "00000") },
            { '=', Rows("00000", "00000", "11111", "00000", "11111", "00000", "00000") },
            { '+', Rows("00000", "00100", "00100", "11111", "00100", "00100", "00000") },
            { '*', Rows("00000", "10101", "01110", "11111", "01110", "10101", "00000") },
            { '/', Rows("00001", "00010", "00010", "00100", "01000", "01000", "10000") },
            { '\\', Rows("10000", "01000", "01000", "00100", "00010", "00010", "00001") },
            { ',', Rows("00000", "00000", "00000", "00000", "00110", "00100", "01000") },
            { '.', Rows("00000", "00000", "00000", "00000", "00000", "00110", "00110") },
            { ';', Rows("00110", "00110", "00000", "00000", "00110", "00100", "01000") },
            { '\'', Rows("00110", "00110", "00100", "00000", "00000", "00000", "00000") },
            { '`', Rows("01100", "00110", "00010", "00000", "00000", "00000", "00000") },
            { '[', Rows("01110", "01000", "01000", "01000", "01000", "01000", "01110") },
            { ']', Rows("01110", "00010", "00010", "00010", "00010", "00010", "01110") },
        };

    [MenuItem("Tools/Supernova/UI/Rebuild Input Glyph Atlas")]
    public static void RebuildInputGlyphAssets()
    {
        TMP_SpriteAsset spriteAsset = BuildInputGlyphAssets();
        GameAssetCatalogBuilder.EnsureCatalog();
        Selection.activeObject = spriteAsset;
        EditorGUIUtility.PingObject(spriteAsset);
        Debug.Log(
            "Rebuilt the keyboard and mouse input glyph atlas.",
            spriteAsset);
    }

    public static TMP_SpriteAsset EnsureInputGlyphAssets()
    {
        TMP_SpriteAsset spriteAsset =
            AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(
                ProjectAssetPaths.Config.InputGlyphSpriteAsset);
        Texture2D atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(
            ProjectAssetPaths.Ui.InputGlyphAtlas);
        if (spriteAsset != null
            && atlas != null
            && spriteAsset.spriteCharacterTable != null
            && spriteAsset.spriteCharacterTable.Count > 0)
        {
            return spriteAsset;
        }
        return BuildInputGlyphAssets();
    }

    private static TMP_SpriteAsset BuildInputGlyphAssets()
    {
        List<GlyphDefinition> definitions = BuildDefinitions();
        Font labelFont = AssetDatabase.LoadAssetAtPath<Font>(
            ProjectAssetPaths.Ui.RuntimeFont);
        if (labelFont == null)
        {
            throw new InvalidOperationException(
                "Could not load the configured runtime UI font.");
        }
        var pixels = new Color32[AtlasWidth * AtlasHeight];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = Transparent;

        var metadata = new List<SpriteMetaData>(definitions.Count);
        int x = PackingGap;
        int y = PackingGap;
        for (int i = 0; i < definitions.Count; i++)
        {
            GlyphDefinition definition = definitions[i];
            if (x + definition.Width + PackingGap > AtlasWidth)
            {
                x = PackingGap;
                y += GlyphHeight + PackingGap;
            }
            if (y + GlyphHeight + PackingGap > AtlasHeight)
            {
                throw new InvalidOperationException(
                    "The input glyph atlas is too small for its definitions.");
            }

            var rect = new RectInt(x, y, definition.Width, GlyphHeight);
            DrawDefinition(pixels, rect, definition, labelFont);
            metadata.Add(new SpriteMetaData
            {
                name = definition.Name,
                rect = new Rect(rect.x, rect.y, rect.width, rect.height),
                alignment = (int)SpriteAlignment.Center,
                pivot = new Vector2(0.5f, 0.5f),
                border = Vector4.zero,
            });
            x += definition.Width + PackingGap;
        }

        bool atlasChanged = WriteAtlas(pixels);
        ImportAtlas(metadata.ToArray(), atlasChanged);
        return CreateOrUpdateSpriteAsset();
    }

    private static List<GlyphDefinition> BuildDefinitions()
    {
        var definitions = new List<GlyphDefinition>();
        for (char letter = 'A'; letter <= 'Z'; letter++)
            definitions.Add(Key("Key_" + letter, letter.ToString(), 52));
        for (char digit = '0'; digit <= '9'; digit++)
            definitions.Add(Key("Key_" + digit, digit.ToString(), 52));
        for (int i = 1; i <= 24; i++)
            definitions.Add(Key("Key_F" + i, "F" + i, 62));

        definitions.Add(Key("Key_CTRL", "CTRL", 88));
        definitions.Add(Key("Key_SHIFT", "SHIFT", 98));
        definitions.Add(Key("Key_ALT", "ALT", 72));
        definitions.Add(Key("Key_META", "META", 88));
        definitions.Add(Key("Key_SPACE", "SPACE", 116));
        definitions.Add(Key("Key_ENTER", "ENTER", 102));
        definitions.Add(Key("Key_ESC", "ESC", 72));
        definitions.Add(Key("Key_TAB", "TAB", 72));
        definitions.Add(Key("Key_CAPS", "CAPS", 88));
        definitions.Add(Key("Key_NUM", "NUM", 72));
        definitions.Add(Key("Key_PRT", "PRT", 72));
        definitions.Add(Key("Key_SCRL", "SCRL", 88));
        definitions.Add(Key("Key_PAUSE", "PAUSE", 98));
        definitions.Add(Key("Key_MENU", "MENU", 88));
        definitions.Add(Key("Key_ANY", "ANY", 72));
        definitions.Add(Key("Key_IME", "IME", 72));
        definitions.Add(Key("Key_BACK", "BACK", 92));
        definitions.Add(Key("Key_DEL", "DEL", 72));
        definitions.Add(Key("Key_INS", "INS", 72));
        definitions.Add(Key("Key_HOME", "HOME", 88));
        definitions.Add(Key("Key_END", "END", 72));
        definitions.Add(Key("Key_PGUP", "PGUP", 88));
        definitions.Add(Key("Key_PGDN", "PGDN", 88));

        definitions.Add(new GlyphDefinition("Key_UP", string.Empty, 52, GlyphKind.ArrowUp));
        definitions.Add(new GlyphDefinition("Key_DOWN", string.Empty, 52, GlyphKind.ArrowDown));
        definitions.Add(new GlyphDefinition("Key_LEFT", string.Empty, 52, GlyphKind.ArrowLeft));
        definitions.Add(new GlyphDefinition("Key_RIGHT", string.Empty, 52, GlyphKind.ArrowRight));

        definitions.Add(Key("Key_MINUS", "-", 52));
        definitions.Add(Key("Key_EQUALS", "=", 52));
        definitions.Add(Key("Key_COMMA", ",", 52));
        definitions.Add(Key("Key_PERIOD", ".", 52));
        definitions.Add(Key("Key_SLASH", "/", 52));
        definitions.Add(Key("Key_BACKSLASH", "\\", 52));
        definitions.Add(Key("Key_SEMICOLON", ";", 52));
        definitions.Add(Key("Key_QUOTE", "'", 52));
        definitions.Add(Key("Key_BACKQUOTE", "`", 52));
        definitions.Add(Key("Key_LBRACKET", "[", 52));
        definitions.Add(Key("Key_RBRACKET", "]", 52));

        for (int i = 0; i <= 9; i++)
            definitions.Add(Key("Key_NP" + i, "N" + i, 62));
        definitions.Add(Key("Key_NPPLUS", "N+", 62));
        definitions.Add(Key("Key_NPMINUS", "N-", 62));
        definitions.Add(Key("Key_NPMULTIPLY", "N*", 62));
        definitions.Add(Key("Key_NPDIVIDE", "N/", 62));
        definitions.Add(Key("Key_NPENTER", "NENT", 88));
        definitions.Add(Key("Key_NPPERIOD", "N.", 62));
        definitions.Add(Key("Key_NPEQUALS", "N=", 62));

        definitions.Add(new GlyphDefinition("MouseLeft", string.Empty, 58, GlyphKind.MouseLeft));
        definitions.Add(new GlyphDefinition("MouseRight", string.Empty, 58, GlyphKind.MouseRight));
        definitions.Add(new GlyphDefinition("MouseMiddle", string.Empty, 58, GlyphKind.MouseMiddle));
        definitions.Add(new GlyphDefinition("MouseWheel", string.Empty, 58, GlyphKind.MouseWheel));
        definitions.Add(new GlyphDefinition("MouseMove", string.Empty, 58, GlyphKind.MouseMove));
        definitions.Add(new GlyphDefinition("MousePointer", string.Empty, 58, GlyphKind.MousePointer));
        definitions.Add(new GlyphDefinition("MouseBack", string.Empty, 58, GlyphKind.MouseBack));
        definitions.Add(new GlyphDefinition("MouseForward", string.Empty, 58, GlyphKind.MouseForward));
        return definitions;
    }

    private static GlyphDefinition Key(string name, string label, int width)
    {
        return new GlyphDefinition(name, label, width, GlyphKind.Key);
    }

    private static bool WriteAtlas(Color32[] pixels)
    {
        var texture = new Texture2D(
            AtlasWidth,
            AtlasHeight,
            TextureFormat.RGBA32,
            false,
            false)
        {
            name = "InputGlyphAtlas",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        texture.SetPixels32(pixels);
        texture.Apply(false, false);

        string absolutePath = ProjectAssetPaths.ToAbsoluteFileSystemPath(
            ProjectAssetPaths.Ui.InputGlyphAtlas);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
        byte[] encoded = texture.EncodeToPNG();
        UnityEngine.Object.DestroyImmediate(texture);
        if (File.Exists(absolutePath)
            && File.ReadAllBytes(absolutePath).SequenceEqual(encoded))
        {
            return false;
        }

        File.WriteAllBytes(absolutePath, encoded);
        return true;
    }

    private static void ImportAtlas(
        SpriteMetaData[] metadata,
        bool atlasChanged)
    {
        TextureImporter importer = AssetImporter.GetAtPath(
            ProjectAssetPaths.Ui.InputGlyphAtlas) as TextureImporter;
        if (importer == null && atlasChanged)
        {
            AssetDatabase.ImportAsset(
                ProjectAssetPaths.Ui.InputGlyphAtlas,
                ImportAssetOptions.ForceSynchronousImport);
            importer = AssetImporter.GetAtPath(
                ProjectAssetPaths.Ui.InputGlyphAtlas) as TextureImporter;
        }
        if (importer == null)
        {
            throw new InvalidOperationException(
                "Could not get the input glyph atlas importer.");
        }

        bool importerChanged = atlasChanged
            || importer.textureType != TextureImporterType.Sprite
            || importer.spriteImportMode != SpriteImportMode.Multiple
            || !importer.alphaIsTransparency
            || importer.mipmapEnabled
            || importer.filterMode != FilterMode.Bilinear
            || importer.wrapMode != TextureWrapMode.Clamp
            || importer.textureCompression
                != TextureImporterCompression.Uncompressed
            || !Mathf.Approximately(importer.spritePixelsPerUnit, 64f)
            || !HasSameSpriteMetadata(importer.spritesheet, metadata);

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.spritePixelsPerUnit = 64f;
#pragma warning disable 618
        importer.spritesheet = metadata;
#pragma warning restore 618
        if (importerChanged)
            importer.SaveAndReimport();
    }

    private static bool HasSameSpriteMetadata(
        SpriteMetaData[] current,
        SpriteMetaData[] expected)
    {
        if (current == null || expected == null
            || current.Length != expected.Length)
        {
            return false;
        }

        for (int i = 0; i < current.Length; i++)
        {
            if (current[i].name != expected[i].name
                || current[i].rect != expected[i].rect
                || current[i].alignment != expected[i].alignment
                || current[i].pivot != expected[i].pivot
                || current[i].border != expected[i].border)
            {
                return false;
            }
        }
        return true;
    }

    private static TMP_SpriteAsset CreateOrUpdateSpriteAsset()
    {
        Texture2D atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(
            ProjectAssetPaths.Ui.InputGlyphAtlas);
        Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(
                ProjectAssetPaths.Ui.InputGlyphAtlas)
            .OfType<Sprite>()
            .OrderByDescending(sprite => sprite.rect.y)
            .ThenBy(sprite => sprite.rect.x)
            .ToArray();
        if (atlas == null || sprites.Length == 0)
        {
            throw new InvalidOperationException(
                "The input glyph atlas did not import as a multi-sprite texture.");
        }

        TMP_SpriteAsset spriteAsset =
            AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(
                ProjectAssetPaths.Config.InputGlyphSpriteAsset);
        if (spriteAsset == null)
        {
            spriteAsset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
            AssetDatabase.CreateAsset(
                spriteAsset,
                ProjectAssetPaths.Config.InputGlyphSpriteAsset);
        }

        spriteAsset.hashCode =
            TMP_TextUtilities.GetSimpleHashCode(spriteAsset.name);
        spriteAsset.spriteSheet = atlas;
        var glyphs = new List<TMP_SpriteGlyph>(sprites.Length);
        var characters = new List<TMP_SpriteCharacter>(sprites.Length);
        for (int i = 0; i < sprites.Length; i++)
        {
            Sprite sprite = sprites[i];
            var glyph = new TMP_SpriteGlyph
            {
                index = (uint)i,
                metrics = new GlyphMetrics(
                    sprite.rect.width,
                    sprite.rect.height,
                    0f,
                    sprite.rect.height * GlyphBaselineRatio,
                    sprite.rect.width),
                glyphRect = new GlyphRect(sprite.rect),
                scale = 1f,
                sprite = sprite,
            };
            glyphs.Add(glyph);
            characters.Add(new TMP_SpriteCharacter(0xFFFE, glyph)
            {
                name = sprite.name,
                scale = 1f,
            });
        }
        spriteAsset.spriteGlyphTable.Clear();
        spriteAsset.spriteGlyphTable.AddRange(glyphs);
        spriteAsset.spriteCharacterTable.Clear();
        spriteAsset.spriteCharacterTable.AddRange(characters);
        spriteAsset.UpdateLookupTables();

        if (spriteAsset.material == null)
        {
            Shader shader = Shader.Find("TextMeshPro/Sprite");
            if (shader == null)
                throw new InvalidOperationException(
                    "The TextMeshPro/Sprite shader is missing.");
            var material = new Material(shader)
            {
                name = "InputGlyphs Material",
                hideFlags = HideFlags.HideInHierarchy,
            };
            spriteAsset.material = material;
            AssetDatabase.AddObjectToAsset(material, spriteAsset);
        }
        spriteAsset.material.SetTexture(
            ShaderUtilities.ID_MainTex,
            spriteAsset.spriteSheet);
        EditorUtility.SetDirty(spriteAsset);
        EditorUtility.SetDirty(spriteAsset.material);
        AssetDatabase.SaveAssets();
        return spriteAsset;
    }

    private static void DrawDefinition(
        Color32[] pixels,
        RectInt rect,
        GlyphDefinition definition,
        Font labelFont)
    {
        RectInt frameRect = new RectInt(
            rect.x + 3,
            rect.y + 3,
            rect.width - 6,
            rect.height - 6);
        DrawRoundedRect(pixels, frameRect, 7, Border);
        RectInt contentRect = new RectInt(
            frameRect.x + 2,
            frameRect.y + 2,
            frameRect.width - 4,
            frameRect.height - 4);
        DrawRoundedRect(pixels, contentRect, 5, Transparent);

        switch (definition.Kind)
        {
            case GlyphKind.Key:
                DrawLabel(
                    pixels,
                    contentRect,
                    definition.Label,
                    labelFont);
                break;
            case GlyphKind.ArrowUp:
                DrawArrow(pixels, contentRect, 0, 1);
                break;
            case GlyphKind.ArrowDown:
                DrawArrow(pixels, contentRect, 0, -1);
                break;
            case GlyphKind.ArrowLeft:
                DrawArrow(pixels, contentRect, -1, 0);
                break;
            case GlyphKind.ArrowRight:
                DrawArrow(pixels, contentRect, 1, 0);
                break;
            default:
                DrawMouse(pixels, contentRect, definition.Kind);
                break;
        }
    }

    private static void DrawLabel(
        Color32[] pixels,
        RectInt rect,
        string label,
        Font font)
    {
        if (string.IsNullOrEmpty(label))
            return;
        label = label.ToUpperInvariant();
        int fontSize = label.Length <= 1
            ? 34
            : label.Length <= 2
                ? 27
                : label.Length <= 4
                    ? 18
                    : 15;
        font.RequestCharactersInTexture(
            label,
            fontSize,
            FontStyle.Normal);
        var characters = new List<CharacterInfo>(label.Length);
        int totalAdvance = 0;
        int minimumY = int.MaxValue;
        int maximumY = int.MinValue;
        for (int i = 0; i < label.Length; i++)
        {
            if (!font.GetCharacterInfo(
                    label[i],
                    out CharacterInfo character,
                    fontSize,
                    FontStyle.Normal))
                continue;
            characters.Add(character);
            totalAdvance += character.advance;
            minimumY = Mathf.Min(minimumY, character.minY);
            maximumY = Mathf.Max(maximumY, character.maxY);
        }
        if (characters.Count == 0)
            return;

        Texture fontTextureSource = font.material.mainTexture;
        if (fontTextureSource == null)
            return;
        Texture2D fontTexture = CreateReadableCopy(fontTextureSource);

        int cursorX = rect.x + (rect.width - totalAdvance) / 2;
        int baselineY = rect.y + rect.height / 2
            - (minimumY + maximumY) / 2;
        for (int i = 0; i < characters.Count; i++)
        {
            CharacterInfo character = characters[i];
            int width = character.maxX - character.minX;
            int height = character.maxY - character.minY;
            for (int y = 0; y < height; y++)
            {
                float vertical = (y + 0.5f) / Mathf.Max(1, height);
                for (int x = 0; x < width; x++)
                {
                    float horizontal =
                        (x + 0.5f) / Mathf.Max(1, width);
                    Vector2 bottom = Vector2.Lerp(
                        character.uvBottomLeft,
                        character.uvBottomRight,
                        horizontal);
                    Vector2 top = Vector2.Lerp(
                        character.uvTopLeft,
                        character.uvTopRight,
                        horizontal);
                    Vector2 uv = Vector2.Lerp(
                        bottom,
                        top,
                        vertical);
                    byte alpha = (byte)Mathf.RoundToInt(
                        fontTexture.GetPixelBilinear(uv.x, uv.y).a
                        * Ink.a);
                    if (alpha == 0)
                        continue;
                    SetPixelIfMoreOpaque(
                        pixels,
                        cursorX + character.minX + x,
                        baselineY + character.minY + y,
                        new Color32(Ink.r, Ink.g, Ink.b, alpha));
                }
            }
            cursorX += character.advance;
        }
        UnityEngine.Object.DestroyImmediate(fontTexture);
    }

    private static Texture2D CreateReadableCopy(Texture source)
    {
        RenderTexture previous = RenderTexture.active;
        RenderTexture temporary = RenderTexture.GetTemporary(
            source.width,
            source.height,
            0,
            RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.Linear);
        try
        {
            Graphics.Blit(source, temporary);
            RenderTexture.active = temporary;
            var readable = new Texture2D(
                source.width,
                source.height,
                TextureFormat.RGBA32,
                false,
                true);
            readable.ReadPixels(
                new Rect(0f, 0f, source.width, source.height),
                0,
                0,
                false);
            readable.Apply(false, false);
            return readable;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temporary);
        }
    }

    private static void DrawArrow(
        Color32[] pixels,
        RectInt rect,
        int xDirection,
        int yDirection)
    {
        int centerX = rect.x + rect.width / 2;
        int centerY = rect.y + rect.height / 2;
        int tipX = centerX + xDirection * 12;
        int tipY = centerY + yDirection * 12;
        int tailX = centerX - xDirection * 10;
        int tailY = centerY - yDirection * 10;
        DrawLine(pixels, tailX, tailY, tipX, tipY, Accent, 3);
        if (xDirection == 0)
        {
            DrawLine(pixels, tipX, tipY, tipX - 7, tipY - yDirection * 7, Accent, 3);
            DrawLine(pixels, tipX, tipY, tipX + 7, tipY - yDirection * 7, Accent, 3);
        }
        else
        {
            DrawLine(pixels, tipX, tipY, tipX - xDirection * 7, tipY - 7, Accent, 3);
            DrawLine(pixels, tipX, tipY, tipX - xDirection * 7, tipY + 7, Accent, 3);
        }
    }

    private static void DrawMouse(
        Color32[] pixels,
        RectInt rect,
        GlyphKind kind)
    {
        int bodyWidth = 24;
        int bodyHeight = 34;
        RectInt body = new RectInt(
            rect.x + (rect.width - bodyWidth) / 2,
            rect.y + (rect.height - bodyHeight) / 2,
            bodyWidth,
            bodyHeight);
        DrawRoundedRect(pixels, body, 10, Ink);
        RectInt inner = new RectInt(
            body.x + 2,
            body.y + 2,
            body.width - 4,
            body.height - 4);
        DrawRoundedRect(pixels, inner, 8, MouseInterior);
        DrawHorizontalLine(
            pixels,
            body.x + 2,
            body.xMax - 3,
            body.y + body.height / 2 + 2,
            Ink,
            2);
        DrawLine(
            pixels,
            body.x + body.width / 2,
            body.y + body.height / 2 + 2,
            body.x + body.width / 2,
            body.yMax - 3,
            Ink,
            1);

        if (kind == GlyphKind.MouseLeft)
        {
            FillRect(
                pixels,
                new RectInt(body.x + 4, body.yMax - 10, 8, 5),
                Accent);
        }
        else if (kind == GlyphKind.MouseRight)
        {
            FillRect(
                pixels,
                new RectInt(body.xMax - 12, body.yMax - 10, 8, 5),
                Accent);
        }
        else if (kind == GlyphKind.MouseMiddle
            || kind == GlyphKind.MouseWheel)
        {
            FillRect(
                pixels,
                new RectInt(body.x + body.width / 2 - 2, body.yMax - 13, 4, 8),
                Accent);
            if (kind == GlyphKind.MouseWheel)
            {
                DrawLine(
                    pixels,
                    body.x + body.width / 2,
                    body.y + 5,
                    body.x + body.width / 2,
                    body.y + 11,
                    Accent,
                    2);
            }
        }
        else if (kind == GlyphKind.MouseMove)
        {
            DrawHorizontalLine(
                pixels,
                body.x - 5,
                body.xMax + 4,
                body.y + body.height / 2,
                Accent,
                2);
        }
        else if (kind == GlyphKind.MousePointer)
        {
            DrawRoundedRect(
                pixels,
                new RectInt(
                    body.x + body.width / 2 - 3,
                    body.y + body.height / 2 - 3,
                    6,
                    6),
                3,
                Accent);
        }
        else if (kind == GlyphKind.MouseBack
            || kind == GlyphKind.MouseForward)
        {
            int direction = kind == GlyphKind.MouseBack ? -1 : 1;
            int originX = direction < 0 ? body.x - 2 : body.xMax + 1;
            DrawLine(
                pixels,
                originX,
                body.y + body.height / 2,
                originX + direction * 6,
                body.y + body.height / 2,
                Accent,
                2);
        }
    }

    private static void DrawRoundedRect(
        Color32[] pixels,
        RectInt rect,
        int radius,
        Color32 color)
    {
        int radiusSquared = radius * radius;
        for (int y = rect.y; y < rect.yMax; y++)
        {
            for (int x = rect.x; x < rect.xMax; x++)
            {
                int nearestX = Mathf.Clamp(
                    x,
                    rect.x + radius,
                    rect.xMax - radius - 1);
                int nearestY = Mathf.Clamp(
                    y,
                    rect.y + radius,
                    rect.yMax - radius - 1);
                int dx = x - nearestX;
                int dy = y - nearestY;
                if (dx * dx + dy * dy <= radiusSquared)
                    SetPixel(pixels, x, y, color);
            }
        }
    }

    private static void FillRect(
        Color32[] pixels,
        RectInt rect,
        Color32 color)
    {
        for (int y = rect.y; y < rect.yMax; y++)
        {
            for (int x = rect.x; x < rect.xMax; x++)
                SetPixel(pixels, x, y, color);
        }
    }

    private static void DrawHorizontalLine(
        Color32[] pixels,
        int startX,
        int endX,
        int y,
        Color32 color,
        int thickness)
    {
        FillRect(
            pixels,
            new RectInt(startX, y, endX - startX + 1, thickness),
            color);
    }

    private static void DrawLine(
        Color32[] pixels,
        int x0,
        int y0,
        int x1,
        int y1,
        Color32 color,
        int thickness)
    {
        int dx = Mathf.Abs(x1 - x0);
        int sx = x0 < x1 ? 1 : -1;
        int dy = -Mathf.Abs(y1 - y0);
        int sy = y0 < y1 ? 1 : -1;
        int error = dx + dy;
        while (true)
        {
            FillRect(
                pixels,
                new RectInt(
                    x0 - thickness / 2,
                    y0 - thickness / 2,
                    thickness,
                    thickness),
                color);
            if (x0 == x1 && y0 == y1)
                break;
            int doubled = 2 * error;
            if (doubled >= dy)
            {
                error += dy;
                x0 += sx;
            }
            if (doubled <= dx)
            {
                error += dx;
                y0 += sy;
            }
        }
    }

    private static void SetPixel(
        Color32[] pixels,
        int x,
        int y,
        Color32 color)
    {
        if ((uint)x >= AtlasWidth || (uint)y >= AtlasHeight)
            return;
        pixels[y * AtlasWidth + x] = color;
    }

    private static void SetPixelIfMoreOpaque(
        Color32[] pixels,
        int x,
        int y,
        Color32 color)
    {
        if ((uint)x >= AtlasWidth || (uint)y >= AtlasHeight)
            return;
        int index = y * AtlasWidth + x;
        if (color.a > pixels[index].a)
            pixels[index] = color;
    }

    private static string[] Rows(params string[] rows)
    {
        return rows;
    }

    private enum GlyphKind
    {
        Key,
        ArrowUp,
        ArrowDown,
        ArrowLeft,
        ArrowRight,
        MouseLeft,
        MouseRight,
        MouseMiddle,
        MouseWheel,
        MouseMove,
        MousePointer,
        MouseBack,
        MouseForward,
    }

    private sealed class GlyphDefinition
    {
        public GlyphDefinition(
            string name,
            string label,
            int width,
            GlyphKind kind)
        {
            Name = name;
            Label = label;
            Width = width;
            Kind = kind;
        }

        public string Name { get; }
        public string Label { get; }
        public int Width { get; }
        public GlyphKind Kind { get; }
    }
}
#endif

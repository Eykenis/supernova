using System;
using System.Collections.Generic;
using System.Text;

namespace Supernova.Inputs
{
    /// <summary>
    /// Maps keyboard and mouse control paths to the named sprites in the
    /// centralized input-glyph atlas. Gamepad paths intentionally fall back to
    /// text because the current UI only targets keyboard and mouse.
    /// </summary>
    public static class InputPromptGlyphs
    {
        private const string SpriteTagPrefix = "<sprite name=\"";
        private const string SpriteTagSuffix = "\" tint=1>";
        private const string SpriteSizePrefix = "<size=150%>";
        private const string SpriteSizeSuffix = "</size>";
        private const string GlyphGap = "\u2009";

        public static string ToRichText(
            IReadOnlyList<string> controlPaths,
            string fallbackDisplay)
        {
            if (controlPaths == null || controlPaths.Count == 0)
                return fallbackDisplay ?? string.Empty;

            var builder = new StringBuilder(controlPaths.Count * 36);
            for (int i = 0; i < controlPaths.Count; i++)
            {
                if (!TryGetSpriteName(controlPaths[i], out string spriteName))
                    return fallbackDisplay ?? string.Empty;
                if (builder.Length > 0)
                    builder.Append(GlyphGap);
                AppendSpriteTag(builder, spriteName);
            }
            return builder.ToString();
        }

        public static string ToRichText(
            string controlPath,
            string fallbackDisplay)
        {
            if (!TryGetSpriteName(controlPath, out string spriteName))
                return fallbackDisplay ?? string.Empty;
            var builder = new StringBuilder(36);
            AppendSpriteTag(builder, spriteName);
            return builder.ToString();
        }

        public static bool TryGetSpriteName(
            string controlPath,
            out string spriteName)
        {
            spriteName = string.Empty;
            if (string.IsNullOrWhiteSpace(controlPath))
                return false;

            bool keyboard = controlPath.IndexOf(
                "<Keyboard>", StringComparison.OrdinalIgnoreCase) >= 0;
            bool mouse = controlPath.IndexOf(
                "<Mouse>", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!keyboard && !mouse)
                return false;

            int slash = controlPath.LastIndexOf('/');
            string control = slash >= 0
                ? controlPath.Substring(slash + 1)
                : controlPath;
            int brace = control.IndexOf('{');
            if (brace >= 0)
                control = control.Substring(0, brace);
            control = control.Trim();

            if (mouse)
                return TryGetMouseSpriteName(control, out spriteName);
            return TryGetKeyboardSpriteName(control, out spriteName);
        }

        private static bool TryGetMouseSpriteName(
            string control,
            out string spriteName)
        {
            switch (control.ToLowerInvariant())
            {
                case "leftbutton":
                    spriteName = "MouseLeft";
                    return true;
                case "rightbutton":
                    spriteName = "MouseRight";
                    return true;
                case "middlebutton":
                    spriteName = "MouseMiddle";
                    return true;
                case "backbutton":
                    spriteName = "MouseBack";
                    return true;
                case "forwardbutton":
                    spriteName = "MouseForward";
                    return true;
                case "scroll":
                case "scrolly":
                    spriteName = "MouseWheel";
                    return true;
                case "delta":
                    spriteName = "MouseMove";
                    return true;
                case "position":
                    spriteName = "MousePointer";
                    return true;
                default:
                    spriteName = string.Empty;
                    return false;
            }
        }

        private static bool TryGetKeyboardSpriteName(
            string control,
            out string spriteName)
        {
            if (control.StartsWith("#(", StringComparison.Ordinal)
                && control.EndsWith(")", StringComparison.Ordinal))
            {
                control = control.Substring(2, control.Length - 3);
            }

            string normalized = control.ToLowerInvariant();
            switch (normalized)
            {
                case "leftctrl":
                case "rightctrl":
                case "ctrl":
                    spriteName = "Key_CTRL";
                    return true;
                case "leftshift":
                case "rightshift":
                case "shift":
                    spriteName = "Key_SHIFT";
                    return true;
                case "leftalt":
                case "rightalt":
                case "alt":
                    spriteName = "Key_ALT";
                    return true;
                case "leftmeta":
                case "rightmeta":
                case "meta":
                    spriteName = "Key_META";
                    return true;
                case "space": spriteName = "Key_SPACE"; return true;
                case "enter": spriteName = "Key_ENTER"; return true;
                case "escape": spriteName = "Key_ESC"; return true;
                case "tab": spriteName = "Key_TAB"; return true;
                case "capslock": spriteName = "Key_CAPS"; return true;
                case "numlock": spriteName = "Key_NUM"; return true;
                case "printscreen": spriteName = "Key_PRT"; return true;
                case "scrolllock": spriteName = "Key_SCRL"; return true;
                case "pause": spriteName = "Key_PAUSE"; return true;
                case "contextmenu": spriteName = "Key_MENU"; return true;
                case "anykey": spriteName = "Key_ANY"; return true;
                case "imeselected": spriteName = "Key_IME"; return true;
                case "backspace": spriteName = "Key_BACK"; return true;
                case "delete": spriteName = "Key_DEL"; return true;
                case "insert": spriteName = "Key_INS"; return true;
                case "home": spriteName = "Key_HOME"; return true;
                case "end": spriteName = "Key_END"; return true;
                case "pageup": spriteName = "Key_PGUP"; return true;
                case "pagedown": spriteName = "Key_PGDN"; return true;
                case "uparrow": spriteName = "Key_UP"; return true;
                case "downarrow": spriteName = "Key_DOWN"; return true;
                case "leftarrow": spriteName = "Key_LEFT"; return true;
                case "rightarrow": spriteName = "Key_RIGHT"; return true;
                case "minus": spriteName = "Key_MINUS"; return true;
                case "equals": spriteName = "Key_EQUALS"; return true;
                case "comma": spriteName = "Key_COMMA"; return true;
                case "period": spriteName = "Key_PERIOD"; return true;
                case "slash": spriteName = "Key_SLASH"; return true;
                case "backslash": spriteName = "Key_BACKSLASH"; return true;
                case "semicolon": spriteName = "Key_SEMICOLON"; return true;
                case "quote": spriteName = "Key_QUOTE"; return true;
                case "backquote": spriteName = "Key_BACKQUOTE"; return true;
                case "leftbracket": spriteName = "Key_LBRACKET"; return true;
                case "rightbracket": spriteName = "Key_RBRACKET"; return true;
                case "numpadplus": spriteName = "Key_NPPLUS"; return true;
                case "numpadminus": spriteName = "Key_NPMINUS"; return true;
                case "numpadmultiply": spriteName = "Key_NPMULTIPLY"; return true;
                case "numpaddivide": spriteName = "Key_NPDIVIDE"; return true;
                case "numpadenter": spriteName = "Key_NPENTER"; return true;
                case "numpadperiod": spriteName = "Key_NPPERIOD"; return true;
                case "numpadequals": spriteName = "Key_NPEQUALS"; return true;
            }

            if (normalized.StartsWith("digit", StringComparison.Ordinal)
                && normalized.Length == 6
                && char.IsDigit(normalized[5]))
            {
                spriteName = "Key_" + normalized[5];
                return true;
            }

            if (normalized.StartsWith("numpad", StringComparison.Ordinal)
                && normalized.Length == 7
                && char.IsDigit(normalized[6]))
            {
                spriteName = "Key_NP" + normalized[6];
                return true;
            }

            if (normalized.Length == 1
                && (char.IsLetter(normalized[0])
                    || char.IsDigit(normalized[0])))
            {
                spriteName = "Key_" + char.ToUpperInvariant(normalized[0]);
                return true;
            }

            if (normalized.Length >= 2
                && normalized[0] == 'f'
                && int.TryParse(normalized.Substring(1), out int functionKey)
                && functionKey >= 1
                && functionKey <= 24)
            {
                spriteName = "Key_F" + functionKey;
                return true;
            }

            spriteName = string.Empty;
            return false;
        }

        private static void AppendSpriteTag(
            StringBuilder builder,
            string spriteName)
        {
            builder.Append(SpriteSizePrefix);
            builder.Append(SpriteTagPrefix);
            builder.Append(spriteName);
            builder.Append(SpriteTagSuffix);
            builder.Append(SpriteSizeSuffix);
        }
    }
}

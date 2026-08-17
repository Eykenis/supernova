using System;
using System.Text.RegularExpressions;

namespace Supernova.Inputs
{
    public static class InputPromptResolver
    {
        public const string Marker = "{{input:";

        private static readonly Regex TokenPattern = new Regex(
            @"(?<!\\)\{\{input:([^}|]+)(?:\|([^}]+))?\}\}",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static string Resolve(string source)
        {
            return ResolveInternal(source, false);
        }

        public static string ResolveWithGlyphs(string source)
        {
            return ResolveInternal(source, true);
        }

        public static string Token(GameInputActionId actionId)
        {
            return Marker + GameInputDefinitions.GetActionPath(actionId) + "}}";
        }

        private static string ResolveInternal(string source, bool useGlyphs)
        {
            if (string.IsNullOrEmpty(source)
                || source.IndexOf(Marker, StringComparison.Ordinal) < 0)
            {
                return source ?? string.Empty;
            }

            string resolved = TokenPattern.Replace(
                source,
                match => ResolveMatch(match, useGlyphs));
            return resolved.Replace("\\{{input:", "{{input:");
        }

        private static string ResolveMatch(Match match, bool useGlyphs)
        {
            string target = match.Groups[1].Value.Trim();
            bool compact = string.Equals(
                match.Groups[2].Value.Trim(),
                "compact",
                StringComparison.OrdinalIgnoreCase);
            string partName = null;
            int slash = target.LastIndexOf('/');
            int dot = target.LastIndexOf('.');
            if (dot > slash)
            {
                partName = target.Substring(dot + 1);
                target = target.Substring(0, dot);
            }

            string display = GameInput.GetBindingDisplayString(
                target,
                partName,
                compact);
            if (string.IsNullOrEmpty(display))
                return match.Value;
            if (!useGlyphs)
                return display;

            return InputPromptGlyphs.ToRichText(
                GameInput.GetBindingControlPaths(target, partName),
                display);
        }
    }
}

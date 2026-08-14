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
            if (string.IsNullOrEmpty(source)
                || source.IndexOf(Marker, StringComparison.Ordinal) < 0)
            {
                return source ?? string.Empty;
            }

            string resolved = TokenPattern.Replace(source, ResolveMatch);
            return resolved.Replace("\\{{input:", "{{input:");
        }

        private static string ResolveMatch(Match match)
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
            return string.IsNullOrEmpty(display) ? match.Value : display;
        }
    }
}

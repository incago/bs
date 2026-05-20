using System.Text;
using System.Text.RegularExpressions;

namespace SpreadAsset.Editor
{
    public static class SpreadAssetNameUtility
    {
        private static readonly Regex InvalidFileNameCharacters = new Regex("[^A-Za-z0-9_\\-]+");

        public static bool IsValidIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (!(char.IsLetter(value[0]) || value[0] == '_'))
            {
                return false;
            }

            for (int i = 1; i < value.Length; i++)
            {
                if (!(char.IsLetterOrDigit(value[i]) || value[i] == '_'))
                {
                    return false;
                }
            }

            return true;
        }

        public static string ToPascalCase(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string[] parts = value.Trim().Split(new[] { '_', '-', ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            StringBuilder builder = new StringBuilder();
            foreach (string part in parts)
            {
                if (part.Length == 0)
                {
                    continue;
                }

                builder.Append(char.ToUpperInvariant(part[0]));
                if (part.Length > 1)
                {
                    builder.Append(part.Substring(1));
                }
            }

            return builder.ToString();
        }

        public static string ToSerializedFieldName(string value)
        {
            string pascalCase = ToPascalCase(value);
            if (string.IsNullOrEmpty(pascalCase))
            {
                return string.Empty;
            }

            return "_" + char.ToLowerInvariant(pascalCase[0]) + pascalCase.Substring(1);
        }

        public static string ToDefaultDocumentName(string menuPath, string assetClassName)
        {
            string name = string.Empty;
            if (!string.IsNullOrWhiteSpace(menuPath))
            {
                string[] menuParts = menuPath.Trim('/').Split('/');
                name = menuParts.Length > 0 ? menuParts[menuParts.Length - 1] : string.Empty;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = assetClassName;
            }

            if (name.EndsWith("Asset", System.StringComparison.Ordinal))
            {
                name = name.Substring(0, name.Length - "Asset".Length);
            }

            return ToSafeFileName(ToSnakeCase(name));
        }

        public static string ToSafeFileName(string value)
        {
            string safe = InvalidFileNameCharacters.Replace(value.Trim(), "_");
            return string.IsNullOrWhiteSpace(safe) ? "spread_asset" : safe;
        }

        public static string ToSnakeCase(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                char current = value[i];
                if (current == '-' || current == ' ')
                {
                    current = '_';
                }

                if (char.IsUpper(current) && i > 0 && builder[builder.Length - 1] != '_')
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(current));
            }

            return builder.ToString().Trim('_');
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEditor;

namespace SpreadAsset.Editor
{
    internal static class SpreadAssetEnumTypeUtility
    {
        private static EnumTypeOption[] _annotatedEnumOptions;

        public static TypePopupOptions CreatePopupOptions(string[] builtInTypeNames, string currentTypeName)
        {
            string normalizedCurrentTypeName = string.IsNullOrWhiteSpace(currentTypeName)
                ? builtInTypeNames[0]
                : currentTypeName.Trim();

            List<string> displayNames = new List<string>();
            List<string> typeNames = new List<string>();
            foreach (string builtInTypeName in builtInTypeNames)
            {
                displayNames.Add(builtInTypeName);
                typeNames.Add(builtInTypeName);
            }

            foreach (EnumTypeOption enumOption in GetAnnotatedEnumOptions())
            {
                displayNames.Add(enumOption.DisplayName);
                typeNames.Add(enumOption.TypeName);
            }

            int selectedIndex = FindTypeNameIndex(typeNames, normalizedCurrentTypeName);
            if (selectedIndex < 0)
            {
                selectedIndex = FindAnnotatedEnumIndexByShortName(normalizedCurrentTypeName);
                if (selectedIndex >= 0)
                {
                    selectedIndex += builtInTypeNames.Length;
                }
            }

            if (selectedIndex < 0)
            {
                displayNames.Insert(0, normalizedCurrentTypeName + " (custom)");
                typeNames.Insert(0, normalizedCurrentTypeName);
                selectedIndex = 0;
            }

            return new TypePopupOptions(displayNames.ToArray(), typeNames.ToArray(), selectedIndex);
        }

        public static bool TryGetAnnotatedEnumType(string typeName, out Type enumType)
        {
            enumType = null;
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return false;
            }

            string normalizedTypeName = typeName.Trim();
            EnumTypeOption[] options = GetAnnotatedEnumOptions();
            foreach (EnumTypeOption option in options)
            {
                if (string.Equals(option.TypeName, normalizedTypeName, StringComparison.Ordinal)
                    || string.Equals(option.ShortName, normalizedTypeName, StringComparison.Ordinal))
                {
                    enumType = option.Type;
                    return true;
                }
            }

            return false;
        }

        private static int FindTypeNameIndex(List<string> typeNames, string typeName)
        {
            for (int i = 0; i < typeNames.Count; i++)
            {
                if (string.Equals(typeNames[i], typeName, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private static int FindAnnotatedEnumIndexByShortName(string typeName)
        {
            EnumTypeOption[] options = GetAnnotatedEnumOptions();
            int matchIndex = -1;
            for (int i = 0; i < options.Length; i++)
            {
                if (!string.Equals(options[i].ShortName, typeName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (matchIndex >= 0)
                {
                    return -1;
                }

                matchIndex = i;
            }

            return matchIndex;
        }

        private static EnumTypeOption[] GetAnnotatedEnumOptions()
        {
            if (_annotatedEnumOptions != null)
            {
                return _annotatedEnumOptions;
            }

            List<EnumTypeOption> options = new List<EnumTypeOption>();
            foreach (Type type in TypeCache.GetTypesWithAttribute<SpreadAssetEnumAttribute>())
            {
                if (type == null || !type.IsEnum)
                {
                    continue;
                }

                string typeName = GetCSharpTypeName(type);
                string displayName = "Enum - " + type.Name;

                options.Add(new EnumTypeOption(type, typeName, type.Name, displayName));
            }

            options.Sort((left, right) => string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase));
            _annotatedEnumOptions = options.ToArray();
            return _annotatedEnumOptions;
        }

        private static string GetCSharpTypeName(Type type)
        {
            string fullName = type.FullName ?? type.Name;
            return fullName.Replace('+', '.');
        }

        public sealed class TypePopupOptions
        {
            public readonly string[] DisplayNames;
            public readonly string[] TypeNames;
            public readonly int SelectedIndex;

            public TypePopupOptions(string[] displayNames, string[] typeNames, int selectedIndex)
            {
                DisplayNames = displayNames;
                TypeNames = typeNames;
                SelectedIndex = selectedIndex;
            }
        }

        private sealed class EnumTypeOption
        {
            public readonly Type Type;
            public readonly string TypeName;
            public readonly string ShortName;
            public readonly string DisplayName;

            public EnumTypeOption(Type type, string typeName, string shortName, string displayName)
            {
                Type = type;
                TypeName = typeName;
                ShortName = shortName;
                DisplayName = displayName;
            }
        }
    }
}

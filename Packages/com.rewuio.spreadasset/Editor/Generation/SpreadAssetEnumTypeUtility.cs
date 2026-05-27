using System;
using System.Collections.Generic;
using UnityEditor;

namespace SpreadAsset.Editor
{
    internal static class SpreadAssetEnumTypeUtility
    {
        private static TypeOption[] _annotatedEnumOptions;
        private static TypeOption[] _annotatedClassOptions;

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

            TypeOption[] enumOptions = GetAnnotatedEnumOptions();
            foreach (TypeOption enumOption in enumOptions)
            {
                displayNames.Add(enumOption.DisplayName);
                typeNames.Add(enumOption.TypeName);
            }

            TypeOption[] classOptions = GetAnnotatedClassOptions();
            foreach (TypeOption classOption in classOptions)
            {
                displayNames.Add(classOption.DisplayName);
                typeNames.Add(classOption.TypeName);
            }

            int selectedIndex = FindTypeNameIndex(typeNames, normalizedCurrentTypeName);
            if (selectedIndex < 0)
            {
                selectedIndex = FindAnnotatedTypeIndexByShortName(enumOptions, normalizedCurrentTypeName);
                if (selectedIndex >= 0)
                {
                    selectedIndex += builtInTypeNames.Length;
                }
            }

            if (selectedIndex < 0)
            {
                selectedIndex = FindAnnotatedTypeIndexByShortName(classOptions, normalizedCurrentTypeName);
                if (selectedIndex >= 0)
                {
                    selectedIndex += builtInTypeNames.Length + enumOptions.Length;
                }
            }

            int customIndex = displayNames.Count;
            displayNames.Add("Custom...");
            typeNames.Add(normalizedCurrentTypeName);
            if (selectedIndex < 0)
            {
                selectedIndex = customIndex;
            }

            return new TypePopupOptions(displayNames.ToArray(), typeNames.ToArray(), selectedIndex, customIndex);
        }

        public static bool TryGetAnnotatedEnumType(string typeName, out Type enumType)
        {
            enumType = null;
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return false;
            }

            string normalizedTypeName = typeName.Trim();
            return TryGetAnnotatedType(GetAnnotatedEnumOptions(), normalizedTypeName, out enumType);
        }

        public static bool TryGetAnnotatedClassType(string typeName, out Type classType)
        {
            classType = null;
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return false;
            }

            string normalizedTypeName = typeName.Trim();
            return TryGetAnnotatedType(GetAnnotatedClassOptions(), normalizedTypeName, out classType);
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

        private static int FindAnnotatedTypeIndexByShortName(TypeOption[] options, string typeName)
        {
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

        private static bool TryGetAnnotatedType(TypeOption[] options, string typeName, out Type type)
        {
            type = null;
            foreach (TypeOption option in options)
            {
                if (string.Equals(option.TypeName, typeName, StringComparison.Ordinal)
                    || string.Equals(option.ShortName, typeName, StringComparison.Ordinal))
                {
                    type = option.Type;
                    return true;
                }
            }

            return false;
        }

        private static TypeOption[] GetAnnotatedEnumOptions()
        {
            if (_annotatedEnumOptions != null)
            {
                return _annotatedEnumOptions;
            }

            List<TypeOption> options = new List<TypeOption>();
            foreach (Type type in TypeCache.GetTypesWithAttribute<SpreadAssetEnumAttribute>())
            {
                if (type == null || !type.IsEnum)
                {
                    continue;
                }

                string typeName = GetCSharpTypeName(type);
                string displayName = "Enum - " + type.Name;

                options.Add(new TypeOption(type, typeName, type.Name, displayName));
            }

            options.Sort((left, right) => string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase));
            _annotatedEnumOptions = options.ToArray();
            return _annotatedEnumOptions;
        }

        private static TypeOption[] GetAnnotatedClassOptions()
        {
            if (_annotatedClassOptions != null)
            {
                return _annotatedClassOptions;
            }

            List<TypeOption> options = new List<TypeOption>();
            foreach (Type type in TypeCache.GetTypesWithAttribute<SpreadAssetClassAttribute>())
            {
                if (!IsSupportedAnnotatedClassType(type))
                {
                    continue;
                }

                string typeName = GetCSharpTypeName(type);
                string displayName = "Class - " + type.Name;

                options.Add(new TypeOption(type, typeName, type.Name, displayName));
            }

            options.Sort((left, right) => string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase));
            _annotatedClassOptions = options.ToArray();
            return _annotatedClassOptions;
        }

        private static bool IsSupportedAnnotatedClassType(Type type)
        {
            return type != null
                && !type.IsEnum
                && !type.IsAbstract
                && !type.IsGenericTypeDefinition
                && (type.IsClass || (type.IsValueType && !type.IsPrimitive))
                && SpreadAssetFieldTypeUtility.IsSupportedResolvedType(type);
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
            public readonly int CustomIndex;

            public TypePopupOptions(string[] displayNames, string[] typeNames, int selectedIndex, int customIndex)
            {
                DisplayNames = displayNames;
                TypeNames = typeNames;
                SelectedIndex = selectedIndex;
                CustomIndex = customIndex;
            }
        }

        private sealed class TypeOption
        {
            public readonly Type Type;
            public readonly string TypeName;
            public readonly string ShortName;
            public readonly string DisplayName;

            public TypeOption(Type type, string typeName, string shortName, string displayName)
            {
                Type = type;
                TypeName = typeName;
                ShortName = shortName;
                DisplayName = displayName;
            }
        }
    }
}

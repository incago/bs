using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace SpreadAsset.Editor
{
    internal static class SpreadAssetFieldTypeUtility
    {
        public static readonly string[] RecommendedDataFieldTypeNames =
        {
            "string",
            "int",
            "float",
            "bool",
            "Vector2",
            "Vector3",
            "Color"
        };

        private static readonly Dictionary<string, Type> PrimitiveTypesByName = new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            { "string", typeof(string) },
            { "int", typeof(int) },
            { "float", typeof(float) },
            { "bool", typeof(bool) },
            { "long", typeof(long) },
            { "double", typeof(double) },
            { "short", typeof(short) },
            { "byte", typeof(byte) },
            { "uint", typeof(uint) },
            { "ulong", typeof(ulong) },
            { "ushort", typeof(ushort) },
            { "sbyte", typeof(sbyte) }
        };

        private static readonly Dictionary<string, Type> ResolvedTypesByName = new Dictionary<string, Type>(StringComparer.Ordinal);
        private static readonly HashSet<string> SerializedPropertyTypeNames =
            new HashSet<string>(Enum.GetNames(typeof(SerializedPropertyType)), StringComparer.Ordinal);

        public static bool IsSupportedDataFieldType(
            string typeName,
            SpreadAssetSchemaTable ownerTable,
            SpreadAssetSchemaTable[] tables)
        {
            string normalizedTypeName = NormalizeTypeName(typeName);
            if (string.IsNullOrEmpty(normalizedTypeName))
            {
                return false;
            }

            if (IsSupportedDataClassFieldType(normalizedTypeName, ownerTable, tables))
            {
                return true;
            }

            if (TryGetCollectionElementTypeName(normalizedTypeName, out string elementTypeName))
            {
                return IsSupportedDataFieldType(elementTypeName, ownerTable, tables);
            }

            if (TryResolveType(normalizedTypeName, out Type type)
                && IsSupportedResolvedType(type))
            {
                return true;
            }

            return SpreadAssetEnumTypeUtility.TryGetAnnotatedEnumType(normalizedTypeName, out _);
        }

        public static bool IsGenericListTypeName(string typeName)
        {
            string normalizedTypeName = NormalizeTypeName(typeName);
            return TryGetGenericListElementTypeName(normalizedTypeName, out _);
        }

        public static string GetUnsupportedDataFieldTypeMessage(string fieldName, string typeName)
        {
            return $"Data field {fieldName} uses unsupported type {typeName}. Use a recommended type, a C# primitive, a Unity serializable type, an enum, another data class declared in this asset schema, or a supported array/List<T> of those types. A data class cannot contain itself.";
        }

        public static string NormalizeDataClassTypeName(string typeName)
        {
            return SpreadAssetNameUtility.ToPascalCase(typeName?.Trim() ?? string.Empty);
        }

        public static bool IsSupportedResolvedType(Type type)
        {
            if (type == null || type.IsPointer || type.IsByRef || type.IsGenericTypeDefinition)
            {
                return false;
            }

            if (type.IsEnum || PrimitiveTypesByName.ContainsValue(type))
            {
                return true;
            }

            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            {
                return true;
            }

            if (type.Namespace == "UnityEngine" && SerializedPropertyTypeNames.Contains(type.Name))
            {
                return true;
            }

            if (type.Namespace != null && type.Namespace.StartsWith("System", StringComparison.Ordinal))
            {
                return false;
            }

            return Attribute.IsDefined(type, typeof(SerializableAttribute), inherit: false);
        }

        private static bool IsSupportedDataClassFieldType(
            string typeName,
            SpreadAssetSchemaTable ownerTable,
            SpreadAssetSchemaTable[] tables)
        {
            string elementTypeName = GetCollectionElementTypeNameOrSelf(typeName);
            if (string.IsNullOrWhiteSpace(elementTypeName) || tables == null)
            {
                return false;
            }

            string ownerRowTypeName = NormalizeDataClassTypeName(ownerTable?.RowTypeName);
            foreach (SpreadAssetSchemaTable table in tables)
            {
                string rowTypeName = NormalizeDataClassTypeName(table?.RowTypeName);
                if (string.IsNullOrEmpty(rowTypeName)
                    || !string.Equals(rowTypeName, NormalizeDataClassTypeName(elementTypeName), StringComparison.Ordinal))
                {
                    continue;
                }

                return !string.Equals(rowTypeName, ownerRowTypeName, StringComparison.Ordinal);
            }

            return false;
        }

        private static string GetCollectionElementTypeNameOrSelf(string typeName)
        {
            return TryGetCollectionElementTypeName(typeName, out string elementTypeName)
                ? elementTypeName
                : typeName;
        }

        private static bool TryGetCollectionElementTypeName(string typeName, out string elementTypeName)
        {
            string normalizedTypeName = NormalizeTypeName(typeName);
            if (TryGetArrayElementTypeName(normalizedTypeName, out elementTypeName))
            {
                return true;
            }

            return TryGetGenericListElementTypeName(normalizedTypeName, out elementTypeName);
        }

        private static bool TryGetArrayElementTypeName(string typeName, out string elementTypeName)
        {
            const string arraySuffix = "[]";
            string normalizedTypeName = NormalizeTypeName(typeName);
            if (normalizedTypeName.EndsWith(arraySuffix, StringComparison.Ordinal))
            {
                elementTypeName = normalizedTypeName.Substring(0, normalizedTypeName.Length - arraySuffix.Length).Trim();
                return !string.IsNullOrEmpty(elementTypeName);
            }

            elementTypeName = string.Empty;
            return false;
        }

        private static bool TryGetGenericListElementTypeName(string typeName, out string elementTypeName)
        {
            elementTypeName = string.Empty;
            string normalizedTypeName = NormalizeTypeName(typeName);
            const string listPrefix = "List<";
            const string fullListPrefix = "System.Collections.Generic.List<";
            string prefix = string.Empty;
            if (normalizedTypeName.StartsWith(listPrefix, StringComparison.Ordinal))
            {
                prefix = listPrefix;
            }
            else if (normalizedTypeName.StartsWith(fullListPrefix, StringComparison.Ordinal))
            {
                prefix = fullListPrefix;
            }

            if (string.IsNullOrEmpty(prefix) || !normalizedTypeName.EndsWith(">", StringComparison.Ordinal))
            {
                return false;
            }

            elementTypeName = normalizedTypeName.Substring(
                prefix.Length,
                normalizedTypeName.Length - prefix.Length - 1).Trim();
            return !string.IsNullOrEmpty(elementTypeName)
                && elementTypeName.IndexOf('<') < 0
                && elementTypeName.IndexOf('>') < 0;
        }

        private static bool TryResolveType(string typeName, out Type type)
        {
            type = null;
            string normalizedTypeName = NormalizeTypeName(typeName);
            if (string.IsNullOrEmpty(normalizedTypeName))
            {
                return false;
            }

            if (PrimitiveTypesByName.TryGetValue(normalizedTypeName, out type))
            {
                return true;
            }

            if (ResolvedTypesByName.TryGetValue(normalizedTypeName, out type))
            {
                return type != null;
            }

            type = ResolveType(normalizedTypeName);
            ResolvedTypesByName[normalizedTypeName] = type;
            return type != null;
        }

        private static Type ResolveType(string typeName)
        {
            Type type = Type.GetType(typeName, throwOnError: false);
            if (type != null)
            {
                return type;
            }

            string runtimeTypeName = typeName.Replace('.', '+');
            Type exactType = null;
            Type shortNameType = null;
            bool shortNameIsAmbiguous = false;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(typeName, throwOnError: false) ?? assembly.GetType(runtimeTypeName, throwOnError: false);
                if (type != null)
                {
                    return type;
                }

                foreach (Type candidate in GetLoadableTypes(assembly))
                {
                    if (candidate == null)
                    {
                        continue;
                    }

                    string candidateName = GetCSharpTypeName(candidate);
                    if (string.Equals(candidateName, typeName, StringComparison.Ordinal))
                    {
                        exactType = candidate;
                    }

                    if (!typeName.Contains(".")
                        && string.Equals(candidate.Name, typeName, StringComparison.Ordinal))
                    {
                        if (shortNameType != null && shortNameType != candidate)
                        {
                            shortNameIsAmbiguous = true;
                        }
                        else
                        {
                            shortNameType = candidate;
                        }
                    }
                }
            }

            if (exactType != null)
            {
                return exactType;
            }

            return shortNameIsAmbiguous ? null : shortNameType;
        }

        private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                return exception.Types ?? Array.Empty<Type>();
            }
            catch
            {
                return Array.Empty<Type>();
            }
        }

        private static string GetCSharpTypeName(Type type)
        {
            string fullName = type.FullName ?? type.Name;
            return fullName.Replace('+', '.');
        }

        private static string NormalizeTypeName(string typeName)
        {
            return (typeName ?? string.Empty).Trim();
        }
    }
}

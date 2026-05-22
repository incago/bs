using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SpreadAsset.Editor
{
    public static class SpreadAssetFactory
    {
        public static void CreatePair<TAsset>(string defaultFileName, SpreadAssetDocumentSchema schema)
            where TAsset : ScriptableObject
        {
            string directory = GetSelectedDirectory();
            string normalizedFileName = SpreadAssetNameUtility.ToSafeFileName(defaultFileName);
            string documentPath = AssetDatabase.GenerateUniqueAssetPath(
                ToAssetPath(Path.Combine(directory, normalizedFileName + SpreadAssetDocumentIO.Extension)));
            string basePath = ToAssetPath(Path.Combine(
                Path.GetDirectoryName(documentPath) ?? directory,
                Path.GetFileNameWithoutExtension(documentPath)));
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(basePath + ".asset");

            TAsset asset = ScriptableObject.CreateInstance<TAsset>();
            AssetDatabase.CreateAsset(asset, assetPath);

            SpreadAssetDocument document = SpreadAssetDocumentIO.CreateDocument(asset, assetPath, schema);
            SpreadAssetDocumentIO.Write(documentPath, document);

            AssetDatabase.ImportAsset(documentPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            UnityEngine.Object documentAsset = AssetDatabase.LoadMainAssetAtPath(documentPath);
            Selection.activeObject = documentAsset;
            EditorGUIUtility.PingObject(documentAsset);
            SpreadAssetWindow.OpenDocument(documentPath);
        }

        public static bool CanCreateDocumentFromAsset(UnityEngine.Object asset)
        {
            string assetPath = AssetDatabase.GetAssetPath(asset);
            return asset is ScriptableObject
                && string.Equals(Path.GetExtension(assetPath), ".asset", StringComparison.OrdinalIgnoreCase);
        }

        public static void CreateDocumentFromAsset(UnityEngine.Object sourceAsset)
        {
            if (!(sourceAsset is ScriptableObject scriptableObject))
            {
                EditorUtility.DisplayDialog(
                    "SpreadAsset Document",
                    "Select a SpreadAsset .asset file first.",
                    "OK");
                return;
            }

            string assetPath = AssetDatabase.GetAssetPath(scriptableObject);
            if (string.IsNullOrEmpty(assetPath))
            {
                EditorUtility.DisplayDialog(
                    "SpreadAsset Document",
                    "The selected asset must be saved in the project first.",
                    "OK");
                return;
            }

            if (!TryResolveSchemaForAsset(scriptableObject, assetPath, out SpreadAssetDocumentSchema schema, out string error))
            {
                EditorUtility.DisplayDialog(
                    "SpreadAsset Document",
                    string.IsNullOrEmpty(error)
                        ? "Could not create a SpreadAsset schema for the selected asset."
                        : error,
                    "OK");
                return;
            }

            string documentPath = ToAssetPath(Path.ChangeExtension(assetPath, SpreadAssetDocumentIO.Extension));
            if (DocumentExists(documentPath)
                && !EditorUtility.DisplayDialog(
                    "Overwrite SpreadAsset document?",
                    $"A document already exists at {documentPath}. Do you want to overwrite it from the selected .asset?",
                    "Overwrite",
                    "Cancel"))
            {
                return;
            }

            SpreadAssetDocument document = SpreadAssetDocumentIO.CreateDocument(
                scriptableObject,
                assetPath,
                schema);
            SpreadAssetDocumentIO.Write(documentPath, document);

            AssetDatabase.ImportAsset(documentPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            UnityEngine.Object documentAsset = AssetDatabase.LoadMainAssetAtPath(documentPath);
            Selection.activeObject = documentAsset;
            EditorGUIUtility.PingObject(documentAsset);
            SpreadAssetWindow.OpenDocument(documentPath);
        }

        private static string GetSelectedDirectory()
        {
            string selectedPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrEmpty(selectedPath))
            {
                return "Assets";
            }

            if (Directory.Exists(selectedPath))
            {
                return selectedPath;
            }

            return ToAssetPath(Path.GetDirectoryName(selectedPath) ?? "Assets");
        }

        private static string ToAssetPath(string path)
        {
            return path.Replace('\\', '/');
        }

        private static bool TryResolveSchemaForAsset(
            ScriptableObject sourceAsset,
            string assetPath,
            out SpreadAssetDocumentSchema schema,
            out string error)
        {
            schema = null;
            error = string.Empty;

            if (SpreadAssetGeneratedClassLoader.TryLoadFromAsset(
                    sourceAsset,
                    out SpreadAssetGenerationRequest request,
                    out _,
                    out _)
                && request?.Schema != null)
            {
                SpreadAssetSchemaUtility.EnsureFieldIds(request.Schema);
                schema = request.Schema;
                return true;
            }

            schema = CreateSchemaFromAssetType(sourceAsset.GetType(), assetPath);
            SpreadAssetSchemaUtility.EnsureFieldIds(schema);
            return TryCreateFactoryForExistingAsset(sourceAsset, assetPath, schema, out error);
        }

        private static SpreadAssetDocumentSchema CreateSchemaFromAssetType(Type assetType, string assetPath)
        {
            List<SpreadAssetSchemaField> fields = new List<SpreadAssetSchemaField>();
            List<SpreadAssetSchemaTable> tables = new List<SpreadAssetSchemaTable>();

            foreach (FieldInfo field in GetSerializableFields(assetType))
            {
                if (TryGetCollectionElementType(field.FieldType, out Type elementType)
                    && TryCreateSchemaTable(field, elementType, out SpreadAssetSchemaTable table))
                {
                    tables.Add(table);
                    continue;
                }

                fields.Add(CreateSchemaField(field));
            }

            string assetName = Path.GetFileNameWithoutExtension(assetPath);
            return new SpreadAssetDocumentSchema
            {
                AssetClassName = assetType.Name,
                NamespaceName = assetType.Namespace ?? string.Empty,
                MenuPath = "SpreadAsset/" + SpreadAssetNameUtility.ToSafeFileName(assetName),
                Fields = fields.ToArray(),
                Tables = tables.ToArray()
            };
        }

        private static bool TryCreateSchemaTable(
            FieldInfo field,
            Type elementType,
            out SpreadAssetSchemaTable table)
        {
            table = null;
            if (elementType == null || elementType == typeof(string))
            {
                return false;
            }

            List<SpreadAssetSchemaField> fields = new List<SpreadAssetSchemaField>();
            foreach (FieldInfo elementField in GetSerializableFields(elementType))
            {
                fields.Add(CreateSchemaField(elementField));
            }

            if (fields.Count == 0)
            {
                return false;
            }

            table = new SpreadAssetSchemaTable
            {
                RowTypeName = GetTypeName(elementType),
                FieldName = SpreadAssetNameUtility.ToPascalCase(field.Name.TrimStart('_')),
                Fields = fields.ToArray()
            };
            return true;
        }

        private static SpreadAssetSchemaField CreateSchemaField(FieldInfo field)
        {
            return new SpreadAssetSchemaField
            {
                TypeName = GetTypeName(field.FieldType),
                Name = SpreadAssetNameUtility.ToPascalCase(field.Name.TrimStart('_'))
            };
        }

        private static bool TryCreateFactoryForExistingAsset(
            ScriptableObject sourceAsset,
            string assetPath,
            SpreadAssetDocumentSchema schema,
            out string error)
        {
            error = string.Empty;

            MonoScript runtimeScript = MonoScript.FromScriptableObject(sourceAsset);
            string runtimeScriptPath = AssetDatabase.GetAssetPath(runtimeScript);
            if (string.IsNullOrEmpty(runtimeScriptPath))
            {
                error = "Could not find the runtime script for the selected asset.";
                return false;
            }

            string runtimeDirectory = Path.GetDirectoryName(runtimeScriptPath)?.Replace('\\', '/') ?? "Assets";
            string editorDirectory = ToAssetPath(Path.Combine(runtimeDirectory, "Editor"));
            string factoryPath = ToAssetPath(Path.Combine(editorDirectory, sourceAsset.GetType().Name + "Factory.cs"));
            if (File.Exists(factoryPath)
                && !EditorUtility.DisplayDialog(
                    "Overwrite generated factory?",
                    $"A generated factory already exists at {factoryPath}. Do you want to overwrite it from the selected .asset type?",
                    "Overwrite",
                    "Cancel"))
            {
                error = "Factory generation was canceled.";
                return false;
            }

            Directory.CreateDirectory(editorDirectory);
            File.WriteAllText(factoryPath, GenerateFactoryCode(sourceAsset.GetType(), Path.GetFileNameWithoutExtension(assetPath), schema));
            AssetDatabase.ImportAsset(factoryPath);
            return true;
        }

        private static string GenerateFactoryCode(Type assetType, string defaultDocumentName, SpreadAssetDocumentSchema schema)
        {
            string assetNamespace = assetType.Namespace ?? string.Empty;
            string factoryNamespace = string.IsNullOrEmpty(assetNamespace) ? string.Empty : assetNamespace + ".Editor";
            string indentation = string.IsNullOrEmpty(factoryNamespace) ? string.Empty : "    ";
            string childIndentation = indentation + "    ";

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("using SpreadAsset.Editor;");
            if (!string.IsNullOrEmpty(assetNamespace))
            {
                builder.Append("using ").Append(assetNamespace).AppendLine(";");
            }

            builder.AppendLine("using UnityEditor;");
            builder.AppendLine();

            if (!string.IsNullOrEmpty(factoryNamespace))
            {
                builder.Append("namespace ").Append(factoryNamespace).AppendLine();
                builder.AppendLine("{");
            }

            builder.Append(indentation).Append("internal static class ").Append(assetType.Name).AppendLine("Factory");
            builder.Append(indentation).AppendLine("{");
            builder.Append(childIndentation).Append("[MenuItem(\"Assets/Create/")
                .Append(EscapeString(NormalizeCreateMenuPath(schema.MenuPath)))
                .AppendLine("\")]");
            builder.Append(childIndentation).AppendLine("private static void Create()");
            builder.Append(childIndentation).AppendLine("{");
            builder.Append(childIndentation).AppendLine("    SpreadAssetDocumentSchema schema = new SpreadAssetDocumentSchema");
            builder.Append(childIndentation).AppendLine("    {");
            builder.Append(childIndentation).Append("        AssetClassName = \"").Append(EscapeString(schema.AssetClassName)).AppendLine("\",");
            builder.Append(childIndentation).Append("        NamespaceName = \"").Append(EscapeString(schema.NamespaceName)).AppendLine("\",");
            builder.Append(childIndentation).Append("        MenuPath = \"").Append(EscapeString(schema.MenuPath)).AppendLine("\",");
            builder.Append(childIndentation).Append("        Fields = ").Append(GenerateSchemaFields(schema.Fields, childIndentation.Length + 8)).AppendLine(",");
            builder.Append(childIndentation).Append("        Tables = ").Append(GenerateSchemaTables(schema.Tables, childIndentation.Length + 8)).AppendLine();
            builder.Append(childIndentation).AppendLine("    };");
            builder.AppendLine();
            builder.Append(childIndentation).Append("    SpreadAssetFactory.CreatePair<")
                .Append(assetType.Name)
                .Append(">(\"")
                .Append(EscapeString(defaultDocumentName))
                .AppendLine("\", schema);");
            builder.Append(childIndentation).AppendLine("}");
            builder.Append(indentation).AppendLine("}");

            if (!string.IsNullOrEmpty(factoryNamespace))
            {
                builder.AppendLine("}");
            }

            return builder.ToString();
        }

        private static string GenerateSchemaFields(SpreadAssetSchemaField[] fields, int indent)
        {
            if (fields == null || fields.Length == 0)
            {
                return "new SpreadAssetSchemaField[0]";
            }

            string indentation = new string(' ', indent);
            string childIndentation = new string(' ', indent + 4);
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("new SpreadAssetSchemaField[]");
            builder.Append(indentation).AppendLine("{");
            foreach (SpreadAssetSchemaField field in fields)
            {
                builder.Append(childIndentation)
                    .Append("new SpreadAssetSchemaField { Id = \"")
                    .Append(EscapeString(field.Id))
                    .Append("\", TypeName = \"")
                    .Append(EscapeString(field.TypeName))
                    .Append("\", Name = \"")
                    .Append(EscapeString(field.Name))
                    .Append("\"");
                if (field.IsDesignField)
                {
                    builder.Append(", IsDesignField = true");
                }

                if (field.IsKeyField)
                {
                    builder.Append(", IsKeyField = true");
                }

                builder.AppendLine(" },");
            }

            builder.Append(indentation).Append("}");
            return builder.ToString();
        }

        private static string GenerateSchemaTables(SpreadAssetSchemaTable[] tables, int indent)
        {
            if (tables == null || tables.Length == 0)
            {
                return "new SpreadAssetSchemaTable[0]";
            }

            string indentation = new string(' ', indent);
            string childIndentation = new string(' ', indent + 4);
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("new SpreadAssetSchemaTable[]");
            builder.Append(indentation).AppendLine("{");
            foreach (SpreadAssetSchemaTable table in tables)
            {
                builder.Append(childIndentation).AppendLine("new SpreadAssetSchemaTable");
                builder.Append(childIndentation).AppendLine("{");
                builder.Append(childIndentation).Append("    RowTypeName = \"").Append(EscapeString(table.RowTypeName)).AppendLine("\",");
                builder.Append(childIndentation).Append("    FieldName = \"").Append(EscapeString(table.FieldName)).AppendLine("\",");
                if (table.OmitArrayField)
                {
                    builder.Append(childIndentation).AppendLine("    OmitArrayField = true,");
                }

                builder.Append(childIndentation).Append("    Fields = ").Append(GenerateSchemaFields(table.Fields, indent + 8)).AppendLine();
                builder.Append(childIndentation).AppendLine("},");
            }

            builder.Append(indentation).Append("}");
            return builder.ToString();
        }

        private static IEnumerable<FieldInfo> GetSerializableFields(Type type)
        {
            for (Type currentType = type;
                currentType != null && currentType != typeof(ScriptableObject) && currentType != typeof(object);
                currentType = currentType.BaseType)
            {
                FieldInfo[] fields = currentType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                foreach (FieldInfo field in fields)
                {
                    if (field.IsStatic || field.IsNotSerialized)
                    {
                        continue;
                    }

                    bool isSerializable = field.IsPublic || Attribute.IsDefined(field, typeof(SerializeField));
                    bool isHidden = Attribute.IsDefined(field, typeof(HideInInspector));
                    if (isSerializable && !isHidden)
                    {
                        yield return field;
                    }
                }
            }
        }

        private static bool TryGetCollectionElementType(Type type, out Type elementType)
        {
            elementType = null;
            if (type == null || type == typeof(string))
            {
                return false;
            }

            if (type.IsArray)
            {
                elementType = type.GetElementType();
                return elementType != null;
            }

            if (type.IsGenericType && type.GetGenericArguments().Length == 1)
            {
                Type genericTypeDefinition = type.GetGenericTypeDefinition();
                if (genericTypeDefinition == typeof(List<>))
                {
                    elementType = type.GetGenericArguments()[0];
                    return true;
                }
            }

            return false;
        }

        private static string GetTypeName(Type type)
        {
            if (type == typeof(string)) return "string";
            if (type == typeof(int)) return "int";
            if (type == typeof(float)) return "float";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(long)) return "long";
            if (type == typeof(double)) return "double";
            if (type == typeof(short)) return "short";
            if (type == typeof(byte)) return "byte";
            if (type == typeof(uint)) return "uint";
            if (type == typeof(ulong)) return "ulong";
            if (type == typeof(ushort)) return "ushort";
            if (type == typeof(sbyte)) return "sbyte";
            if (type == typeof(Vector2)) return "Vector2";
            if (type == typeof(Vector3)) return "Vector3";
            if (type == typeof(Vector4)) return "Vector4";
            if (type == typeof(Vector2Int)) return "Vector2Int";
            if (type == typeof(Vector3Int)) return "Vector3Int";
            if (type == typeof(Color)) return "Color";
            if (type == typeof(Rect)) return "Rect";
            if (type == typeof(Bounds)) return "Bounds";
            if (type == typeof(RectInt)) return "RectInt";
            if (type == typeof(BoundsInt)) return "BoundsInt";
            return type.Name;
        }

        private static string NormalizeCreateMenuPath(string menuPath)
        {
            return (menuPath ?? string.Empty).Trim().Trim('/');
        }

        private static string EscapeString(string value)
        {
            return value?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? string.Empty;
        }

        private static bool DocumentExists(string assetPath)
        {
            return AssetDatabase.LoadMainAssetAtPath(assetPath) != null
                || File.Exists(Path.GetFullPath(assetPath));
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;

namespace SpreadAsset.Editor
{
    public static class SpreadAssetCodeGenerator
    {
        public const string DefaultOutputDirectory = "Assets/SpreadAsset/Generated";
        public const string DefaultNamespace = "SpreadAsset.Generated";

        public static void Generate(SpreadAssetGenerationRequest request)
        {
            ValidateRequest(request);
            SpreadAssetSchemaUtility.EnsureFieldIds(request.Schema);

            string runtimeDirectory = request.OutputDirectory;
            string editorDirectory = Path.Combine(runtimeDirectory, "Editor").Replace('\\', '/');
            Directory.CreateDirectory(runtimeDirectory);
            Directory.CreateDirectory(editorDirectory);

            string runtimePath = Path.Combine(runtimeDirectory, request.AssetClassName + ".cs").Replace('\\', '/');
            string editorPath = Path.Combine(editorDirectory, request.AssetClassName + "Factory.cs").Replace('\\', '/');

            WriteOrConfirmOverwrite(runtimePath, GenerateRuntimeCode(request));
            WriteOrConfirmOverwrite(editorPath, GenerateEditorFactoryCode(request));

            AssetDatabase.Refresh();
            UnityEngine.Object generatedScript = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(runtimePath);
            if (generatedScript != null)
            {
                Selection.activeObject = generatedScript;
                EditorGUIUtility.PingObject(generatedScript);
            }
        }

        private static string GenerateRuntimeCode(SpreadAssetGenerationRequest request)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("using SpreadAsset;");
            if (UsesGenericListType(request.Schema))
            {
                builder.AppendLine("using System.Collections.Generic;");
            }

            builder.AppendLine("using UnityEngine;");
            builder.AppendLine();
            builder.AppendLine($"namespace {request.NamespaceName}");
            builder.AppendLine("{");

            foreach (SpreadAssetSchemaTable table in request.Schema.Tables)
            {
                AppendDataClass(builder, table);
                builder.AppendLine();
            }

            builder.AppendLine($"    public sealed class {request.AssetClassName} : SpreadAssetObject");
            builder.AppendLine("    {");
            builder.AppendLine("        // Paired asset creation is handled by the generated Editor factory.");

            foreach (SpreadAssetSchemaField field in request.Schema.Fields)
            {
                AppendSerializedField(builder, field.TypeName, field.Name, null);
            }

            foreach (SpreadAssetSchemaTable table in request.Schema.Tables)
            {
                if (ShouldGenerateArrayField(table))
                {
                    AppendSerializedField(builder, table.RowTypeName + "[]", table.FieldName, $"new {table.RowTypeName}[0]");
                }
            }

            foreach (SpreadAssetSchemaTable table in request.Schema.Tables)
            {
                if (!ShouldGenerateArrayField(table))
                {
                    continue;
                }

                foreach (SpreadAssetSchemaField keyField in GetKeyFields(table))
                {
                    AppendKeyLookupCacheField(builder, table, keyField);
                }
            }

            if (request.Schema.Fields.Length > 0 || HasGeneratedArrayField(request.Schema.Tables))
            {
                builder.AppendLine();
            }

            foreach (SpreadAssetSchemaField field in request.Schema.Fields)
            {
                AppendGetter(builder, field.TypeName, field.Name);
            }

            foreach (SpreadAssetSchemaTable table in request.Schema.Tables)
            {
                if (ShouldGenerateArrayField(table))
                {
                    AppendGetter(builder, table.RowTypeName + "[]", table.FieldName);
                }
            }

            foreach (SpreadAssetSchemaTable table in request.Schema.Tables)
            {
                if (!ShouldGenerateArrayField(table))
                {
                    continue;
                }

                foreach (SpreadAssetSchemaField keyField in GetKeyFields(table))
                {
                    builder.AppendLine();
                    AppendKeyLookupMethods(builder, table, keyField);
                }
            }

            if (HasAnyKeyField(request.Schema.Tables))
            {
                builder.AppendLine();
                AppendClearLookupCaches(builder, request.Schema.Tables);
            }

            builder.AppendLine("    }");
            builder.AppendLine("}");
            return builder.ToString();
        }

        private static string GenerateEditorFactoryCode(SpreadAssetGenerationRequest request)
        {
            string menuPath = NormalizeCreateMenuPath(request.MenuPath);
            string defaultDocumentName = SpreadAssetNameUtility.ToDefaultDocumentName(request.MenuPath, request.AssetClassName);

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("using SpreadAsset.Editor;");
            builder.AppendLine($"using {request.NamespaceName};");
            builder.AppendLine("using UnityEditor;");
            builder.AppendLine();
            builder.AppendLine($"{WrapNamespace(request.NamespaceName)}.Editor");
            builder.AppendLine("{");
            builder.AppendLine($"    internal static class {request.AssetClassName}Factory");
            builder.AppendLine("    {");
            builder.AppendLine($"        [MenuItem(\"{EscapeString(menuPath)}\")]");
            builder.AppendLine("        private static void Create()");
            builder.AppendLine("        {");
            builder.AppendLine("            SpreadAssetDocumentSchema schema = new SpreadAssetDocumentSchema");
            builder.AppendLine("            {");
            builder.AppendLine($"                AssetClassName = \"{EscapeString(request.AssetClassName)}\",");
            builder.AppendLine($"                NamespaceName = \"{EscapeString(request.NamespaceName)}\",");
            builder.AppendLine($"                MenuPath = \"{EscapeString(request.MenuPath)}\",");
            builder.AppendLine($"                Fields = {GenerateSchemaFields(request.Schema.Fields, 16)},");
            builder.AppendLine($"                Tables = {GenerateSchemaTables(request.Schema.Tables, 16)}");
            builder.AppendLine("            };");
            builder.AppendLine();
            builder.AppendLine($"            SpreadAssetFactory.CreatePair<{request.AssetClassName}>(\"{EscapeString(defaultDocumentName)}\", schema);");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
            builder.AppendLine("}");
            return builder.ToString();
        }

        private static void AppendDataClass(StringBuilder builder, SpreadAssetSchemaTable table)
        {
            builder.AppendLine("    [System.Serializable]");
            builder.AppendLine($"    public sealed class {table.RowTypeName}");
            builder.AppendLine("    {");

            foreach (SpreadAssetSchemaField field in table.Fields)
            {
                if (field.IsDesignField)
                {
                    continue;
                }

                AppendSerializedField(builder, field.TypeName, field.Name, null);
            }

            if (HasRuntimeFields(table.Fields))
            {
                builder.AppendLine();
            }

            foreach (SpreadAssetSchemaField field in table.Fields)
            {
                if (field.IsDesignField)
                {
                    continue;
                }

                AppendGetter(builder, field.TypeName, field.Name);
            }

            builder.AppendLine("    }");
        }

        private static void AppendSerializedField(StringBuilder builder, string typeName, string fieldName, string initializer)
        {
            string serializedFieldName = SpreadAssetNameUtility.ToSerializedFieldName(fieldName);
            string initializerCode = string.IsNullOrWhiteSpace(initializer) ? string.Empty : " = " + initializer;
            builder.AppendLine($"        [SerializeField] private {typeName} {serializedFieldName}{initializerCode};");
        }

        private static void AppendGetter(StringBuilder builder, string typeName, string fieldName)
        {
            string propertyName = SpreadAssetNameUtility.ToPascalCase(fieldName);
            string serializedFieldName = SpreadAssetNameUtility.ToSerializedFieldName(fieldName);
            builder.AppendLine($"        public {typeName} {propertyName} => {serializedFieldName};");
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

        private static void ValidateRequest(SpreadAssetGenerationRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            ValidateIdentifier(request.AssetClassName, "Asset class name");
            ValidateNamespace(request.NamespaceName);

            if (string.IsNullOrWhiteSpace(request.MenuPath))
            {
                throw new ArgumentException("Create menu path is required.");
            }

            foreach (SpreadAssetSchemaField field in request.Schema.Fields)
            {
                ValidateTypeName(field.TypeName, field.Name);
                ValidateIdentifier(SpreadAssetNameUtility.ToPascalCase(field.Name), "Field name");
            }

            foreach (SpreadAssetSchemaTable table in request.Schema.Tables)
            {
                ValidateIdentifier(table.RowTypeName, "Data class name");
                if (!table.OmitArrayField || !string.IsNullOrWhiteSpace(table.FieldName))
                {
                    ValidateIdentifier(SpreadAssetNameUtility.ToPascalCase(table.FieldName), "Array field name");
                }

                foreach (SpreadAssetSchemaField field in table.Fields)
                {
                    ValidateTypeName(field.TypeName, field.Name);
                    ValidateDataFieldType(field.TypeName, field.Name, table, request.Schema.Tables);
                    ValidateIdentifier(SpreadAssetNameUtility.ToPascalCase(field.Name), "Data field name");
                    if (field.IsDesignField && field.IsKeyField)
                    {
                        throw new ArgumentException($"Data field {field.Name} cannot be both a key and a design field.");
                    }
                }
            }
        }

        private static void ValidateDataFieldType(
            string typeName,
            string fieldName,
            SpreadAssetSchemaTable ownerTable,
            SpreadAssetSchemaTable[] tables)
        {
            if (SpreadAssetFieldTypeUtility.IsSupportedDataFieldType(typeName, ownerTable, tables))
            {
                return;
            }

            throw new ArgumentException(SpreadAssetFieldTypeUtility.GetUnsupportedDataFieldTypeMessage(fieldName, typeName));
        }

        private static bool HasRuntimeFields(SpreadAssetSchemaField[] fields)
        {
            if (fields == null)
            {
                return false;
            }

            foreach (SpreadAssetSchemaField field in fields)
            {
                if (field != null && !field.IsDesignField)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool UsesGenericListType(SpreadAssetDocumentSchema schema)
        {
            foreach (SpreadAssetSchemaField field in schema.Fields ?? Array.Empty<SpreadAssetSchemaField>())
            {
                if (field != null && SpreadAssetFieldTypeUtility.IsGenericListTypeName(field.TypeName))
                {
                    return true;
                }
            }

            foreach (SpreadAssetSchemaTable table in schema.Tables ?? Array.Empty<SpreadAssetSchemaTable>())
            {
                foreach (SpreadAssetSchemaField field in table?.Fields ?? Array.Empty<SpreadAssetSchemaField>())
                {
                    if (field != null && SpreadAssetFieldTypeUtility.IsGenericListTypeName(field.TypeName))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool ShouldGenerateArrayField(SpreadAssetSchemaTable table)
        {
            return table != null && !table.OmitArrayField;
        }

        private static bool HasGeneratedArrayField(SpreadAssetSchemaTable[] tables)
        {
            if (tables == null)
            {
                return false;
            }

            foreach (SpreadAssetSchemaTable table in tables)
            {
                if (ShouldGenerateArrayField(table))
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<SpreadAssetSchemaField> GetKeyFields(SpreadAssetSchemaTable table)
        {
            if (table?.Fields == null)
            {
                yield break;
            }

            foreach (SpreadAssetSchemaField field in table.Fields)
            {
                if (field != null && field.IsKeyField && !field.IsDesignField)
                {
                    yield return field;
                }
            }
        }

        private static bool HasAnyKeyField(SpreadAssetSchemaTable[] tables)
        {
            if (tables == null)
            {
                return false;
            }

            foreach (SpreadAssetSchemaTable table in tables)
            {
                if (!ShouldGenerateArrayField(table))
                {
                    continue;
                }

                foreach (SpreadAssetSchemaField _ in GetKeyFields(table))
                {
                    return true;
                }
            }

            return false;
        }

        private static void AppendKeyLookupCacheField(
            StringBuilder builder,
            SpreadAssetSchemaTable table,
            SpreadAssetSchemaField keyField)
        {
            builder.AppendLine($"        [System.NonSerialized] private System.Collections.Generic.Dictionary<{keyField.TypeName}, {table.RowTypeName}> {GetKeyLookupFieldName(table, keyField)};");
        }

        private static void AppendKeyLookupMethods(
            StringBuilder builder,
            SpreadAssetSchemaTable table,
            SpreadAssetSchemaField keyField)
        {
            string lookupFieldName = GetKeyLookupFieldName(table, keyField);
            string lookupMethodName = GetKeyLookupMethodName(table, keyField);
            string rowVariableName = "row";
            string arrayFieldName = SpreadAssetNameUtility.ToSerializedFieldName(table.FieldName);
            string keyPropertyName = SpreadAssetNameUtility.ToPascalCase(keyField.Name);
            string tryGetMethodName = GetTryGetByKeyMethodName(table, keyField);
            string getMethodName = GetGetByKeyMethodName(table, keyField);

            builder.AppendLine($"        public bool {tryGetMethodName}({keyField.TypeName} key, out {table.RowTypeName} value)");
            builder.AppendLine("        {");
            if (IsNullKeyCheckRequired(keyField.TypeName))
            {
                builder.AppendLine("            if (key == null)");
                builder.AppendLine("            {");
                builder.AppendLine("                value = default;");
                builder.AppendLine("                return false;");
                builder.AppendLine("            }");
                builder.AppendLine();
            }

            builder.AppendLine($"            return {lookupMethodName}().TryGetValue(key, out value);");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine($"        public {table.RowTypeName} {getMethodName}({keyField.TypeName} key)");
            builder.AppendLine("        {");
            builder.AppendLine($"            {tryGetMethodName}(key, out {table.RowTypeName} value);");
            builder.AppendLine("            return value;");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine($"        private System.Collections.Generic.Dictionary<{keyField.TypeName}, {table.RowTypeName}> {lookupMethodName}()");
            builder.AppendLine("        {");
            builder.AppendLine($"            if ({lookupFieldName} != null)");
            builder.AppendLine("            {");
            builder.AppendLine($"                return {lookupFieldName};");
            builder.AppendLine("            }");
            builder.AppendLine();
            builder.AppendLine($"            {lookupFieldName} = new System.Collections.Generic.Dictionary<{keyField.TypeName}, {table.RowTypeName}>();");
            builder.AppendLine($"            if ({arrayFieldName} == null)");
            builder.AppendLine("            {");
            builder.AppendLine($"                return {lookupFieldName};");
            builder.AppendLine("            }");
            builder.AppendLine();
            builder.AppendLine($"            foreach ({table.RowTypeName} {rowVariableName} in {arrayFieldName})");
            builder.AppendLine("            {");
            builder.AppendLine($"                if ({rowVariableName} == null)");
            builder.AppendLine("                {");
            builder.AppendLine("                    continue;");
            builder.AppendLine("                }");

            if (IsNullKeyCheckRequired(keyField.TypeName))
            {
                builder.AppendLine();
                builder.AppendLine($"                {keyField.TypeName} rowKey = {rowVariableName}.{keyPropertyName};");
                builder.AppendLine("                if (rowKey == null)");
                builder.AppendLine("                {");
                builder.AppendLine("                    continue;");
                builder.AppendLine("                }");
                builder.AppendLine();
                builder.AppendLine($"                {lookupFieldName}[rowKey] = {rowVariableName};");
            }
            else
            {
                builder.AppendLine($"                {lookupFieldName}[{rowVariableName}.{keyPropertyName}] = {rowVariableName};");
            }

            builder.AppendLine("            }");
            builder.AppendLine();
            builder.AppendLine($"            return {lookupFieldName};");
            builder.AppendLine("        }");
        }

        private static void AppendClearLookupCaches(
            StringBuilder builder,
            SpreadAssetSchemaTable[] tables)
        {
            builder.AppendLine("        public void ClearLookupCaches()");
            builder.AppendLine("        {");
            foreach (SpreadAssetSchemaTable table in tables)
            {
                if (!ShouldGenerateArrayField(table))
                {
                    continue;
                }

                foreach (SpreadAssetSchemaField keyField in GetKeyFields(table))
                {
                    builder.AppendLine($"            {GetKeyLookupFieldName(table, keyField)} = null;");
                }
            }

            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine("        private void OnEnable()");
            builder.AppendLine("        {");
            builder.AppendLine("            ClearLookupCaches();");
            builder.AppendLine("        }");
        }

        private static string GetKeyLookupFieldName(SpreadAssetSchemaTable table, SpreadAssetSchemaField keyField)
        {
            return SpreadAssetNameUtility.ToSerializedFieldName(table.FieldName + "By" + keyField.Name);
        }

        private static string GetKeyLookupMethodName(SpreadAssetSchemaTable table, SpreadAssetSchemaField keyField)
        {
            return "Get" + SpreadAssetNameUtility.ToPascalCase(table.FieldName) + "By" + SpreadAssetNameUtility.ToPascalCase(keyField.Name) + "Lookup";
        }

        private static string GetTryGetByKeyMethodName(SpreadAssetSchemaTable table, SpreadAssetSchemaField keyField)
        {
            return "TryGet" + SpreadAssetNameUtility.ToPascalCase(table.RowTypeName) + "By" + SpreadAssetNameUtility.ToPascalCase(keyField.Name);
        }

        private static string GetGetByKeyMethodName(SpreadAssetSchemaTable table, SpreadAssetSchemaField keyField)
        {
            return "Get" + SpreadAssetNameUtility.ToPascalCase(table.RowTypeName) + "By" + SpreadAssetNameUtility.ToPascalCase(keyField.Name);
        }

        private static bool IsNullKeyCheckRequired(string typeName)
        {
            string normalizedTypeName = (typeName ?? string.Empty).Trim();
            return string.Equals(normalizedTypeName, "string", StringComparison.Ordinal)
                || string.Equals(normalizedTypeName, "System.String", StringComparison.Ordinal);
        }

        private static void ValidateIdentifier(string identifier, string label)
        {
            if (!SpreadAssetNameUtility.IsValidIdentifier(identifier))
            {
                throw new ArgumentException($"{label} is not a valid C# identifier: {identifier}");
            }
        }

        private static void ValidateNamespace(string namespaceName)
        {
            string[] parts = namespaceName.Split('.');
            foreach (string part in parts)
            {
                ValidateIdentifier(part, "Namespace");
            }
        }

        private static void ValidateTypeName(string typeName, string label)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                throw new ArgumentException($"Type is required for {label}.");
            }
        }

        private static void WriteOrConfirmOverwrite(string path, string contents)
        {
            if (File.Exists(path))
            {
                bool overwrite = EditorUtility.DisplayDialog(
                    "Overwrite generated file?",
                    $"A file already exists at {path}. Do you want to overwrite it?",
                    "Overwrite",
                    "Cancel");

                if (!overwrite)
                {
                    throw new OperationCanceledException("Generation canceled.");
                }
            }

            File.WriteAllText(path, contents);
        }

        private static string NormalizeCreateMenuPath(string menuPath)
        {
            string normalized = menuPath.Trim().Trim('/');
            const string createPrefix = "Assets/Create/";
            if (normalized.StartsWith(createPrefix, StringComparison.Ordinal))
            {
                return normalized;
            }

            return createPrefix + normalized;
        }

        private static string WrapNamespace(string namespaceName)
        {
            return "namespace " + namespaceName;
        }

        private static string EscapeString(string value)
        {
            return value?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? string.Empty;
        }
    }

    public sealed class SpreadAssetGenerationRequest
    {
        public string AssetClassName = "ItemDataAsset";
        public string NamespaceName = SpreadAssetCodeGenerator.DefaultNamespace;
        public string MenuPath = "SpreadAsset/game_data";
        public string OutputDirectory = SpreadAssetCodeGenerator.DefaultOutputDirectory;
        public SpreadAssetDocumentSchema Schema = new SpreadAssetDocumentSchema();
    }
}

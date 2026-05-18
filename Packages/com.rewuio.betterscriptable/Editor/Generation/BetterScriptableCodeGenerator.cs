using System;
using System.IO;
using System.Text;
using UnityEditor;

namespace BetterScriptable.Editor
{
    public static class BetterScriptableCodeGenerator
    {
        public const string DefaultOutputDirectory = "Assets/BetterScriptable/Generated";
        public const string DefaultNamespace = "BetterScriptable.Generated";

        public static void Generate(BetterScriptableGenerationRequest request)
        {
            ValidateRequest(request);

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

        private static string GenerateRuntimeCode(BetterScriptableGenerationRequest request)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("using BetterScriptable;");
            builder.AppendLine("using UnityEngine;");
            builder.AppendLine();
            builder.AppendLine($"namespace {request.NamespaceName}");
            builder.AppendLine("{");

            foreach (BetterScriptableSchemaTable table in request.Schema.Tables)
            {
                AppendDataClass(builder, table);
                builder.AppendLine();
            }

            builder.AppendLine($"    public sealed class {request.AssetClassName} : BetterScriptableAsset");
            builder.AppendLine("    {");
            builder.AppendLine("        // Paired asset creation is handled by the generated Editor factory.");

            foreach (BetterScriptableSchemaField field in request.Schema.Fields)
            {
                AppendSerializedField(builder, field.TypeName, field.Name, null);
            }

            foreach (BetterScriptableSchemaTable table in request.Schema.Tables)
            {
                AppendSerializedField(builder, table.RowTypeName + "[]", table.FieldName, $"new {table.RowTypeName}[0]");
            }

            if (request.Schema.Fields.Length > 0 || request.Schema.Tables.Length > 0)
            {
                builder.AppendLine();
            }

            foreach (BetterScriptableSchemaField field in request.Schema.Fields)
            {
                AppendGetter(builder, field.TypeName, field.Name);
            }

            foreach (BetterScriptableSchemaTable table in request.Schema.Tables)
            {
                AppendGetter(builder, table.RowTypeName + "[]", table.FieldName);
            }

            builder.AppendLine("    }");
            builder.AppendLine("}");
            return builder.ToString();
        }

        private static string GenerateEditorFactoryCode(BetterScriptableGenerationRequest request)
        {
            string menuPath = NormalizeCreateMenuPath(request.MenuPath);
            string defaultDocumentName = BetterScriptableNameUtility.ToDefaultDocumentName(request.MenuPath, request.AssetClassName);

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("using BetterScriptable.Editor;");
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
            builder.AppendLine("            BetterScriptableDocumentSchema schema = new BetterScriptableDocumentSchema");
            builder.AppendLine("            {");
            builder.AppendLine($"                AssetClassName = \"{EscapeString(request.AssetClassName)}\",");
            builder.AppendLine($"                NamespaceName = \"{EscapeString(request.NamespaceName)}\",");
            builder.AppendLine($"                MenuPath = \"{EscapeString(request.MenuPath)}\",");
            builder.AppendLine($"                Fields = {GenerateSchemaFields(request.Schema.Fields, 16)},");
            builder.AppendLine($"                Tables = {GenerateSchemaTables(request.Schema.Tables, 16)}");
            builder.AppendLine("            };");
            builder.AppendLine();
            builder.AppendLine($"            BetterScriptableAssetFactory.CreatePair<{request.AssetClassName}>(\"{EscapeString(defaultDocumentName)}\", schema);");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
            builder.AppendLine("}");
            return builder.ToString();
        }

        private static void AppendDataClass(StringBuilder builder, BetterScriptableSchemaTable table)
        {
            builder.AppendLine("    [System.Serializable]");
            builder.AppendLine($"    public sealed class {table.RowTypeName}");
            builder.AppendLine("    {");

            foreach (BetterScriptableSchemaField field in table.Fields)
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

            foreach (BetterScriptableSchemaField field in table.Fields)
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
            string serializedFieldName = BetterScriptableNameUtility.ToSerializedFieldName(fieldName);
            string initializerCode = string.IsNullOrWhiteSpace(initializer) ? string.Empty : " = " + initializer;
            builder.AppendLine($"        [SerializeField] private {typeName} {serializedFieldName}{initializerCode};");
        }

        private static void AppendGetter(StringBuilder builder, string typeName, string fieldName)
        {
            string propertyName = BetterScriptableNameUtility.ToPascalCase(fieldName);
            string serializedFieldName = BetterScriptableNameUtility.ToSerializedFieldName(fieldName);
            builder.AppendLine($"        public {typeName} {propertyName} => {serializedFieldName};");
        }

        private static string GenerateSchemaFields(BetterScriptableSchemaField[] fields, int indent)
        {
            if (fields == null || fields.Length == 0)
            {
                return "new BetterScriptableSchemaField[0]";
            }

            string indentation = new string(' ', indent);
            string childIndentation = new string(' ', indent + 4);
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("new BetterScriptableSchemaField[]");
            builder.Append(indentation).AppendLine("{");
            foreach (BetterScriptableSchemaField field in fields)
            {
                builder.Append(childIndentation)
                    .Append("new BetterScriptableSchemaField { TypeName = \"")
                    .Append(EscapeString(field.TypeName))
                    .Append("\", Name = \"")
                    .Append(EscapeString(field.Name))
                    .Append("\"");
                if (field.IsDesignField)
                {
                    builder.Append(", IsDesignField = true");
                }

                builder.AppendLine(" },");
            }

            builder.Append(indentation).Append("}");
            return builder.ToString();
        }

        private static string GenerateSchemaTables(BetterScriptableSchemaTable[] tables, int indent)
        {
            if (tables == null || tables.Length == 0)
            {
                return "new BetterScriptableSchemaTable[0]";
            }

            string indentation = new string(' ', indent);
            string childIndentation = new string(' ', indent + 4);
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("new BetterScriptableSchemaTable[]");
            builder.Append(indentation).AppendLine("{");
            foreach (BetterScriptableSchemaTable table in tables)
            {
                builder.Append(childIndentation).AppendLine("new BetterScriptableSchemaTable");
                builder.Append(childIndentation).AppendLine("{");
                builder.Append(childIndentation).Append("    RowTypeName = \"").Append(EscapeString(table.RowTypeName)).AppendLine("\",");
                builder.Append(childIndentation).Append("    FieldName = \"").Append(EscapeString(table.FieldName)).AppendLine("\",");
                builder.Append(childIndentation).Append("    Fields = ").Append(GenerateSchemaFields(table.Fields, indent + 8)).AppendLine();
                builder.Append(childIndentation).AppendLine("},");
            }

            builder.Append(indentation).Append("}");
            return builder.ToString();
        }

        private static void ValidateRequest(BetterScriptableGenerationRequest request)
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

            foreach (BetterScriptableSchemaField field in request.Schema.Fields)
            {
                ValidateTypeName(field.TypeName, field.Name);
                ValidateIdentifier(BetterScriptableNameUtility.ToPascalCase(field.Name), "Field name");
            }

            foreach (BetterScriptableSchemaTable table in request.Schema.Tables)
            {
                ValidateIdentifier(table.RowTypeName, "Data class name");
                ValidateIdentifier(BetterScriptableNameUtility.ToPascalCase(table.FieldName), "Array field name");
                foreach (BetterScriptableSchemaField field in table.Fields)
                {
                    ValidateTypeName(field.TypeName, field.Name);
                    ValidateIdentifier(BetterScriptableNameUtility.ToPascalCase(field.Name), "Data field name");
                }
            }
        }

        private static bool HasRuntimeFields(BetterScriptableSchemaField[] fields)
        {
            if (fields == null)
            {
                return false;
            }

            foreach (BetterScriptableSchemaField field in fields)
            {
                if (field != null && !field.IsDesignField)
                {
                    return true;
                }
            }

            return false;
        }

        private static void ValidateIdentifier(string identifier, string label)
        {
            if (!BetterScriptableNameUtility.IsValidIdentifier(identifier))
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

    public sealed class BetterScriptableGenerationRequest
    {
        public string AssetClassName = "ItemDataAsset";
        public string NamespaceName = BetterScriptableCodeGenerator.DefaultNamespace;
        public string MenuPath = "BetterScriptable/game_data";
        public string OutputDirectory = BetterScriptableCodeGenerator.DefaultOutputDirectory;
        public BetterScriptableDocumentSchema Schema = new BetterScriptableDocumentSchema();
    }
}

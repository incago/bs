using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;

namespace SpreadAsset.Editor
{
    internal static class SpreadAssetGeneratedClassLoader
    {
        private static readonly Regex QuotedAssignmentPattern = new Regex(
            @"\b(?<name>AssetClassName|NamespaceName|MenuPath|RowTypeName|FieldName)\s*=\s*""(?<value>[^""]*)""",
            RegexOptions.Compiled);

        private static readonly Regex FieldPattern = new Regex(
            @"new\s+SpreadAssetSchemaField\s*\{(?<body>.*?)\}",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex StringAssignmentPattern = new Regex(
            @"\b(?<name>Id|TypeName|Name)\s*=\s*""(?<value>[^""]*)""",
            RegexOptions.Compiled);

        private static readonly Regex BoolAssignmentPattern = new Regex(
            @"\b(?<name>IsDesignField|IsKeyField)\s*=\s*(?<value>true|false)",
            RegexOptions.Compiled);

        public static bool TryLoadFromSelection(out SpreadAssetGenerationRequest request, out string sourcePath, out string error)
        {
            request = null;
            sourcePath = string.Empty;
            error = string.Empty;

            string selectedPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrEmpty(selectedPath) || Path.GetExtension(selectedPath) != ".cs")
            {
                return false;
            }

            if (selectedPath.EndsWith("Factory.cs", StringComparison.Ordinal)
                && selectedPath.Contains("/Editor/", StringComparison.Ordinal))
            {
                return TryLoadFromFactoryScript(selectedPath, out request, out sourcePath, out error);
            }

            return TryLoadFromRuntimeScript(selectedPath, out request, out sourcePath, out error);
        }

        public static bool TryLoadFromFactoryScript(
            string factoryScriptPath,
            out SpreadAssetGenerationRequest request,
            out string sourcePath,
            out string error)
        {
            request = null;
            sourcePath = string.Empty;
            error = string.Empty;

            string factoryCode = File.ReadAllText(factoryScriptPath);
            if (!TryParseFactory(factoryCode, out SpreadAssetDocumentSchema schema, out error))
            {
                return false;
            }

            string editorDirectory = Path.GetDirectoryName(factoryScriptPath)?.Replace('\\', '/') ?? string.Empty;
            string outputDirectory = Path.GetDirectoryName(editorDirectory)?.Replace('\\', '/') ?? string.Empty;
            string runtimeScriptPath = $"{outputDirectory}/{schema.AssetClassName}.cs";

            request = new SpreadAssetGenerationRequest
            {
                AssetClassName = schema.AssetClassName,
                NamespaceName = string.IsNullOrEmpty(schema.NamespaceName)
                    ? SpreadAssetCodeGenerator.DefaultNamespace
                    : schema.NamespaceName,
                MenuPath = schema.MenuPath,
                OutputDirectory = outputDirectory,
                Schema = schema
            };

            sourcePath = File.Exists(runtimeScriptPath) ? runtimeScriptPath : factoryScriptPath;
            return true;
        }

        public static bool TryLoadFromRuntimeScript(
            string runtimeScriptPath,
            out SpreadAssetGenerationRequest request,
            out string sourcePath,
            out string error)
        {
            request = null;
            sourcePath = string.Empty;
            error = string.Empty;

            string assetClassName = Path.GetFileNameWithoutExtension(runtimeScriptPath);
            string outputDirectory = Path.GetDirectoryName(runtimeScriptPath)?.Replace('\\', '/') ?? string.Empty;
            string factoryPath = $"{outputDirectory}/Editor/{assetClassName}Factory.cs";

            if (!File.Exists(factoryPath))
            {
                return false;
            }

            string factoryCode = File.ReadAllText(factoryPath);
            if (!TryParseFactory(factoryCode, out SpreadAssetDocumentSchema schema, out error))
            {
                return false;
            }

            request = new SpreadAssetGenerationRequest
            {
                AssetClassName = string.IsNullOrEmpty(schema.AssetClassName) ? assetClassName : schema.AssetClassName,
                NamespaceName = string.IsNullOrEmpty(schema.NamespaceName)
                    ? SpreadAssetCodeGenerator.DefaultNamespace
                    : schema.NamespaceName,
                MenuPath = schema.MenuPath,
                OutputDirectory = outputDirectory,
                Schema = schema
            };

            sourcePath = runtimeScriptPath;
            return true;
        }

        public static bool TryLoadFromAsset(
            UnityEngine.Object asset,
            out SpreadAssetGenerationRequest request,
            out string sourcePath,
            out string error)
        {
            request = null;
            sourcePath = string.Empty;
            error = string.Empty;

            if (!(asset is UnityEngine.ScriptableObject scriptableObject))
            {
                return false;
            }

            MonoScript runtimeScript = MonoScript.FromScriptableObject(scriptableObject);
            string runtimeScriptPath = AssetDatabase.GetAssetPath(runtimeScript);
            if (string.IsNullOrEmpty(runtimeScriptPath))
            {
                error = "Could not find the runtime script for the selected asset.";
                return false;
            }

            return TryLoadFromRuntimeScript(runtimeScriptPath, out request, out sourcePath, out error);
        }

        private static bool TryParseFactory(
            string factoryCode,
            out SpreadAssetDocumentSchema schema,
            out string error)
        {
            schema = new SpreadAssetDocumentSchema();
            error = string.Empty;

            foreach (Match match in QuotedAssignmentPattern.Matches(factoryCode))
            {
                string name = match.Groups["name"].Value;
                string value = match.Groups["value"].Value;
                switch (name)
                {
                    case "AssetClassName":
                        schema.AssetClassName = value;
                        break;
                    case "NamespaceName":
                        schema.NamespaceName = value;
                        break;
                    case "MenuPath":
                        schema.MenuPath = value;
                        break;
                }
            }

            if (string.IsNullOrEmpty(schema.AssetClassName))
            {
                error = "Generated factory schema does not contain AssetClassName.";
                return false;
            }

            schema.Fields = ParseAssetFields(factoryCode);
            schema.Tables = ParseTables(factoryCode);
            SpreadAssetSchemaUtility.EnsureFieldIds(schema);
            return true;
        }

        private static SpreadAssetSchemaField[] ParseAssetFields(string factoryCode)
        {
            int fieldsStart = factoryCode.IndexOf("Fields = new SpreadAssetSchemaField[]", StringComparison.Ordinal);
            int fieldsEnd = factoryCode.IndexOf("Tables =", fieldsStart, StringComparison.Ordinal);
            if (fieldsStart < 0 || fieldsEnd <= fieldsStart)
            {
                return Array.Empty<SpreadAssetSchemaField>();
            }

            string fieldsBlock = factoryCode.Substring(fieldsStart, fieldsEnd - fieldsStart);
            return ParseFields(fieldsBlock);
        }

        private static SpreadAssetSchemaTable[] ParseTables(string factoryCode)
        {
            List<SpreadAssetSchemaTable> tables = new List<SpreadAssetSchemaTable>();
            int tablesStart = factoryCode.IndexOf("Tables =", StringComparison.Ordinal);
            if (tablesStart < 0)
            {
                return tables.ToArray();
            }

            int arrayBraceStart = factoryCode.IndexOf('{', tablesStart);
            if (arrayBraceStart < 0 || !TryFindMatchingBrace(factoryCode, arrayBraceStart, out int arrayBraceEnd))
            {
                return tables.ToArray();
            }

            string tablesBlock = factoryCode.Substring(arrayBraceStart + 1, arrayBraceEnd - arrayBraceStart - 1);
            int searchIndex = 0;

            while (true)
            {
                int tableStart = tablesBlock.IndexOf("new SpreadAssetSchemaTable", searchIndex, StringComparison.Ordinal);
                if (tableStart < 0)
                {
                    break;
                }

                int braceStart = tablesBlock.IndexOf('{', tableStart);
                if (braceStart < 0 || !TryFindMatchingBrace(tablesBlock, braceStart, out int braceEnd))
                {
                    break;
                }

                string tableBlock = tablesBlock.Substring(braceStart, braceEnd - braceStart + 1);
                SpreadAssetSchemaTable table = ParseTable(tableBlock);
                if (!string.IsNullOrEmpty(table.RowTypeName) && !string.IsNullOrEmpty(table.FieldName))
                {
                    tables.Add(table);
                }

                searchIndex = braceEnd + 1;
            }

            return tables.ToArray();
        }

        private static SpreadAssetSchemaTable ParseTable(string tableBlock)
        {
            SpreadAssetSchemaTable table = new SpreadAssetSchemaTable();
            foreach (Match match in QuotedAssignmentPattern.Matches(tableBlock))
            {
                string name = match.Groups["name"].Value;
                string value = match.Groups["value"].Value;
                switch (name)
                {
                    case "RowTypeName":
                        table.RowTypeName = value;
                        break;
                    case "FieldName":
                        table.FieldName = value;
                        break;
                }
            }

            table.Fields = ParseFields(tableBlock);
            return table;
        }

        private static SpreadAssetSchemaField[] ParseFields(string block)
        {
            List<SpreadAssetSchemaField> fields = new List<SpreadAssetSchemaField>();
            foreach (Match match in FieldPattern.Matches(block))
            {
                string body = match.Groups["body"].Value;
                string fieldId = string.Empty;
                string typeName = string.Empty;
                string fieldName = string.Empty;
                bool isDesignField = false;
                bool isKeyField = false;

                foreach (Match assignment in StringAssignmentPattern.Matches(body))
                {
                    string name = assignment.Groups["name"].Value;
                    string value = assignment.Groups["value"].Value;
                    switch (name)
                    {
                        case "Id":
                            fieldId = value;
                            break;
                        case "TypeName":
                            typeName = value;
                            break;
                        case "Name":
                            fieldName = value;
                            break;
                    }
                }

                foreach (Match assignment in BoolAssignmentPattern.Matches(body))
                {
                    if (assignment.Groups["name"].Value == "IsDesignField")
                    {
                        isDesignField = string.Equals(
                            assignment.Groups["value"].Value,
                            "true",
                            StringComparison.OrdinalIgnoreCase);
                    }
                    else if (assignment.Groups["name"].Value == "IsKeyField")
                    {
                        isKeyField = string.Equals(
                            assignment.Groups["value"].Value,
                            "true",
                            StringComparison.OrdinalIgnoreCase);
                    }
                }

                if (string.IsNullOrEmpty(typeName) || string.IsNullOrEmpty(fieldName))
                {
                    continue;
                }

                fields.Add(new SpreadAssetSchemaField
                {
                    Id = fieldId,
                    TypeName = typeName,
                    Name = fieldName,
                    IsDesignField = isDesignField,
                    IsKeyField = isDesignField ? false : isKeyField
                });
            }

            return fields.ToArray();
        }

        private static bool TryFindMatchingBrace(string text, int braceStart, out int braceEnd)
        {
            braceEnd = -1;
            int depth = 0;
            for (int i = braceStart; i < text.Length; i++)
            {
                if (text[i] == '{')
                {
                    depth++;
                }
                else if (text[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        braceEnd = i;
                        return true;
                    }
                }
            }

            return false;
        }
    }
}

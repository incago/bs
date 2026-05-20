using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SpreadAsset.Editor
{
    public static class SpreadAssetDocumentIO
    {
        public const string Extension = ".spreadasset";

        public static bool IsDocumentPath(string assetPath)
        {
            return !string.IsNullOrEmpty(assetPath)
                && string.Equals(Path.GetExtension(assetPath), Extension, StringComparison.OrdinalIgnoreCase);
        }

        public static SpreadAssetDocument CreateDocument(
            UnityEngine.Object targetAsset,
            string targetAssetPath,
            SpreadAssetDocumentSchema schema)
        {
            SpreadAssetSchemaUtility.EnsureFieldIds(schema);
            return new SpreadAssetDocument
            {
                AssetGuid = AssetDatabase.AssetPathToGUID(targetAssetPath),
                AssetPath = targetAssetPath,
                AssetTypeName = targetAsset.GetType().AssemblyQualifiedName,
                SerializedAssetJson = EditorJsonUtility.ToJson(targetAsset, true),
                Schema = schema,
                Sheets = CreateSheetStates(schema)
            };
        }

        public static bool TryRead(string assetPath, out SpreadAssetDocument document, out string error)
        {
            document = null;
            error = string.Empty;

            if (!IsDocumentPath(assetPath))
            {
                error = "Selected asset is not a SpreadAsset document.";
                return false;
            }

            string fullPath = ToFullPath(assetPath);
            if (!File.Exists(fullPath))
            {
                error = $"Document file does not exist: {assetPath}";
                return false;
            }

            try
            {
                string json = File.ReadAllText(fullPath);
                document = JsonUtility.FromJson<SpreadAssetDocument>(json);
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }

            if (document == null)
            {
                error = "Document JSON is empty or invalid.";
                return false;
            }

            return true;
        }

        public static void Write(string assetPath, SpreadAssetDocument document)
        {
            string fullPath = ToFullPath(assetPath);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(fullPath, JsonUtility.ToJson(document, true));
        }

        public static UnityEngine.Object LoadLinkedAsset(SpreadAssetDocument document)
        {
            string assetPath = ResolveLinkedAssetPath(document);
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
        }

        public static string ResolveLinkedAssetPath(SpreadAssetDocument document)
        {
            if (document == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrEmpty(document.AssetGuid))
            {
                string guidPath = AssetDatabase.GUIDToAssetPath(document.AssetGuid);
                if (!string.IsNullOrEmpty(guidPath))
                {
                    return guidPath;
                }
            }

            return document.AssetPath;
        }

        private static SpreadAssetSheetState[] CreateSheetStates(SpreadAssetDocumentSchema schema)
        {
            if (schema?.Tables == null || schema.Tables.Length == 0)
            {
                return Array.Empty<SpreadAssetSheetState>();
            }

            SpreadAssetSheetState[] states = new SpreadAssetSheetState[schema.Tables.Length];
            for (int i = 0; i < schema.Tables.Length; i++)
            {
                states[i] = new SpreadAssetSheetState
                {
                    ArrayFieldName = schema.Tables[i].FieldName,
                    Formulas = Array.Empty<SpreadAssetFormulaState>(),
                    Cells = Array.Empty<SpreadAssetCellState>()
                };
            }

            return states;
        }

        private static string ToFullPath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            return Path.Combine(projectRoot ?? string.Empty, assetPath);
        }
    }
}

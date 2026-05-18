using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BetterScriptable.Editor
{
    public static class BetterScriptableDocumentIO
    {
        public const string Extension = ".betterscriptable";

        public static bool IsDocumentPath(string assetPath)
        {
            return !string.IsNullOrEmpty(assetPath)
                && string.Equals(Path.GetExtension(assetPath), Extension, StringComparison.OrdinalIgnoreCase);
        }

        public static BetterScriptableDocument CreateDocument(
            UnityEngine.Object targetAsset,
            string targetAssetPath,
            BetterScriptableDocumentSchema schema)
        {
            return new BetterScriptableDocument
            {
                AssetGuid = AssetDatabase.AssetPathToGUID(targetAssetPath),
                AssetPath = targetAssetPath,
                AssetTypeName = targetAsset.GetType().AssemblyQualifiedName,
                SerializedAssetJson = EditorJsonUtility.ToJson(targetAsset, true),
                Schema = schema,
                Sheets = CreateSheetStates(schema)
            };
        }

        public static bool TryRead(string assetPath, out BetterScriptableDocument document, out string error)
        {
            document = null;
            error = string.Empty;

            if (!IsDocumentPath(assetPath))
            {
                error = "Selected asset is not a BetterScriptable document.";
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
                document = JsonUtility.FromJson<BetterScriptableDocument>(json);
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

        public static void Write(string assetPath, BetterScriptableDocument document)
        {
            string fullPath = ToFullPath(assetPath);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(fullPath, JsonUtility.ToJson(document, true));
        }

        public static UnityEngine.Object LoadLinkedAsset(BetterScriptableDocument document)
        {
            string assetPath = ResolveLinkedAssetPath(document);
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
        }

        public static string ResolveLinkedAssetPath(BetterScriptableDocument document)
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

        private static BetterScriptableSheetState[] CreateSheetStates(BetterScriptableDocumentSchema schema)
        {
            if (schema?.Tables == null || schema.Tables.Length == 0)
            {
                return Array.Empty<BetterScriptableSheetState>();
            }

            BetterScriptableSheetState[] states = new BetterScriptableSheetState[schema.Tables.Length];
            for (int i = 0; i < schema.Tables.Length; i++)
            {
                states[i] = new BetterScriptableSheetState
                {
                    ArrayFieldName = schema.Tables[i].FieldName,
                    Formulas = Array.Empty<BetterScriptableFormulaState>(),
                    Cells = Array.Empty<BetterScriptableCellState>()
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

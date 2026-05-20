using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SpreadAsset.Editor
{
    [InitializeOnLoad]
    internal static class SpreadAssetExportTargetInspectorWarning
    {
        private static readonly Dictionary<string, LinkedDocumentInfo> DocumentByAssetGuid =
            new Dictionary<string, LinkedDocumentInfo>();

        private static bool _isCacheBuilt;

        static SpreadAssetExportTargetInspectorWarning()
        {
            UnityEditor.Editor.finishedDefaultHeaderGUI += DrawWarningIfNeeded;
            EditorApplication.projectChanged += ClearCache;
        }

        private static void DrawWarningIfNeeded(UnityEditor.Editor editor)
        {
            if (editor == null
                || editor.targets == null
                || editor.targets.Length != 1
                || editor.target == null
                || !(editor.target is ScriptableObject))
            {
                return;
            }

            string assetPath = AssetDatabase.GetAssetPath(editor.target);
            if (string.IsNullOrEmpty(assetPath)
                || SpreadAssetDocumentIO.IsDocumentPath(assetPath)
                || !string.Equals(Path.GetExtension(assetPath), ".asset", StringComparison.OrdinalIgnoreCase)
                || !TryFindLinkedDocument(assetPath, out LinkedDocumentInfo documentInfo))
            {
                return;
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.HelpBox(
                "This asset is a runtime export target for a SpreadAsset document. Direct Inspector edits can be overwritten the next time the source document is saved and exported.",
                MessageType.Warning);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open Source Document in SpreadAsset Editor"))
                {
                    SpreadAssetWindow.OpenDocument(documentInfo.DocumentPath);
                }

                if (GUILayout.Button("Select Source Document"))
                {
                    UnityEngine.Object documentAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(documentInfo.DocumentPath);
                    Selection.activeObject = documentAsset;
                    EditorGUIUtility.PingObject(documentAsset);
                }
            }
        }

        private static bool TryFindLinkedDocument(string assetPath, out LinkedDocumentInfo documentInfo)
        {
            EnsureCache();
            string assetGuid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(assetGuid))
            {
                documentInfo = null;
                return false;
            }

            return DocumentByAssetGuid.TryGetValue(assetGuid, out documentInfo);
        }

        private static void EnsureCache()
        {
            if (_isCacheBuilt)
            {
                return;
            }

            DocumentByAssetGuid.Clear();
            foreach (string documentPath in FindDocumentPaths())
            {
                if (!SpreadAssetDocumentIO.TryRead(documentPath, out SpreadAssetDocument document, out _))
                {
                    continue;
                }

                string assetGuid = ResolveLinkedAssetGuid(document);
                if (string.IsNullOrEmpty(assetGuid))
                {
                    continue;
                }

                if (!DocumentByAssetGuid.ContainsKey(assetGuid))
                {
                    DocumentByAssetGuid.Add(assetGuid, new LinkedDocumentInfo(documentPath));
                }
            }

            _isCacheBuilt = true;
        }

        private static IEnumerable<string> FindDocumentPaths()
        {
            string assetsDirectory = Application.dataPath;
            if (string.IsNullOrEmpty(assetsDirectory) || !Directory.Exists(assetsDirectory))
            {
                yield break;
            }

            foreach (string fullPath in Directory.GetFiles(
                         assetsDirectory,
                         "*" + SpreadAssetDocumentIO.Extension,
                         SearchOption.AllDirectories))
            {
                string normalizedPath = fullPath.Replace('\\', '/');
                string normalizedAssetsDirectory = assetsDirectory.Replace('\\', '/');
                if (!normalizedPath.StartsWith(normalizedAssetsDirectory, StringComparison.Ordinal))
                {
                    continue;
                }

                yield return "Assets" + normalizedPath.Substring(normalizedAssetsDirectory.Length);
            }
        }

        private static string ResolveLinkedAssetGuid(SpreadAssetDocument document)
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
                    return document.AssetGuid;
                }
            }

            return string.IsNullOrEmpty(document.AssetPath)
                ? string.Empty
                : AssetDatabase.AssetPathToGUID(document.AssetPath);
        }

        private static void ClearCache()
        {
            _isCacheBuilt = false;
            DocumentByAssetGuid.Clear();
        }

        private sealed class LinkedDocumentInfo
        {
            public readonly string DocumentPath;

            public LinkedDocumentInfo(string documentPath)
            {
                DocumentPath = documentPath;
            }
        }
    }
}

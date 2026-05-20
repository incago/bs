using System;
using UnityEditor;
using UnityEngine;

namespace SpreadAsset.Editor
{
    public static class SpreadAssetDocumentSync
    {
        public static bool EnsureDocumentData(SpreadAssetDocument document, UnityEngine.Object linkedAsset)
        {
            if (document == null || linkedAsset == null)
            {
                return false;
            }

            bool changed = false;
            string assetPath = AssetDatabase.GetAssetPath(linkedAsset);
            string assetGuid = AssetDatabase.AssetPathToGUID(assetPath);
            string assetTypeName = linkedAsset.GetType().AssemblyQualifiedName;

            if (document.Version < 2)
            {
                document.Version = 2;
                changed = true;
            }

            if (document.Schema == null)
            {
                document.Schema = new SpreadAssetDocumentSchema();
                changed = true;
            }

            changed |= SpreadAssetSchemaUtility.EnsureFieldIds(document.Schema);

            if (!string.IsNullOrEmpty(assetPath) && document.AssetPath != assetPath)
            {
                document.AssetPath = assetPath;
                changed = true;
            }

            if (!string.IsNullOrEmpty(assetGuid) && document.AssetGuid != assetGuid)
            {
                document.AssetGuid = assetGuid;
                changed = true;
            }

            if (!string.IsNullOrEmpty(assetTypeName) && document.AssetTypeName != assetTypeName)
            {
                document.AssetTypeName = assetTypeName;
                changed = true;
            }

            if (string.IsNullOrEmpty(document.SerializedAssetJson))
            {
                document.SerializedAssetJson = EditorJsonUtility.ToJson(linkedAsset, true);
                changed = true;
            }

            if (document.Sheets == null)
            {
                document.Sheets = Array.Empty<SpreadAssetSheetState>();
                changed = true;
            }
            else
            {
                foreach (SpreadAssetSheetState sheet in document.Sheets)
                {
                    if (sheet == null)
                    {
                        continue;
                    }

                    if (sheet.Formulas == null)
                    {
                        sheet.Formulas = Array.Empty<SpreadAssetFormulaState>();
                        changed = true;
                    }

                    if (sheet.Cells == null)
                    {
                        sheet.Cells = Array.Empty<SpreadAssetCellState>();
                        changed = true;
                    }
                }
            }

            return changed;
        }

        public static ScriptableObject CreateWorkingCopy(SpreadAssetDocument document, UnityEngine.Object linkedAsset)
        {
            if (linkedAsset == null)
            {
                return null;
            }

            Type assetType = ResolveAssetType(document, linkedAsset);
            if (assetType == null || !typeof(ScriptableObject).IsAssignableFrom(assetType))
            {
                return null;
            }

            ScriptableObject workingCopy = ScriptableObject.CreateInstance(assetType);
            workingCopy.name = linkedAsset.name;

            string sourceJson = string.IsNullOrEmpty(document?.SerializedAssetJson)
                ? EditorJsonUtility.ToJson(linkedAsset, true)
                : document.SerializedAssetJson;

            EditorJsonUtility.FromJsonOverwrite(sourceJson, workingCopy);
            workingCopy.name = linkedAsset.name;
            return workingCopy;
        }

        public static void CaptureWorkingCopy(SpreadAssetDocument document, ScriptableObject workingCopy)
        {
            if (document == null || workingCopy == null)
            {
                return;
            }

            document.SerializedAssetJson = EditorJsonUtility.ToJson(workingCopy, true);
        }

        public static void ExportToLinkedAsset(SpreadAssetDocument document, UnityEngine.Object linkedAsset)
        {
            if (document == null || linkedAsset == null || string.IsNullOrEmpty(document.SerializedAssetJson))
            {
                return;
            }

            string assetName = linkedAsset.name;
            EditorJsonUtility.FromJsonOverwrite(document.SerializedAssetJson, linkedAsset);
            linkedAsset.name = assetName;
            EditorUtility.SetDirty(linkedAsset);
            AssetDatabase.SaveAssets();
        }

        public static void ImportFromLinkedAsset(SpreadAssetDocument document, UnityEngine.Object linkedAsset)
        {
            if (document == null || linkedAsset == null)
            {
                return;
            }

            document.SerializedAssetJson = EditorJsonUtility.ToJson(linkedAsset, true);
        }

        private static Type ResolveAssetType(SpreadAssetDocument document, UnityEngine.Object linkedAsset)
        {
            if (!string.IsNullOrEmpty(document?.AssetTypeName))
            {
                Type type = Type.GetType(document.AssetTypeName);
                if (type != null)
                {
                    return type;
                }
            }

            return linkedAsset.GetType();
        }
    }
}

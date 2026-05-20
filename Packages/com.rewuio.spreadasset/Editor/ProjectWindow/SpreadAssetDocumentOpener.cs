using UnityEditor;
using UnityEditor.Callbacks;

namespace SpreadAsset.Editor
{
    internal static class SpreadAssetDocumentOpener
    {
        [MenuItem("Assets/Open in SpreadAsset Editor", true)]
        private static bool ValidateOpen()
        {
            return TryGetSelectedDocumentPath(out _);
        }

        [MenuItem("Assets/Open in SpreadAsset Editor")]
        private static void OpenSelected()
        {
            if (TryGetSelectedDocumentPath(out string documentPath))
            {
                SpreadAssetWindow.OpenDocument(documentPath);
            }
        }

        [MenuItem("Assets/SpreadAsset/Recreate Document From Asset", true)]
        private static bool ValidateRecreateDocumentFromAsset()
        {
            return SpreadAssetFactory.CanCreateDocumentFromAsset(Selection.activeObject);
        }

        [MenuItem("Assets/SpreadAsset/Recreate Document From Asset")]
        private static void RecreateDocumentFromAsset()
        {
            SpreadAssetFactory.CreateDocumentFromAsset(Selection.activeObject);
        }

        [OnOpenAsset]
        private static bool OnOpenAsset(int instanceId, int line)
        {
#pragma warning disable 0618
            string assetPath = AssetDatabase.GetAssetPath(instanceId);
#pragma warning restore 0618
            if (!SpreadAssetDocumentIO.IsDocumentPath(assetPath))
            {
                return false;
            }

            SpreadAssetWindow.OpenDocument(assetPath);
            return true;
        }

        private static bool TryGetSelectedDocumentPath(out string documentPath)
        {
            documentPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            return SpreadAssetDocumentIO.IsDocumentPath(documentPath);
        }
    }
}

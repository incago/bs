using UnityEditor;
using UnityEditor.Callbacks;

namespace BetterScriptable.Editor
{
    internal static class BetterScriptableDocumentOpener
    {
        [MenuItem("Assets/Open in BetterScriptable Editor", true)]
        private static bool ValidateOpen()
        {
            return TryGetSelectedDocumentPath(out _);
        }

        [MenuItem("Assets/Open in BetterScriptable Editor")]
        private static void OpenSelected()
        {
            if (TryGetSelectedDocumentPath(out string documentPath))
            {
                BetterScriptableWindow.OpenDocument(documentPath);
            }
        }

        [MenuItem("Assets/BetterScriptable/Recreate Document From Asset", true)]
        private static bool ValidateRecreateDocumentFromAsset()
        {
            return BetterScriptableAssetFactory.CanCreateDocumentFromAsset(Selection.activeObject);
        }

        [MenuItem("Assets/BetterScriptable/Recreate Document From Asset")]
        private static void RecreateDocumentFromAsset()
        {
            BetterScriptableAssetFactory.CreateDocumentFromAsset(Selection.activeObject);
        }

        [OnOpenAsset]
        private static bool OnOpenAsset(int instanceId, int line)
        {
#pragma warning disable 0618
            string assetPath = AssetDatabase.GetAssetPath(instanceId);
#pragma warning restore 0618
            if (!BetterScriptableDocumentIO.IsDocumentPath(assetPath))
            {
                return false;
            }

            BetterScriptableWindow.OpenDocument(assetPath);
            return true;
        }

        private static bool TryGetSelectedDocumentPath(out string documentPath)
        {
            documentPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            return BetterScriptableDocumentIO.IsDocumentPath(documentPath);
        }
    }
}

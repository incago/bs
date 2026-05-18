using BetterScriptable.Editor;
using BetterScriptable.Generated;
using UnityEditor;

namespace BetterScriptable.Generated.Editor
{
    internal static class ItemDataAssetFactory
    {
        [MenuItem("Assets/Create/BetterScriptable/game_data_item")]
        private static void Create()
        {
            BetterScriptableDocumentSchema schema = new BetterScriptableDocumentSchema
            {
                AssetClassName = "ItemDataAsset",
                NamespaceName = "BetterScriptable.Generated",
                MenuPath = "BetterScriptable/game_data_item",
                Fields = new BetterScriptableSchemaField[]
                {
                    new BetterScriptableSchemaField { TypeName = "string", Name = "ItemCategory" },
                },
                Tables = new BetterScriptableSchemaTable[]
                {
                    new BetterScriptableSchemaTable
                    {
                        RowTypeName = "ItemData",
                        FieldName = "ItemDatas",
                        Fields = new BetterScriptableSchemaField[]
                        {
                            new BetterScriptableSchemaField { TypeName = "int", Name = "Id" },
                            new BetterScriptableSchemaField { TypeName = "float", Name = "Weight" },
                            new BetterScriptableSchemaField { TypeName = "Vector2", Name = "Position" },
                        }
                    },
                }
            };

            BetterScriptableAssetFactory.CreatePair<ItemDataAsset>("game_data_item", schema);
        }
    }
}

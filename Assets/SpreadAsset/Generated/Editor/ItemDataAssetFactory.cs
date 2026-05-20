using SpreadAsset.Editor;
using SpreadAsset.Generated;
using UnityEditor;

namespace SpreadAsset.Generated.Editor
{
    internal static class ItemDataAssetFactory
    {
        [MenuItem("Assets/Create/SpreadAsset/game_data_item")]
        private static void Create()
        {
            SpreadAssetDocumentSchema schema = new SpreadAssetDocumentSchema
            {
                AssetClassName = "ItemDataAsset",
                NamespaceName = "SpreadAsset.Generated",
                MenuPath = "SpreadAsset/game_data_item",
                Fields = new SpreadAssetSchemaField[]
                {
                    new SpreadAssetSchemaField { TypeName = "string", Name = "ItemCategory" },
                },
                Tables = new SpreadAssetSchemaTable[]
                {
                    new SpreadAssetSchemaTable
                    {
                        RowTypeName = "ItemData",
                        FieldName = "ItemDatas",
                        Fields = new SpreadAssetSchemaField[]
                        {
                            new SpreadAssetSchemaField { TypeName = "int", Name = "Id" },
                            new SpreadAssetSchemaField { TypeName = "float", Name = "Weight" },
                            new SpreadAssetSchemaField { TypeName = "Vector2", Name = "Position" },
                        }
                    },
                }
            };

            SpreadAssetFactory.CreatePair<ItemDataAsset>("game_data_item", schema);
        }
    }
}

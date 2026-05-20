using SpreadAsset.Editor;
using UnityEditor;

internal static class StageDataAssetFactory
{
    [MenuItem("Assets/Create/SpreadAsset/game_data_stage")]
    private static void Create()
    {
        SpreadAssetDocumentSchema schema = new SpreadAssetDocumentSchema
        {
            AssetClassName = "StageDataAsset",
            NamespaceName = "",
            MenuPath = "SpreadAsset/game_data_stage",
            Fields = new SpreadAssetSchemaField[0],
            Tables = new SpreadAssetSchemaTable[]
            {
                new SpreadAssetSchemaTable
                {
                    RowTypeName = "StageData",
                    FieldName = "StageDatas",
                    Fields = new SpreadAssetSchemaField[]
                    {
                        new SpreadAssetSchemaField { TypeName = "int", Name = "Id" },
                        new SpreadAssetSchemaField { TypeName = "string", Name = "FirstName" },
                        new SpreadAssetSchemaField { TypeName = "string", Name = "LastName" },
                        new SpreadAssetSchemaField { TypeName = "float", Name = "Weight" },
                        new SpreadAssetSchemaField { TypeName = "RoomData[]", Name = "RoomDatas" },
                    }
                },
            }
        };

        SpreadAssetFactory.CreatePair<StageDataAsset>("game_data_stage", schema);
    }
}

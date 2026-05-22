using SpreadAsset.Editor;
using SpreadAsset.Generated;
using UnityEditor;

namespace SpreadAsset.Generated.Editor
{
    internal static class StageDataAssetFactory
    {
        [MenuItem("Assets/Create/SpreadAsset/game_data_stage")]
        private static void Create()
        {
            SpreadAssetDocumentSchema schema = new SpreadAssetDocumentSchema
            {
                AssetClassName = "StageDataAsset",
                NamespaceName = "SpreadAsset.Generated",
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
                            new SpreadAssetSchemaField { Id = "field_99738f1cc27dc9b6", TypeName = "int", Name = "Id" },
                            new SpreadAssetSchemaField { Id = "field_bd750a0bd349b6ac", TypeName = "string", Name = "FirstName" },
                            new SpreadAssetSchemaField { Id = "field_eaa1fa9d2d6d3a6a", TypeName = "string", Name = "LastName" },
                            new SpreadAssetSchemaField { Id = "field_424352ca1d5d24a4", TypeName = "float", Name = "Weight" },
                            new SpreadAssetSchemaField { Id = "field_0938a8729add4c078c178137c15f89c2", TypeName = "int[]", Name = "RoomDataIds" },
                        }
                    },
                    new SpreadAssetSchemaTable
                    {
                        RowTypeName = "RoomData",
                        FieldName = "RoomDatas",
                        Fields = new SpreadAssetSchemaField[]
                        {
                            new SpreadAssetSchemaField { Id = "field_d97946bdcfb24f31911c5e9fdc5cfee8", TypeName = "int", Name = "Id", IsKeyField = true },
                            new SpreadAssetSchemaField { Id = "field_252739a3695e40208f43da182b205485", TypeName = "string", Name = "Name" },
                        }
                    },
                }
            };

            SpreadAssetFactory.CreatePair<StageDataAsset>("game_data_stage", schema);
        }
    }
}

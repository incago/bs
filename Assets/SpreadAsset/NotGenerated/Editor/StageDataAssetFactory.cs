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
                            new SpreadAssetSchemaField { Id = "field_99738f1cc27dc9b6", TypeName = "int", Name = "Id", IsKeyField = true },
                            new SpreadAssetSchemaField { Id = "field_bd750a0bd349b6ac", TypeName = "string", Name = "FirstName" },
                            new SpreadAssetSchemaField { Id = "field_eaa1fa9d2d6d3a6a", TypeName = "string", Name = "LastName" },
                            new SpreadAssetSchemaField { Id = "field_424352ca1d5d24a4", TypeName = "float", Name = "Weight" },
                            new SpreadAssetSchemaField { Id = "field_0938a8729add4c078c178137c15f89c2", TypeName = "int[]", Name = "RoomDataIds" },
                            new SpreadAssetSchemaField { Id = "field_6847da1bde274c599b544c8d13350cef", TypeName = "SpreadAsset.NotGenerated.UserEnum", Name = "Type" },
                            new SpreadAssetSchemaField { Id = "field_f827695b80264c9688625891586ae87d", TypeName = "TestScript", Name = "Prefab" },
                            new SpreadAssetSchemaField { Id = "field_ec70ef179e8a460aa7f8e1417083edf8", TypeName = "AnimationCurve", Name = "Curve" },
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
                            new SpreadAssetSchemaField { Id = "field_1022eaa5231240fe8e5eb8d0c7172be2", TypeName = "AnimationCurve", Name = "Curve" },
                            new SpreadAssetSchemaField { Id = "field_ab9b469c0cba48d4b9598e87c9f659fa", TypeName = "double", Name = "DoubleNumber" },
                            new SpreadAssetSchemaField { Id = "field_cf1395aefdab4582a69f84dbb239cf04", TypeName = "TestScript", Name = "Prefab" },
                        }
                    },
                }
            };

            SpreadAssetFactory.CreatePair<StageDataAsset>("game_data_stage", schema);
        }
    }
}

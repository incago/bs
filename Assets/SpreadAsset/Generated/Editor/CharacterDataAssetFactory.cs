using SpreadAsset.Editor;
using SpreadAsset.Generated;
using UnityEditor;

namespace SpreadAsset.Generated.Editor
{
    internal static class CharacterDataAssetFactory
    {
        [MenuItem("Assets/Create/SpreadAsset/game_data_character")]
        private static void Create()
        {
            SpreadAssetDocumentSchema schema = new SpreadAssetDocumentSchema
            {
                AssetClassName = "CharacterDataAsset",
                NamespaceName = "SpreadAsset.Generated",
                MenuPath = "SpreadAsset/game_data_character",
                Fields = new SpreadAssetSchemaField[]
                {
                    new SpreadAssetSchemaField { Id = "field_44363b49812899c2", TypeName = "string", Name = "CharacterType" },
                },
                Tables = new SpreadAssetSchemaTable[]
                {
                    new SpreadAssetSchemaTable
                    {
                        RowTypeName = "CharacterData",
                        FieldName = "CharacterDatas",
                        Fields = new SpreadAssetSchemaField[]
                        {
                            new SpreadAssetSchemaField { Id = "field_a631c88c0301bb17", TypeName = "int", Name = "Id" },
                            new SpreadAssetSchemaField { Id = "field_8b3423c2c8dfe2fd", TypeName = "string", Name = "Name", IsDesignField = true },
                            new SpreadAssetSchemaField { Id = "field_d65905368e261141", TypeName = "float", Name = "Weight" },
                            new SpreadAssetSchemaField { Id = "field_0a7bcfc06e89ec59", TypeName = "string", Name = "ResourceName", IsDesignField = true },
                            new SpreadAssetSchemaField { Id = "field_455eb383716f6c95", TypeName = "string", Name = "ThumbnailResourceKey" },
                            new SpreadAssetSchemaField { Id = "field_ddb02aa0036591a6", TypeName = "string", Name = "SpineResourceKey" },
                            new SpreadAssetSchemaField { Id = "field_0bd143838be7506c", TypeName = "Vector3", Name = "ResourceOffset" },
                            new SpreadAssetSchemaField { Id = "field_48f02033e91fd8b1", TypeName = "float", Name = "RunSpeed" },
                            new SpreadAssetSchemaField { Id = "field_a81b74f1d5de0cda", TypeName = "float", Name = "ActionCooldown" },
                            new SpreadAssetSchemaField { Id = "field_5bca0e7beace5aac", TypeName = "float", Name = "AttackCooldown" },
                            new SpreadAssetSchemaField { Id = "field_b12897b423e37463", TypeName = "float", Name = "HealthPoint" },
                            new SpreadAssetSchemaField { Id = "field_e03f3a7f3ed6b31d", TypeName = "float", Name = "AttackPoint" },
                            new SpreadAssetSchemaField { Id = "field_23821483e69b45f8a8eb91daf87df363", TypeName = "SpreadAsset.NotGenerated.UserEnum", Name = "UserEnum" },
                        }
                    },
                    new SpreadAssetSchemaTable
                    {
                        RowTypeName = "SkillData",
                        FieldName = "SkillDatas",
                        Fields = new SpreadAssetSchemaField[]
                        {
                            new SpreadAssetSchemaField { Id = "field_84f1401280d5777f", TypeName = "int", Name = "Id" },
                            new SpreadAssetSchemaField { Id = "field_fb1d12c6907c4c06b7a3a9968d6c85e8", TypeName = "string", Name = "Name" },
                            new SpreadAssetSchemaField { Id = "field_c5992b76da9b4636985f312244e85aa7", TypeName = "int", Name = "Cost" },
                            new SpreadAssetSchemaField { Id = "field_1c819ce986d0429b92b1179d3b1ca927", TypeName = "float", Name = "Cooldown" },
                            new SpreadAssetSchemaField { Id = "field_0eb51c09d9fe44fb87775c534584b079", TypeName = "float", Name = "Duration" },
                        }
                    },
                }
            };

            SpreadAssetFactory.CreatePair<CharacterDataAsset>("game_data_character", schema);
        }
    }
}

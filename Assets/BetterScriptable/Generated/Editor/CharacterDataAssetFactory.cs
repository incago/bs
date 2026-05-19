using BetterScriptable.Editor;
using BetterScriptable.Generated;
using UnityEditor;

namespace BetterScriptable.Generated.Editor
{
    internal static class CharacterDataAssetFactory
    {
        [MenuItem("Assets/Create/BetterScriptable/game_data_character")]
        private static void Create()
        {
            BetterScriptableDocumentSchema schema = new BetterScriptableDocumentSchema
            {
                AssetClassName = "CharacterDataAsset",
                NamespaceName = "BetterScriptable.Generated",
                MenuPath = "BetterScriptable/game_data_character",
                Fields = new BetterScriptableSchemaField[]
                {
                    new BetterScriptableSchemaField { Id = "field_44363b49812899c2", TypeName = "string", Name = "CharacterType" },
                },
                Tables = new BetterScriptableSchemaTable[]
                {
                    new BetterScriptableSchemaTable
                    {
                        RowTypeName = "CharacterData",
                        FieldName = "CharacterDatas",
                        Fields = new BetterScriptableSchemaField[]
                        {
                            new BetterScriptableSchemaField { Id = "field_a631c88c0301bb17", TypeName = "int", Name = "Id" },
                            new BetterScriptableSchemaField { Id = "field_8b3423c2c8dfe2fd", TypeName = "string", Name = "Name", IsDesignField = true },
                            new BetterScriptableSchemaField { Id = "field_d65905368e261141", TypeName = "float", Name = "Weight" },
                            new BetterScriptableSchemaField { Id = "field_0a7bcfc06e89ec59", TypeName = "string", Name = "ResourceName", IsDesignField = true },
                            new BetterScriptableSchemaField { Id = "field_455eb383716f6c95", TypeName = "string", Name = "ThumbnailResourceKey" },
                            new BetterScriptableSchemaField { Id = "field_ddb02aa0036591a6", TypeName = "string", Name = "SpineResourceKey" },
                            new BetterScriptableSchemaField { Id = "field_0bd143838be7506c", TypeName = "Vector3", Name = "ResourceOffset" },
                            new BetterScriptableSchemaField { Id = "field_48f02033e91fd8b1", TypeName = "float", Name = "RunSpeed" },
                            new BetterScriptableSchemaField { Id = "field_a81b74f1d5de0cda", TypeName = "float", Name = "ActionCooldown" },
                            new BetterScriptableSchemaField { Id = "field_5bca0e7beace5aac", TypeName = "float", Name = "AttackCooldown" },
                            new BetterScriptableSchemaField { Id = "field_b12897b423e37463", TypeName = "float", Name = "HealthPoint" },
                            new BetterScriptableSchemaField { Id = "field_e03f3a7f3ed6b31d", TypeName = "float", Name = "AttackPoint" },
                            new BetterScriptableSchemaField { Id = "field_23821483e69b45f8a8eb91daf87df363", TypeName = "BetterScriptable.NotGenerated.UserEnum", Name = "UserEnum" },
                        }
                    },
                    new BetterScriptableSchemaTable
                    {
                        RowTypeName = "SkillData",
                        FieldName = "SkillDatas",
                        Fields = new BetterScriptableSchemaField[]
                        {
                            new BetterScriptableSchemaField { Id = "field_84f1401280d5777f", TypeName = "int", Name = "Id" },
                        }
                    },
                }
            };

            BetterScriptableAssetFactory.CreatePair<CharacterDataAsset>("game_data_character", schema);
        }
    }
}

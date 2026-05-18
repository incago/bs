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
                    new BetterScriptableSchemaField { TypeName = "string", Name = "CharacterType" },
                },
                Tables = new BetterScriptableSchemaTable[]
                {
                    new BetterScriptableSchemaTable
                    {
                        RowTypeName = "CharacterData",
                        FieldName = "CharacterDatas",
                        Fields = new BetterScriptableSchemaField[]
                        {
                            new BetterScriptableSchemaField { TypeName = "int", Name = "Id" },
                            new BetterScriptableSchemaField { TypeName = "string", Name = "Name", IsDesignField = true },
                            new BetterScriptableSchemaField { TypeName = "float", Name = "Weight" },
                            new BetterScriptableSchemaField { TypeName = "string", Name = "ResourceName", IsDesignField = true },
                            new BetterScriptableSchemaField { TypeName = "string", Name = "ThumbnailResourceKey" },
                            new BetterScriptableSchemaField { TypeName = "string", Name = "SpineResourceKey" },
                            new BetterScriptableSchemaField { TypeName = "Vector3", Name = "ResourceOffset" },
                            new BetterScriptableSchemaField { TypeName = "float", Name = "RunSpeed" },
                            new BetterScriptableSchemaField { TypeName = "float", Name = "ActionCooldown" },
                            new BetterScriptableSchemaField { TypeName = "float", Name = "AttackCooldown" },
                            new BetterScriptableSchemaField { TypeName = "float", Name = "HealthPoint" },
                            new BetterScriptableSchemaField { TypeName = "float", Name = "AttackPoint" },
                        }
                    },
                    new BetterScriptableSchemaTable
                    {
                        RowTypeName = "SkillData",
                        FieldName = "SkillDatas",
                        Fields = new BetterScriptableSchemaField[]
                        {
                            new BetterScriptableSchemaField { TypeName = "int", Name = "Id" },
                        }
                    },
                }
            };

            BetterScriptableAssetFactory.CreatePair<CharacterDataAsset>("game_data_character", schema);
        }
    }
}

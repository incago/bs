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
                            new BetterScriptableSchemaField { TypeName = "float", Name = "Weight" },
                            new BetterScriptableSchemaField { TypeName = "string", Name = "ResourceName", IsDesignField = true },
                            new BetterScriptableSchemaField { TypeName = "string", Name = "ThumbnailResourceKey" },
                            new BetterScriptableSchemaField { TypeName = "string", Name = "SpineResourceKey" },
                        }
                    },
                }
            };

            BetterScriptableAssetFactory.CreatePair<CharacterDataAsset>("game_data_character", schema);
        }
    }
}

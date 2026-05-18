using BetterScriptable.Editor;
using UnityEditor;

internal static class StageDataAssetFactory
{
    [MenuItem("Assets/Create/BetterScriptable/game_data_stage")]
    private static void Create()
    {
        BetterScriptableDocumentSchema schema = new BetterScriptableDocumentSchema
        {
            AssetClassName = "StageDataAsset",
            NamespaceName = "",
            MenuPath = "BetterScriptable/game_data_stage",
            Fields = new BetterScriptableSchemaField[0],
            Tables = new BetterScriptableSchemaTable[]
            {
                new BetterScriptableSchemaTable
                {
                    RowTypeName = "StageData",
                    FieldName = "StageDatas",
                    Fields = new BetterScriptableSchemaField[]
                    {
                        new BetterScriptableSchemaField { TypeName = "int", Name = "Id" },
                        new BetterScriptableSchemaField { TypeName = "string", Name = "FirstName" },
                        new BetterScriptableSchemaField { TypeName = "string", Name = "LastName" },
                        new BetterScriptableSchemaField { TypeName = "float", Name = "Weight" },
                    }
                },
            }
        };

        BetterScriptableAssetFactory.CreatePair<StageDataAsset>("game_data_stage", schema);
    }
}

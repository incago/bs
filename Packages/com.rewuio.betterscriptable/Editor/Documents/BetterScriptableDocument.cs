using System;

namespace BetterScriptable.Editor
{
    [Serializable]
    public sealed class BetterScriptableDocument
    {
        public int Version = 2;
        public string AssetGuid = string.Empty;
        public string AssetPath = string.Empty;
        public string AssetTypeName = string.Empty;
        public string SerializedAssetJson = string.Empty;
        public BetterScriptableDocumentSchema Schema = new BetterScriptableDocumentSchema();
        public BetterScriptableSheetState[] Sheets = Array.Empty<BetterScriptableSheetState>();
    }

    [Serializable]
    public sealed class BetterScriptableDocumentSchema
    {
        public string AssetClassName = string.Empty;
        public string NamespaceName = string.Empty;
        public string MenuPath = string.Empty;
        public BetterScriptableSchemaField[] Fields = Array.Empty<BetterScriptableSchemaField>();
        public BetterScriptableSchemaTable[] Tables = Array.Empty<BetterScriptableSchemaTable>();
    }

    [Serializable]
    public sealed class BetterScriptableSchemaField
    {
        public string Id = string.Empty;
        public string TypeName = string.Empty;
        public string Name = string.Empty;
        public bool IsDesignField;
    }

    [Serializable]
    public sealed class BetterScriptableSchemaTable
    {
        public string RowTypeName = string.Empty;
        public string FieldName = string.Empty;
        public BetterScriptableSchemaField[] Fields = Array.Empty<BetterScriptableSchemaField>();
    }

    [Serializable]
    public sealed class BetterScriptableSheetState
    {
        public string ArrayFieldName = string.Empty;
        public BetterScriptableFormulaState[] Formulas = Array.Empty<BetterScriptableFormulaState>();
        public BetterScriptableCellState[] Cells = Array.Empty<BetterScriptableCellState>();
    }

    [Serializable]
    public sealed class BetterScriptableFormulaState
    {
        public string Id = string.Empty;
        public bool Enabled = true;
        public string Expression = string.Empty;
    }

    [Serializable]
    public sealed class BetterScriptableCellState
    {
        public int Row;
        public string ColumnId = string.Empty;
        public string ColumnName = string.Empty;
        public string Value = string.Empty;
        public string Formula = string.Empty;
        public string Note = string.Empty;
    }
}

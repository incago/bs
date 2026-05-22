using System;

namespace SpreadAsset.Editor
{
    [Serializable]
    public sealed class SpreadAssetDocument
    {
        public int Version = 2;
        public string AssetGuid = string.Empty;
        public string AssetPath = string.Empty;
        public string AssetTypeName = string.Empty;
        public string SerializedAssetJson = string.Empty;
        public SpreadAssetDocumentSchema Schema = new SpreadAssetDocumentSchema();
        public SpreadAssetSheetState[] Sheets = Array.Empty<SpreadAssetSheetState>();
    }

    [Serializable]
    public sealed class SpreadAssetDocumentSchema
    {
        public string AssetClassName = string.Empty;
        public string NamespaceName = string.Empty;
        public string MenuPath = string.Empty;
        public SpreadAssetSchemaField[] Fields = Array.Empty<SpreadAssetSchemaField>();
        public SpreadAssetSchemaTable[] Tables = Array.Empty<SpreadAssetSchemaTable>();
    }

    [Serializable]
    public sealed class SpreadAssetSchemaField
    {
        public string Id = string.Empty;
        public string TypeName = string.Empty;
        public string Name = string.Empty;
        public bool IsDesignField;
    }

    [Serializable]
    public sealed class SpreadAssetSchemaTable
    {
        public string RowTypeName = string.Empty;
        public string FieldName = string.Empty;
        public SpreadAssetSchemaField[] Fields = Array.Empty<SpreadAssetSchemaField>();
    }

    [Serializable]
    public sealed class SpreadAssetSheetState
    {
        public string ArrayFieldName = string.Empty;
        public SpreadAssetFormulaState[] Formulas = Array.Empty<SpreadAssetFormulaState>();
        public SpreadAssetColumnState[] Columns = Array.Empty<SpreadAssetColumnState>();
        public SpreadAssetCellState[] Cells = Array.Empty<SpreadAssetCellState>();
    }

    [Serializable]
    public sealed class SpreadAssetFormulaState
    {
        public string Id = string.Empty;
        public bool Enabled = true;
        public string Expression = string.Empty;
    }

    [Serializable]
    public sealed class SpreadAssetCellState
    {
        public int Row;
        public string ColumnId = string.Empty;
        public string ColumnName = string.Empty;
        public string Value = string.Empty;
        public string Formula = string.Empty;
        public string Note = string.Empty;
    }

    [Serializable]
    public sealed class SpreadAssetColumnState
    {
        public string ColumnId = string.Empty;
        public string ColumnName = string.Empty;
        public float Width;
    }
}

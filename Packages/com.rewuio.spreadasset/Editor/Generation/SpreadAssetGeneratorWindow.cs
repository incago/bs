using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SpreadAsset.Editor
{
    public sealed class SpreadAssetGeneratorWindow : EditorWindow
    {
        private const string WindowTitle = "SpreadAsset Generator";
        private static GUIStyle _columnLabelStyle;
        private static readonly string[] DataFieldTypeNames =
        {
            "string",
            "int",
            "float",
            "bool",
            "long",
            "double",
            "string[]",
            "int[]",
            "float[]",
            "bool[]",
            "long[]",
            "double[]",
            "Vector2",
            "Vector3",
            "Vector4",
            "Vector2Int",
            "Vector3Int",
            "Color",
            "Rect",
            "Bounds",
            "RectInt",
            "BoundsInt"
        };

        private readonly List<FieldDraft> _assetFields = new List<FieldDraft>();
        private readonly List<TableDraft> _tables = new List<TableDraft>();
        private Vector2 _scroll;

        [SerializeField] private string _assetClassName = "ItemDataAsset";
        [SerializeField] private string _namespaceName = SpreadAssetCodeGenerator.DefaultNamespace;
        [SerializeField] private string _menuPath = "SpreadAsset/game_data";
        [SerializeField] private string _outputDirectory = SpreadAssetCodeGenerator.DefaultOutputDirectory;
        [SerializeField] private string _loadedSourcePath = string.Empty;
        [SerializeField] private string _loadMessage = string.Empty;

        [MenuItem("Tools/SpreadAsset/Generator")]
        public static void Open()
        {
            GetWindow<SpreadAssetGeneratorWindow>(WindowTitle).Show();
        }

        private void OnEnable()
        {
            Selection.selectionChanged += OnSelectionChanged;
            if (_assetFields.Count == 0)
            {
                _assetFields.Add(new FieldDraft("string", "ItemCategory"));
            }

            if (_tables.Count == 0)
            {
                TableDraft table = new TableDraft("ItemData", "ItemDatas");
                table.Fields.Add(new FieldDraft("int", "Id", isKeyField: true));
                table.Fields.Add(new FieldDraft("float", "Weight"));
                _tables.Add(table);
            }

            TryLoadSelectedGeneratedClass();
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
        }

        private void OnSelectionChanged()
        {
            if (TryLoadSelectedGeneratedClass(force: false))
            {
                Repaint();
            }
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("ScriptableObject Schema", EditorStyles.boldLabel);
            _assetClassName = EditorGUILayout.TextField("Asset Class", _assetClassName);
            _namespaceName = EditorGUILayout.TextField("Namespace", _namespaceName);
            _menuPath = EditorGUILayout.TextField("Create Menu", _menuPath);
            _outputDirectory = EditorGUILayout.TextField("Output Directory", _outputDirectory);
            if (GUILayout.Button("Load Selected Generated Class"))
            {
                if (!TryLoadSelectedGeneratedClass(force: true))
                {
                    _loadMessage = "Select a generated asset class script or its generated factory script.";
                }
            }

            if (!string.IsNullOrEmpty(_loadMessage))
            {
                EditorGUILayout.HelpBox(_loadMessage, MessageType.Info);
            }

            EditorGUILayout.Space(10);
            DrawAssetFields();

            EditorGUILayout.Space(10);
            DrawTables();

            EditorGUILayout.Space(16);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Generate", GUILayout.Width(160), GUILayout.Height(32)))
                {
                    Generate();
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawAssetFields()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Asset Fields", EditorStyles.boldLabel);
                for (int i = 0; i < _assetFields.Count; i++)
                {
                    DrawFieldDraft(_assetFields, i, allowDesignField: false);
                }

                if (GUILayout.Button("Add Asset Field"))
                {
                    _assetFields.Add(new FieldDraft("string", "NewField"));
                }
            }
        }

        private void DrawTables()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Array Data Tables", EditorStyles.boldLabel);
                for (int i = 0; i < _tables.Count; i++)
                {
                    DrawTableDraft(i);
                }

                if (GUILayout.Button("Add Array Data"))
                {
                    TableDraft table = new TableDraft("NewData", "NewDatas");
                    table.Fields.Add(new FieldDraft("int", "Id", isKeyField: true));
                    _tables.Add(table);
                }
            }
        }

        private void DrawTableDraft(int index)
        {
            TableDraft table = _tables[index];
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    table.RowTypeName = EditorGUILayout.TextField("Data Class", table.RowTypeName);
                    if (GUILayout.Button("Remove", GUILayout.Width(80)))
                    {
                        _tables.RemoveAt(index);
                        GUIUtility.ExitGUI();
                    }
                }

                table.OmitArrayField = EditorGUILayout.ToggleLeft(
                    "Data class only (do not generate asset array field)",
                    table.OmitArrayField);
                using (new EditorGUI.DisabledScope(table.OmitArrayField))
                {
                    table.FieldName = EditorGUILayout.TextField("Array Field", table.FieldName);
                }

                EditorGUILayout.LabelField("Data Fields", EditorStyles.miniBoldLabel);
                string[] dataFieldTypeNames = CreateDataFieldTypeNames(table);
                for (int i = 0; i < table.Fields.Count; i++)
                {
                    DrawFieldDraft(table.Fields, i, allowDesignField: true, dataFieldTypeNames);
                }

                if (GUILayout.Button("Add Data Field"))
                {
                    table.Fields.Add(new FieldDraft("string", "NewField"));
                }
            }
        }

        private static void DrawFieldDraft(
            List<FieldDraft> fields,
            int index,
            bool allowDesignField,
            string[] dataFieldTypeNames = null)
        {
            FieldDraft field = fields[index];
            using (new EditorGUILayout.HorizontalScope())
            {
                if (allowDesignField)
                {
                    DrawColumnLabel(index);
                }

                field.TypeName = allowDesignField
                    ? DrawDataFieldTypePopup(field.TypeName, dataFieldTypeNames)
                    : EditorGUILayout.TextField(field.TypeName, GUILayout.MinWidth(90));
                field.Name = EditorGUILayout.TextField(field.Name, GUILayout.MinWidth(120));
                if (allowDesignField)
                {
                    bool nextKeyField = EditorGUILayout.ToggleLeft(
                        "Key",
                        field.IsKeyField,
                        GUILayout.Width(56));
                    if (nextKeyField != field.IsKeyField)
                    {
                        field.IsKeyField = nextKeyField;
                        if (field.IsKeyField)
                        {
                            field.IsDesignField = false;
                        }
                    }

                    bool nextDesignField = EditorGUILayout.ToggleLeft(
                        "Design",
                        field.IsDesignField,
                        GUILayout.Width(72));
                    if (nextDesignField != field.IsDesignField)
                    {
                        field.IsDesignField = nextDesignField;
                        if (field.IsDesignField)
                        {
                            field.IsKeyField = false;
                        }
                    }

                    using (new EditorGUI.DisabledScope(index <= 0))
                    {
                        if (GUILayout.Button("Up", GUILayout.Width(42)))
                        {
                            MoveFieldDraft(fields, index, index - 1);
                            GUIUtility.ExitGUI();
                        }
                    }

                    using (new EditorGUI.DisabledScope(index >= fields.Count - 1))
                    {
                        if (GUILayout.Button("Down", GUILayout.Width(52)))
                        {
                            MoveFieldDraft(fields, index, index + 1);
                            GUIUtility.ExitGUI();
                        }
                    }
                }

                if (GUILayout.Button("-", GUILayout.Width(28)))
                {
                    fields.RemoveAt(index);
                    GUIUtility.ExitGUI();
                }
            }
        }

        private static void MoveFieldDraft(List<FieldDraft> fields, int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || fromIndex >= fields.Count || toIndex < 0 || toIndex >= fields.Count)
            {
                return;
            }

            FieldDraft field = fields[fromIndex];
            fields.RemoveAt(fromIndex);
            fields.Insert(toIndex, field);
        }

        private static string GetColumnName(int zeroBasedIndex)
        {
            int index = zeroBasedIndex + 1;
            string columnName = string.Empty;

            while (index > 0)
            {
                index--;
                columnName = (char)('A' + index % 26) + columnName;
                index /= 26;
            }

            return columnName;
        }

        private static void DrawColumnLabel(int zeroBasedIndex)
        {
            Rect rect = GUILayoutUtility.GetRect(
                32f,
                EditorGUIUtility.singleLineHeight,
                GUILayout.Width(32f),
                GUILayout.Height(EditorGUIUtility.singleLineHeight));
            rect.y += 2f;
            GUI.Label(rect, GetColumnName(zeroBasedIndex), ColumnLabelStyle);
        }

        private static GUIStyle ColumnLabelStyle
        {
            get
            {
                if (_columnLabelStyle == null)
                {
                    _columnLabelStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        padding = new RectOffset(0, 0, 0, 0),
                        margin = new RectOffset(0, 2, 0, 0)
                    };
                }

                return _columnLabelStyle;
            }
        }

        private string[] CreateDataFieldTypeNames(TableDraft ownerTable)
        {
            List<string> typeNames = new List<string>(DataFieldTypeNames);
            HashSet<string> usedTypeNames = new HashSet<string>(DataFieldTypeNames, StringComparer.Ordinal);
            string ownerRowTypeName = NormalizeDataClassTypeName(ownerTable?.RowTypeName);
            foreach (TableDraft table in _tables)
            {
                string rowTypeName = NormalizeDataClassTypeName(table?.RowTypeName);
                if (string.IsNullOrEmpty(rowTypeName)
                    || string.Equals(rowTypeName, ownerRowTypeName, StringComparison.Ordinal))
                {
                    continue;
                }

                AddDataFieldTypeName(typeNames, usedTypeNames, rowTypeName);
                AddDataFieldTypeName(typeNames, usedTypeNames, rowTypeName + "[]");
            }

            return typeNames.ToArray();
        }

        private static void AddDataFieldTypeName(
            List<string> typeNames,
            HashSet<string> usedTypeNames,
            string typeName)
        {
            if (usedTypeNames.Add(typeName))
            {
                typeNames.Add(typeName);
            }
        }

        private static string DrawDataFieldTypePopup(string currentTypeName, string[] dataFieldTypeNames)
        {
            if (!IsSupportedDataFieldType(currentTypeName, dataFieldTypeNames))
            {
                EditorGUILayout.LabelField(
                    string.IsNullOrWhiteSpace(currentTypeName)
                        ? "Unsupported"
                        : currentTypeName.Trim() + " (unsupported)",
                    EditorStyles.miniLabel,
                    GUILayout.MinWidth(120));
                if (GUILayout.Button("Reset", GUILayout.Width(52)))
                {
                    return "int";
                }

                return currentTypeName;
            }

            SpreadAssetEnumTypeUtility.TypePopupOptions options =
                SpreadAssetEnumTypeUtility.CreatePopupOptions(dataFieldTypeNames ?? DataFieldTypeNames, currentTypeName);
            int selectedIndex = EditorGUILayout.Popup(
                options.SelectedIndex,
                options.DisplayNames,
                GUILayout.MinWidth(90));
            return options.TypeNames[Mathf.Clamp(selectedIndex, 0, options.TypeNames.Length - 1)];
        }

        private static bool IsSupportedDataFieldType(string typeName, string[] dataFieldTypeNames)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return true;
            }

            string normalizedTypeName = typeName.Trim();
            foreach (string dataFieldTypeName in dataFieldTypeNames ?? DataFieldTypeNames)
            {
                if (string.Equals(dataFieldTypeName, normalizedTypeName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return SpreadAssetEnumTypeUtility.TryGetAnnotatedEnumType(normalizedTypeName, out _);
        }

        private static string NormalizeDataClassTypeName(string typeName)
        {
            return SpreadAssetNameUtility.ToPascalCase(typeName?.Trim() ?? string.Empty);
        }

        private void Generate()
        {
            try
            {
                SpreadAssetGenerationRequest request = CreateRequest();
                if (!ConfirmPotentialFormulaColumnChanges(request))
                {
                    return;
                }

                SpreadAssetCodeGenerator.Generate(request);
                _loadedSourcePath = string.Empty;
                _loadMessage = "Generated scripts. Select the generated asset class later to load this schema again.";
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("SpreadAsset Generation Failed", exception.Message, "OK");
            }
        }

        private static bool ConfirmPotentialFormulaColumnChanges(SpreadAssetGenerationRequest nextRequest)
        {
            if (!TryLoadExistingGenerationRequest(nextRequest, out SpreadAssetGenerationRequest previousRequest)
                || !TryCreateFormulaColumnWarning(previousRequest.Schema, nextRequest.Schema, out string warningMessage))
            {
                return true;
            }

            return EditorUtility.DisplayDialog(
                "Formula column references may change",
                warningMessage,
                "Generate Anyway",
                "Cancel");
        }

        private static bool TryLoadExistingGenerationRequest(
            SpreadAssetGenerationRequest nextRequest,
            out SpreadAssetGenerationRequest previousRequest)
        {
            previousRequest = null;
            string outputDirectory = (nextRequest.OutputDirectory ?? string.Empty).Trim().Replace('\\', '/');
            string assetClassName = (nextRequest.AssetClassName ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(outputDirectory) || string.IsNullOrEmpty(assetClassName))
            {
                return false;
            }

            string runtimePath = $"{outputDirectory}/{assetClassName}.cs";
            if (File.Exists(runtimePath)
                && SpreadAssetGeneratedClassLoader.TryLoadFromRuntimeScript(
                    runtimePath,
                    out previousRequest,
                    out _,
                    out _))
            {
                return true;
            }

            string factoryPath = $"{outputDirectory}/Editor/{assetClassName}Factory.cs";
            return File.Exists(factoryPath)
                && SpreadAssetGeneratedClassLoader.TryLoadFromFactoryScript(
                    factoryPath,
                    out previousRequest,
                    out _,
                    out _);
        }

        private static bool TryCreateFormulaColumnWarning(
            SpreadAssetDocumentSchema previousSchema,
            SpreadAssetDocumentSchema nextSchema,
            out string warningMessage)
        {
            warningMessage = string.Empty;
            List<string> changedTables = new List<string>();
            SpreadAssetSchemaTable[] previousTables = previousSchema?.Tables ?? Array.Empty<SpreadAssetSchemaTable>();

            foreach (SpreadAssetSchemaTable previousTable in previousTables)
            {
                if (previousTable == null || previousTable.OmitArrayField || string.IsNullOrEmpty(previousTable.FieldName))
                {
                    continue;
                }

                SpreadAssetSchemaTable nextTable = FindMatchingTable(nextSchema, previousTable);
                if (nextTable == null)
                {
                    changedTables.Add($"- {previousTable.FieldName}: table was removed or renamed.");
                    continue;
                }

                if (!previousTable.OmitArrayField && nextTable.OmitArrayField)
                {
                    changedTables.Add($"- {previousTable.FieldName}: asset array field will no longer be generated.");
                    continue;
                }

                if (!IsSimpleFieldAppend(previousTable.Fields, nextTable.Fields))
                {
                    changedTables.Add(
                        $"- {previousTable.FieldName}: columns changed from [{FormatFieldList(previousTable.Fields)}] to [{FormatFieldList(nextTable.Fields)}].");
                }
            }

            if (changedTables.Count == 0)
            {
                return false;
            }

            warningMessage =
                "This appears to regenerate an existing SpreadAsset class.\n\n" +
                "One or more array data fields changed in a way that is not a simple append to the end. Existing formulas that reference columns such as A, B, C may no longer point to the intended fields.\n\n" +
                "Affected tables:\n" +
                string.Join("\n", changedTables) +
                "\n\nDo you want to continue generation?";
            return true;
        }

        private static SpreadAssetSchemaTable FindMatchingTable(
            SpreadAssetDocumentSchema schema,
            SpreadAssetSchemaTable table)
        {
            SpreadAssetSchemaTable[] tables = schema?.Tables ?? Array.Empty<SpreadAssetSchemaTable>();
            foreach (SpreadAssetSchemaTable candidate in tables)
            {
                if (candidate == null)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(candidate.FieldName)
                    && !string.IsNullOrEmpty(table.FieldName)
                    && IsSameSchemaName(candidate.FieldName, table.FieldName))
                {
                    return candidate;
                }

                if (candidate.OmitArrayField
                    && table.OmitArrayField
                    && IsSameSchemaName(candidate.RowTypeName, table.RowTypeName))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static bool IsSimpleFieldAppend(
            SpreadAssetSchemaField[] previousFields,
            SpreadAssetSchemaField[] nextFields)
        {
            previousFields = previousFields ?? Array.Empty<SpreadAssetSchemaField>();
            nextFields = nextFields ?? Array.Empty<SpreadAssetSchemaField>();
            if (nextFields.Length < previousFields.Length)
            {
                return false;
            }

            for (int i = 0; i < previousFields.Length; i++)
            {
                if (!IsSameSchemaField(previousFields[i], nextFields[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsSameSchemaField(SpreadAssetSchemaField previousField, SpreadAssetSchemaField nextField)
        {
            if (previousField == null || nextField == null)
            {
                return previousField == nextField;
            }

            if (SpreadAssetSchemaUtility.HasFieldId(previousField)
                && SpreadAssetSchemaUtility.HasFieldId(nextField))
            {
                return SpreadAssetSchemaUtility.AreSameFieldId(previousField, nextField)
                    && string.Equals(NormalizeTypeName(previousField.TypeName), NormalizeTypeName(nextField.TypeName), StringComparison.Ordinal)
                    && previousField.IsDesignField == nextField.IsDesignField
                    && previousField.IsKeyField == nextField.IsKeyField;
            }

            return IsSameSchemaName(previousField.Name, nextField.Name)
                && string.Equals(NormalizeTypeName(previousField.TypeName), NormalizeTypeName(nextField.TypeName), StringComparison.Ordinal)
                && previousField.IsDesignField == nextField.IsDesignField
                && previousField.IsKeyField == nextField.IsKeyField;
        }

        private static bool IsSameSchemaName(string left, string right)
        {
            return string.Equals(
                SpreadAssetNameUtility.ToPascalCase(left ?? string.Empty),
                SpreadAssetNameUtility.ToPascalCase(right ?? string.Empty),
                StringComparison.Ordinal);
        }

        private static string NormalizeTypeName(string typeName)
        {
            return (typeName ?? string.Empty).Trim();
        }

        private static string FormatFieldList(SpreadAssetSchemaField[] fields)
        {
            fields = fields ?? Array.Empty<SpreadAssetSchemaField>();
            if (fields.Length == 0)
            {
                return "none";
            }

            List<string> labels = new List<string>();
            for (int i = 0; i < fields.Length; i++)
            {
                SpreadAssetSchemaField field = fields[i];
                if (field == null)
                {
                    continue;
                }

                List<string> flags = new List<string>();
                if (field.IsKeyField)
                {
                    flags.Add("key");
                }

                if (field.IsDesignField)
                {
                    flags.Add("design");
                }

                string flagLabel = flags.Count == 0 ? string.Empty : ", " + string.Join(", ", flags);
                labels.Add($"{GetColumnName(i)} {field.Name}({field.TypeName}{flagLabel})");
            }

            return string.Join(", ", labels);
        }

        private bool TryLoadSelectedGeneratedClass(bool force = false)
        {
            if (!SpreadAssetGeneratedClassLoader.TryLoadFromSelection(
                    out SpreadAssetGenerationRequest request,
                    out string sourcePath,
                    out string error))
            {
                if (!string.IsNullOrEmpty(error))
                {
                    _loadMessage = error;
                }

                return false;
            }

            if (!force && sourcePath == _loadedSourcePath)
            {
                return false;
            }

            ApplyRequest(request);
            _loadedSourcePath = sourcePath;
            _loadMessage = $"Loaded generator settings from {sourcePath}.";
            return true;
        }

        private void ApplyRequest(SpreadAssetGenerationRequest request)
        {
            SpreadAssetSchemaUtility.EnsureFieldIds(request.Schema);
            _assetClassName = request.AssetClassName;
            _namespaceName = request.NamespaceName;
            _menuPath = request.MenuPath;
            _outputDirectory = request.OutputDirectory;

            _assetFields.Clear();
            foreach (SpreadAssetSchemaField field in request.Schema.Fields)
            {
                _assetFields.Add(new FieldDraft(field.TypeName, field.Name, false, false, field.Id));
            }

            _tables.Clear();
            foreach (SpreadAssetSchemaTable table in request.Schema.Tables)
            {
                TableDraft tableDraft = new TableDraft(table.RowTypeName, table.FieldName, table.OmitArrayField);
                foreach (SpreadAssetSchemaField field in table.Fields)
                {
                    tableDraft.Fields.Add(new FieldDraft(
                        field.TypeName,
                        field.Name,
                        field.IsDesignField,
                        field.IsKeyField,
                        field.Id));
                }

                _tables.Add(tableDraft);
            }
        }

        private SpreadAssetGenerationRequest CreateRequest()
        {
            SpreadAssetDocumentSchema schema = new SpreadAssetDocumentSchema
            {
                AssetClassName = _assetClassName.Trim(),
                NamespaceName = _namespaceName.Trim(),
                MenuPath = _menuPath.Trim(),
                Fields = ToSchemaFields(_assetFields),
                Tables = ToSchemaTables(_tables)
            };
            SpreadAssetSchemaUtility.EnsureFieldIds(schema);

            return new SpreadAssetGenerationRequest
            {
                AssetClassName = _assetClassName.Trim(),
                NamespaceName = _namespaceName.Trim(),
                MenuPath = _menuPath.Trim(),
                OutputDirectory = _outputDirectory.Trim(),
                Schema = schema
            };
        }

        private static SpreadAssetSchemaField[] ToSchemaFields(List<FieldDraft> fields)
        {
            List<SpreadAssetSchemaField> schemaFields = new List<SpreadAssetSchemaField>();
            foreach (FieldDraft field in fields)
            {
                if (field.IsEmpty)
                {
                    continue;
                }

                schemaFields.Add(new SpreadAssetSchemaField
                {
                    Id = string.IsNullOrWhiteSpace(field.Id)
                        ? SpreadAssetSchemaUtility.CreateNewFieldId()
                        : field.Id.Trim(),
                    TypeName = field.TypeName.Trim(),
                    Name = SpreadAssetNameUtility.ToPascalCase(field.Name),
                    IsDesignField = field.IsDesignField,
                    IsKeyField = field.IsDesignField ? false : field.IsKeyField
                });
            }

            return schemaFields.ToArray();
        }

        private static SpreadAssetSchemaTable[] ToSchemaTables(List<TableDraft> tables)
        {
            List<SpreadAssetSchemaTable> schemaTables = new List<SpreadAssetSchemaTable>();
            foreach (TableDraft table in tables)
            {
                if (table.IsEmpty)
                {
                    continue;
                }

                schemaTables.Add(new SpreadAssetSchemaTable
                {
                    RowTypeName = table.RowTypeName.Trim(),
                    FieldName = SpreadAssetNameUtility.ToPascalCase(table.FieldName),
                    OmitArrayField = table.OmitArrayField,
                    Fields = ToSchemaFields(table.Fields)
                });
            }

            return schemaTables.ToArray();
        }

        [Serializable]
        private sealed class FieldDraft
        {
            public string TypeName;
            public string Name;
            public bool IsDesignField;
            public bool IsKeyField;
            public string Id;

            public FieldDraft(
                string typeName,
                string name,
                bool isDesignField = false,
                bool isKeyField = false,
                string id = "")
            {
                TypeName = typeName;
                Name = name;
                IsDesignField = isDesignField;
                IsKeyField = isDesignField ? false : isKeyField;
                Id = string.IsNullOrWhiteSpace(id)
                    ? SpreadAssetSchemaUtility.CreateNewFieldId()
                    : id.Trim();
            }

            public bool IsEmpty => string.IsNullOrWhiteSpace(TypeName) && string.IsNullOrWhiteSpace(Name);
        }

        [Serializable]
        private sealed class TableDraft
        {
            public string RowTypeName;
            public string FieldName;
            public bool OmitArrayField;
            public readonly List<FieldDraft> Fields = new List<FieldDraft>();

            public TableDraft(string rowTypeName, string fieldName, bool omitArrayField = false)
            {
                RowTypeName = rowTypeName;
                FieldName = fieldName;
                OmitArrayField = omitArrayField;
            }

            public bool IsEmpty => string.IsNullOrWhiteSpace(RowTypeName) && string.IsNullOrWhiteSpace(FieldName);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BetterScriptable.Editor
{
    public sealed class BetterScriptableGeneratorWindow : EditorWindow
    {
        private const string WindowTitle = "BetterScriptable Generator";
        private static GUIStyle _columnLabelStyle;
        private static readonly string[] DataFieldTypeNames =
        {
            "string",
            "int",
            "float",
            "bool",
            "long",
            "double",
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
        [SerializeField] private string _namespaceName = BetterScriptableCodeGenerator.DefaultNamespace;
        [SerializeField] private string _menuPath = "BetterScriptable/game_data";
        [SerializeField] private string _outputDirectory = BetterScriptableCodeGenerator.DefaultOutputDirectory;
        [SerializeField] private string _loadedSourcePath = string.Empty;
        [SerializeField] private string _loadMessage = string.Empty;

        [MenuItem("Tools/BetterScriptable/Generator")]
        public static void Open()
        {
            GetWindow<BetterScriptableGeneratorWindow>(WindowTitle).Show();
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
                table.Fields.Add(new FieldDraft("int", "Id"));
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
                    table.Fields.Add(new FieldDraft("int", "Id"));
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

                table.FieldName = EditorGUILayout.TextField("Array Field", table.FieldName);
                EditorGUILayout.LabelField("Data Fields", EditorStyles.miniBoldLabel);
                for (int i = 0; i < table.Fields.Count; i++)
                {
                    DrawFieldDraft(table.Fields, i, allowDesignField: true);
                }

                if (GUILayout.Button("Add Data Field"))
                {
                    table.Fields.Add(new FieldDraft("string", "NewField"));
                }
            }
        }

        private static void DrawFieldDraft(List<FieldDraft> fields, int index, bool allowDesignField)
        {
            FieldDraft field = fields[index];
            using (new EditorGUILayout.HorizontalScope())
            {
                if (allowDesignField)
                {
                    DrawColumnLabel(index);
                }

                field.TypeName = allowDesignField
                    ? DrawDataFieldTypePopup(field.TypeName)
                    : EditorGUILayout.TextField(field.TypeName, GUILayout.MinWidth(90));
                field.Name = EditorGUILayout.TextField(field.Name, GUILayout.MinWidth(120));
                if (allowDesignField)
                {
                    field.IsDesignField = EditorGUILayout.ToggleLeft(
                        "Design",
                        field.IsDesignField,
                        GUILayout.Width(72));

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

        private static string DrawDataFieldTypePopup(string currentTypeName)
        {
            string normalizedCurrentTypeName = string.IsNullOrWhiteSpace(currentTypeName)
                ? DataFieldTypeNames[0]
                : currentTypeName.Trim();
            int selectedIndex = Array.IndexOf(DataFieldTypeNames, normalizedCurrentTypeName);
            string[] typeNames = DataFieldTypeNames;

            if (selectedIndex < 0)
            {
                typeNames = new string[DataFieldTypeNames.Length + 1];
                typeNames[0] = normalizedCurrentTypeName;
                Array.Copy(DataFieldTypeNames, 0, typeNames, 1, DataFieldTypeNames.Length);
                selectedIndex = 0;
            }

            selectedIndex = EditorGUILayout.Popup(selectedIndex, typeNames, GUILayout.MinWidth(90));
            return typeNames[Mathf.Clamp(selectedIndex, 0, typeNames.Length - 1)];
        }

        private void Generate()
        {
            try
            {
                BetterScriptableGenerationRequest request = CreateRequest();
                if (!ConfirmPotentialFormulaColumnChanges(request))
                {
                    return;
                }

                BetterScriptableCodeGenerator.Generate(request);
                _loadedSourcePath = string.Empty;
                _loadMessage = "Generated scripts. Select the generated asset class later to load this schema again.";
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("BetterScriptable Generation Failed", exception.Message, "OK");
            }
        }

        private static bool ConfirmPotentialFormulaColumnChanges(BetterScriptableGenerationRequest nextRequest)
        {
            if (!TryLoadExistingGenerationRequest(nextRequest, out BetterScriptableGenerationRequest previousRequest)
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
            BetterScriptableGenerationRequest nextRequest,
            out BetterScriptableGenerationRequest previousRequest)
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
                && BetterScriptableGeneratedClassLoader.TryLoadFromRuntimeScript(
                    runtimePath,
                    out previousRequest,
                    out _,
                    out _))
            {
                return true;
            }

            string factoryPath = $"{outputDirectory}/Editor/{assetClassName}Factory.cs";
            return File.Exists(factoryPath)
                && BetterScriptableGeneratedClassLoader.TryLoadFromFactoryScript(
                    factoryPath,
                    out previousRequest,
                    out _,
                    out _);
        }

        private static bool TryCreateFormulaColumnWarning(
            BetterScriptableDocumentSchema previousSchema,
            BetterScriptableDocumentSchema nextSchema,
            out string warningMessage)
        {
            warningMessage = string.Empty;
            List<string> changedTables = new List<string>();
            BetterScriptableSchemaTable[] previousTables = previousSchema?.Tables ?? Array.Empty<BetterScriptableSchemaTable>();

            foreach (BetterScriptableSchemaTable previousTable in previousTables)
            {
                if (previousTable == null || string.IsNullOrEmpty(previousTable.FieldName))
                {
                    continue;
                }

                BetterScriptableSchemaTable nextTable = FindMatchingTable(nextSchema, previousTable);
                if (nextTable == null)
                {
                    changedTables.Add($"- {previousTable.FieldName}: table was removed or renamed.");
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
                "This appears to regenerate an existing BetterScriptable class.\n\n" +
                "One or more array data fields changed in a way that is not a simple append to the end. Existing formulas that reference columns such as A, B, C may no longer point to the intended fields.\n\n" +
                "Affected tables:\n" +
                string.Join("\n", changedTables) +
                "\n\nDo you want to continue generation?";
            return true;
        }

        private static BetterScriptableSchemaTable FindMatchingTable(
            BetterScriptableDocumentSchema schema,
            BetterScriptableSchemaTable table)
        {
            BetterScriptableSchemaTable[] tables = schema?.Tables ?? Array.Empty<BetterScriptableSchemaTable>();
            foreach (BetterScriptableSchemaTable candidate in tables)
            {
                if (candidate == null)
                {
                    continue;
                }

                if (IsSameSchemaName(candidate.FieldName, table.FieldName))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static bool IsSimpleFieldAppend(
            BetterScriptableSchemaField[] previousFields,
            BetterScriptableSchemaField[] nextFields)
        {
            previousFields = previousFields ?? Array.Empty<BetterScriptableSchemaField>();
            nextFields = nextFields ?? Array.Empty<BetterScriptableSchemaField>();
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

        private static bool IsSameSchemaField(BetterScriptableSchemaField previousField, BetterScriptableSchemaField nextField)
        {
            if (previousField == null || nextField == null)
            {
                return previousField == nextField;
            }

            if (BetterScriptableSchemaUtility.HasFieldId(previousField)
                && BetterScriptableSchemaUtility.HasFieldId(nextField))
            {
                return BetterScriptableSchemaUtility.AreSameFieldId(previousField, nextField)
                    && string.Equals(NormalizeTypeName(previousField.TypeName), NormalizeTypeName(nextField.TypeName), StringComparison.Ordinal)
                    && previousField.IsDesignField == nextField.IsDesignField;
            }

            return IsSameSchemaName(previousField.Name, nextField.Name)
                && string.Equals(NormalizeTypeName(previousField.TypeName), NormalizeTypeName(nextField.TypeName), StringComparison.Ordinal)
                && previousField.IsDesignField == nextField.IsDesignField;
        }

        private static bool IsSameSchemaName(string left, string right)
        {
            return string.Equals(
                BetterScriptableNameUtility.ToPascalCase(left ?? string.Empty),
                BetterScriptableNameUtility.ToPascalCase(right ?? string.Empty),
                StringComparison.Ordinal);
        }

        private static string NormalizeTypeName(string typeName)
        {
            return (typeName ?? string.Empty).Trim();
        }

        private static string FormatFieldList(BetterScriptableSchemaField[] fields)
        {
            fields = fields ?? Array.Empty<BetterScriptableSchemaField>();
            if (fields.Length == 0)
            {
                return "none";
            }

            List<string> labels = new List<string>();
            for (int i = 0; i < fields.Length; i++)
            {
                BetterScriptableSchemaField field = fields[i];
                if (field == null)
                {
                    continue;
                }

                string designLabel = field.IsDesignField ? ", design" : string.Empty;
                labels.Add($"{GetColumnName(i)} {field.Name}({field.TypeName}{designLabel})");
            }

            return string.Join(", ", labels);
        }

        private bool TryLoadSelectedGeneratedClass(bool force = false)
        {
            if (!BetterScriptableGeneratedClassLoader.TryLoadFromSelection(
                    out BetterScriptableGenerationRequest request,
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

        private void ApplyRequest(BetterScriptableGenerationRequest request)
        {
            BetterScriptableSchemaUtility.EnsureFieldIds(request.Schema);
            _assetClassName = request.AssetClassName;
            _namespaceName = request.NamespaceName;
            _menuPath = request.MenuPath;
            _outputDirectory = request.OutputDirectory;

            _assetFields.Clear();
            foreach (BetterScriptableSchemaField field in request.Schema.Fields)
            {
                _assetFields.Add(new FieldDraft(field.TypeName, field.Name, false, field.Id));
            }

            _tables.Clear();
            foreach (BetterScriptableSchemaTable table in request.Schema.Tables)
            {
                TableDraft tableDraft = new TableDraft(table.RowTypeName, table.FieldName);
                foreach (BetterScriptableSchemaField field in table.Fields)
                {
                    tableDraft.Fields.Add(new FieldDraft(field.TypeName, field.Name, field.IsDesignField, field.Id));
                }

                _tables.Add(tableDraft);
            }
        }

        private BetterScriptableGenerationRequest CreateRequest()
        {
            BetterScriptableDocumentSchema schema = new BetterScriptableDocumentSchema
            {
                AssetClassName = _assetClassName.Trim(),
                NamespaceName = _namespaceName.Trim(),
                MenuPath = _menuPath.Trim(),
                Fields = ToSchemaFields(_assetFields),
                Tables = ToSchemaTables(_tables)
            };
            BetterScriptableSchemaUtility.EnsureFieldIds(schema);

            return new BetterScriptableGenerationRequest
            {
                AssetClassName = _assetClassName.Trim(),
                NamespaceName = _namespaceName.Trim(),
                MenuPath = _menuPath.Trim(),
                OutputDirectory = _outputDirectory.Trim(),
                Schema = schema
            };
        }

        private static BetterScriptableSchemaField[] ToSchemaFields(List<FieldDraft> fields)
        {
            List<BetterScriptableSchemaField> schemaFields = new List<BetterScriptableSchemaField>();
            foreach (FieldDraft field in fields)
            {
                if (field.IsEmpty)
                {
                    continue;
                }

                schemaFields.Add(new BetterScriptableSchemaField
                {
                    Id = string.IsNullOrWhiteSpace(field.Id)
                        ? BetterScriptableSchemaUtility.CreateNewFieldId()
                        : field.Id.Trim(),
                    TypeName = field.TypeName.Trim(),
                    Name = BetterScriptableNameUtility.ToPascalCase(field.Name),
                    IsDesignField = field.IsDesignField
                });
            }

            return schemaFields.ToArray();
        }

        private static BetterScriptableSchemaTable[] ToSchemaTables(List<TableDraft> tables)
        {
            List<BetterScriptableSchemaTable> schemaTables = new List<BetterScriptableSchemaTable>();
            foreach (TableDraft table in tables)
            {
                if (table.IsEmpty)
                {
                    continue;
                }

                schemaTables.Add(new BetterScriptableSchemaTable
                {
                    RowTypeName = table.RowTypeName.Trim(),
                    FieldName = BetterScriptableNameUtility.ToPascalCase(table.FieldName),
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
            public string Id;

            public FieldDraft(string typeName, string name, bool isDesignField = false, string id = "")
            {
                TypeName = typeName;
                Name = name;
                IsDesignField = isDesignField;
                Id = string.IsNullOrWhiteSpace(id)
                    ? BetterScriptableSchemaUtility.CreateNewFieldId()
                    : id.Trim();
            }

            public bool IsEmpty => string.IsNullOrWhiteSpace(TypeName) && string.IsNullOrWhiteSpace(Name);
        }

        [Serializable]
        private sealed class TableDraft
        {
            public string RowTypeName;
            public string FieldName;
            public readonly List<FieldDraft> Fields = new List<FieldDraft>();

            public TableDraft(string rowTypeName, string fieldName)
            {
                RowTypeName = rowTypeName;
                FieldName = fieldName;
            }

            public bool IsEmpty => string.IsNullOrWhiteSpace(RowTypeName) && string.IsNullOrWhiteSpace(FieldName);
        }
    }
}

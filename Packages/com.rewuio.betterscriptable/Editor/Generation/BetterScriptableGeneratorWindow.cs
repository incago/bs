using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BetterScriptable.Editor
{
    public sealed class BetterScriptableGeneratorWindow : EditorWindow
    {
        private const string WindowTitle = "BetterScriptable Generator";
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
                }

                if (GUILayout.Button("-", GUILayout.Width(28)))
                {
                    fields.RemoveAt(index);
                    GUIUtility.ExitGUI();
                }
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
                BetterScriptableCodeGenerator.Generate(CreateRequest());
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
            _assetClassName = request.AssetClassName;
            _namespaceName = request.NamespaceName;
            _menuPath = request.MenuPath;
            _outputDirectory = request.OutputDirectory;

            _assetFields.Clear();
            foreach (BetterScriptableSchemaField field in request.Schema.Fields)
            {
                _assetFields.Add(new FieldDraft(field.TypeName, field.Name));
            }

            _tables.Clear();
            foreach (BetterScriptableSchemaTable table in request.Schema.Tables)
            {
                TableDraft tableDraft = new TableDraft(table.RowTypeName, table.FieldName);
                foreach (BetterScriptableSchemaField field in table.Fields)
                {
                    tableDraft.Fields.Add(new FieldDraft(field.TypeName, field.Name, field.IsDesignField));
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

            public FieldDraft(string typeName, string name, bool isDesignField = false)
            {
                TypeName = typeName;
                Name = name;
                IsDesignField = isDesignField;
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

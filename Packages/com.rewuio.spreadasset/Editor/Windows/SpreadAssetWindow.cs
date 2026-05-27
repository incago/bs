using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SpreadAsset.Editor
{
    public sealed class SpreadAssetWindow : EditorWindow
    {
        private const string WindowTitle = "SpreadAsset Editor";
        private const float RowNumberWidth = 44f;
        private const float RowButtonWidth = 28f;
        private const float MinimumColumnWidth = 100f;
        private const float MaximumColumnWidth = 360f;
        private const float DefaultColumnWidth = MinimumColumnWidth;
        private const float ManualMinimumColumnWidth = 40f;
        private const float ManualMaximumColumnWidth = 720f;
        private const float ColumnResizeHandleWidth = 6f;
        private const float FormulaRowHeight = 22f;
        private const float TableRowHeight = 24f;
        private const float ColumnLetterHeaderHeight = 20f;
        private const float ColumnHeaderGap = 1f;
        private const float ColumnHeaderLabelHeight = 31f;
        private const float ColumnHeaderBottomPadding = 1f;
        private const float TableHeaderHeight =
            ColumnLetterHeaderHeight + ColumnHeaderGap + ColumnHeaderLabelHeight + ColumnHeaderBottomPadding;
        private const float HorizontalScrollbarHeight = 16f;
        private const float HorizontalWheelScrollSpeed = 24f;
        private const float TableLayoutPadding = 6f;
        private const float CellControlHorizontalPadding = 4f;
        private const float CellControlVerticalPadding = 3f;
        private const float CellMinimumLabelWidth = 24f;
        private const float CellMaximumLabelWidth = 80f;
        private const float CellMinimumPropertyFieldWidth = 32f;
        private const float CellLabelGap = 4f;
        private const float CellTreeIndentWidth = 8f;
        private const float CellMaximumTreeIndent = 18f;
        private const float CellHideLabelWidth = 84f;
        private const string CellControlPrefix = "SpreadAsset Editor.Cell.";
        private const string SearchControlName = "SpreadAsset Editor.Search";

        private IMGUIContainer _imguiContainer;
        private string _documentPath;
        private string _loadError;
        private SpreadAssetDocument _document;
        private UnityEngine.Object _targetAsset;
        private ScriptableObject _workingCopy;
        private SerializedObject _serializedObject;
        private readonly List<string> _arrayPropertyPaths = new List<string>();
        private Vector2 _propertyScroll;
        private Vector2 _tableScroll;
        private int _selectedArrayIndex;
        private bool _showAssetFields = true;
        private bool _showFormulaList = true;
        private bool _showSearchPanel = true;
        private bool _isDocumentDirty;
        private string _formulaError;
        private string _searchQuery = string.Empty;
        private string _searchStatus = string.Empty;
        private string _pendingFormulaFocusControlName;
        private string _pendingCellFocusControlName;
        private string _pendingCellScrollArrayPropertyPath;
        private int _pendingCellScrollRowIndex = -1;
        private int _pendingCellScrollColumnIndex = -1;
        private string _focusedCellArrayPropertyPath;
        private int _focusedCellRowIndex = -1;
        private int _focusedCellColumnIndex = -1;
        private float _tableRowViewportHeight;
        private static GUIStyle _columnHeaderLabelStyle;
        private readonly Dictionary<string, string> _formulaDrafts = new Dictionary<string, string>();
        private readonly Dictionary<SpreadAssetSheetState, FormulaSheetCache> _formulaSheetCaches =
            new Dictionary<SpreadAssetSheetState, FormulaSheetCache>();
        private readonly HashSet<SpreadAssetSheetState> _dirtyFormulaSheets = new HashSet<SpreadAssetSheetState>();

        [MenuItem("Tools/SpreadAsset/Open")]
        public static SpreadAssetWindow Open()
        {
            SpreadAssetWindow window = GetWindow<SpreadAssetWindow>(WindowTitle);
            window.titleContent = new GUIContent(WindowTitle);
            window.Show();
            return window;
        }

        public static void OpenDocument(string documentPath)
        {
            SpreadAssetWindow window = Open();
            window.LoadDocument(documentPath);
        }

        private void OnEnable()
        {
            titleContent = new GUIContent(WindowTitle);
            Selection.selectionChanged += OnSelectionChanged;
            TryLoadSelectedDocument();
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            DestroyWorkingCopy();
        }

        private void OnFocus()
        {
            TryLoadSelectedDocument();
        }

        private void CreateGUI()
        {
            rootVisualElement.AddToClassList("spread-asset-window");
            _imguiContainer = new IMGUIContainer(DrawWindow);
            _imguiContainer.style.flexGrow = 1f;
            rootVisualElement.Add(_imguiContainer);
        }

        private void OnSelectionChanged()
        {
            TryLoadSelectedDocument();
            Repaint();
        }

        private void TryLoadSelectedDocument()
        {
            string selectedPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (!SpreadAssetDocumentIO.IsDocumentPath(selectedPath))
            {
                return;
            }

            if (string.Equals(selectedPath, _documentPath, StringComparison.Ordinal) && _serializedObject != null)
            {
                if (TryRefreshLoadedDocumentSchema())
                {
                    Repaint();
                }

                return;
            }

            LoadDocument(selectedPath);
        }

        private void LoadDocument(string documentPath)
        {
            _documentPath = documentPath;
            _loadError = string.Empty;
            _document = null;
            _targetAsset = null;
            DestroyWorkingCopy();
            _serializedObject = null;
            _arrayPropertyPaths.Clear();
            _selectedArrayIndex = 0;
            _isDocumentDirty = false;
            _formulaError = string.Empty;
            _searchQuery = string.Empty;
            _searchStatus = string.Empty;
            _pendingFormulaFocusControlName = string.Empty;
            _pendingCellFocusControlName = string.Empty;
            ClearPendingCellScroll();
            ClearFocusedCell();
            _formulaDrafts.Clear();
            _formulaSheetCaches.Clear();
            _dirtyFormulaSheets.Clear();

            if (!SpreadAssetDocumentIO.TryRead(documentPath, out _document, out _loadError))
            {
                return;
            }

            _targetAsset = SpreadAssetDocumentIO.LoadLinkedAsset(_document);
            if (_targetAsset == null)
            {
                _loadError = "Linked .asset could not be found.";
                return;
            }

            bool documentChanged = SpreadAssetDocumentSync.EnsureDocumentData(_document, _targetAsset);
            documentChanged |= RefreshDocumentSchemaFromGeneratedFactory(_document, _targetAsset);
            documentChanged |= EnsureDocumentSheetsForSchema(_document);

            if (documentChanged)
            {
                SpreadAssetDocumentIO.Write(_documentPath, _document);
                AssetDatabase.ImportAsset(_documentPath);
            }

            _workingCopy = SpreadAssetDocumentSync.CreateWorkingCopy(_document, _targetAsset);
            if (_workingCopy == null)
            {
                _loadError = "Could not create a working copy from the SpreadAsset document.";
                return;
            }

            _serializedObject = new SerializedObject(_workingCopy);
            RefreshArrayPropertyPaths();
            MarkAllFormulaSheetsDirty();
        }

        private bool TryRefreshLoadedDocumentSchema()
        {
            if (_document == null || _targetAsset == null || _isDocumentDirty)
            {
                return false;
            }

            bool documentChanged = RefreshDocumentSchemaFromGeneratedFactory(_document, _targetAsset);
            documentChanged |= EnsureDocumentSheetsForSchema(_document);
            if (!documentChanged)
            {
                return false;
            }

            SpreadAssetDocumentIO.Write(_documentPath, _document);
            AssetDatabase.ImportAsset(_documentPath);
            RecreateWorkingCopy();
            return true;
        }

        private void DrawWindow()
        {
            if (_serializedObject == null)
            {
                DrawEmptyState();
                return;
            }

            _serializedObject.Update();
            bool formulaChanged = ApplySelectedSheetFormulasIfDirty();

            EditorGUI.BeginChangeCheck();
            DrawHeader();
            DrawNonArrayProperties();
            DrawArrayDataTabBar();
            DrawSelectedArrayFormulaPanel();
            EditorGUILayout.Space(8);

            DrawSelectedArrayTable();

            DrawFooter();

            EditorGUI.EndChangeCheck();
            bool serializedChanged = _serializedObject.ApplyModifiedProperties();
            if (serializedChanged)
            {
                MarkSelectedSheetFormulasDirty();
            }

            if (ShouldApplyFormulasAfterDraw())
            {
                formulaChanged |= ApplySelectedSheetFormulasIfDirty();
            }

            if (serializedChanged || formulaChanged)
            {
                _isDocumentDirty = true;
                RefreshArrayPropertyPaths();
            }
        }

        private static bool ShouldApplyFormulasAfterDraw()
        {
            EventType eventType = Event.current.type;
            return eventType != EventType.Layout && eventType != EventType.Repaint;
        }

        private void DrawEmptyState()
        {
            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField(WindowTitle, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Select a .spreadasset file in the Project window to edit its linked ScriptableObject data.",
                MessageType.Info);

            if (!string.IsNullOrEmpty(_loadError))
            {
                EditorGUILayout.HelpBox(_loadError, MessageType.Error);
            }

            if (GUILayout.Button("Open Generator", GUILayout.Width(160)))
            {
                SpreadAssetGeneratorWindow.Open();
            }
        }

        private void DrawHeader()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(WindowTitle, EditorStyles.boldLabel);
            DrawTopSaveActions();

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("Document", _documentPath);
                EditorGUILayout.ObjectField("Export Target", _targetAsset, typeof(UnityEngine.Object), false);
            }

            if (_document?.Schema != null && !string.IsNullOrEmpty(_document.Schema.AssetClassName))
            {
                EditorGUILayout.LabelField("Schema", _document.Schema.AssetClassName);
            }

            EditorGUILayout.HelpBox(
                "Edits are applied to the .spreadasset source document first. Save & Export writes the data portion to the linked .asset.",
                MessageType.Info);
        }

        private void DrawTopSaveActions()
        {
            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                Color previousColor = GUI.backgroundColor;

                GUI.backgroundColor = new Color(0.16f, 0.68f, 0.28f);
                if (GUILayout.Button("Save & Export", GUILayout.Width(172), GUILayout.Height(30)))
                {
                    SaveDocument(exportToAsset: true);
                }

                GUI.backgroundColor = previousColor;

                if (GUILayout.Button("Import Asset", GUILayout.Width(96), GUILayout.Height(30)))
                {
                    ImportFromLinkedAsset();
                }

                if (GUILayout.Button("Export CSV", GUILayout.Width(92), GUILayout.Height(30)))
                {
                    ExportSelectedSheetToCsv();
                }

                if (GUILayout.Button("Import CSV", GUILayout.Width(92), GUILayout.Height(30)))
                {
                    ImportSelectedSheetFromCsv();
                }

                if (GUILayout.Button("Ping Asset", GUILayout.Width(88), GUILayout.Height(30)))
                {
                    EditorGUIUtility.PingObject(_targetAsset);
                }

                GUILayout.FlexibleSpace();
                GUILayout.Label(_isDocumentDirty ? "Unsaved" : "Saved", EditorStyles.boldLabel);
            }
        }

        private void DrawNonArrayProperties()
        {
            EditorGUILayout.Space(8);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                _showAssetFields = EditorGUILayout.Foldout(_showAssetFields, "Asset Fields", true);
                if (!_showAssetFields)
                {
                    return;
                }

                SerializedProperty property = _serializedObject.GetIterator();
                bool enterChildren = true;
                bool hasVisibleField = false;

                int previousIndent = EditorGUI.indentLevel;
                EditorGUI.indentLevel = previousIndent + 1;
                try
                {
                    while (property.NextVisible(enterChildren))
                    {
                        enterChildren = false;

                        if (property.name == "m_Script")
                        {
                            using (new EditorGUI.DisabledScope(true))
                            {
                                EditorGUILayout.PropertyField(property, true);
                            }

                            continue;
                        }

                        if (IsTableArray(property))
                        {
                            continue;
                        }

                        hasVisibleField = true;
                        EditorGUILayout.PropertyField(property, true);
                    }
                }
                finally
                {
                    EditorGUI.indentLevel = previousIndent;
                }

                if (!hasVisibleField)
                {
                    EditorGUILayout.HelpBox("No non-array serialized fields.", MessageType.None);
                }
            }
        }

        private void DrawSelectedArrayFormulaPanel()
        {
            if (_arrayPropertyPaths.Count == 0)
            {
                return;
            }

            _selectedArrayIndex = Mathf.Clamp(_selectedArrayIndex, 0, _arrayPropertyPaths.Count - 1);
            SerializedProperty arrayProperty = _serializedObject.FindProperty(_arrayPropertyPaths[_selectedArrayIndex]);
            if (arrayProperty == null)
            {
                return;
            }

            EditorGUILayout.Space(8);
            SpreadAssetSheetState sheetState = GetOrCreateSheetState(arrayProperty);
            List<TableColumn> columns = GetColumns(arrayProperty, sheetState);
            DrawFormulaPanel(arrayProperty, columns, sheetState);
            DrawSearchPanel(arrayProperty, columns, sheetState);
        }

        private void DrawSelectedArrayTable()
        {
            EditorGUILayout.LabelField("Array Table", EditorStyles.boldLabel);

            if (_arrayPropertyPaths.Count == 0)
            {
                EditorGUILayout.HelpBox("No serialized array fields were found on the linked asset.", MessageType.None);
                return;
            }

            _selectedArrayIndex = Mathf.Clamp(_selectedArrayIndex, 0, _arrayPropertyPaths.Count - 1);
            SerializedProperty arrayProperty = _serializedObject.FindProperty(_arrayPropertyPaths[_selectedArrayIndex]);
            if (arrayProperty == null)
            {
                EditorGUILayout.HelpBox("Selected array field could not be found.", MessageType.Warning);
                return;
            }

            SpreadAssetSheetState sheetState = GetOrCreateSheetState(arrayProperty);
            List<TableColumn> columns = GetColumns(arrayProperty, sheetState);
            KeyValidationResult keyValidation = ValidateKeyFields(arrayProperty, columns, sheetState);
            DrawKeyValidationPanel(keyValidation);
            DrawArrayToolbar(arrayProperty, sheetState);
            DrawArrayGrid(arrayProperty, columns, sheetState, keyValidation.InvalidCellKeys);
        }

        private void DrawFormulaPanel(
            SerializedProperty arrayProperty,
            List<TableColumn> columns,
            SpreadAssetSheetState sheetState)
        {
            if (EnsureFormulaIds(sheetState))
            {
                _isDocumentDirty = true;
                InvalidateSheetFormulaCache(sheetState, markDirty: true);
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                List<SpreadAssetFormulaState> formulaList = GetFormulaList(sheetState);
                using (new EditorGUILayout.HorizontalScope())
                {
                    _showFormulaList = EditorGUILayout.Foldout(
                        _showFormulaList,
                        $"Formulas ({formulaList.Count})",
                        true);
                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button("Add Formula", GUILayout.Width(104)))
                    {
                        List<SpreadAssetFormulaState> formulas = GetFormulaList(sheetState);
                        SpreadAssetFormulaState newFormula = new SpreadAssetFormulaState
                        {
                            Id = CreateFormulaId(),
                            Expression = string.Empty
                        };
                        formulas.Add(newFormula);
                        sheetState.Formulas = formulas.ToArray();
                        _isDocumentDirty = true;
                        InvalidateSheetFormulaCache(sheetState, markDirty: true);
                        _pendingFormulaFocusControlName = GetFormulaControlName(newFormula);
                        _showFormulaList = true;
                        ClearTextFieldFocus();
                        Repaint();
                        GUIUtility.ExitGUI();
                    }

                    if (GUILayout.Button("Recalculate", GUILayout.Width(96)))
                    {
                        if (CommitFormulaDrafts(sheetState))
                        {
                            _isDocumentDirty = true;
                            InvalidateSheetFormulaCache(sheetState, markDirty: true);
                        }

                        ApplyFormulas(arrayProperty, columns, sheetState, markDirty: true, forceRecompile: true);
                    }
                }

                if (!string.IsNullOrEmpty(_formulaError))
                {
                    EditorGUILayout.HelpBox(_formulaError, MessageType.Warning);
                }

                bool changed = false;
                if (_showFormulaList)
                {
                    if (formulaList.Count == 0)
                    {
                        EditorGUILayout.HelpBox("No formulas. Examples: C = A + B, C1 = A1 + B1, Key = 'cookie' + TEXT(Id, '0000')", MessageType.None);
                    }
                    else
                    {
                        changed = DrawFormulaRows(arrayProperty, columns, sheetState, formulaList);
                    }
                }

                bool committedDrafts = CommitFormulaDraftsIfFocusLost(sheetState);
                if (changed)
                {
                    sheetState.Formulas = formulaList.ToArray();
                }

                if (changed || committedDrafts)
                {
                    _isDocumentDirty = true;
                    InvalidateSheetFormulaCache(sheetState, markDirty: true);
                    ApplyFormulas(arrayProperty, columns, sheetState, markDirty: true, forceRecompile: true);
                }
            }
        }

        private void DrawSearchPanel(
            SerializedProperty arrayProperty,
            List<TableColumn> columns,
            SpreadAssetSheetState sheetState)
        {
            EditorGUILayout.Space(4);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                _showSearchPanel = EditorGUILayout.Foldout(_showSearchPanel, "Search", true);
                if (!_showSearchPanel)
                {
                    return;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    GUI.SetNextControlName(SearchControlName);
                    string nextQuery = EditorGUILayout.TextField("Text", _searchQuery ?? string.Empty);
                    if (EditorGUI.EndChangeCheck())
                    {
                        _searchQuery = nextQuery ?? string.Empty;
                    }

                    if (GUILayout.Button("Previous", GUILayout.Width(82)))
                    {
                        FocusSearchMatch(arrayProperty, columns, sheetState, SearchDirection.Previous);
                    }

                    if (GUILayout.Button("Next", GUILayout.Width(64)))
                    {
                        FocusSearchMatch(arrayProperty, columns, sheetState, SearchDirection.Next);
                    }
                }

                if (!string.IsNullOrEmpty(_searchStatus))
                {
                    EditorGUILayout.LabelField(_searchStatus, EditorStyles.miniLabel);
                }
            }
        }

        private void FocusSearchMatch(
            SerializedProperty arrayProperty,
            List<TableColumn> columns,
            SpreadAssetSheetState sheetState,
            SearchDirection direction)
        {
            string query = _searchQuery ?? string.Empty;
            if (string.IsNullOrWhiteSpace(query))
            {
                return;
            }

            if (arrayProperty == null || arrayProperty.arraySize == 0)
            {
                _searchStatus = "No rows to search.";
                return;
            }

            if (!TryFindSearchMatch(
                    arrayProperty,
                    columns,
                    sheetState,
                    query,
                    direction,
                    out int rowIndex,
                    out int columnIndex))
            {
                _searchStatus = CreateSearchMissStatus(query, direction);
                return;
            }

            string controlName = GetCellControlName(arrayProperty.propertyPath, rowIndex, columnIndex);
            RequestCellFocus(controlName, rowIndex, columns, columnIndex);
            _searchStatus = $"Found {GetCellAddress(rowIndex, columnIndex)}.";
        }

        private bool TryFindSearchMatch(
            SerializedProperty arrayProperty,
            List<TableColumn> columns,
            SpreadAssetSheetState sheetState,
            string query,
            SearchDirection direction,
            out int rowIndex,
            out int columnIndex)
        {
            rowIndex = -1;
            columnIndex = -1;

            int rowCount = arrayProperty?.arraySize ?? 0;
            int columnCount = GetSearchColumnCount(columns);
            int cellCount = rowCount * columnCount;
            if (rowCount == 0 || cellCount == 0)
            {
                return false;
            }

            int startIndex = GetSearchStartIndex(arrayProperty.propertyPath, rowCount, columnCount, direction);
            int step = direction == SearchDirection.Previous ? -1 : 1;
            for (int index = startIndex; index >= 0 && index < cellCount; index += step)
            {
                int candidateRow = index / columnCount;
                int candidateColumn = index % columnCount;
                if (CellMatchesSearch(arrayProperty, columns, sheetState, candidateRow, candidateColumn, query))
                {
                    rowIndex = candidateRow;
                    columnIndex = candidateColumn;
                    return true;
                }
            }

            return false;
        }

        private int GetSearchStartIndex(
            string arrayPropertyPath,
            int rowCount,
            int columnCount,
            SearchDirection direction)
        {
            if (direction == SearchDirection.First)
            {
                return 0;
            }

            int cellCount = rowCount * columnCount;
            if (!TryGetFocusedCellPosition(arrayPropertyPath, out int rowIndex, out int columnIndex)
                || rowIndex < 0
                || rowIndex >= rowCount
                || columnIndex < 0
                || columnIndex >= columnCount)
            {
                return direction == SearchDirection.Previous ? cellCount - 1 : 0;
            }

            int focusedIndex = rowIndex * columnCount + columnIndex;
            return focusedIndex + (direction == SearchDirection.Previous ? -1 : 1);
        }

        private static string CreateSearchMissStatus(string query, SearchDirection direction)
        {
            switch (direction)
            {
                case SearchDirection.Previous:
                    return $"No previous cell contains \"{query}\".";
                case SearchDirection.Next:
                    return $"No next cell contains \"{query}\".";
                default:
                    return $"No cells contain \"{query}\".";
            }
        }

        private static int GetSearchColumnCount(List<TableColumn> columns)
        {
            return columns == null || columns.Count == 0 ? 1 : columns.Count;
        }

        private static bool CellMatchesSearch(
            SerializedProperty arrayProperty,
            List<TableColumn> columns,
            SpreadAssetSheetState sheetState,
            int rowIndex,
            int columnIndex,
            string query)
        {
            if (!TryGetCellSearchText(arrayProperty, columns, sheetState, rowIndex, columnIndex, out string value)
                || string.IsNullOrEmpty(value))
            {
                return false;
            }

            return value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryGetCellSearchText(
            SerializedProperty arrayProperty,
            List<TableColumn> columns,
            SpreadAssetSheetState sheetState,
            int rowIndex,
            int columnIndex,
            out string value)
        {
            value = string.Empty;
            if (arrayProperty == null || rowIndex < 0 || rowIndex >= arrayProperty.arraySize)
            {
                return false;
            }

            SerializedProperty element = arrayProperty.GetArrayElementAtIndex(rowIndex);
            if (columns == null || columns.Count == 0)
            {
                return TryGetSerializedPropertySearchText(element, out value);
            }

            if (columnIndex < 0 || columnIndex >= columns.Count)
            {
                return false;
            }

            TableColumn column = columns[columnIndex];
            if (column.IsDesignField)
            {
                value = GetDesignCellValue(sheetState, rowIndex, column);
                return true;
            }

            SerializedProperty cell = GetCellProperty(element, column);
            return TryGetSerializedPropertySearchText(cell, out value);
        }

        private static bool TryGetSerializedPropertySearchText(SerializedProperty property, out string value)
        {
            value = string.Empty;
            if (property == null)
            {
                return false;
            }

            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    value = property.longValue.ToString(CultureInfo.InvariantCulture);
                    return true;
                case SerializedPropertyType.Boolean:
                    value = property.boolValue.ToString();
                    return true;
                case SerializedPropertyType.Float:
                    value = property.floatValue.ToString(CultureInfo.InvariantCulture);
                    return true;
                case SerializedPropertyType.String:
                    value = property.stringValue ?? string.Empty;
                    return true;
                case SerializedPropertyType.Color:
                    value = property.colorValue.ToString();
                    return true;
                case SerializedPropertyType.ObjectReference:
                    value = property.objectReferenceValue == null ? string.Empty : property.objectReferenceValue.name;
                    return true;
                case SerializedPropertyType.LayerMask:
                    value = property.intValue.ToString(CultureInfo.InvariantCulture);
                    return true;
                case SerializedPropertyType.Enum:
                    value = GetEnumSearchText(property);
                    return true;
                case SerializedPropertyType.Vector2:
                    value = property.vector2Value.ToString();
                    return true;
                case SerializedPropertyType.Vector3:
                    value = property.vector3Value.ToString();
                    return true;
                case SerializedPropertyType.Vector4:
                    value = property.vector4Value.ToString();
                    return true;
                case SerializedPropertyType.Rect:
                    value = property.rectValue.ToString();
                    return true;
                case SerializedPropertyType.Bounds:
                    value = property.boundsValue.ToString();
                    return true;
                case SerializedPropertyType.Quaternion:
                    value = property.quaternionValue.ToString();
                    return true;
                case SerializedPropertyType.Vector2Int:
                    value = property.vector2IntValue.ToString();
                    return true;
                case SerializedPropertyType.Vector3Int:
                    value = property.vector3IntValue.ToString();
                    return true;
                case SerializedPropertyType.RectInt:
                    value = property.rectIntValue.ToString();
                    return true;
                case SerializedPropertyType.BoundsInt:
                    value = property.boundsIntValue.ToString();
                    return true;
                case SerializedPropertyType.ManagedReference:
                    value = property.managedReferenceValue == null
                        ? property.managedReferenceFullTypename ?? string.Empty
                        : property.managedReferenceValue.ToString();
                    return true;
                case SerializedPropertyType.Generic:
                    value = GetGenericPropertySearchText(property);
                    return true;
                default:
                    return false;
            }
        }

        private static string GetEnumSearchText(SerializedProperty property)
        {
            string[] displayNames = property.enumDisplayNames;
            if (displayNames != null
                && property.enumValueIndex >= 0
                && property.enumValueIndex < displayNames.Length)
            {
                return displayNames[property.enumValueIndex];
            }

            return property.intValue.ToString(CultureInfo.InvariantCulture);
        }

        private static string GetGenericPropertySearchText(SerializedProperty property)
        {
            List<string> values = new List<string>();
            SerializedProperty child = property.Copy();
            SerializedProperty end = property.GetEndProperty();
            bool enterChildren = true;

            while (child.NextVisible(enterChildren) && !SerializedProperty.EqualContents(child, end))
            {
                enterChildren = false;
                if (child.depth != property.depth + 1)
                {
                    continue;
                }

                if (TryGetSerializedPropertySearchText(child, out string childValue)
                    && !string.IsNullOrEmpty(childValue))
                {
                    values.Add(childValue);
                }
            }

            return string.Join(" ", values);
        }

        private bool DrawFormulaRows(
            SerializedProperty arrayProperty,
            List<TableColumn> columns,
            SpreadAssetSheetState sheetState,
            List<SpreadAssetFormulaState> formulaList)
        {
            bool changed = false;
            for (int i = 0; i < formulaList.Count; i++)
            {
                SpreadAssetFormulaState formula = formulaList[i];
                using (new EditorGUILayout.HorizontalScope(GUILayout.Height(FormulaRowHeight)))
                {
                    bool enabled = EditorGUILayout.Toggle(formula.Enabled, GUILayout.Width(20));
                    if (enabled != formula.Enabled)
                    {
                        formula.Enabled = enabled;
                        changed = true;
                    }

                    string controlName = GetFormulaControlName(formula);
                    GUI.SetNextControlName(controlName);
                    string draftExpression = GetFormulaDraft(formula);
                    string expression = EditorGUILayout.TextField(draftExpression);
                    if (expression != draftExpression)
                    {
                        _formulaDrafts[formula.Id] = expression;
                        _formulaError = string.Empty;
                    }

                    if (GUILayout.Button("-", GUILayout.Width(28)))
                    {
                        _formulaDrafts.Remove(formula.Id);
                        formulaList.RemoveAt(i);
                        sheetState.Formulas = formulaList.ToArray();
                        _isDocumentDirty = true;
                        InvalidateSheetFormulaCache(sheetState, markDirty: true);
                        ClearTextFieldFocus();
                        ApplyFormulas(arrayProperty, columns, sheetState, markDirty: true, forceRecompile: true);
                        Repaint();
                        GUIUtility.ExitGUI();
                    }

                    FocusPendingFormulaControl(controlName);
                }
            }

            return changed;
        }

        private void DrawArrayToolbar(SerializedProperty arrayProperty, SpreadAssetSheetState sheetState)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Add Row", GUILayout.Width(92)))
                {
                    AddArrayRow(arrayProperty);
                    InvalidateSheetFormulaCache(sheetState, markDirty: true);
                    CompleteRowStructureChange(arrayProperty, columnsChanged: false, sheetState);
                }

                if (GUILayout.Button("Clear", GUILayout.Width(72))
                    && EditorUtility.DisplayDialog("Clear rows?", $"Remove every row from {arrayProperty.displayName}?", "Clear", "Cancel"))
                {
                    arrayProperty.ClearArray();
                    ClearSheetCells(sheetState);
                    InvalidateSheetFormulaCache(sheetState, markDirty: true);
                    _isDocumentDirty = true;
                }
            }

            int previousSize = arrayProperty.arraySize;
            int size = Mathf.Max(0, EditorGUILayout.IntField("Rows", arrayProperty.arraySize));
            if (size != arrayProperty.arraySize)
            {
                arrayProperty.arraySize = size;
                InvalidateSheetFormulaCache(sheetState, markDirty: true);
                if (size < previousSize)
                {
                    TrimSheetCells(sheetState, size);
                }

                _isDocumentDirty = true;
            }
        }

        private static void DrawKeyValidationPanel(KeyValidationResult validation)
        {
            if (validation == null || !validation.HasErrors)
            {
                return;
            }

            EditorGUILayout.HelpBox(
                "Key validation failed. The .spreadasset source can be saved, but Save & Export will not write the linked .asset until key values are unique and non-empty.\n\n"
                + FormatKeyValidationSummary(validation, 6),
                MessageType.Error);
        }

        private KeyValidationResult ValidateAllKeyFields()
        {
            KeyValidationResult result = new KeyValidationResult();
            if (_serializedObject == null)
            {
                return result;
            }

            RefreshArrayPropertyPaths();
            foreach (string arrayPropertyPath in _arrayPropertyPaths)
            {
                SerializedProperty arrayProperty = _serializedObject.FindProperty(arrayPropertyPath);
                if (arrayProperty == null)
                {
                    continue;
                }

                SpreadAssetSheetState sheetState = GetOrCreateSheetState(arrayProperty);
                List<TableColumn> columns = GetColumns(arrayProperty, sheetState);
                result.Add(ValidateKeyFields(arrayProperty, columns, sheetState));
            }

            return result;
        }

        private static KeyValidationResult ValidateKeyFields(
            SerializedProperty arrayProperty,
            List<TableColumn> columns,
            SpreadAssetSheetState sheetState)
        {
            KeyValidationResult result = new KeyValidationResult();
            if (arrayProperty == null || columns == null || columns.Count == 0)
            {
                return result;
            }

            string sheetName = GetSheetDisplayName(arrayProperty, sheetState);
            for (int columnIndex = 0; columnIndex < columns.Count; columnIndex++)
            {
                TableColumn column = columns[columnIndex];
                if (!column.IsKeyField)
                {
                    continue;
                }

                ValidateKeyColumn(arrayProperty, columns, sheetState, sheetName, columnIndex, result);
            }

            return result;
        }

        private static void ValidateKeyColumn(
            SerializedProperty arrayProperty,
            List<TableColumn> columns,
            SpreadAssetSheetState sheetState,
            string sheetName,
            int columnIndex,
            KeyValidationResult result)
        {
            TableColumn column = columns[columnIndex];
            string columnName = GetCsvColumnName(column);
            Dictionary<string, List<int>> rowsByValue = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            List<int> emptyRows = new List<int>();
            List<int> unsupportedRows = new List<int>();

            for (int rowIndex = 0; rowIndex < arrayProperty.arraySize; rowIndex++)
            {
                SerializedProperty element = arrayProperty.GetArrayElementAtIndex(rowIndex);
                if (!TryGetKeyCellValue(element, column, sheetState, rowIndex, out string value))
                {
                    unsupportedRows.Add(rowIndex);
                    continue;
                }

                string keyValue = NormalizeKeyValidationValue(value);
                if (string.IsNullOrEmpty(keyValue))
                {
                    emptyRows.Add(rowIndex);
                    continue;
                }

                if (!rowsByValue.TryGetValue(keyValue, out List<int> rows))
                {
                    rows = new List<int>();
                    rowsByValue.Add(keyValue, rows);
                }

                rows.Add(rowIndex);
            }

            if (emptyRows.Count > 0)
            {
                result.AddIssue(new KeyValidationIssue(
                    KeyValidationIssueKind.Empty,
                    sheetName,
                    columnName,
                    columnIndex,
                    string.Empty,
                    emptyRows));
            }

            if (unsupportedRows.Count > 0)
            {
                result.AddIssue(new KeyValidationIssue(
                    KeyValidationIssueKind.Unsupported,
                    sheetName,
                    columnName,
                    columnIndex,
                    string.Empty,
                    unsupportedRows));
            }

            foreach (KeyValuePair<string, List<int>> pair in rowsByValue)
            {
                if (pair.Value.Count <= 1)
                {
                    continue;
                }

                result.AddIssue(new KeyValidationIssue(
                    KeyValidationIssueKind.Duplicate,
                    sheetName,
                    columnName,
                    columnIndex,
                    pair.Key,
                    pair.Value));
            }
        }

        private static bool TryGetKeyCellValue(
            SerializedProperty element,
            TableColumn column,
            SpreadAssetSheetState sheetState,
            int rowIndex,
            out string value)
        {
            value = string.Empty;
            if (column.IsDesignField)
            {
                value = GetDesignCellValue(sheetState, rowIndex, column);
                return true;
            }

            SerializedProperty cell = GetCellProperty(element, column);
            return TryGetSerializedPropertyCsvValue(cell, out value);
        }

        private static string NormalizeKeyValidationValue(string value)
        {
            return (value ?? string.Empty).Trim();
        }

        private static string FormatKeyValidationSummary(KeyValidationResult validation, int maxIssues)
        {
            if (validation == null || validation.Issues.Count == 0)
            {
                return string.Empty;
            }

            int issueCount = Mathf.Min(Mathf.Max(1, maxIssues), validation.Issues.Count);
            List<string> messages = new List<string>(issueCount + 1);
            for (int i = 0; i < issueCount; i++)
            {
                messages.Add("- " + FormatKeyValidationIssue(validation.Issues[i]));
            }

            int hiddenIssueCount = validation.Issues.Count - issueCount;
            if (hiddenIssueCount > 0)
            {
                messages.Add("- ... and " + hiddenIssueCount.ToString(CultureInfo.InvariantCulture) + " more.");
            }

            return string.Join("\n", messages);
        }

        private static string FormatKeyValidationIssue(KeyValidationIssue issue)
        {
            string location = issue.SheetName + "." + issue.ColumnName;
            string rows = FormatKeyValidationRows(issue.Rows);
            switch (issue.Kind)
            {
                case KeyValidationIssueKind.Duplicate:
                    return location + ": duplicate key \"" + issue.KeyValue + "\" at rows " + rows + ".";
                case KeyValidationIssueKind.Empty:
                    return location + ": blank key at rows " + rows + ".";
                case KeyValidationIssueKind.Unsupported:
                    return location + ": unsupported key type/value at rows " + rows + ".";
                default:
                    return location + ": invalid key at rows " + rows + ".";
            }
        }

        private static string FormatKeyValidationRows(List<int> rows)
        {
            if (rows == null || rows.Count == 0)
            {
                return "none";
            }

            const int maxVisibleRows = 8;
            int visibleCount = Mathf.Min(maxVisibleRows, rows.Count);
            List<string> labels = new List<string>(visibleCount + 1);
            for (int i = 0; i < visibleCount; i++)
            {
                labels.Add((rows[i] + 1).ToString(CultureInfo.InvariantCulture));
            }

            if (rows.Count > visibleCount)
            {
                labels.Add("+" + (rows.Count - visibleCount).ToString(CultureInfo.InvariantCulture) + " more");
            }

            return string.Join(", ", labels);
        }

        private void DrawArrayGrid(
            SerializedProperty arrayProperty,
            List<TableColumn> columns,
            SpreadAssetSheetState sheetState,
            HashSet<string> invalidKeyCellKeys)
        {
            HashSet<string> formulaTargetKeys = GetFormulaTargetKeys(sheetState, arrayProperty.arraySize, columns);
            float frozenWidth = CalculateFrozenRowHeaderWidth();
            float dataWidth = CalculateDataGridWidth(columns);
            float dataViewportWidth = CalculateDataGridViewportWidth(frozenWidth);
            bool useHorizontalScroll = ShouldUseHorizontalGridScroll(dataWidth, dataViewportWidth);
            _tableScroll.x = useHorizontalScroll
                ? Mathf.Clamp(_tableScroll.x, 0f, Mathf.Max(0f, dataWidth - dataViewportWidth))
                : 0f;

            bool hasFocusedCell = TryGetFocusedCellPosition(
                arrayProperty.propertyPath,
                out int _,
                out int focusedColumnIndex);
            DrawFrozenGridHeader(
                sheetState,
                columns,
                dataWidth,
                dataViewportWidth,
                frozenWidth,
                useHorizontalScroll,
                hasFocusedCell ? focusedColumnIndex : -1);
            DrawFrozenGridHorizontalScrollbar(frozenWidth, dataWidth, dataViewportWidth, useHorizontalScroll);

            if (arrayProperty.arraySize == 0)
            {
                EditorGUILayout.HelpBox("No rows. Use Add Row to start entering data.", MessageType.None);
                return;
            }

            DrawFrozenGridRows(
                arrayProperty,
                columns,
                sheetState,
                formulaTargetKeys,
                dataWidth,
                dataViewportWidth,
                frozenWidth,
                useHorizontalScroll,
                invalidKeyCellKeys);
        }

        private void DrawFrozenGridHeader(
            SpreadAssetSheetState sheetState,
            List<TableColumn> columns,
            float dataWidth,
            float dataViewportWidth,
            float frozenWidth,
            bool useHorizontalScroll,
            int focusedColumnIndex)
        {
            float headerHeight = CalculateGridHeaderHeight();
            using (new EditorGUILayout.HorizontalScope(GUILayout.Height(headerHeight)))
            {
                DrawFrozenGridCorner(frozenWidth, headerHeight);
                DrawColumnHeaderCells(
                    sheetState,
                    columns,
                    dataWidth,
                    dataViewportWidth,
                    headerHeight,
                    useHorizontalScroll,
                    focusedColumnIndex);
            }
        }

        private static void DrawFrozenGridCorner(float frozenWidth, float headerHeight)
        {
            Rect cornerRect = GUILayoutUtility.GetRect(
                frozenWidth,
                headerHeight,
                GUILayout.Width(frozenWidth),
                GUILayout.Height(headerHeight));
            GUI.Label(
                new Rect(cornerRect.x, cornerRect.y, RowNumberWidth, ColumnLetterHeaderHeight),
                "#",
                EditorStyles.boldLabel);
        }

        private void DrawColumnHeaderCells(
            SpreadAssetSheetState sheetState,
            List<TableColumn> columns,
            float dataWidth,
            float dataViewportWidth,
            float headerHeight,
            bool useHorizontalScroll,
            int focusedColumnIndex)
        {
            if (useHorizontalScroll)
            {
                Rect viewportRect = GUILayoutUtility.GetRect(
                    dataViewportWidth,
                    headerHeight,
                    GUILayout.Width(dataViewportWidth),
                    GUILayout.Height(headerHeight));
                HandleHorizontalScrollWheel(viewportRect, dataWidth, dataViewportWidth, useHorizontalScroll);
                DrawClippedTableArea(viewportRect, dataWidth, headerHeight, () =>
                {
                    DrawColumnHeaderContent(sheetState, columns, dataWidth, headerHeight, focusedColumnIndex);
                });
            }
            else
            {
                _tableScroll = Vector2.zero;
                DrawColumnHeaderContent(sheetState, columns, dataWidth, headerHeight, focusedColumnIndex);
            }
        }

        private void DrawColumnHeaderContent(
            SpreadAssetSheetState sheetState,
            List<TableColumn> columns,
            float dataWidth,
            float headerHeight,
            int focusedColumnIndex)
        {
            Rect tableRect = GUILayoutUtility.GetRect(
                dataWidth,
                headerHeight,
                GUILayout.Width(dataWidth),
                GUILayout.Height(headerHeight));
            GUI.BeginGroup(tableRect);
            float x = 0f;
            for (int columnIndex = 0; columnIndex < columns.Count; columnIndex++)
            {
                TableColumn column = columns[columnIndex];
                Rect columnRect = new Rect(x, 0f, column.Width, TableHeaderHeight);
                Rect letterRect = new Rect(x, 0f, column.Width, ColumnLetterHeaderHeight);
                Rect labelRect = new Rect(
                    x,
                    ColumnLetterHeaderHeight + ColumnHeaderGap,
                    column.Width,
                    ColumnHeaderLabelHeight);

                DrawFocusedColumnHeaderBackground(columnRect, columnIndex == focusedColumnIndex);
                GUI.Label(letterRect, GetColumnName(columnIndex), EditorStyles.boldLabel);
                GUI.Label(labelRect, column.HeaderLabel, GetColumnHeaderLabelStyle());
                HandleColumnResize(columnRect, sheetState, columns, columnIndex);
                x += column.Width;
            }
            GUI.EndGroup();
        }

        private static GUIStyle GetColumnHeaderLabelStyle()
        {
            if (_columnHeaderLabelStyle == null)
            {
                _columnHeaderLabelStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.UpperLeft,
                    clipping = TextClipping.Clip,
                    wordWrap = false
                };
            }

            return _columnHeaderLabelStyle;
        }

        private void HandleColumnResize(
            Rect headerRect,
            SpreadAssetSheetState sheetState,
            List<TableColumn> columns,
            int columnIndex)
        {
            if (sheetState == null || columns == null || columnIndex < 0 || columnIndex >= columns.Count)
            {
                return;
            }

            Rect handleRect = new Rect(
                headerRect.xMax - ColumnResizeHandleWidth * 0.5f,
                headerRect.y,
                ColumnResizeHandleWidth,
                headerRect.height);
            EditorGUIUtility.AddCursorRect(handleRect, MouseCursor.ResizeHorizontal);

            int controlId = GUIUtility.GetControlID(columnIndex + 100000, FocusType.Passive, handleRect);
            Event current = Event.current;
            switch (current.GetTypeForControl(controlId))
            {
                case EventType.MouseDown:
                    if (current.button == 0 && handleRect.Contains(current.mousePosition))
                    {
                        GUIUtility.hotControl = controlId;
                        current.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == controlId)
                    {
                        ResizeColumn(sheetState, columns, columnIndex, columns[columnIndex].Width + current.delta.x);
                        current.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == controlId)
                    {
                        GUIUtility.hotControl = 0;
                        current.Use();
                    }
                    break;
                case EventType.Repaint:
                    if (GUIUtility.hotControl == controlId || handleRect.Contains(current.mousePosition))
                    {
                        Rect lineRect = new Rect(headerRect.xMax - 1f, headerRect.y + 3f, 1f, headerRect.height - 6f);
                        EditorGUI.DrawRect(lineRect, EditorGUIUtility.isProSkin
                            ? new Color(0.74f, 0.82f, 0.95f, 0.65f)
                            : new Color(0.22f, 0.36f, 0.68f, 0.55f));
                    }
                    break;
            }
        }

        private void ResizeColumn(
            SpreadAssetSheetState sheetState,
            List<TableColumn> columns,
            int columnIndex,
            float requestedWidth)
        {
            float width = Mathf.Clamp(requestedWidth, ManualMinimumColumnWidth, ManualMaximumColumnWidth);
            TableColumn column = columns[columnIndex];
            if (Mathf.Approximately(column.Width, width))
            {
                return;
            }

            columns[columnIndex] = column.WithWidth(width);
            if (SetSheetColumnWidth(sheetState, column, width))
            {
                _isDocumentDirty = true;
            }

            Repaint();
        }

        private void DrawFrozenGridHorizontalScrollbar(
            float frozenWidth,
            float dataWidth,
            float dataViewportWidth,
            bool useHorizontalScroll)
        {
            if (!useHorizontalScroll)
            {
                return;
            }

            float maxScroll = Mathf.Max(0f, dataWidth - dataViewportWidth);
            _tableScroll.x = Mathf.Clamp(_tableScroll.x, 0f, maxScroll);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(frozenWidth);
                EditorGUI.BeginChangeCheck();
                Rect scrollbarRect = GUILayoutUtility.GetRect(
                    dataViewportWidth,
                    HorizontalScrollbarHeight,
                    GUILayout.Width(dataViewportWidth),
                    GUILayout.Height(HorizontalScrollbarHeight));
                _tableScroll.x = GUI.HorizontalScrollbar(
                    scrollbarRect,
                    _tableScroll.x,
                    dataViewportWidth,
                    0f,
                    dataWidth);
                if (EditorGUI.EndChangeCheck())
                {
                    Repaint();
                }
            }
        }

        private void DrawFrozenGridRows(
            SerializedProperty arrayProperty,
            List<TableColumn> columns,
            SpreadAssetSheetState sheetState,
            HashSet<string> formulaTargetKeys,
            float dataWidth,
            float dataViewportWidth,
            float frozenWidth,
            bool useHorizontalScroll,
            HashSet<string> invalidKeyCellKeys)
        {
            GridRowLayout rowLayout = CalculateGridRowLayout(arrayProperty, columns);
            float rowContentHeight = rowLayout.ContentHeight;
            float dataHeight = rowContentHeight;

            Rect scrollViewRect = GUILayoutUtility.GetRect(
                0f,
                100000f,
                0f,
                100000f,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true));
            _tableRowViewportHeight = scrollViewRect.height;
            ApplyPendingCellScroll(arrayProperty.propertyPath, columns, dataWidth, dataViewportWidth, rowLayout);
            HandleHorizontalScrollWheel(scrollViewRect, dataWidth, dataViewportWidth, useHorizontalScroll);

            _propertyScroll.x = 0f;
            Rect contentRect = new Rect(0f, 0f, frozenWidth + dataViewportWidth, dataHeight);
            Vector2 previousScroll = _propertyScroll;
            _propertyScroll = GUI.BeginScrollView(
                scrollViewRect,
                _propertyScroll,
                contentRect,
                false,
                false,
                GUIStyle.none,
                GUI.skin.verticalScrollbar);
            _propertyScroll.x = 0f;
            if (!Mathf.Approximately(previousScroll.y, _propertyScroll.y))
            {
                ReleaseCellEditorForVerticalScroll();
            }

            VisibleRowRange visibleRows = CalculateVisibleRowRange(
                rowLayout,
                _propertyScroll.y,
                scrollViewRect.height);

            GUILayout.BeginArea(contentRect);
            using (new EditorGUILayout.HorizontalScope(GUILayout.Height(dataHeight)))
            {
                DrawFrozenRowHeaders(arrayProperty, sheetState, frozenWidth, rowContentHeight, rowLayout, visibleRows);
                DrawScrollableRowCells(
                    arrayProperty,
                    columns,
                    formulaTargetKeys,
                    sheetState,
                    dataWidth,
                    dataViewportWidth,
                    dataHeight,
                    useHorizontalScroll,
                    rowLayout,
                    invalidKeyCellKeys,
                    visibleRows);
            }
            GUILayout.EndArea();

            GUI.EndScrollView();
        }

        private void DrawFrozenRowHeaders(
            SerializedProperty arrayProperty,
            SpreadAssetSheetState sheetState,
            float frozenWidth,
            float rowContentHeight,
            GridRowLayout rowLayout,
            VisibleRowRange visibleRows)
        {
            Rect viewportRect = GUILayoutUtility.GetRect(
                frozenWidth,
                rowContentHeight,
                GUILayout.Width(frozenWidth),
                GUILayout.Height(rowContentHeight));
            bool hasFocusedCell = TryGetFocusedCellPosition(
                arrayProperty.propertyPath,
                out int focusedRowIndex,
                out int _);

            GUI.BeginGroup(viewportRect);
            for (int row = visibleRows.StartIndex; row < visibleRows.EndIndex; row++)
            {
                float y = rowLayout.GetY(row);
                float rowHeight = rowLayout.GetHeight(row);
                Rect rowHeaderRect = new Rect(0f, y, frozenWidth, rowHeight);
                DrawFocusedRowHeaderBackground(rowHeaderRect, hasFocusedCell && row == focusedRowIndex);
                GUI.Label(
                    GetCenteredRowControlRect(new Rect(0f, y, RowNumberWidth, rowHeight)),
                    (row + 1).ToString());

                if (GUI.Button(GetCenteredRowControlRect(new Rect(RowNumberWidth, y, RowButtonWidth, rowHeight)), "+"))
                {
                    InsertArrayRow(arrayProperty, row);
                    ShiftSheetCellsForInsert(sheetState, row);
                    CompleteRowStructureChange(arrayProperty, columnsChanged: false, sheetState);
                }

                if (GUI.Button(
                        GetCenteredRowControlRect(new Rect(RowNumberWidth + RowButtonWidth, y, RowButtonWidth, rowHeight)),
                        "-"))
                {
                    DeleteArrayRow(arrayProperty, row);
                    ShiftSheetCellsForDelete(sheetState, row);
                    CompleteRowStructureChange(arrayProperty, columnsChanged: false, sheetState);
                }
            }
            GUI.EndGroup();
        }

        private void DrawScrollableRowCells(
            SerializedProperty arrayProperty,
            List<TableColumn> columns,
            HashSet<string> formulaTargetKeys,
            SpreadAssetSheetState sheetState,
            float dataWidth,
            float dataViewportWidth,
            float dataHeight,
            bool useHorizontalScroll,
            GridRowLayout rowLayout,
            HashSet<string> invalidKeyCellKeys,
            VisibleRowRange visibleRows)
        {
            if (useHorizontalScroll)
            {
                Rect viewportRect = GUILayoutUtility.GetRect(
                    dataViewportWidth,
                    dataHeight,
                    GUILayout.Width(dataViewportWidth),
                    GUILayout.Height(dataHeight));
                DrawClippedTableRows(
                    viewportRect,
                    dataWidth,
                    dataHeight,
                    () => DrawDataRows(
                        arrayProperty,
                        columns,
                        formulaTargetKeys,
                        sheetState,
                        dataWidth,
                        rowLayout,
                        invalidKeyCellKeys,
                        visibleRows));
                return;
            }

            _tableScroll = Vector2.zero;
            Rect tableRect = GUILayoutUtility.GetRect(
                dataWidth,
                dataHeight,
                GUILayout.Width(dataWidth),
                GUILayout.Height(dataHeight));
            GUI.BeginGroup(tableRect);
            DrawDataRows(
                arrayProperty,
                columns,
                formulaTargetKeys,
                sheetState,
                dataWidth,
                rowLayout,
                invalidKeyCellKeys,
                visibleRows);
            GUI.EndGroup();
        }

        private void DrawDataRows(
            SerializedProperty arrayProperty,
            List<TableColumn> columns,
            HashSet<string> formulaTargetKeys,
            SpreadAssetSheetState sheetState,
            float dataWidth,
            GridRowLayout rowLayout,
            HashSet<string> invalidKeyCellKeys,
            VisibleRowRange visibleRows)
        {
            for (int row = visibleRows.StartIndex; row < visibleRows.EndIndex; row++)
            {
                SerializedProperty element = arrayProperty.GetArrayElementAtIndex(row);
                DrawRowCells(
                    new Rect(0f, rowLayout.GetY(row), dataWidth, rowLayout.GetHeight(row)),
                    arrayProperty,
                    element,
                    columns,
                    row,
                    formulaTargetKeys,
                    invalidKeyCellKeys,
                    sheetState);
            }
        }

        private void DrawClippedTableRows(Rect viewportRect, float contentWidth, float contentHeight, Action drawContent)
        {
            GUI.BeginGroup(viewportRect);
            GUI.BeginGroup(new Rect(-_tableScroll.x, 0f, contentWidth, contentHeight));
            drawContent();
            GUI.EndGroup();
            GUI.EndGroup();
        }

        private void DrawClippedTableArea(Rect viewportRect, float contentWidth, float contentHeight, Action drawContent)
        {
            GUI.BeginGroup(viewportRect);
            GUILayout.BeginArea(new Rect(-_tableScroll.x, 0f, contentWidth, contentHeight));
            drawContent();
            GUILayout.EndArea();
            GUI.EndGroup();
        }

        private void HandleHorizontalScrollWheel(
            Rect hitRect,
            float dataWidth,
            float dataViewportWidth,
            bool useHorizontalScroll)
        {
            Event current = Event.current;
            if (!useHorizontalScroll
                || current.type != EventType.ScrollWheel
                || !current.shift
                || !hitRect.Contains(current.mousePosition))
            {
                return;
            }

            float scrollDelta = Mathf.Abs(current.delta.x) > Mathf.Abs(current.delta.y)
                ? current.delta.x
                : current.delta.y;
            if (Mathf.Approximately(scrollDelta, 0f))
            {
                return;
            }

            float maxScroll = Mathf.Max(0f, dataWidth - dataViewportWidth);
            _tableScroll.x = Mathf.Clamp(_tableScroll.x + scrollDelta * HorizontalWheelScrollSpeed, 0f, maxScroll);
            current.Use();
            Repaint();
        }

        private void CompleteRowStructureChange(
            SerializedProperty arrayProperty,
            bool columnsChanged,
            SpreadAssetSheetState sheetState)
        {
            _serializedObject.ApplyModifiedProperties();
            _isDocumentDirty = true;
            if (columnsChanged)
            {
                RefreshArrayPropertyPaths();
            }

            List<TableColumn> columns = GetColumns(arrayProperty);
            InvalidateSheetFormulaCache(sheetState, markDirty: true);
            ApplyFormulas(arrayProperty, columns, sheetState, markDirty: true, forceRecompile: true);
            Repaint();
            GUIUtility.ExitGUI();
        }

        private void DrawRowCells(
            Rect rowRect,
            SerializedProperty arrayProperty,
            SerializedProperty element,
            List<TableColumn> columns,
            int rowIndex,
            HashSet<string> formulaTargetKeys,
            HashSet<string> invalidKeyCellKeys,
            SpreadAssetSheetState sheetState)
        {
            bool hasFocusedCell = TryGetFocusedCellPosition(
                arrayProperty.propertyPath,
                out int focusedRowIndex,
                out int focusedColumnIndex);

            if (columns.Count == 0)
            {
                Rect fallbackCellRect = new Rect(rowRect.x, rowRect.y, DefaultColumnWidth, rowRect.height);
                string controlName = GetCellControlName(arrayProperty.propertyPath, rowIndex, 0);
                GUI.SetNextControlName(controlName);
                if (TryCaptureFocusedCellFromMouseDown(arrayProperty.propertyPath, rowIndex, 0, fallbackCellRect))
                {
                    hasFocusedCell = true;
                    focusedRowIndex = rowIndex;
                    focusedColumnIndex = 0;
                }

                DrawFocusedCellBackground(
                    fallbackCellRect,
                    hasFocusedCell && rowIndex == focusedRowIndex,
                    hasFocusedCell && focusedColumnIndex == 0);
                DrawTablePropertyField(GetPropertyCellControlRect(fallbackCellRect, element), element);
                DrawCellTooltip(fallbackCellRect, rowIndex, 0);
                FocusPendingCellControl(controlName);
                return;
            }

            float x = rowRect.x;
            for (int columnIndex = 0; columnIndex < columns.Count; columnIndex++)
            {
                TableColumn column = columns[columnIndex];
                bool formulaControlled = formulaTargetKeys.Contains(GetCellKey(rowIndex, columnIndex));
                string controlName = GetCellControlName(arrayProperty.propertyPath, rowIndex, columnIndex);
                HandleCellNavigationKey(arrayProperty, columns, rowIndex, columnIndex, formulaTargetKeys, controlName);
                GUI.SetNextControlName(controlName);
                Rect cellAreaRect = new Rect(
                    x,
                    rowRect.y,
                    column.Width,
                    rowRect.height);
                if (TryCaptureFocusedCellFromMouseDown(arrayProperty.propertyPath, rowIndex, columnIndex, cellAreaRect))
                {
                    hasFocusedCell = true;
                    focusedRowIndex = rowIndex;
                    focusedColumnIndex = columnIndex;
                }

                bool isFocusedRow = hasFocusedCell && rowIndex == focusedRowIndex;
                bool isFocusedColumn = hasFocusedCell && columnIndex == focusedColumnIndex;
                DrawFocusedCellBackground(cellAreaRect, isFocusedRow, isFocusedColumn);
                DrawKeyValidationCellBackground(
                    cellAreaRect,
                    invalidKeyCellKeys != null && invalidKeyCellKeys.Contains(GetCellKey(rowIndex, columnIndex)));

                if (column.IsDesignField)
                {
                    Rect cellRect = GetCenteredCellControlRect(cellAreaRect);
                    using (new EditorGUI.DisabledScope(formulaControlled))
                    {
                        DrawDesignCell(cellRect, sheetState, rowIndex, column);
                    }

                    DrawCellTooltip(cellAreaRect, rowIndex, columnIndex);
                    FocusPendingCellControl(controlName);
                    x += column.Width;
                    continue;
                }

                SerializedProperty cell = GetCellProperty(element, column);
                if (cell == null)
                {
                    GUI.Label(GetCenteredCellControlRect(cellAreaRect), "-");
                    DrawCellTooltip(cellAreaRect, rowIndex, columnIndex);
                    x += column.Width;
                    continue;
                }

                using (new EditorGUI.DisabledScope(formulaControlled))
                {
                    Rect cellRect = GetPropertyCellControlRect(cellAreaRect, cell);
                    DrawTablePropertyField(cellRect, cell);
                }

                DrawCellTooltip(cellAreaRect, rowIndex, columnIndex);
                FocusPendingCellControl(controlName);
                x += column.Width;
            }
        }

        private static Rect GetCenteredCellControlRect(Rect cellAreaRect)
        {
            float horizontalPadding = Mathf.Min(CellControlHorizontalPadding, cellAreaRect.width * 0.5f);
            return new Rect(
                cellAreaRect.x + horizontalPadding,
                cellAreaRect.y + Mathf.Max(0f, (cellAreaRect.height - EditorGUIUtility.singleLineHeight) * 0.5f),
                Mathf.Max(1f, cellAreaRect.width - horizontalPadding * 2f),
                EditorGUIUtility.singleLineHeight);
        }

        private static Rect GetPropertyCellControlRect(Rect cellAreaRect, SerializedProperty property)
        {
            float propertyHeight = CalculatePropertyControlHeight(property);
            if (propertyHeight <= EditorGUIUtility.singleLineHeight + 0.5f)
            {
                return GetCenteredCellControlRect(cellAreaRect);
            }

            float horizontalPadding = Mathf.Min(CellControlHorizontalPadding, cellAreaRect.width * 0.5f);
            float verticalPadding = Mathf.Min(CellControlVerticalPadding, cellAreaRect.height * 0.5f);
            return new Rect(
                cellAreaRect.x + horizontalPadding,
                cellAreaRect.y + verticalPadding,
                Mathf.Max(1f, cellAreaRect.width - horizontalPadding * 2f),
                Mathf.Max(
                    EditorGUIUtility.singleLineHeight,
                    Mathf.Min(propertyHeight, cellAreaRect.height - verticalPadding * 2f)));
        }

        private static void DrawTablePropertyField(Rect cellRect, SerializedProperty property)
        {
            if (property == null)
            {
                return;
            }

            float previousLabelWidth = EditorGUIUtility.labelWidth;
            float previousFieldWidth = EditorGUIUtility.fieldWidth;
            bool previousWideMode = EditorGUIUtility.wideMode;
            int previousIndentLevel = EditorGUI.indentLevel;

            try
            {
                EditorGUI.indentLevel = 0;
                EditorGUIUtility.wideMode = true;
                EditorGUIUtility.labelWidth = CalculateTableCellLabelWidth(cellRect.width);
                EditorGUIUtility.fieldWidth = Mathf.Max(
                    CellMinimumPropertyFieldWidth,
                    cellRect.width - EditorGUIUtility.labelWidth);
                if (ShouldDrawTablePropertyTree(property))
                {
                    DrawTablePropertyTree(cellRect, property);
                }
                else
                {
                    DrawTablePropertyLine(cellRect, property, GUIContent.none);
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
                EditorGUIUtility.wideMode = previousWideMode;
                EditorGUIUtility.fieldWidth = previousFieldWidth;
                EditorGUIUtility.labelWidth = previousLabelWidth;
            }
        }

        private static void DrawTablePropertyTree(Rect cellRect, SerializedProperty property)
        {
            float y = cellRect.y;
            Rect rootRect = new Rect(cellRect.x, y, cellRect.width, GetTablePropertyLineHeight(property));
            DrawTablePropertyLine(rootRect, property, GUIContent.none);
            y = rootRect.yMax + EditorGUIUtility.standardVerticalSpacing;
            if (!property.isExpanded)
            {
                return;
            }

            SerializedProperty child = property.Copy();
            SerializedProperty end = property.GetEndProperty();
            bool enterChildren = true;
            while (child.NextVisible(enterChildren) && !SerializedProperty.EqualContents(child, end))
            {
                float lineHeight = GetTablePropertyLineHeight(child);
                if (y + lineHeight > cellRect.yMax + 0.5f)
                {
                    break;
                }

                int relativeDepth = Mathf.Max(0, child.depth - property.depth - 1);
                float indent = CalculateTableCellTreeIndent(cellRect.width, relativeDepth);
                Rect lineRect = new Rect(
                    cellRect.x + indent,
                    y,
                    Mathf.Max(1f, cellRect.width - indent),
                    lineHeight);
                DrawTablePropertyLine(lineRect, child, new GUIContent(child.displayName));
                y = lineRect.yMax + EditorGUIUtility.standardVerticalSpacing;
                enterChildren = ShouldDrawTablePropertyTree(child);
            }
        }

        private static void DrawTablePropertyLine(Rect lineRect, SerializedProperty property, GUIContent label)
        {
            if (IsTableExpandableProperty(property))
            {
                property.isExpanded = EditorGUI.Foldout(lineRect, property.isExpanded, label, true);
                return;
            }

            if (label == GUIContent.none || string.IsNullOrEmpty(label.text))
            {
                EditorGUI.PropertyField(lineRect, property, GUIContent.none, false);
                return;
            }

            float labelWidth = CalculateTableCellLabelWidth(lineRect.width);
            if (labelWidth <= 0f)
            {
                EditorGUI.PropertyField(lineRect, property, GUIContent.none, false);
                return;
            }

            Rect labelRect = new Rect(lineRect.x, lineRect.y, labelWidth, lineRect.height);
            Rect fieldRect = new Rect(
                labelRect.xMax + CellLabelGap,
                lineRect.y,
                Mathf.Max(1f, lineRect.width - labelWidth - CellLabelGap),
                lineRect.height);
            EditorGUI.LabelField(labelRect, label, EditorStyles.miniLabel);
            EditorGUI.PropertyField(fieldRect, property, GUIContent.none, false);
        }

        private static bool ShouldDrawTablePropertyTree(SerializedProperty property)
        {
            return IsTableExpandableProperty(property) && property.isExpanded;
        }

        private static bool IsTableExpandableProperty(SerializedProperty property)
        {
            return property != null
                && property.hasVisibleChildren
                && (property.propertyType == SerializedPropertyType.Generic
                    || property.propertyType == SerializedPropertyType.ManagedReference);
        }

        private static float GetTablePropertyLineHeight(SerializedProperty property)
        {
            if (IsTableExpandableProperty(property))
            {
                return EditorGUIUtility.singleLineHeight;
            }

            return Mathf.Max(
                EditorGUIUtility.singleLineHeight,
                EditorGUI.GetPropertyHeight(property, GUIContent.none, includeChildren: false));
        }

        private static float CalculateTableCellLabelWidth(float cellWidth)
        {
            float availableWidth = Mathf.Max(1f, cellWidth);
            if (availableWidth < CellHideLabelWidth)
            {
                return 0f;
            }

            float preferredLabelWidth = Mathf.Clamp(
                availableWidth * 0.35f,
                CellMinimumLabelWidth,
                CellMaximumLabelWidth);
            float maxLabelWidthWithField = Mathf.Max(
                CellMinimumLabelWidth,
                availableWidth - CellMinimumPropertyFieldWidth - CellLabelGap);
            return Mathf.Min(preferredLabelWidth, maxLabelWidthWithField);
        }

        private static float CalculateTableCellTreeIndent(float cellWidth, int relativeDepth)
        {
            if (relativeDepth <= 0 || cellWidth < CellHideLabelWidth)
            {
                return 0f;
            }

            return Mathf.Min(relativeDepth * CellTreeIndentWidth, CellMaximumTreeIndent);
        }

        private static Rect GetCenteredRowControlRect(Rect controlAreaRect)
        {
            float controlHeight = Mathf.Min(TableRowHeight, controlAreaRect.height);
            return new Rect(
                controlAreaRect.x,
                controlAreaRect.y + Mathf.Max(0f, (controlAreaRect.height - controlHeight) * 0.5f),
                controlAreaRect.width,
                controlHeight);
        }

        private static void DrawCellTooltip(Rect cellRect, int rowIndex, int columnIndex)
        {
            GUI.Label(
                cellRect,
                new GUIContent(string.Empty, GetCellAddress(rowIndex, columnIndex)),
                GUIStyle.none);
        }

        private static void DrawFocusedCellBackground(Rect cellRect, bool isFocusedRow, bool isFocusedColumn)
        {
            if (!isFocusedRow && !isFocusedColumn)
            {
                return;
            }

            EditorGUI.DrawRect(cellRect, GetFocusedCellBackgroundColor(isFocusedRow, isFocusedColumn));
        }

        private static void DrawKeyValidationCellBackground(Rect cellRect, bool isInvalidKeyCell)
        {
            if (!isInvalidKeyCell)
            {
                return;
            }

            EditorGUI.DrawRect(cellRect, GetKeyValidationCellBackgroundColor());
        }

        private static void DrawFocusedRowHeaderBackground(Rect rect, bool isFocusedRow)
        {
            if (isFocusedRow)
            {
                EditorGUI.DrawRect(rect, GetFocusedCellBackgroundColor(true, false));
            }
        }

        private static void DrawFocusedColumnHeaderBackground(Rect rect, bool isFocusedColumn)
        {
            if (isFocusedColumn)
            {
                EditorGUI.DrawRect(rect, GetFocusedCellBackgroundColor(false, true));
            }
        }

        private static Color GetFocusedCellBackgroundColor(bool isFocusedRow, bool isFocusedColumn)
        {
            if (isFocusedRow && isFocusedColumn)
            {
                return EditorGUIUtility.isProSkin
                    ? new Color(0.34f, 0.57f, 0.88f, 0.34f)
                    : new Color(0.24f, 0.47f, 0.86f, 0.22f);
            }

            if (isFocusedRow)
            {
                return EditorGUIUtility.isProSkin
                    ? new Color(0.96f, 0.70f, 0.30f, 0.20f)
                    : new Color(1.00f, 0.80f, 0.24f, 0.20f);
            }

            return EditorGUIUtility.isProSkin
                ? new Color(0.26f, 0.58f, 0.95f, 0.18f)
                : new Color(0.24f, 0.52f, 0.95f, 0.14f);
        }

        private static Color GetKeyValidationCellBackgroundColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.95f, 0.20f, 0.16f, 0.34f)
                : new Color(1.00f, 0.12f, 0.08f, 0.24f);
        }

        private static string GetCellAddress(int rowIndex, int columnIndex)
        {
            return GetColumnName(columnIndex) + (rowIndex + 1).ToString(CultureInfo.InvariantCulture);
        }

        private void DrawDesignCell(Rect cellRect, SpreadAssetSheetState sheetState, int rowIndex, TableColumn column)
        {
            string currentValue = GetDesignCellValue(sheetState, rowIndex, column);
            EditorGUI.BeginChangeCheck();
            string nextValue = DrawDesignCellValue(cellRect, column.TypeName, currentValue);
            if (EditorGUI.EndChangeCheck())
            {
                SetDesignCellValue(sheetState, rowIndex, column, nextValue);
                _isDocumentDirty = true;
                MarkSheetFormulasDirty(sheetState);
            }
        }

        private static string DrawDesignCellValue(Rect cellRect, string typeName, string value)
        {
            if (SpreadAssetEnumTypeUtility.TryGetAnnotatedEnumType(typeName, out Type enumType))
            {
                string[] names = Enum.GetNames(enumType);
                if (names.Length == 0)
                {
                    return value ?? string.Empty;
                }

                int selectedIndex = GetEnumIndex(names, value);
                selectedIndex = EditorGUI.Popup(cellRect, selectedIndex, names);
                return names[Mathf.Clamp(selectedIndex, 0, names.Length - 1)];
            }

            string normalizedType = NormalizeTypeName(typeName);
            switch (normalizedType)
            {
                case "byte":
                case "sbyte":
                case "short":
                case "ushort":
                case "int":
                case "uint":
                    int intValue = ParseInt(value);
                    intValue = EditorGUI.IntField(cellRect, intValue);
                    return intValue.ToString(CultureInfo.InvariantCulture);
                case "long":
                case "ulong":
                    long longValue = ParseLong(value);
                    longValue = EditorGUI.LongField(cellRect, longValue);
                    return longValue.ToString(CultureInfo.InvariantCulture);
                case "float":
                    float floatValue = ParseFloat(value);
                    floatValue = EditorGUI.FloatField(cellRect, floatValue);
                    return floatValue.ToString(CultureInfo.InvariantCulture);
                case "double":
                case "decimal":
                    double doubleValue = ParseDouble(value);
                    doubleValue = EditorGUI.DoubleField(cellRect, doubleValue);
                    return doubleValue.ToString(CultureInfo.InvariantCulture);
                case "bool":
                case "boolean":
                    bool boolValue = ParseBool(value);
                    boolValue = EditorGUI.Toggle(cellRect, boolValue);
                    return boolValue.ToString();
                default:
                    return EditorGUI.TextField(cellRect, value ?? string.Empty);
            }
        }

        private static int GetEnumIndex(string[] names, string value)
        {
            if (names == null || names.Length == 0)
            {
                return 0;
            }

            string normalizedValue = (value ?? string.Empty).Trim();
            for (int i = 0; i < names.Length; i++)
            {
                if (string.Equals(names[i], normalizedValue, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            if (int.TryParse(normalizedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedIndex))
            {
                return Mathf.Clamp(parsedIndex, 0, names.Length - 1);
            }

            return 0;
        }

        private void HandleCellNavigationKey(
            SerializedProperty arrayProperty,
            List<TableColumn> columns,
            int rowIndex,
            int columnIndex,
            HashSet<string> formulaTargetKeys,
            string controlName)
        {
            Event current = Event.current;
            if (current.type != EventType.KeyDown || GUI.GetNameOfFocusedControl() != controlName)
            {
                return;
            }

            if (current.keyCode == KeyCode.Tab)
            {
                int direction = current.shift ? -1 : 1;
                if (TryFindEditableCellInRow(
                        arrayProperty,
                        columns,
                        rowIndex,
                        columnIndex + direction,
                        direction,
                        formulaTargetKeys,
                        out string targetControlName,
                        out int targetRowIndex))
                {
                    RequestCellFocus(targetControlName, targetRowIndex);
                    current.Use();
                }

                return;
            }

            if (current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter)
            {
                int direction = current.shift ? -1 : 1;
                if (TryFindEditableCellInColumn(
                        arrayProperty,
                        columns,
                        rowIndex + direction,
                        columnIndex,
                        direction,
                        formulaTargetKeys,
                        out string targetControlName,
                        out int targetRowIndex))
                {
                    RequestCellFocus(targetControlName, targetRowIndex);
                    current.Use();
                }
            }
        }

        private void RequestCellFocus(string controlName)
        {
            RememberFocusedCell(controlName);
            _pendingCellFocusControlName = controlName;
            ClearTextFieldFocus();
            Repaint();
        }

        private void RequestCellFocus(string controlName, int rowIndex)
        {
            RememberFocusedCell(controlName);
            _pendingCellFocusControlName = controlName;
            if (TryParseCellControlName(
                    controlName,
                    out string arrayPropertyPath,
                    out _,
                    out int columnIndex))
            {
                _pendingCellScrollArrayPropertyPath = arrayPropertyPath;
                _pendingCellScrollRowIndex = rowIndex;
                _pendingCellScrollColumnIndex = columnIndex;
            }

            ScrollRowIntoView(rowIndex);
            ClearTextFieldFocus();
            Repaint();
        }

        private void RequestCellFocus(
            string controlName,
            int rowIndex,
            List<TableColumn> columns,
            int columnIndex)
        {
            RememberFocusedCell(controlName);
            _pendingCellFocusControlName = controlName;
            _pendingCellScrollArrayPropertyPath = _focusedCellArrayPropertyPath;
            _pendingCellScrollRowIndex = rowIndex;
            _pendingCellScrollColumnIndex = columnIndex;
            ScrollCellIntoView(rowIndex, columns, columnIndex);
            ClearTextFieldFocus();
            Repaint();
        }

        private void ScrollCellIntoView(int rowIndex, List<TableColumn> columns, int columnIndex)
        {
            ScrollRowIntoView(rowIndex);

            float frozenWidth = CalculateFrozenRowHeaderWidth();
            float dataWidth = CalculateDataGridWidth(columns);
            float dataViewportWidth = CalculateDataGridViewportWidth(frozenWidth);
            ScrollColumnIntoView(columns, columnIndex, dataWidth, dataViewportWidth);
        }

        private void ScrollRowIntoView(int rowIndex)
        {
            if (rowIndex < 0 || _tableRowViewportHeight <= 0f)
            {
                return;
            }

            float rowTop = CalculateGridRowY(rowIndex);
            float rowBottom = rowTop + TableRowHeight;
            if (rowTop < _propertyScroll.y)
            {
                _propertyScroll.y = rowTop;
                return;
            }

            float viewportBottom = _propertyScroll.y + _tableRowViewportHeight;
            if (rowBottom > viewportBottom)
            {
                _propertyScroll.y = Mathf.Max(0f, rowBottom - _tableRowViewportHeight);
            }
        }

        private void ScrollRowIntoView(int rowIndex, GridRowLayout rowLayout)
        {
            if (rowIndex < 0 || rowIndex >= rowLayout.Count || _tableRowViewportHeight <= 0f)
            {
                return;
            }

            float rowTop = rowLayout.GetY(rowIndex);
            float rowBottom = rowLayout.GetBottom(rowIndex);
            if (rowBottom - rowTop > _tableRowViewportHeight)
            {
                _propertyScroll.y = rowTop;
                return;
            }

            if (rowTop < _propertyScroll.y)
            {
                _propertyScroll.y = rowTop;
                return;
            }

            float viewportBottom = _propertyScroll.y + _tableRowViewportHeight;
            if (rowBottom > viewportBottom)
            {
                _propertyScroll.y = Mathf.Max(0f, rowBottom - _tableRowViewportHeight);
            }
        }

        private void ScrollColumnIntoView(
            List<TableColumn> columns,
            int columnIndex,
            float dataWidth,
            float dataViewportWidth)
        {
            if (columnIndex < 0 || !ShouldUseHorizontalGridScroll(dataWidth, dataViewportWidth))
            {
                return;
            }

            float columnLeft = CalculateColumnX(columns, columnIndex);
            float columnRight = columnLeft + GetColumnWidth(columns, columnIndex);
            if (columnLeft < _tableScroll.x)
            {
                _tableScroll.x = columnLeft;
            }
            else if (columnRight > _tableScroll.x + dataViewportWidth)
            {
                _tableScroll.x = columnRight - dataViewportWidth;
            }

            _tableScroll.x = Mathf.Clamp(_tableScroll.x, 0f, Mathf.Max(0f, dataWidth - dataViewportWidth));
        }

        private void ApplyPendingCellScroll(
            string arrayPropertyPath,
            List<TableColumn> columns,
            float dataWidth,
            float dataViewportWidth,
            GridRowLayout rowLayout)
        {
            if (_pendingCellScrollRowIndex < 0
                || _pendingCellScrollColumnIndex < 0
                || string.IsNullOrEmpty(_pendingCellScrollArrayPropertyPath)
                || !string.Equals(_pendingCellScrollArrayPropertyPath, arrayPropertyPath, StringComparison.Ordinal))
            {
                return;
            }

            ScrollRowIntoView(_pendingCellScrollRowIndex, rowLayout);
            ScrollColumnIntoView(columns, _pendingCellScrollColumnIndex, dataWidth, dataViewportWidth);
            ClearPendingCellScroll();
        }

        private void ClearPendingCellScroll()
        {
            _pendingCellScrollArrayPropertyPath = string.Empty;
            _pendingCellScrollRowIndex = -1;
            _pendingCellScrollColumnIndex = -1;
        }

        private void FocusPendingCellControl(string controlName)
        {
            if (string.IsNullOrEmpty(_pendingCellFocusControlName)
                || _pendingCellFocusControlName != controlName
                || Event.current.type != EventType.Repaint)
            {
                return;
            }

            GUI.FocusControl(controlName);
            EditorGUI.FocusTextInControl(controlName);
            RememberFocusedCell(controlName);
            _pendingCellFocusControlName = string.Empty;
        }

        private void ReleaseCellEditorForVerticalScroll()
        {
            if (!IsCellTextEditorActive())
            {
                return;
            }

            ClearTextFieldFocus();
        }

        private bool IsCellTextEditorActive()
        {
            string focusedControl = GUI.GetNameOfFocusedControl();
            if (TryParseCellControlName(focusedControl, out _, out _, out _))
            {
                return true;
            }

            return string.IsNullOrEmpty(focusedControl)
                && !string.IsNullOrEmpty(_focusedCellArrayPropertyPath)
                && GUIUtility.keyboardControl != 0
                && EditorGUIUtility.editingTextField;
        }

        private static bool TryFindEditableCellInRow(
            SerializedProperty arrayProperty,
            List<TableColumn> columns,
            int rowIndex,
            int startColumnIndex,
            int direction,
            HashSet<string> formulaTargetKeys,
            out string controlName,
            out int targetRowIndex)
        {
            controlName = string.Empty;
            targetRowIndex = -1;
            for (int columnIndex = startColumnIndex;
                columnIndex >= 0 && columnIndex < columns.Count;
                columnIndex += direction)
            {
                if (IsEditableCell(arrayProperty, columns, rowIndex, columnIndex, formulaTargetKeys))
                {
                    controlName = GetCellControlName(arrayProperty.propertyPath, rowIndex, columnIndex);
                    targetRowIndex = rowIndex;
                    return true;
                }
            }

            return false;
        }

        private static bool TryFindEditableCellInColumn(
            SerializedProperty arrayProperty,
            List<TableColumn> columns,
            int startRowIndex,
            int columnIndex,
            int direction,
            HashSet<string> formulaTargetKeys,
            out string controlName,
            out int targetRowIndex)
        {
            controlName = string.Empty;
            targetRowIndex = -1;
            for (int rowIndex = startRowIndex;
                rowIndex >= 0 && rowIndex < arrayProperty.arraySize;
                rowIndex += direction)
            {
                if (IsEditableCell(arrayProperty, columns, rowIndex, columnIndex, formulaTargetKeys))
                {
                    controlName = GetCellControlName(arrayProperty.propertyPath, rowIndex, columnIndex);
                    targetRowIndex = rowIndex;
                    return true;
                }
            }

            return false;
        }

        private static bool IsEditableCell(
            SerializedProperty arrayProperty,
            List<TableColumn> columns,
            int rowIndex,
            int columnIndex,
            HashSet<string> formulaTargetKeys)
        {
            if (rowIndex < 0
                || rowIndex >= arrayProperty.arraySize
                || columnIndex < 0
                || columnIndex >= columns.Count
                || formulaTargetKeys.Contains(GetCellKey(rowIndex, columnIndex)))
            {
                return false;
            }

            if (columns[columnIndex].IsDesignField)
            {
                return true;
            }

            SerializedProperty element = arrayProperty.GetArrayElementAtIndex(rowIndex);
            return GetCellProperty(element, columns[columnIndex]) != null;
        }

        private static SerializedProperty GetCellProperty(SerializedProperty element, TableColumn column)
        {
            if (string.IsNullOrEmpty(column.PropertyName))
            {
                return element;
            }

            SerializedProperty property = element.FindPropertyRelative(column.PropertyName);
            if (property != null)
            {
                return property;
            }

            if (string.IsNullOrEmpty(column.SchemaName) || column.SchemaName == column.PropertyName)
            {
                return null;
            }

            property = element.FindPropertyRelative(column.SchemaName);
            if (property != null)
            {
                return property;
            }

            string camelCaseSchemaName = ToLowerCamelCase(column.SchemaName);
            return camelCaseSchemaName == column.SchemaName
                ? null
                : element.FindPropertyRelative(camelCaseSchemaName);
        }

        private static string ToLowerCamelCase(string value)
        {
            return string.IsNullOrEmpty(value)
                ? string.Empty
                : char.ToLowerInvariant(value[0]) + value.Substring(1);
        }

        private static string GetCellControlName(string arrayPropertyPath, int rowIndex, int columnIndex)
        {
            return $"{CellControlPrefix}{arrayPropertyPath}.{rowIndex}.{columnIndex}";
        }

        private bool TryGetFocusedCellPosition(
            string arrayPropertyPath,
            out int rowIndex,
            out int columnIndex)
        {
            rowIndex = -1;
            columnIndex = -1;

            if (!string.IsNullOrEmpty(_focusedCellArrayPropertyPath)
                && string.Equals(_focusedCellArrayPropertyPath, arrayPropertyPath, StringComparison.Ordinal)
                && _focusedCellRowIndex >= 0
                && _focusedCellColumnIndex >= 0)
            {
                rowIndex = _focusedCellRowIndex;
                columnIndex = _focusedCellColumnIndex;
                return true;
            }

            string focusedControl = GUI.GetNameOfFocusedControl();
            if (TryParseCellControlName(
                    focusedControl,
                    out string focusedArrayPropertyPath,
                    out rowIndex,
                    out columnIndex)
                && string.Equals(focusedArrayPropertyPath, arrayPropertyPath, StringComparison.Ordinal))
            {
                RememberFocusedCell(focusedArrayPropertyPath, rowIndex, columnIndex);
                return true;
            }

            rowIndex = -1;
            columnIndex = -1;
            return false;
        }

        private bool TryCaptureFocusedCellFromMouseDown(
            string arrayPropertyPath,
            int rowIndex,
            int columnIndex,
            Rect cellRect)
        {
            Event current = Event.current;
            if (current.type != EventType.MouseDown
                || current.button != 0
                || !cellRect.Contains(current.mousePosition))
            {
                return false;
            }

            RememberFocusedCell(arrayPropertyPath, rowIndex, columnIndex);
            Repaint();
            return true;
        }

        private void RememberFocusedCell(string controlName)
        {
            if (TryParseCellControlName(
                    controlName,
                    out string arrayPropertyPath,
                    out int rowIndex,
                    out int columnIndex))
            {
                RememberFocusedCell(arrayPropertyPath, rowIndex, columnIndex);
            }
        }

        private void RememberFocusedCell(string arrayPropertyPath, int rowIndex, int columnIndex)
        {
            _focusedCellArrayPropertyPath = arrayPropertyPath ?? string.Empty;
            _focusedCellRowIndex = rowIndex;
            _focusedCellColumnIndex = columnIndex;
        }

        private void ClearFocusedCell()
        {
            _focusedCellArrayPropertyPath = string.Empty;
            _focusedCellRowIndex = -1;
            _focusedCellColumnIndex = -1;
        }

        private static bool TryParseCellControlName(
            string controlName,
            out string arrayPropertyPath,
            out int rowIndex,
            out int columnIndex)
        {
            arrayPropertyPath = string.Empty;
            rowIndex = -1;
            columnIndex = -1;

            if (string.IsNullOrEmpty(controlName)
                || !controlName.StartsWith(CellControlPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            string suffix = controlName.Substring(CellControlPrefix.Length);
            int columnSeparatorIndex = suffix.LastIndexOf('.');
            int rowSeparatorIndex = columnSeparatorIndex > 0
                ? suffix.LastIndexOf('.', columnSeparatorIndex - 1)
                : -1;
            if (rowSeparatorIndex <= 0
                || columnSeparatorIndex <= rowSeparatorIndex + 1
                || columnSeparatorIndex >= suffix.Length - 1)
            {
                return false;
            }

            if (!int.TryParse(
                    suffix.Substring(rowSeparatorIndex + 1, columnSeparatorIndex - rowSeparatorIndex - 1),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out rowIndex)
                || !int.TryParse(
                    suffix.Substring(columnSeparatorIndex + 1),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out columnIndex))
            {
                rowIndex = -1;
                columnIndex = -1;
                return false;
            }

            if (rowIndex < 0 || columnIndex < 0)
            {
                rowIndex = -1;
                columnIndex = -1;
                return false;
            }

            arrayPropertyPath = suffix.Substring(0, rowSeparatorIndex);
            return true;
        }

        private static void ApplyColumnWidths(List<TableColumn> columns, SpreadAssetSheetState sheetState)
        {
            if (columns == null || sheetState?.Columns == null)
            {
                return;
            }

            for (int columnIndex = 0; columnIndex < columns.Count; columnIndex++)
            {
                TableColumn column = columns[columnIndex];
                SpreadAssetColumnState columnState = FindSheetColumn(sheetState, column);
                if (columnState == null || columnState.Width <= 0f)
                {
                    continue;
                }

                float width = Mathf.Clamp(columnState.Width, ManualMinimumColumnWidth, ManualMaximumColumnWidth);
                columns[columnIndex] = column.WithWidth(width);
            }
        }

        private static bool SetSheetColumnWidth(SpreadAssetSheetState sheetState, TableColumn column, float width)
        {
            if (sheetState == null)
            {
                return false;
            }

            if (sheetState.Columns == null)
            {
                sheetState.Columns = Array.Empty<SpreadAssetColumnState>();
            }

            SpreadAssetColumnState columnState = FindSheetColumn(sheetState, column);
            if (columnState == null)
            {
                columnState = new SpreadAssetColumnState
                {
                    ColumnId = column.FieldId,
                    ColumnName = GetColumnStateName(column)
                };

                Array.Resize(ref sheetState.Columns, sheetState.Columns.Length + 1);
                sheetState.Columns[sheetState.Columns.Length - 1] = columnState;
            }

            bool changed = !Mathf.Approximately(columnState.Width, width);
            columnState.Width = width;
            changed |= BackfillSheetColumnIdentity(columnState, column);
            return changed;
        }

        private static SpreadAssetColumnState FindSheetColumn(SpreadAssetSheetState sheetState, TableColumn column)
        {
            if (sheetState?.Columns == null)
            {
                return null;
            }

            string columnName = GetColumnStateName(column);
            SpreadAssetColumnState fallbackColumn = null;
            foreach (SpreadAssetColumnState columnState in sheetState.Columns)
            {
                if (columnState == null)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(column.FieldId)
                    && string.Equals(columnState.ColumnId, column.FieldId, StringComparison.Ordinal))
                {
                    BackfillSheetColumnIdentity(columnState, column);
                    return columnState;
                }

                if (fallbackColumn == null
                    && !string.IsNullOrEmpty(columnName)
                    && string.Equals(columnState.ColumnName, columnName, StringComparison.Ordinal))
                {
                    fallbackColumn = columnState;
                }
            }

            if (fallbackColumn != null)
            {
                BackfillSheetColumnIdentity(fallbackColumn, column);
            }

            return fallbackColumn;
        }

        private static bool BackfillSheetColumnIdentity(SpreadAssetColumnState columnState, TableColumn column)
        {
            if (columnState == null)
            {
                return false;
            }

            bool changed = false;
            if (string.IsNullOrEmpty(columnState.ColumnId) && !string.IsNullOrEmpty(column.FieldId))
            {
                columnState.ColumnId = column.FieldId;
                changed = true;
            }

            string columnName = GetColumnStateName(column);
            if (!string.IsNullOrEmpty(columnName)
                && !string.Equals(columnState.ColumnName, columnName, StringComparison.Ordinal))
            {
                columnState.ColumnName = columnName;
                changed = true;
            }

            return changed;
        }

        private static string GetColumnStateName(TableColumn column)
        {
            if (!string.IsNullOrEmpty(column.SchemaName))
            {
                return column.SchemaName;
            }

            if (!string.IsNullOrEmpty(column.PropertyName))
            {
                return column.PropertyName;
            }

            return column.DisplayName ?? string.Empty;
        }

        private static string GetDesignCellValue(
            SpreadAssetSheetState sheetState,
            int rowIndex,
            TableColumn column)
        {
            SpreadAssetCellState cell = FindSheetCell(sheetState, rowIndex, column);
            return cell?.Value ?? string.Empty;
        }

        private static void SetDesignCellValue(
            SpreadAssetSheetState sheetState,
            int rowIndex,
            TableColumn column,
            string value)
        {
            if (sheetState == null)
            {
                return;
            }

            if (sheetState.Cells == null)
            {
                sheetState.Cells = Array.Empty<SpreadAssetCellState>();
            }

            SpreadAssetCellState cell = FindSheetCell(sheetState, rowIndex, column);
            if (cell == null)
            {
                cell = new SpreadAssetCellState
                {
                    Row = rowIndex,
                    ColumnId = column.FieldId,
                    ColumnName = column.SchemaName
                };

                Array.Resize(ref sheetState.Cells, sheetState.Cells.Length + 1);
                sheetState.Cells[sheetState.Cells.Length - 1] = cell;
            }

            cell.Value = value ?? string.Empty;
        }

        private static SpreadAssetCellState FindSheetCell(
            SpreadAssetSheetState sheetState,
            int rowIndex,
            TableColumn column)
        {
            if (sheetState?.Cells == null)
            {
                return null;
            }

            SpreadAssetCellState fallbackCell = null;
            foreach (SpreadAssetCellState cell in sheetState.Cells)
            {
                if (cell == null)
                {
                    continue;
                }

                if (cell.Row != rowIndex)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(column.FieldId)
                    && string.Equals(cell.ColumnId, column.FieldId, StringComparison.Ordinal))
                {
                    BackfillSheetCellIdentity(cell, column);
                    return cell;
                }

                if (fallbackCell == null
                    && !string.IsNullOrEmpty(column.SchemaName)
                    && string.Equals(cell.ColumnName, column.SchemaName, StringComparison.Ordinal))
                {
                    fallbackCell = cell;
                }
            }

            if (fallbackCell != null)
            {
                BackfillSheetCellIdentity(fallbackCell, column);
            }

            return fallbackCell;
        }

        private static void BackfillSheetCellIdentity(SpreadAssetCellState cell, TableColumn column)
        {
            if (cell == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(cell.ColumnId) && !string.IsNullOrEmpty(column.FieldId))
            {
                cell.ColumnId = column.FieldId;
            }

            if (!string.IsNullOrEmpty(column.SchemaName)
                && !string.Equals(cell.ColumnName, column.SchemaName, StringComparison.Ordinal))
            {
                cell.ColumnName = column.SchemaName;
            }
        }

        private static void ClearSheetCells(SpreadAssetSheetState sheetState)
        {
            if (sheetState != null)
            {
                sheetState.Cells = Array.Empty<SpreadAssetCellState>();
            }
        }

        private static void TrimSheetCells(SpreadAssetSheetState sheetState, int rowCount)
        {
            if (sheetState?.Cells == null || sheetState.Cells.Length == 0)
            {
                return;
            }

            List<SpreadAssetCellState> cells = new List<SpreadAssetCellState>();
            foreach (SpreadAssetCellState cell in sheetState.Cells)
            {
                if (cell != null && cell.Row >= 0 && cell.Row < rowCount)
                {
                    cells.Add(cell);
                }
            }

            sheetState.Cells = cells.ToArray();
        }

        private static void ShiftSheetCellsForInsert(SpreadAssetSheetState sheetState, int rowIndex)
        {
            if (sheetState?.Cells == null)
            {
                return;
            }

            foreach (SpreadAssetCellState cell in sheetState.Cells)
            {
                if (cell != null && cell.Row >= rowIndex)
                {
                    cell.Row++;
                }
            }
        }

        private static void ShiftSheetCellsForDelete(SpreadAssetSheetState sheetState, int rowIndex)
        {
            if (sheetState?.Cells == null || sheetState.Cells.Length == 0)
            {
                return;
            }

            List<SpreadAssetCellState> cells = new List<SpreadAssetCellState>();
            foreach (SpreadAssetCellState cell in sheetState.Cells)
            {
                if (cell == null || cell.Row == rowIndex)
                {
                    continue;
                }

                if (cell.Row > rowIndex)
                {
                    cell.Row--;
                }

                cells.Add(cell);
            }

            sheetState.Cells = cells.ToArray();
        }

        private void DrawFooter()
        {
            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label(_isDocumentDirty ? "Unsaved" : "Saved", EditorStyles.miniLabel);
                GUILayout.Space(8);
                GUILayout.Label(SpreadAssetDocumentIO.ResolveLinkedAssetPath(_document), EditorStyles.miniLabel);
            }
        }

        private void DrawArrayDataTabBar()
        {
            EditorGUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                DrawArrayDataButtons();
                GUILayout.FlexibleSpace();
            }
        }

        private void DrawArrayDataButtons()
        {
            if (_arrayPropertyPaths.Count == 0)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    GUILayout.Button("No Array Data", EditorStyles.toolbarButton, GUILayout.Width(112));
                }

                return;
            }

            _selectedArrayIndex = Mathf.Clamp(_selectedArrayIndex, 0, _arrayPropertyPaths.Count - 1);
            for (int i = 0; i < _arrayPropertyPaths.Count; i++)
            {
                SerializedProperty property = _serializedObject.FindProperty(_arrayPropertyPaths[i]);
                string label = property?.displayName ?? _arrayPropertyPaths[i];
                bool selected = i == _selectedArrayIndex;
                bool nextSelected = GUILayout.Toggle(
                    selected,
                    label,
                    EditorStyles.toolbarButton,
                    GUILayout.MinWidth(96));
                if (nextSelected && !selected)
                {
                    _selectedArrayIndex = i;
                    ClearFocusedCell();
                    ClearPendingCellScroll();
                    _searchStatus = string.Empty;
                    GUI.FocusControl(null);
                    Repaint();
                }
            }
        }

        private void SaveDocument(bool exportToAsset)
        {
            if (_document == null || _workingCopy == null || string.IsNullOrEmpty(_documentPath))
            {
                return;
            }

            _serializedObject.ApplyModifiedProperties();
            CommitAllFormulaDrafts();
            if (!TryApplyAllSheetFormulas(out string formulaError))
            {
                EditorUtility.DisplayDialog("Formula Error", formulaError, "OK");
                return;
            }

            KeyValidationResult keyValidation = exportToAsset ? ValidateAllKeyFields() : new KeyValidationResult();
            SpreadAssetDocumentSync.CaptureWorkingCopy(_document, _workingCopy);
            SpreadAssetDocumentSync.EnsureDocumentData(_document, _targetAsset);
            SpreadAssetDocumentIO.Write(_documentPath, _document);
            AssetDatabase.ImportAsset(_documentPath);

            if (exportToAsset)
            {
                if (keyValidation.HasErrors)
                {
                    EditorUtility.DisplayDialog(
                        "Export Blocked",
                        "Saved the .spreadasset source, but the linked .asset was not exported because key validation failed.\n\n"
                        + FormatKeyValidationSummary(keyValidation, 10),
                        "OK");
                    _isDocumentDirty = false;
                    return;
                }

                SpreadAssetDocumentSync.ExportToLinkedAsset(_document, _targetAsset);
            }

            _isDocumentDirty = false;
        }

        private void ImportFromLinkedAsset()
        {
            if (_document == null || _targetAsset == null)
            {
                return;
            }

            bool import = EditorUtility.DisplayDialog(
                "Import linked asset data?",
                "This replaces the source data inside the .spreadasset document with the current linked .asset data.",
                "Import",
                "Cancel");

            if (!import)
            {
                return;
            }

            SpreadAssetDocumentSync.ImportFromLinkedAsset(_document, _targetAsset);
            SpreadAssetDocumentSync.EnsureDocumentData(_document, _targetAsset);
            RefreshDocumentSchemaFromGeneratedFactory(_document, _targetAsset);
            EnsureDocumentSheetsForSchema(_document);
            SpreadAssetDocumentIO.Write(_documentPath, _document);
            AssetDatabase.ImportAsset(_documentPath);
            RecreateWorkingCopy();
            _isDocumentDirty = false;
        }

        private void ExportSelectedSheetToCsv()
        {
            if (!TryPrepareSelectedSheetForCsv(
                    out SerializedProperty arrayProperty,
                    out SpreadAssetSheetState sheetState,
                    out List<TableColumn> columns,
                    out string error))
            {
                EditorUtility.DisplayDialog("Export CSV", error, "OK");
                return;
            }

            string path = EditorUtility.SaveFilePanel(
                "Export SpreadAsset CSV",
                GetCsvPanelDirectory(),
                CreateCsvFileName(arrayProperty, sheetState),
                "csv");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            List<List<string>> rows = CreateCsvRows(arrayProperty, columns, sheetState, out int unsupportedCellCount);
            try
            {
                SpreadAssetCsvUtility.WriteFile(path, rows);
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("Export CSV", exception.Message, "OK");
                return;
            }

            string message = $"Exported {arrayProperty.arraySize} rows from {GetSheetDisplayName(arrayProperty, sheetState)}.";
            if (unsupportedCellCount > 0)
            {
                message += $"\n{unsupportedCellCount} complex cells were left empty because they are not supported by CSV import/export.";
            }

            EditorUtility.DisplayDialog("Export CSV", message, "OK");
        }

        private void ImportSelectedSheetFromCsv()
        {
            if (!TryPrepareSelectedSheetForCsv(
                    out SerializedProperty arrayProperty,
                    out SpreadAssetSheetState sheetState,
                    out List<TableColumn> columns,
                    out string error))
            {
                EditorUtility.DisplayDialog("Import CSV", error, "OK");
                return;
            }

            string path = EditorUtility.OpenFilePanel(
                "Import SpreadAsset CSV",
                GetCsvPanelDirectory(),
                "csv");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            if (!SpreadAssetCsvUtility.TryReadFile(path, out List<List<string>> rows, out error))
            {
                EditorUtility.DisplayDialog("Import CSV", error, "OK");
                return;
            }

            if (!TryCreateCsvImportPlan(rows, columns, out CsvImportPlan importPlan, out error))
            {
                EditorUtility.DisplayDialog("Import CSV", error, "OK");
                return;
            }

            string sheetName = GetSheetDisplayName(arrayProperty, sheetState);
            bool import = EditorUtility.DisplayDialog(
                "Import CSV?",
                $"Import {importPlan.RowCount} rows into {sheetName}?\n\nThis updates the selected sheet in the editor. Use Save & Export when you are ready to write the .spreadasset source and linked .asset.",
                "Import",
                "Cancel");
            if (!import)
            {
                return;
            }

            string importBackupJson = EditorJsonUtility.ToJson(_workingCopy, true);
            SpreadAssetCellState[] importBackupCells = CloneSheetCells(sheetState.Cells);
            if (!TryApplyCsvImportPlan(arrayProperty, columns, sheetState, rows, importPlan, out CsvImportSummary summary, out error))
            {
                RestoreCsvImportBackup(importBackupJson, sheetState, importBackupCells);
                EditorUtility.DisplayDialog("Import CSV", error, "OK");
                return;
            }

            _serializedObject.ApplyModifiedProperties();
            InvalidateSheetFormulaCache(sheetState, markDirty: true);
            ApplyFormulas(arrayProperty, columns, sheetState, markDirty: true, forceRecompile: true);
            if (!string.IsNullOrEmpty(_formulaError))
            {
                RestoreCsvImportBackup(importBackupJson, sheetState, importBackupCells);
                EditorUtility.DisplayDialog("Import CSV", _formulaError, "OK");
                return;
            }

            _isDocumentDirty = true;
            RefreshArrayPropertyPaths();
            Repaint();

            string message = $"Imported {summary.RowCount} rows into {sheetName}.";
            if (summary.UnknownHeaderCount > 0)
            {
                message += $"\nIgnored {summary.UnknownHeaderCount} unmatched CSV columns.";
            }

            if (summary.UnsupportedCellCount > 0)
            {
                message += $"\nSkipped {summary.UnsupportedCellCount} cells whose Unity property type is not supported by CSV import.";
            }

            EditorUtility.DisplayDialog("Import CSV", message, "OK");
        }

        private void RestoreCsvImportBackup(
            string serializedAssetJson,
            SpreadAssetSheetState sheetState,
            SpreadAssetCellState[] cells)
        {
            if (_workingCopy != null && !string.IsNullOrEmpty(serializedAssetJson))
            {
                EditorJsonUtility.FromJsonOverwrite(serializedAssetJson, _workingCopy);
                _serializedObject = new SerializedObject(_workingCopy);
            }

            if (sheetState != null)
            {
                sheetState.Cells = CloneSheetCells(cells);
                InvalidateSheetFormulaCache(sheetState, markDirty: true);
            }

            RefreshArrayPropertyPaths();
            Repaint();
        }

        private static SpreadAssetCellState[] CloneSheetCells(SpreadAssetCellState[] cells)
        {
            if (cells == null || cells.Length == 0)
            {
                return Array.Empty<SpreadAssetCellState>();
            }

            SpreadAssetCellState[] clones = new SpreadAssetCellState[cells.Length];
            for (int i = 0; i < cells.Length; i++)
            {
                SpreadAssetCellState cell = cells[i];
                if (cell == null)
                {
                    continue;
                }

                clones[i] = new SpreadAssetCellState
                {
                    Row = cell.Row,
                    ColumnId = cell.ColumnId,
                    ColumnName = cell.ColumnName,
                    Value = cell.Value,
                    Formula = cell.Formula,
                    Note = cell.Note
                };
            }

            return clones;
        }

        private bool TryPrepareSelectedSheetForCsv(
            out SerializedProperty arrayProperty,
            out SpreadAssetSheetState sheetState,
            out List<TableColumn> columns,
            out string error)
        {
            arrayProperty = null;
            sheetState = null;
            columns = null;
            error = string.Empty;

            if (_serializedObject == null || _workingCopy == null)
            {
                error = "Open a .spreadasset document first.";
                return false;
            }

            if (_arrayPropertyPaths.Count == 0)
            {
                error = "The opened document has no array sheets to export or import.";
                return false;
            }

            _serializedObject.ApplyModifiedProperties();
            CommitAllFormulaDrafts();

            _selectedArrayIndex = Mathf.Clamp(_selectedArrayIndex, 0, _arrayPropertyPaths.Count - 1);
            arrayProperty = _serializedObject.FindProperty(_arrayPropertyPaths[_selectedArrayIndex]);
            if (arrayProperty == null)
            {
                error = "Selected array sheet could not be found.";
                return false;
            }

            sheetState = GetOrCreateSheetState(arrayProperty);
            columns = GetColumns(arrayProperty, sheetState);
            if (!TryApplyFormulaSet(arrayProperty, columns, sheetState, forceRecompile: true, out _, out error))
            {
                _formulaError = error;
                return false;
            }

            _formulaError = string.Empty;
            _dirtyFormulaSheets.Remove(sheetState);
            _serializedObject.ApplyModifiedProperties();
            return true;
        }

        private List<List<string>> CreateCsvRows(
            SerializedProperty arrayProperty,
            List<TableColumn> columns,
            SpreadAssetSheetState sheetState,
            out int unsupportedCellCount)
        {
            unsupportedCellCount = 0;
            List<CsvColumnBinding> bindings = CreateCsvColumnBindings(columns);
            List<List<string>> rows = new List<List<string>>(arrayProperty.arraySize + 1);
            List<string> header = new List<string>(bindings.Count);
            foreach (CsvColumnBinding binding in bindings)
            {
                header.Add(binding.Header);
            }

            rows.Add(header);
            for (int rowIndex = 0; rowIndex < arrayProperty.arraySize; rowIndex++)
            {
                SerializedProperty element = arrayProperty.GetArrayElementAtIndex(rowIndex);
                List<string> row = new List<string>(bindings.Count);
                foreach (CsvColumnBinding binding in bindings)
                {
                    row.Add(GetCsvCellValue(
                        element,
                        columns,
                        sheetState,
                        rowIndex,
                        binding,
                        out bool supported));
                    if (!supported)
                    {
                        unsupportedCellCount++;
                    }
                }

                rows.Add(row);
            }

            return rows;
        }

        private static string GetCsvCellValue(
            SerializedProperty element,
            List<TableColumn> columns,
            SpreadAssetSheetState sheetState,
            int rowIndex,
            CsvColumnBinding binding,
            out bool supported)
        {
            supported = true;
            if (binding.ColumnIndex < 0)
            {
                if (TryGetSerializedPropertyCsvValue(element, out string elementValue))
                {
                    return elementValue;
                }

                supported = false;
                return string.Empty;
            }

            TableColumn column = columns[binding.ColumnIndex];
            if (column.IsDesignField)
            {
                return GetDesignCellValue(sheetState, rowIndex, column);
            }

            SerializedProperty cell = GetCellProperty(element, column);
            if (cell != null && TryGetSerializedPropertyCsvValue(cell, out string value))
            {
                return value;
            }

            supported = false;
            return string.Empty;
        }

        private static bool TryCreateCsvImportPlan(
            List<List<string>> rows,
            List<TableColumn> columns,
            out CsvImportPlan importPlan,
            out string error)
        {
            importPlan = default;
            error = string.Empty;

            int headerRowIndex = FindFirstCsvContentRow(rows);
            if (headerRowIndex < 0)
            {
                error = "CSV does not contain a header row.";
                return false;
            }

            List<string> header = rows[headerRowIndex];
            Dictionary<string, int> lookup = CreateCsvColumnLookup(columns);
            List<CsvImportColumn> importColumns = new List<CsvImportColumn>();
            int unknownHeaderCount = 0;

            for (int sourceColumnIndex = 0; sourceColumnIndex < header.Count; sourceColumnIndex++)
            {
                string normalizedHeader = NormalizeCsvHeader(header[sourceColumnIndex]);
                if (string.IsNullOrEmpty(normalizedHeader))
                {
                    continue;
                }

                if (lookup.TryGetValue(normalizedHeader, out int targetColumnIndex))
                {
                    importColumns.Add(new CsvImportColumn(sourceColumnIndex, targetColumnIndex, header[sourceColumnIndex]));
                }
                else
                {
                    unknownHeaderCount++;
                }
            }

            if (importColumns.Count == 0)
            {
                error = "CSV headers do not match any columns in the selected sheet.";
                return false;
            }

            int dataStartRowIndex = headerRowIndex + 1;
            int dataEndRowIndex = rows.Count;
            while (dataEndRowIndex > dataStartRowIndex && IsBlankCsvRow(rows[dataEndRowIndex - 1]))
            {
                dataEndRowIndex--;
            }

            importPlan = new CsvImportPlan(
                headerRowIndex,
                dataStartRowIndex,
                dataEndRowIndex,
                importColumns,
                unknownHeaderCount);
            return true;
        }

        private static bool TryApplyCsvImportPlan(
            SerializedProperty arrayProperty,
            List<TableColumn> columns,
            SpreadAssetSheetState sheetState,
            List<List<string>> rows,
            CsvImportPlan importPlan,
            out CsvImportSummary summary,
            out string error)
        {
            summary = default;
            error = string.Empty;
            int rowCount = importPlan.RowCount;
            int previousSize = arrayProperty.arraySize;
            arrayProperty.arraySize = rowCount;

            for (int rowIndex = previousSize; rowIndex < rowCount; rowIndex++)
            {
                ClearPropertyValue(arrayProperty.GetArrayElementAtIndex(rowIndex));
            }

            if (rowCount < previousSize)
            {
                TrimSheetCells(sheetState, rowCount);
            }

            int unsupportedCellCount = 0;
            for (int dataRowIndex = importPlan.DataStartRowIndex; dataRowIndex < importPlan.DataEndRowIndex; dataRowIndex++)
            {
                int targetRowIndex = dataRowIndex - importPlan.DataStartRowIndex;
                List<string> csvRow = rows[dataRowIndex];
                SerializedProperty element = arrayProperty.GetArrayElementAtIndex(targetRowIndex);

                foreach (CsvImportColumn importColumn in importPlan.Columns)
                {
                    string value = importColumn.SourceColumnIndex < csvRow.Count
                        ? csvRow[importColumn.SourceColumnIndex]
                        : string.Empty;
                    if (!TrySetCsvCellValue(
                            element,
                            columns,
                            sheetState,
                            targetRowIndex,
                            importColumn.TargetColumnIndex,
                            value,
                            out bool supported,
                            out error))
                    {
                        error = $"Row {targetRowIndex + 1}, column {importColumn.Header}: {error}";
                        return false;
                    }

                    if (!supported)
                    {
                        unsupportedCellCount++;
                    }
                }
            }

            summary = new CsvImportSummary(rowCount, importPlan.UnknownHeaderCount, unsupportedCellCount);
            return true;
        }

        private static bool TrySetCsvCellValue(
            SerializedProperty element,
            List<TableColumn> columns,
            SpreadAssetSheetState sheetState,
            int rowIndex,
            int targetColumnIndex,
            string value,
            out bool supported,
            out string error)
        {
            supported = true;
            error = string.Empty;

            if (targetColumnIndex < 0)
            {
                return TrySetSerializedPropertyCsvValue(element, value, out supported, out error);
            }

            TableColumn column = columns[targetColumnIndex];
            if (column.IsDesignField)
            {
                SetDesignCellValue(sheetState, rowIndex, column, value);
                return true;
            }

            SerializedProperty cell = GetCellProperty(element, column);
            if (cell == null)
            {
                supported = false;
                return true;
            }

            return TrySetSerializedPropertyCsvValue(cell, value, out supported, out error);
        }

        private static List<CsvColumnBinding> CreateCsvColumnBindings(List<TableColumn> columns)
        {
            List<CsvColumnBinding> bindings = new List<CsvColumnBinding>();
            HashSet<string> usedHeaders = new HashSet<string>(StringComparer.Ordinal);
            if (columns == null || columns.Count == 0)
            {
                bindings.Add(new CsvColumnBinding(MakeUniqueCsvHeader("Value", usedHeaders), -1));
                return bindings;
            }

            for (int columnIndex = 0; columnIndex < columns.Count; columnIndex++)
            {
                bindings.Add(new CsvColumnBinding(
                    MakeUniqueCsvHeader(GetCsvColumnName(columns[columnIndex]), usedHeaders),
                    columnIndex));
            }

            return bindings;
        }

        private static Dictionary<string, int> CreateCsvColumnLookup(List<TableColumn> columns)
        {
            Dictionary<string, int> lookup = new Dictionary<string, int>(StringComparer.Ordinal);
            if (columns == null || columns.Count == 0)
            {
                AddCsvColumnLookup(lookup, "Value", -1);
                return lookup;
            }

            foreach (CsvColumnBinding binding in CreateCsvColumnBindings(columns))
            {
                AddCsvColumnLookup(lookup, binding.Header, binding.ColumnIndex);
            }

            for (int columnIndex = 0; columnIndex < columns.Count; columnIndex++)
            {
                TableColumn column = columns[columnIndex];
                AddCsvColumnLookup(lookup, GetCsvColumnName(column), columnIndex);
                AddCsvColumnLookup(lookup, column.DisplayName, columnIndex);
                AddCsvColumnLookup(lookup, column.SchemaName, columnIndex);
                AddCsvColumnLookup(lookup, column.PropertyName, columnIndex);

                if (!string.IsNullOrEmpty(column.PropertyName))
                {
                    AddCsvColumnLookup(
                        lookup,
                        SpreadAssetNameUtility.ToPascalCase(column.PropertyName.TrimStart('_')),
                        columnIndex);
                }
            }

            return lookup;
        }

        private static void AddCsvColumnLookup(Dictionary<string, int> lookup, string header, int columnIndex)
        {
            string normalizedHeader = NormalizeCsvHeader(header);
            if (!string.IsNullOrEmpty(normalizedHeader) && !lookup.ContainsKey(normalizedHeader))
            {
                lookup.Add(normalizedHeader, columnIndex);
            }
        }

        private static string GetCsvColumnName(TableColumn column)
        {
            if (!string.IsNullOrWhiteSpace(column.SchemaName))
            {
                return column.SchemaName;
            }

            if (!string.IsNullOrWhiteSpace(column.DisplayName))
            {
                return column.DisplayName;
            }

            return string.IsNullOrWhiteSpace(column.PropertyName) ? "Value" : column.PropertyName;
        }

        private static string MakeUniqueCsvHeader(string header, HashSet<string> usedHeaders)
        {
            string baseHeader = string.IsNullOrWhiteSpace(header) ? "Column" : header.Trim();
            string uniqueHeader = baseHeader;
            int suffix = 2;
            while (!usedHeaders.Add(NormalizeCsvHeader(uniqueHeader)))
            {
                uniqueHeader = baseHeader + " " + suffix.ToString(CultureInfo.InvariantCulture);
                suffix++;
            }

            return uniqueHeader;
        }

        private static string NormalizeCsvHeader(string header)
        {
            if (string.IsNullOrWhiteSpace(header))
            {
                return string.Empty;
            }

            string trimmed = header.Trim().TrimStart('\uFEFF');
            List<char> chars = new List<char>(trimmed.Length);
            foreach (char character in trimmed)
            {
                if (char.IsWhiteSpace(character) || character == '_')
                {
                    continue;
                }

                chars.Add(char.ToUpperInvariant(character));
            }

            return new string(chars.ToArray());
        }

        private static int FindFirstCsvContentRow(List<List<string>> rows)
        {
            if (rows == null)
            {
                return -1;
            }

            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                if (!IsBlankCsvRow(rows[rowIndex]))
                {
                    return rowIndex;
                }
            }

            return -1;
        }

        private static bool IsBlankCsvRow(List<string> row)
        {
            if (row == null || row.Count == 0)
            {
                return true;
            }

            foreach (string value in row)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return false;
                }
            }

            return true;
        }

        private string GetCsvPanelDirectory()
        {
            string assetPath = _documentPath;
            if (string.IsNullOrEmpty(assetPath) && _targetAsset != null)
            {
                assetPath = AssetDatabase.GetAssetPath(_targetAsset);
            }

            if (string.IsNullOrEmpty(assetPath))
            {
                return Application.dataPath;
            }

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            string assetDirectory = Path.GetDirectoryName(assetPath);
            return string.IsNullOrEmpty(assetDirectory)
                ? Application.dataPath
                : Path.Combine(projectRoot, assetDirectory);
        }

        private string CreateCsvFileName(SerializedProperty arrayProperty, SpreadAssetSheetState sheetState)
        {
            string documentName = string.IsNullOrEmpty(_documentPath)
                ? "SpreadAsset"
                : Path.GetFileNameWithoutExtension(_documentPath);
            string sheetName = GetSheetDisplayName(arrayProperty, sheetState);
            return SpreadAssetNameUtility.ToSafeFileName(documentName + "_" + sheetName) + ".csv";
        }

        private static string GetSheetDisplayName(SerializedProperty arrayProperty, SpreadAssetSheetState sheetState)
        {
            if (!string.IsNullOrWhiteSpace(sheetState?.ArrayFieldName))
            {
                return sheetState.ArrayFieldName;
            }

            if (!string.IsNullOrWhiteSpace(arrayProperty?.displayName))
            {
                return arrayProperty.displayName;
            }

            return string.IsNullOrWhiteSpace(arrayProperty?.name) ? "Sheet" : arrayProperty.name;
        }

        private static bool TryGetSerializedPropertyCsvValue(SerializedProperty property, out string value)
        {
            value = string.Empty;
            if (property == null)
            {
                return false;
            }

            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    value = property.longValue.ToString(CultureInfo.InvariantCulture);
                    return true;
                case SerializedPropertyType.Boolean:
                    value = property.boolValue.ToString();
                    return true;
                case SerializedPropertyType.Float:
                    value = FormatFloat(property.floatValue);
                    return true;
                case SerializedPropertyType.String:
                    value = property.stringValue ?? string.Empty;
                    return true;
                case SerializedPropertyType.Color:
                    value = JoinCsvComponents(
                        property.colorValue.r,
                        property.colorValue.g,
                        property.colorValue.b,
                        property.colorValue.a);
                    return true;
                case SerializedPropertyType.ObjectReference:
                    value = property.objectReferenceValue == null
                        ? string.Empty
                        : AssetDatabase.GetAssetPath(property.objectReferenceValue);
                    return true;
                case SerializedPropertyType.LayerMask:
                    value = property.intValue.ToString(CultureInfo.InvariantCulture);
                    return true;
                case SerializedPropertyType.Enum:
                    if (property.enumValueIndex >= 0 && property.enumValueIndex < property.enumNames.Length)
                    {
                        value = property.enumNames[property.enumValueIndex];
                    }
                    else
                    {
                        value = property.enumValueIndex.ToString(CultureInfo.InvariantCulture);
                    }

                    return true;
                case SerializedPropertyType.Vector2:
                    value = JoinCsvComponents(property.vector2Value.x, property.vector2Value.y);
                    return true;
                case SerializedPropertyType.Vector3:
                    value = JoinCsvComponents(property.vector3Value.x, property.vector3Value.y, property.vector3Value.z);
                    return true;
                case SerializedPropertyType.Vector4:
                    value = JoinCsvComponents(
                        property.vector4Value.x,
                        property.vector4Value.y,
                        property.vector4Value.z,
                        property.vector4Value.w);
                    return true;
                case SerializedPropertyType.Rect:
                    value = JoinCsvComponents(
                        property.rectValue.x,
                        property.rectValue.y,
                        property.rectValue.width,
                        property.rectValue.height);
                    return true;
                case SerializedPropertyType.Bounds:
                    value = JoinCsvComponents(
                        property.boundsValue.center.x,
                        property.boundsValue.center.y,
                        property.boundsValue.center.z,
                        property.boundsValue.size.x,
                        property.boundsValue.size.y,
                        property.boundsValue.size.z);
                    return true;
                case SerializedPropertyType.Quaternion:
                    value = JoinCsvComponents(
                        property.quaternionValue.x,
                        property.quaternionValue.y,
                        property.quaternionValue.z,
                        property.quaternionValue.w);
                    return true;
                case SerializedPropertyType.Vector2Int:
                    value = JoinCsvComponents(property.vector2IntValue.x, property.vector2IntValue.y);
                    return true;
                case SerializedPropertyType.Vector3Int:
                    value = JoinCsvComponents(
                        property.vector3IntValue.x,
                        property.vector3IntValue.y,
                        property.vector3IntValue.z);
                    return true;
                case SerializedPropertyType.RectInt:
                    value = JoinCsvComponents(
                        property.rectIntValue.x,
                        property.rectIntValue.y,
                        property.rectIntValue.width,
                        property.rectIntValue.height);
                    return true;
                case SerializedPropertyType.BoundsInt:
                    value = JoinCsvComponents(
                        property.boundsIntValue.position.x,
                        property.boundsIntValue.position.y,
                        property.boundsIntValue.position.z,
                        property.boundsIntValue.size.x,
                        property.boundsIntValue.size.y,
                        property.boundsIntValue.size.z);
                    return true;
                default:
                    return false;
            }
        }

        private static bool TrySetSerializedPropertyCsvValue(
            SerializedProperty property,
            string value,
            out bool supported,
            out string error)
        {
            supported = true;
            error = string.Empty;
            if (property == null)
            {
                supported = false;
                return true;
            }

            value ??= string.Empty;
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    if (!TryParseCsvLong(value, out long longValue))
                    {
                        error = $"expected an integer value, got \"{value}\".";
                        return false;
                    }

                    property.longValue = longValue;
                    return true;
                case SerializedPropertyType.Boolean:
                    if (!TryParseCsvBool(value, out bool boolValue))
                    {
                        error = $"expected a boolean value, got \"{value}\".";
                        return false;
                    }

                    property.boolValue = boolValue;
                    return true;
                case SerializedPropertyType.Float:
                    if (!TryParseCsvFloat(value, out float floatValue))
                    {
                        error = $"expected a numeric value, got \"{value}\".";
                        return false;
                    }

                    property.floatValue = floatValue;
                    return true;
                case SerializedPropertyType.String:
                    property.stringValue = value;
                    return true;
                case SerializedPropertyType.Color:
                    if (!TryParseCsvColor(value, out Color colorValue))
                    {
                        error = $"expected a color value as r;g;b;a or #RRGGBBAA, got \"{value}\".";
                        return false;
                    }

                    property.colorValue = colorValue;
                    return true;
                case SerializedPropertyType.ObjectReference:
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        property.objectReferenceValue = null;
                        return true;
                    }

                    UnityEngine.Object reference = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(value);
                    if (reference == null)
                    {
                        error = $"could not load asset reference at \"{value}\".";
                        return false;
                    }

                    property.objectReferenceValue = reference;
                    return true;
                case SerializedPropertyType.LayerMask:
                    if (!TryParseCsvInt(value, out int layerMaskValue))
                    {
                        error = $"expected a layer mask integer value, got \"{value}\".";
                        return false;
                    }

                    property.intValue = layerMaskValue;
                    return true;
                case SerializedPropertyType.Enum:
                    if (!TryParseCsvEnum(property, value, out int enumValueIndex))
                    {
                        error = $"expected one of the enum names or an enum index, got \"{value}\".";
                        return false;
                    }

                    property.enumValueIndex = enumValueIndex;
                    return true;
                case SerializedPropertyType.Vector2:
                    if (!TryParseCsvFloatComponents(value, 2, out float[] vector2Components))
                    {
                        error = $"expected 2 numeric components, got \"{value}\".";
                        return false;
                    }

                    property.vector2Value = new Vector2(vector2Components[0], vector2Components[1]);
                    return true;
                case SerializedPropertyType.Vector3:
                    if (!TryParseCsvFloatComponents(value, 3, out float[] vector3Components))
                    {
                        error = $"expected 3 numeric components, got \"{value}\".";
                        return false;
                    }

                    property.vector3Value = new Vector3(vector3Components[0], vector3Components[1], vector3Components[2]);
                    return true;
                case SerializedPropertyType.Vector4:
                    if (!TryParseCsvFloatComponents(value, 4, out float[] vector4Components))
                    {
                        error = $"expected 4 numeric components, got \"{value}\".";
                        return false;
                    }

                    property.vector4Value = new Vector4(
                        vector4Components[0],
                        vector4Components[1],
                        vector4Components[2],
                        vector4Components[3]);
                    return true;
                case SerializedPropertyType.Rect:
                    if (!TryParseCsvFloatComponents(value, 4, out float[] rectComponents))
                    {
                        error = $"expected x;y;width;height, got \"{value}\".";
                        return false;
                    }

                    property.rectValue = new Rect(rectComponents[0], rectComponents[1], rectComponents[2], rectComponents[3]);
                    return true;
                case SerializedPropertyType.Bounds:
                    if (!TryParseCsvFloatComponents(value, 6, out float[] boundsComponents))
                    {
                        error = $"expected center x;y;z and size x;y;z, got \"{value}\".";
                        return false;
                    }

                    property.boundsValue = new Bounds(
                        new Vector3(boundsComponents[0], boundsComponents[1], boundsComponents[2]),
                        new Vector3(boundsComponents[3], boundsComponents[4], boundsComponents[5]));
                    return true;
                case SerializedPropertyType.Quaternion:
                    if (!TryParseCsvFloatComponents(value, 4, out float[] quaternionComponents))
                    {
                        error = $"expected x;y;z;w, got \"{value}\".";
                        return false;
                    }

                    property.quaternionValue = new Quaternion(
                        quaternionComponents[0],
                        quaternionComponents[1],
                        quaternionComponents[2],
                        quaternionComponents[3]);
                    return true;
                case SerializedPropertyType.Vector2Int:
                    if (!TryParseCsvIntComponents(value, 2, out int[] vector2IntComponents))
                    {
                        error = $"expected 2 integer components, got \"{value}\".";
                        return false;
                    }

                    property.vector2IntValue = new Vector2Int(vector2IntComponents[0], vector2IntComponents[1]);
                    return true;
                case SerializedPropertyType.Vector3Int:
                    if (!TryParseCsvIntComponents(value, 3, out int[] vector3IntComponents))
                    {
                        error = $"expected 3 integer components, got \"{value}\".";
                        return false;
                    }

                    property.vector3IntValue = new Vector3Int(
                        vector3IntComponents[0],
                        vector3IntComponents[1],
                        vector3IntComponents[2]);
                    return true;
                case SerializedPropertyType.RectInt:
                    if (!TryParseCsvIntComponents(value, 4, out int[] rectIntComponents))
                    {
                        error = $"expected x;y;width;height, got \"{value}\".";
                        return false;
                    }

                    property.rectIntValue = new RectInt(
                        rectIntComponents[0],
                        rectIntComponents[1],
                        rectIntComponents[2],
                        rectIntComponents[3]);
                    return true;
                case SerializedPropertyType.BoundsInt:
                    if (!TryParseCsvIntComponents(value, 6, out int[] boundsIntComponents))
                    {
                        error = $"expected position x;y;z and size x;y;z, got \"{value}\".";
                        return false;
                    }

                    property.boundsIntValue = new BoundsInt(
                        new Vector3Int(boundsIntComponents[0], boundsIntComponents[1], boundsIntComponents[2]),
                        new Vector3Int(boundsIntComponents[3], boundsIntComponents[4], boundsIntComponents[5]));
                    return true;
                default:
                    supported = false;
                    return true;
            }
        }

        private static bool TryParseCsvLong(string value, out long result)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                result = 0L;
                return true;
            }

            return long.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        }

        private static bool TryParseCsvInt(string value, out int result)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                result = 0;
                return true;
            }

            return int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        }

        private static bool TryParseCsvFloat(string value, out float result)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                result = 0f;
                return true;
            }

            return float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }

        private static bool TryParseCsvBool(string value, out bool result)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                result = false;
                return true;
            }

            string trimmed = value.Trim();
            if (bool.TryParse(trimmed, out result))
            {
                return true;
            }

            if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            {
                result = Math.Abs(number) > double.Epsilon;
                return true;
            }

            result = false;
            return false;
        }

        private static bool TryParseCsvEnum(SerializedProperty property, string value, out int enumValueIndex)
        {
            enumValueIndex = 0;
            if (property == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            string trimmed = value.Trim();
            string[] enumNames = property.enumNames ?? Array.Empty<string>();
            for (int i = 0; i < enumNames.Length; i++)
            {
                if (string.Equals(enumNames[i], trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    enumValueIndex = i;
                    return true;
                }
            }

            string[] displayNames = property.enumDisplayNames ?? Array.Empty<string>();
            for (int i = 0; i < displayNames.Length; i++)
            {
                if (string.Equals(displayNames[i], trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    enumValueIndex = i;
                    return true;
                }
            }

            if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedIndex))
            {
                return false;
            }

            enumValueIndex = enumNames.Length == 0
                ? Mathf.Max(0, parsedIndex)
                : Mathf.Clamp(parsedIndex, 0, enumNames.Length - 1);
            return true;
        }

        private static bool TryParseCsvColor(string value, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrWhiteSpace(value))
            {
                color = new Color(0f, 0f, 0f, 0f);
                return true;
            }

            string trimmed = value.Trim();
            if (!trimmed.StartsWith("#", StringComparison.Ordinal)
                && (trimmed.Length == 6 || trimmed.Length == 8)
                && IsHexString(trimmed))
            {
                trimmed = "#" + trimmed;
            }

            if (trimmed.StartsWith("#", StringComparison.Ordinal)
                && ColorUtility.TryParseHtmlString(trimmed, out color))
            {
                return true;
            }

            if (!TryParseCsvFloatComponents(value, 4, out float[] components))
            {
                return false;
            }

            color = new Color(components[0], components[1], components[2], components[3]);
            return true;
        }

        private static bool IsHexString(string value)
        {
            foreach (char character in value)
            {
                bool isHex = (character >= '0' && character <= '9')
                    || (character >= 'a' && character <= 'f')
                    || (character >= 'A' && character <= 'F');
                if (!isHex)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryParseCsvFloatComponents(string value, int count, out float[] components)
        {
            components = new float[count];
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            string[] parts = SplitCsvComponents(value);
            if (parts.Length != count)
            {
                return false;
            }

            for (int i = 0; i < count; i++)
            {
                if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out components[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryParseCsvIntComponents(string value, int count, out int[] components)
        {
            components = new int[count];
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            string[] parts = SplitCsvComponents(value);
            if (parts.Length != count)
            {
                return false;
            }

            for (int i = 0; i < count; i++)
            {
                if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out components[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static string[] SplitCsvComponents(string value)
        {
            string normalized = (value ?? string.Empty)
                .Trim()
                .Trim('(', ')', '[', ']');
            return normalized.Split(
                new[] { ';', ',', '|' },
                StringSplitOptions.RemoveEmptyEntries);
        }

        private static string JoinCsvComponents(params float[] components)
        {
            string[] parts = new string[components.Length];
            for (int i = 0; i < components.Length; i++)
            {
                parts[i] = FormatFloat(components[i]);
            }

            return string.Join(";", parts);
        }

        private static string JoinCsvComponents(params int[] components)
        {
            string[] parts = new string[components.Length];
            for (int i = 0; i < components.Length; i++)
            {
                parts[i] = components[i].ToString(CultureInfo.InvariantCulture);
            }

            return string.Join(";", parts);
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private void RecreateWorkingCopy()
        {
            DestroyWorkingCopy();
            _workingCopy = SpreadAssetDocumentSync.CreateWorkingCopy(_document, _targetAsset);
            _serializedObject = _workingCopy == null ? null : new SerializedObject(_workingCopy);
            RefreshArrayPropertyPaths();
            InvalidateAllFormulaSheetCaches(markDirty: true);
        }

        private static bool RefreshDocumentSchemaFromGeneratedFactory(
            SpreadAssetDocument document,
            UnityEngine.Object targetAsset)
        {
            if (document == null || !(targetAsset is ScriptableObject scriptableObject))
            {
                return false;
            }

            MonoScript runtimeScript = MonoScript.FromScriptableObject(scriptableObject);
            string runtimeScriptPath = AssetDatabase.GetAssetPath(runtimeScript);
            if (string.IsNullOrEmpty(runtimeScriptPath))
            {
                return false;
            }

            if (!SpreadAssetGeneratedClassLoader.TryLoadFromRuntimeScript(
                    runtimeScriptPath,
                    out SpreadAssetGenerationRequest request,
                    out _,
                    out _)
                || request?.Schema == null)
            {
                return false;
            }

            bool changed = false;
            if (document.Schema == null)
            {
                document.Schema = new SpreadAssetDocumentSchema();
                changed = true;
            }

            changed |= SpreadAssetSchemaUtility.EnsureFieldIds(document.Schema);
            SpreadAssetSchemaUtility.EnsureFieldIds(request.Schema);
            if (AreSchemasEqual(document.Schema, request.Schema))
            {
                return changed;
            }

            changed |= MigrateDocumentForSchemaChange(document, request.Schema);
            document.Schema = request.Schema;
            return true;
        }

        private static bool EnsureDocumentSheetsForSchema(SpreadAssetDocument document)
        {
            if (document?.Schema?.Tables == null)
            {
                return false;
            }

            if (document.Sheets == null)
            {
                document.Sheets = Array.Empty<SpreadAssetSheetState>();
            }

            bool changed = false;
            List<SpreadAssetSheetState> sheets = new List<SpreadAssetSheetState>(document.Sheets);
            foreach (SpreadAssetSchemaTable table in document.Schema.Tables)
            {
                if (table == null || string.IsNullOrEmpty(table.FieldName))
                {
                    continue;
                }

                SpreadAssetSheetState sheet = FindSheetByName(sheets, table.FieldName);
                if (sheet == null)
                {
                    sheets.Add(new SpreadAssetSheetState
                    {
                        ArrayFieldName = table.FieldName,
                        Formulas = Array.Empty<SpreadAssetFormulaState>(),
                        Columns = Array.Empty<SpreadAssetColumnState>(),
                        Cells = Array.Empty<SpreadAssetCellState>()
                    });
                    changed = true;
                    continue;
                }

                if (sheet.Formulas == null)
                {
                    sheet.Formulas = Array.Empty<SpreadAssetFormulaState>();
                    changed = true;
                }

                if (sheet.Cells == null)
                {
                    sheet.Cells = Array.Empty<SpreadAssetCellState>();
                    changed = true;
                }

                if (sheet.Columns == null)
                {
                    sheet.Columns = Array.Empty<SpreadAssetColumnState>();
                    changed = true;
                }

                changed |= EnsureSheetColumnsUseFieldIds(sheet, table);
                changed |= EnsureSheetCellsUseFieldIds(sheet, table);
            }

            if (changed)
            {
                document.Sheets = sheets.ToArray();
            }

            return changed;
        }

        private static SpreadAssetSheetState FindSheetByName(
            List<SpreadAssetSheetState> sheets,
            string arrayFieldName)
        {
            foreach (SpreadAssetSheetState sheet in sheets)
            {
                if (sheet == null)
                {
                    continue;
                }

                if (sheet.ArrayFieldName == arrayFieldName)
                {
                    return sheet;
                }
            }

            return null;
        }

        private static bool MigrateDocumentForSchemaChange(
            SpreadAssetDocument document,
            SpreadAssetDocumentSchema nextSchema)
        {
            if (document == null || document.Schema == null || nextSchema == null)
            {
                return false;
            }

            bool changed = false;
            changed |= MigrateSerializedAssetJsonForSchemaChange(document, document.Schema, nextSchema);
            changed |= MigrateSheetCellsForSchemaChange(document, document.Schema, nextSchema);
            return changed;
        }

        private static bool MigrateSerializedAssetJsonForSchemaChange(
            SpreadAssetDocument document,
            SpreadAssetDocumentSchema previousSchema,
            SpreadAssetDocumentSchema nextSchema)
        {
            if (string.IsNullOrEmpty(document.SerializedAssetJson))
            {
                return false;
            }

            bool changed = false;
            string json = document.SerializedAssetJson;
            changed |= RenameJsonPropertiesForFields(
                ref json,
                previousSchema.Fields,
                nextSchema.Fields,
                normalizeUnchangedNames: false);

            SpreadAssetSchemaTable[] previousTables = previousSchema.Tables ?? Array.Empty<SpreadAssetSchemaTable>();
            foreach (SpreadAssetSchemaTable previousTable in previousTables)
            {
                SpreadAssetSchemaTable nextTable = FindMatchingTableForMigration(nextSchema, previousTable);
                if (nextTable == null)
                {
                    continue;
                }

                changed |= RenameJsonPropertiesForTable(ref json, previousTable, nextTable);
            }

            if (changed)
            {
                document.SerializedAssetJson = json;
            }

            return changed;
        }

        private static bool RenameJsonPropertiesForTable(
            ref string json,
            SpreadAssetSchemaTable previousTable,
            SpreadAssetSchemaTable nextTable)
        {
            if (previousTable == null || nextTable == null)
            {
                return false;
            }

            bool changed = false;
            foreach (string arrayPropertyName in GetSerializedPropertyNameCandidates(previousTable.FieldName))
            {
                if (!TryFindJsonArrayProperty(json, arrayPropertyName, out int arrayStart, out int arrayEnd))
                {
                    continue;
                }

                string arrayJson = json.Substring(arrayStart, arrayEnd - arrayStart + 1);
                if (RenameJsonPropertiesForFields(
                        ref arrayJson,
                        previousTable.Fields,
                        nextTable.Fields,
                        normalizeUnchangedNames: true))
                {
                    json = json.Substring(0, arrayStart)
                        + arrayJson
                        + json.Substring(arrayEnd + 1);
                    changed = true;
                }

                break;
            }

            if (!string.Equals(previousTable.FieldName, nextTable.FieldName, StringComparison.Ordinal))
            {
                string nextPropertyName = SpreadAssetNameUtility.ToSerializedFieldName(nextTable.FieldName);
                foreach (string previousPropertyName in GetSerializedPropertyNameCandidates(previousTable.FieldName))
                {
                    changed |= RenameJsonProperty(ref json, previousPropertyName, nextPropertyName);
                }
            }

            return changed;
        }

        private static bool RenameJsonPropertiesForFields(
            ref string json,
            SpreadAssetSchemaField[] previousFields,
            SpreadAssetSchemaField[] nextFields,
            bool normalizeUnchangedNames)
        {
            if (previousFields == null || nextFields == null)
            {
                return false;
            }

            bool changed = false;
            foreach (SpreadAssetSchemaField previousField in previousFields)
            {
                SpreadAssetSchemaField nextField = FindFieldById(nextFields, previousField?.Id);
                if (nextField == null
                    || previousField == null
                    || (!normalizeUnchangedNames
                        && string.Equals(previousField.Name, nextField.Name, StringComparison.Ordinal)))
                {
                    continue;
                }

                string nextPropertyName = SpreadAssetNameUtility.ToSerializedFieldName(nextField.Name);
                foreach (string previousPropertyName in GetSerializedPropertyNameCandidates(previousField.Name))
                {
                    changed |= RenameJsonProperty(ref json, previousPropertyName, nextPropertyName);
                }
            }

            return changed;
        }

        private static bool TryFindJsonArrayProperty(
            string json,
            string propertyName,
            out int arrayStart,
            out int arrayEnd)
        {
            arrayStart = -1;
            arrayEnd = -1;
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(propertyName))
            {
                return false;
            }

            string pattern = "\"" + Regex.Escape(propertyName) + "\"\\s*:\\s*\\[";
            Match match = Regex.Match(json, pattern);
            if (!match.Success)
            {
                return false;
            }

            arrayStart = json.IndexOf('[', match.Index + match.Length - 1);
            return arrayStart >= 0 && TryFindMatchingJsonDelimiter(json, arrayStart, '[', ']', out arrayEnd);
        }

        private static bool TryFindMatchingJsonDelimiter(
            string json,
            int start,
            char openDelimiter,
            char closeDelimiter,
            out int end)
        {
            end = -1;
            int depth = 0;
            bool inString = false;
            bool isEscaped = false;

            for (int i = start; i < json.Length; i++)
            {
                char current = json[i];
                if (inString)
                {
                    if (isEscaped)
                    {
                        isEscaped = false;
                    }
                    else if (current == '\\')
                    {
                        isEscaped = true;
                    }
                    else if (current == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (current == '"')
                {
                    inString = true;
                    continue;
                }

                if (current == openDelimiter)
                {
                    depth++;
                    continue;
                }

                if (current == closeDelimiter)
                {
                    depth--;
                    if (depth == 0)
                    {
                        end = i;
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool RenameJsonProperty(
            ref string json,
            string previousPropertyName,
            string nextPropertyName)
        {
            if (string.IsNullOrEmpty(json)
                || string.IsNullOrEmpty(previousPropertyName)
                || string.IsNullOrEmpty(nextPropertyName)
                || string.Equals(previousPropertyName, nextPropertyName, StringComparison.Ordinal))
            {
                return false;
            }

            string pattern = "\"" + Regex.Escape(previousPropertyName) + "\"(?<suffix>\\s*:)";
            string replaced = Regex.Replace(
                json,
                pattern,
                match => "\"" + nextPropertyName + "\"" + match.Groups["suffix"].Value);
            if (string.Equals(json, replaced, StringComparison.Ordinal))
            {
                return false;
            }

            json = replaced;
            return true;
        }

        private static IEnumerable<string> GetSerializedPropertyNameCandidates(string schemaName)
        {
            string serializedName = SpreadAssetNameUtility.ToSerializedFieldName(schemaName);
            if (!string.IsNullOrEmpty(serializedName))
            {
                yield return serializedName;
            }

            string lowerCamelName = ToLowerCamelCase(SpreadAssetNameUtility.ToPascalCase(schemaName));
            if (!string.IsNullOrEmpty(lowerCamelName)
                && !string.Equals(lowerCamelName, serializedName, StringComparison.Ordinal))
            {
                yield return lowerCamelName;
            }

            string pascalName = SpreadAssetNameUtility.ToPascalCase(schemaName);
            if (!string.IsNullOrEmpty(pascalName)
                && !string.Equals(pascalName, serializedName, StringComparison.Ordinal)
                && !string.Equals(pascalName, lowerCamelName, StringComparison.Ordinal))
            {
                yield return pascalName;
            }
        }

        private static bool MigrateSheetCellsForSchemaChange(
            SpreadAssetDocument document,
            SpreadAssetDocumentSchema previousSchema,
            SpreadAssetDocumentSchema nextSchema)
        {
            if (document.Sheets == null || previousSchema?.Tables == null)
            {
                return false;
            }

            bool changed = false;
            foreach (SpreadAssetSchemaTable previousTable in previousSchema.Tables)
            {
                SpreadAssetSchemaTable nextTable = FindMatchingTableForMigration(nextSchema, previousTable);
                if (nextTable == null)
                {
                    continue;
                }

                foreach (SpreadAssetSheetState sheet in document.Sheets)
                {
                    if (sheet == null || !IsSameSchemaName(sheet.ArrayFieldName, previousTable.FieldName))
                    {
                        continue;
                    }

                    if (!string.Equals(sheet.ArrayFieldName, nextTable.FieldName, StringComparison.Ordinal))
                    {
                        sheet.ArrayFieldName = nextTable.FieldName;
                        changed = true;
                    }

                    changed |= MigrateSheetCellsForTableChange(sheet, previousTable, nextTable);
                    changed |= MigrateSheetColumnsForTableChange(sheet, previousTable, nextTable);
                }
            }

            return changed;
        }

        private static bool MigrateSheetCellsForTableChange(
            SpreadAssetSheetState sheet,
            SpreadAssetSchemaTable previousTable,
            SpreadAssetSchemaTable nextTable)
        {
            if (sheet?.Cells == null)
            {
                return false;
            }

            bool changed = false;
            foreach (SpreadAssetCellState cell in sheet.Cells)
            {
                if (cell == null)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(cell.ColumnId))
                {
                    SpreadAssetSchemaField previousField = FindFieldByName(previousTable.Fields, cell.ColumnName);
                    if (previousField != null)
                    {
                        cell.ColumnId = previousField.Id;
                        changed = true;
                    }
                }

                SpreadAssetSchemaField nextField = FindFieldById(nextTable.Fields, cell.ColumnId);
                if (nextField != null
                    && !string.Equals(cell.ColumnName, nextField.Name, StringComparison.Ordinal))
                {
                    cell.ColumnName = nextField.Name;
                    changed = true;
                }
            }

            return changed;
        }

        private static bool MigrateSheetColumnsForTableChange(
            SpreadAssetSheetState sheet,
            SpreadAssetSchemaTable previousTable,
            SpreadAssetSchemaTable nextTable)
        {
            if (sheet?.Columns == null)
            {
                return false;
            }

            bool changed = false;
            foreach (SpreadAssetColumnState column in sheet.Columns)
            {
                if (column == null)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(column.ColumnId))
                {
                    SpreadAssetSchemaField previousField = FindFieldByName(previousTable.Fields, column.ColumnName);
                    if (previousField != null)
                    {
                        column.ColumnId = previousField.Id;
                        changed = true;
                    }
                }

                SpreadAssetSchemaField nextField = FindFieldById(nextTable.Fields, column.ColumnId);
                if (nextField != null
                    && !string.Equals(column.ColumnName, nextField.Name, StringComparison.Ordinal))
                {
                    column.ColumnName = nextField.Name;
                    changed = true;
                }
            }

            return changed;
        }

        private static bool EnsureSheetCellsUseFieldIds(
            SpreadAssetSheetState sheet,
            SpreadAssetSchemaTable table)
        {
            if (sheet?.Cells == null || table?.Fields == null)
            {
                return false;
            }

            bool changed = false;
            foreach (SpreadAssetCellState cell in sheet.Cells)
            {
                if (cell == null)
                {
                    continue;
                }

                SpreadAssetSchemaField field = FindFieldById(table.Fields, cell.ColumnId)
                    ?? FindFieldByName(table.Fields, cell.ColumnName);
                if (field == null)
                {
                    continue;
                }

                if (!string.Equals(cell.ColumnId, field.Id, StringComparison.Ordinal))
                {
                    cell.ColumnId = field.Id;
                    changed = true;
                }

                if (!string.Equals(cell.ColumnName, field.Name, StringComparison.Ordinal))
                {
                    cell.ColumnName = field.Name;
                    changed = true;
                }
            }

            return changed;
        }

        private static bool EnsureSheetColumnsUseFieldIds(
            SpreadAssetSheetState sheet,
            SpreadAssetSchemaTable table)
        {
            if (sheet?.Columns == null || table?.Fields == null)
            {
                return false;
            }

            bool changed = false;
            foreach (SpreadAssetColumnState column in sheet.Columns)
            {
                if (column == null)
                {
                    continue;
                }

                SpreadAssetSchemaField field = FindFieldById(table.Fields, column.ColumnId)
                    ?? FindFieldByName(table.Fields, column.ColumnName);
                if (field == null)
                {
                    continue;
                }

                if (!string.Equals(column.ColumnId, field.Id, StringComparison.Ordinal))
                {
                    column.ColumnId = field.Id;
                    changed = true;
                }

                if (!string.Equals(column.ColumnName, field.Name, StringComparison.Ordinal))
                {
                    column.ColumnName = field.Name;
                    changed = true;
                }
            }

            return changed;
        }

        private static SpreadAssetSchemaTable FindMatchingTableForMigration(
            SpreadAssetDocumentSchema schema,
            SpreadAssetSchemaTable previousTable)
        {
            if (schema?.Tables == null || previousTable == null)
            {
                return null;
            }

            foreach (SpreadAssetSchemaTable table in schema.Tables)
            {
                if (table != null && string.Equals(table.FieldName, previousTable.FieldName, StringComparison.Ordinal))
                {
                    return table;
                }
            }

            foreach (SpreadAssetSchemaTable table in schema.Tables)
            {
                if (table != null && IsSameSchemaName(table.FieldName, previousTable.FieldName))
                {
                    return table;
                }
            }

            foreach (SpreadAssetSchemaTable table in schema.Tables)
            {
                if (table != null && string.Equals(table.RowTypeName, previousTable.RowTypeName, StringComparison.Ordinal))
                {
                    return table;
                }
            }

            return null;
        }

        private static SpreadAssetSchemaField FindFieldById(
            SpreadAssetSchemaField[] fields,
            string fieldId)
        {
            if (fields == null || string.IsNullOrEmpty(fieldId))
            {
                return null;
            }

            foreach (SpreadAssetSchemaField field in fields)
            {
                if (field != null && string.Equals(field.Id, fieldId, StringComparison.Ordinal))
                {
                    return field;
                }
            }

            return null;
        }

        private static SpreadAssetSchemaField FindFieldByName(
            SpreadAssetSchemaField[] fields,
            string fieldName)
        {
            if (fields == null || string.IsNullOrEmpty(fieldName))
            {
                return null;
            }

            foreach (SpreadAssetSchemaField field in fields)
            {
                if (field == null)
                {
                    continue;
                }

                if (string.Equals(field.Name, fieldName, StringComparison.Ordinal)
                    || string.Equals(SpreadAssetNameUtility.ToSerializedFieldName(field.Name), fieldName, StringComparison.Ordinal)
                    || IsSameSchemaName(field.Name, fieldName))
                {
                    return field;
                }
            }

            return null;
        }

        private static bool IsSameSchemaName(string left, string right)
        {
            return string.Equals(
                SpreadAssetNameUtility.ToPascalCase(left ?? string.Empty),
                SpreadAssetNameUtility.ToPascalCase(right ?? string.Empty),
                StringComparison.Ordinal);
        }

        private static bool AreSchemasEqual(
            SpreadAssetDocumentSchema left,
            SpreadAssetDocumentSchema right)
        {
            return JsonUtility.ToJson(left ?? new SpreadAssetDocumentSchema())
                == JsonUtility.ToJson(right ?? new SpreadAssetDocumentSchema());
        }

        private bool ApplySelectedSheetFormulasIfDirty(bool force = false)
        {
            if (_serializedObject == null
                || _arrayPropertyPaths.Count == 0
                || _selectedArrayIndex < 0
                || _selectedArrayIndex >= _arrayPropertyPaths.Count
                || IsFormulaTextFocused())
            {
                return false;
            }

            SerializedProperty arrayProperty = _serializedObject.FindProperty(_arrayPropertyPaths[_selectedArrayIndex]);
            if (arrayProperty == null)
            {
                return false;
            }

            List<TableColumn> columns = GetColumns(arrayProperty);
            SpreadAssetSheetState sheetState = GetOrCreateSheetState(arrayProperty);
            if (!force && !IsSheetFormulaDirty(sheetState))
            {
                return false;
            }

            bool changed = ApplyFormulas(arrayProperty, columns, sheetState, markDirty: false);
            if (string.IsNullOrEmpty(_formulaError))
            {
                _dirtyFormulaSheets.Remove(sheetState);
            }

            return changed;
        }

        private bool TryApplyAllSheetFormulas(out string error)
        {
            error = string.Empty;
            if (_serializedObject == null)
            {
                return true;
            }

            _serializedObject.Update();
            RefreshArrayPropertyPaths();

            foreach (string arrayPropertyPath in _arrayPropertyPaths)
            {
                SerializedProperty arrayProperty = _serializedObject.FindProperty(arrayPropertyPath);
                if (arrayProperty == null)
                {
                    continue;
                }

            List<TableColumn> columns = GetColumns(arrayProperty);
            SpreadAssetSheetState sheetState = GetOrCreateSheetState(arrayProperty);
            if (!TryApplyFormulaSet(arrayProperty, columns, sheetState, forceRecompile: false, out _, out error))
            {
                _formulaError = error;
                return false;
            }

            _dirtyFormulaSheets.Remove(sheetState);
        }

            _serializedObject.ApplyModifiedProperties();
            _formulaError = string.Empty;
            return true;
        }

        private bool ApplyFormulas(
            SerializedProperty arrayProperty,
            List<TableColumn> columns,
            SpreadAssetSheetState sheetState,
            bool markDirty,
            bool forceRecompile = false)
        {
            if (!TryApplyFormulaSet(arrayProperty, columns, sheetState, forceRecompile, out bool changed, out string error))
            {
                _formulaError = error;
                return false;
            }

            _formulaError = string.Empty;
            _dirtyFormulaSheets.Remove(sheetState);
            if (changed)
            {
                _serializedObject.ApplyModifiedProperties();
            }

            if (markDirty && changed)
            {
                _isDocumentDirty = true;
            }

            return changed;
        }

        private bool TryApplyFormulaSet(
            SerializedProperty arrayProperty,
            List<TableColumn> columns,
            SpreadAssetSheetState sheetState,
            bool forceRecompile,
            out bool changed,
            out string error)
        {
            changed = false;
            error = string.Empty;
            if (sheetState?.Formulas == null || sheetState.Formulas.Length == 0)
            {
                return true;
            }

            FormulaSheetCache cache = GetOrCreateFormulaSheetCache(
                sheetState,
                arrayProperty.arraySize,
                columns,
                forceRecompile,
                out error);
            if (cache == null)
            {
                return false;
            }

            changed = SpreadAssetFormulaEngine.TryApplyCompiled(
                arrayProperty,
                cache.ColumnPropertyNames,
                cache.CompiledFormulas,
                new DesignFormulaCellStore(sheetState, columns),
                out error);
            return string.IsNullOrEmpty(error);
        }

        private FormulaSheetCache GetOrCreateFormulaSheetCache(
            SpreadAssetSheetState sheetState,
            int rowCount,
            List<TableColumn> columns,
            bool forceRecompile,
            out string error)
        {
            error = string.Empty;
            if (sheetState == null)
            {
                return null;
            }

            string columnSignature = CreateColumnSignature(columns);
            string formulaSignature = CreateFormulaSignature(sheetState.Formulas);
            if (!forceRecompile
                && _formulaSheetCaches.TryGetValue(sheetState, out FormulaSheetCache existingCache)
                && existingCache.Matches(rowCount, columnSignature, formulaSignature))
            {
                return existingCache;
            }

            List<string> columnPropertyNames = GetColumnPropertyNames(columns);
            if (!SpreadAssetFormulaEngine.TryCompile(
                    columnPropertyNames,
                    sheetState.Formulas,
                    rowCount,
                    out SpreadAssetFormulaEngine.CompiledFormulaSet compiledFormulas,
                    out error))
            {
                return null;
            }

            FormulaSheetCache cache = new FormulaSheetCache(
                rowCount,
                columnSignature,
                formulaSignature,
                columnPropertyNames,
                compiledFormulas);
            _formulaSheetCaches[sheetState] = cache;
            return cache;
        }

        private void MarkSelectedSheetFormulasDirty()
        {
            if (_serializedObject == null
                || _arrayPropertyPaths.Count == 0
                || _selectedArrayIndex < 0
                || _selectedArrayIndex >= _arrayPropertyPaths.Count)
            {
                return;
            }

            SerializedProperty arrayProperty = _serializedObject.FindProperty(_arrayPropertyPaths[_selectedArrayIndex]);
            if (arrayProperty != null)
            {
                MarkSheetFormulasDirty(GetOrCreateSheetState(arrayProperty));
            }
        }

        private void MarkAllFormulaSheetsDirty()
        {
            if (_document?.Sheets == null)
            {
                return;
            }

            foreach (SpreadAssetSheetState sheet in _document.Sheets)
            {
                MarkSheetFormulasDirty(sheet);
            }
        }

        private void MarkSheetFormulasDirty(SpreadAssetSheetState sheetState)
        {
            if (sheetState?.Formulas == null || sheetState.Formulas.Length == 0)
            {
                return;
            }

            _dirtyFormulaSheets.Add(sheetState);
        }

        private bool IsSheetFormulaDirty(SpreadAssetSheetState sheetState)
        {
            return sheetState != null && _dirtyFormulaSheets.Contains(sheetState);
        }

        private void InvalidateAllFormulaSheetCaches(bool markDirty)
        {
            _formulaSheetCaches.Clear();
            if (markDirty)
            {
                MarkAllFormulaSheetsDirty();
            }
            else
            {
                _dirtyFormulaSheets.Clear();
            }
        }

        private void InvalidateSheetFormulaCache(SpreadAssetSheetState sheetState, bool markDirty)
        {
            if (sheetState == null)
            {
                return;
            }

            _formulaSheetCaches.Remove(sheetState);
            if (markDirty)
            {
                MarkSheetFormulasDirty(sheetState);
            }
            else
            {
                _dirtyFormulaSheets.Remove(sheetState);
            }
        }

        private static string CreateColumnSignature(List<TableColumn> columns)
        {
            if (columns == null || columns.Count == 0)
            {
                return string.Empty;
            }

            List<string> parts = new List<string>(columns.Count);
            foreach (TableColumn column in columns)
            {
                parts.Add(string.Join("|",
                    column.FieldId ?? string.Empty,
                    column.PropertyName ?? string.Empty,
                    column.SchemaName ?? string.Empty,
                    column.TypeName ?? string.Empty,
                    column.IsDesignField ? "1" : "0",
                    column.IsKeyField ? "1" : "0"));
            }

            return string.Join("\n", parts);
        }

        private static string CreateFormulaSignature(SpreadAssetFormulaState[] formulas)
        {
            if (formulas == null || formulas.Length == 0)
            {
                return string.Empty;
            }

            List<string> parts = new List<string>(formulas.Length);
            foreach (SpreadAssetFormulaState formula in formulas)
            {
                if (formula == null)
                {
                    parts.Add("null");
                    continue;
                }

                parts.Add(string.Join("|",
                    formula.Id ?? string.Empty,
                    formula.Enabled ? "1" : "0",
                    formula.Expression ?? string.Empty));
            }

            return string.Join("\n", parts);
        }

        private void DestroyWorkingCopy()
        {
            if (_workingCopy == null)
            {
                return;
            }

            DestroyImmediate(_workingCopy);
            _workingCopy = null;
        }

        private void RefreshArrayPropertyPaths()
        {
            _arrayPropertyPaths.Clear();
            if (_serializedObject == null)
            {
                return;
            }

            SerializedProperty property = _serializedObject.GetIterator();
            bool enterChildren = true;
            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (IsTableArray(property))
                {
                    _arrayPropertyPaths.Add(property.propertyPath);
                }
            }

            _selectedArrayIndex = Mathf.Clamp(_selectedArrayIndex, 0, Mathf.Max(0, _arrayPropertyPaths.Count - 1));
        }

        private SpreadAssetSheetState GetOrCreateSheetState(SerializedProperty arrayProperty)
        {
            if (_document.Sheets == null)
            {
                _document.Sheets = Array.Empty<SpreadAssetSheetState>();
            }

            string sheetKey = GetSheetKey(arrayProperty);
            for (int i = 0; i < _document.Sheets.Length; i++)
            {
                SpreadAssetSheetState sheet = _document.Sheets[i];
                if (sheet == null)
                {
                    continue;
                }

                if (IsMatchingSheet(sheet, arrayProperty, sheetKey))
                {
                    if (sheet.Formulas == null)
                    {
                        sheet.Formulas = Array.Empty<SpreadAssetFormulaState>();
                    }

                    if (sheet.Cells == null)
                    {
                        sheet.Cells = Array.Empty<SpreadAssetCellState>();
                    }

                    if (sheet.Columns == null)
                    {
                        sheet.Columns = Array.Empty<SpreadAssetColumnState>();
                    }

                    return sheet;
                }
            }

            SpreadAssetSheetState newSheet = new SpreadAssetSheetState
            {
                ArrayFieldName = sheetKey,
                Formulas = Array.Empty<SpreadAssetFormulaState>(),
                Columns = Array.Empty<SpreadAssetColumnState>(),
                Cells = Array.Empty<SpreadAssetCellState>()
            };

            Array.Resize(ref _document.Sheets, _document.Sheets.Length + 1);
            _document.Sheets[_document.Sheets.Length - 1] = newSheet;
            _isDocumentDirty = true;
            return newSheet;
        }

        private static bool IsMatchingSheet(
            SpreadAssetSheetState sheet,
            SerializedProperty arrayProperty,
            string sheetKey)
        {
            if (string.IsNullOrEmpty(sheet.ArrayFieldName))
            {
                return false;
            }

            return sheet.ArrayFieldName == sheetKey
                || sheet.ArrayFieldName == arrayProperty.name
                || sheet.ArrayFieldName == arrayProperty.propertyPath
                || sheet.ArrayFieldName.Replace(" ", string.Empty) == arrayProperty.displayName.Replace(" ", string.Empty);
        }

        private static string GetSheetKey(SerializedProperty arrayProperty)
        {
            return SpreadAssetNameUtility.ToPascalCase(arrayProperty.name.TrimStart('_'));
        }

        private static bool IsTableArray(SerializedProperty property)
        {
            return property.isArray
                && property.propertyType == SerializedPropertyType.Generic
                && property.name != "m_Script";
        }

        private static void AddArrayRow(SerializedProperty arrayProperty)
        {
            InsertArrayRow(arrayProperty, arrayProperty.arraySize);
        }

        private static void InsertArrayRow(SerializedProperty arrayProperty, int index)
        {
            arrayProperty.InsertArrayElementAtIndex(index);
            SerializedProperty inserted = arrayProperty.GetArrayElementAtIndex(index);
            ClearPropertyValue(inserted);
        }

        private static void DeleteArrayRow(SerializedProperty arrayProperty, int index)
        {
            int previousSize = arrayProperty.arraySize;
            arrayProperty.DeleteArrayElementAtIndex(index);
            if (arrayProperty.arraySize == previousSize)
            {
                arrayProperty.DeleteArrayElementAtIndex(index);
            }
        }

        private static void ClearPropertyValue(SerializedProperty property)
        {
            if (property == null)
            {
                return;
            }

            if (property.isArray && property.propertyType == SerializedPropertyType.Generic)
            {
                property.ClearArray();
                return;
            }

            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    property.longValue = 0L;
                    break;
                case SerializedPropertyType.Boolean:
                    property.boolValue = false;
                    break;
                case SerializedPropertyType.Float:
                    property.floatValue = 0f;
                    break;
                case SerializedPropertyType.String:
                    property.stringValue = string.Empty;
                    break;
                case SerializedPropertyType.Color:
                    property.colorValue = Color.white;
                    break;
                case SerializedPropertyType.AnimationCurve:
                    property.animationCurveValue = new AnimationCurve();
                    break;
                case SerializedPropertyType.ObjectReference:
                    property.objectReferenceValue = null;
                    break;
                case SerializedPropertyType.LayerMask:
                    property.intValue = 0;
                    break;
                case SerializedPropertyType.Enum:
                    property.enumValueIndex = 0;
                    break;
                case SerializedPropertyType.Vector2:
                    property.vector2Value = Vector2.zero;
                    break;
                case SerializedPropertyType.Vector3:
                    property.vector3Value = Vector3.zero;
                    break;
                case SerializedPropertyType.Vector4:
                    property.vector4Value = Vector4.zero;
                    break;
                case SerializedPropertyType.Rect:
                    property.rectValue = Rect.zero;
                    break;
                case SerializedPropertyType.Bounds:
                    property.boundsValue = new Bounds(Vector3.zero, Vector3.zero);
                    break;
                case SerializedPropertyType.Quaternion:
                    property.quaternionValue = Quaternion.identity;
                    break;
                case SerializedPropertyType.Vector2Int:
                    property.vector2IntValue = Vector2Int.zero;
                    break;
                case SerializedPropertyType.Vector3Int:
                    property.vector3IntValue = Vector3Int.zero;
                    break;
                case SerializedPropertyType.RectInt:
                    property.rectIntValue = new RectInt();
                    break;
                case SerializedPropertyType.BoundsInt:
                    property.boundsIntValue = new BoundsInt();
                    break;
                case SerializedPropertyType.ManagedReference:
                    property.managedReferenceValue = null;
                    break;
                case SerializedPropertyType.Generic:
                    ClearChildren(property);
                    break;
            }
        }

        private static void ClearChildren(SerializedProperty property)
        {
            SerializedProperty child = property.Copy();
            SerializedProperty end = property.GetEndProperty();
            bool enterChildren = true;

            while (child.NextVisible(enterChildren) && !SerializedProperty.EqualContents(child, end))
            {
                enterChildren = false;
                if (child.depth == property.depth + 1)
                {
                    ClearPropertyValue(child);
                }
            }
        }

        private List<TableColumn> GetColumns(SerializedProperty arrayProperty, SpreadAssetSheetState sheetState = null)
        {
            List<TableColumn> columns;
            SpreadAssetSchemaTable schemaTable = FindSchemaTable(arrayProperty);
            if (schemaTable?.Fields != null && schemaTable.Fields.Length > 0)
            {
                columns = GetColumnsFromSchema(schemaTable);
                ApplyColumnWidths(columns, sheetState);
                return columns;
            }

            FieldInfo arrayField = FindField(_targetAsset.GetType(), arrayProperty.propertyPath);
            Type elementType = GetElementType(arrayField?.FieldType);
            if (arrayProperty.arraySize > 0)
            {
                columns = GetColumnsFromFirstElement(arrayProperty.GetArrayElementAtIndex(0), elementType);
                ApplyColumnWidths(columns, sheetState);
                return columns;
            }

            columns = GetColumnsFromReflection(elementType);
            ApplyColumnWidths(columns, sheetState);
            return columns;
        }

        private SpreadAssetSchemaTable FindSchemaTable(SerializedProperty arrayProperty)
        {
            if (_document?.Schema?.Tables == null || arrayProperty == null)
            {
                return null;
            }

            string sheetKey = GetSheetKey(arrayProperty);
            foreach (SpreadAssetSchemaTable table in _document.Schema.Tables)
            {
                if (table == null || table.OmitArrayField || string.IsNullOrEmpty(table.FieldName))
                {
                    continue;
                }

                string serializedFieldName = SpreadAssetNameUtility.ToSerializedFieldName(table.FieldName);
                if (table.FieldName == sheetKey
                    || table.FieldName == arrayProperty.name
                    || serializedFieldName == arrayProperty.name
                    || serializedFieldName == arrayProperty.propertyPath)
                {
                    return table;
                }
            }

            return null;
        }

        private static List<TableColumn> GetColumnsFromSchema(SpreadAssetSchemaTable table)
        {
            List<TableColumn> columns = new List<TableColumn>();
            foreach (SpreadAssetSchemaField field in table.Fields)
            {
                if (field == null || string.IsNullOrWhiteSpace(field.Name))
                {
                    continue;
                }

                string propertyName = SpreadAssetNameUtility.ToSerializedFieldName(field.Name);
                string displayName = ObjectNames.NicifyVariableName(field.Name);
                columns.Add(CreateTableColumn(
                    propertyName,
                    displayName,
                    field.TypeName,
                    field.Name,
                    field.IsDesignField,
                    field.IsKeyField,
                    field.Id));
            }

            return columns;
        }

        private static List<TableColumn> GetColumnsFromFirstElement(SerializedProperty element, Type elementType)
        {
            List<TableColumn> columns = new List<TableColumn>();
            if (element.propertyType != SerializedPropertyType.Generic)
            {
                string typeName = GetFriendlyTypeName(elementType) ?? GetSerializedPropertyTypeName(element);
                string fieldId = SpreadAssetSchemaUtility.CreateFieldId(
                    "serialized:" + (elementType?.FullName ?? string.Empty),
                    element.displayName,
                    typeName);
                columns.Add(CreateTableColumn(string.Empty, element.displayName, typeName, fieldId: fieldId));
                return columns;
            }

            SerializedProperty child = element.Copy();
            SerializedProperty end = element.GetEndProperty();
            bool enterChildren = true;

            while (child.NextVisible(enterChildren) && !SerializedProperty.EqualContents(child, end))
            {
                enterChildren = false;
                if (child.depth != element.depth + 1)
                {
                    continue;
                }

                FieldInfo field = elementType?.GetField(child.name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                string typeName = GetFriendlyTypeName(field?.FieldType) ?? GetSerializedPropertyTypeName(child);
                string fieldId = SpreadAssetSchemaUtility.CreateFieldId(
                    "serialized:" + (elementType?.FullName ?? string.Empty),
                    child.name,
                    typeName);
                columns.Add(CreateTableColumn(child.name, child.displayName, typeName, fieldId: fieldId));
            }

            return columns;
        }

        private static List<TableColumn> GetColumnsFromReflection(Type elementType)
        {
            List<TableColumn> columns = new List<TableColumn>();
            if (elementType == null)
            {
                return columns;
            }

            foreach (FieldInfo field in GetSerializableFields(elementType))
            {
                string displayName = ObjectNames.NicifyVariableName(field.Name.TrimStart('_'));
                string typeName = GetFriendlyTypeName(field.FieldType);
                string fieldId = SpreadAssetSchemaUtility.CreateFieldId(
                    "reflection:" + elementType.FullName,
                    field.Name,
                    typeName);
                columns.Add(CreateTableColumn(field.Name, displayName, typeName, fieldId: fieldId));
            }

            return columns;
        }

        private static FieldInfo FindField(Type ownerType, string propertyPath)
        {
            string[] parts = propertyPath.Split('.');
            Type currentType = ownerType;
            FieldInfo field = null;

            foreach (string part in parts)
            {
                if (part == "Array" || part.StartsWith("data[", StringComparison.Ordinal))
                {
                    continue;
                }

                field = currentType?.GetField(part, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null)
                {
                    return null;
                }

                currentType = field.FieldType;
            }

            return field;
        }

        private static Type GetElementType(Type collectionType)
        {
            if (collectionType == null)
            {
                return null;
            }

            if (collectionType.IsArray)
            {
                return collectionType.GetElementType();
            }

            if (collectionType.IsGenericType && collectionType.GetGenericArguments().Length == 1)
            {
                return collectionType.GetGenericArguments()[0];
            }

            return null;
        }

        private static IEnumerable<FieldInfo> GetSerializableFields(Type type)
        {
            FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (FieldInfo field in fields)
            {
                if (field.IsStatic || field.IsNotSerialized)
                {
                    continue;
                }

                bool isSerializable = field.IsPublic || Attribute.IsDefined(field, typeof(SerializeField));
                bool isHidden = Attribute.IsDefined(field, typeof(HideInInspector));
                if (isSerializable && !isHidden)
                {
                    yield return field;
                }
            }
        }

        private static TableColumn CreateTableColumn(
            string propertyName,
            string displayName,
            string typeName,
            string schemaName = "",
            bool isDesignField = false,
            bool isKeyField = false,
            string fieldId = "")
        {
            string displayTypeName = GetColumnTypeDisplayName(typeName);
            List<string> typeLabels = new List<string>();
            if (!string.IsNullOrEmpty(displayTypeName))
            {
                typeLabels.Add(displayTypeName);
            }

            if (isKeyField)
            {
                typeLabels.Add("key");
            }

            if (isDesignField)
            {
                typeLabels.Add("design");
            }

            string typeLabel = string.Join(", ", typeLabels);
            string headerLabel = string.IsNullOrEmpty(typeLabel) ? displayName : $"{displayName}\n{typeLabel}";
            return new TableColumn(
                propertyName,
                displayName,
                typeName,
                headerLabel,
                CalculateColumnWidth(headerLabel),
                schemaName,
                isDesignField,
                isKeyField,
                fieldId);
        }

        private static string GetColumnTypeDisplayName(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return string.Empty;
            }

            if (SpreadAssetEnumTypeUtility.TryGetAnnotatedEnumType(typeName, out Type enumType))
            {
                return enumType.Name;
            }

            string trimmed = typeName.Trim().Replace('+', '.');
            const string arraySuffix = "[]";
            if (trimmed.EndsWith(arraySuffix, StringComparison.Ordinal))
            {
                return GetColumnTypeDisplayName(trimmed.Substring(0, trimmed.Length - arraySuffix.Length)) + arraySuffix;
            }

            int lastDot = trimmed.LastIndexOf('.');
            return lastDot >= 0 && lastDot + 1 < trimmed.Length
                ? trimmed.Substring(lastDot + 1)
                : trimmed;
        }

        private static float CalculateFrozenRowHeaderWidth()
        {
            return RowNumberWidth + RowButtonWidth * 2f + 6f;
        }

        private static float CalculateDataGridWidth(List<TableColumn> columns)
        {
            if (columns == null || columns.Count == 0)
            {
                return DefaultColumnWidth;
            }

            float width = 0f;
            foreach (TableColumn column in columns)
            {
                width += column.Width;
            }

            return width;
        }

        private static float CalculateColumnX(List<TableColumn> columns, int columnIndex)
        {
            if (columns == null || columns.Count == 0 || columnIndex <= 0)
            {
                return 0f;
            }

            float x = 0f;
            int boundedColumnIndex = Mathf.Min(columnIndex, columns.Count);
            for (int i = 0; i < boundedColumnIndex; i++)
            {
                x += columns[i].Width;
            }

            return x;
        }

        private static float GetColumnWidth(List<TableColumn> columns, int columnIndex)
        {
            if (columns == null || columns.Count == 0)
            {
                return DefaultColumnWidth;
            }

            return columnIndex >= 0 && columnIndex < columns.Count
                ? columns[columnIndex].Width
                : DefaultColumnWidth;
        }

        private static float CalculateColumnWidth(string label)
        {
            if (string.IsNullOrEmpty(label))
            {
                return MinimumColumnWidth;
            }

            float width = 0f;
            string[] lines = label.Split('\n');
            foreach (string line in lines)
            {
                width = Mathf.Max(width, EditorStyles.label.CalcSize(new GUIContent(line)).x);
            }

            return Mathf.Clamp(width + 48f, MinimumColumnWidth, MaximumColumnWidth);
        }

        private static float CalculateGridHeaderHeight()
        {
            return TableHeaderHeight;
        }

        private static GridRowLayout CalculateGridRowLayout(
            SerializedProperty arrayProperty,
            List<TableColumn> columns)
        {
            int rowCount = arrayProperty?.arraySize ?? 0;
            if (rowCount <= 0)
            {
                return new GridRowLayout(Array.Empty<float>(), Array.Empty<float>(), TableRowHeight + TableLayoutPadding);
            }

            float[] yOffsets = new float[rowCount];
            float[] heights = new float[rowCount];
            float y = 0f;
            float rowSpacing = EditorGUIUtility.standardVerticalSpacing;
            for (int row = 0; row < rowCount; row++)
            {
                SerializedProperty element = arrayProperty.GetArrayElementAtIndex(row);
                float rowHeight = CalculateGridRowHeight(element, columns);
                yOffsets[row] = y;
                heights[row] = rowHeight;
                y += rowHeight + rowSpacing;
            }

            float contentHeight = y - rowSpacing + TableLayoutPadding;
            return new GridRowLayout(yOffsets, heights, contentHeight);
        }

        private static float CalculateGridRowHeight(SerializedProperty element, List<TableColumn> columns)
        {
            float height = TableRowHeight;
            if (columns == null || columns.Count == 0)
            {
                return Mathf.Max(height, CalculatePropertyCellHeight(element));
            }

            foreach (TableColumn column in columns)
            {
                if (column.IsDesignField)
                {
                    continue;
                }

                SerializedProperty cell = GetCellProperty(element, column);
                if (cell != null)
                {
                    height = Mathf.Max(height, CalculatePropertyCellHeight(cell));
                }
            }

            return height;
        }

        private static float CalculatePropertyCellHeight(SerializedProperty property)
        {
            return Mathf.Max(
                TableRowHeight,
                CalculatePropertyControlHeight(property) + CellControlVerticalPadding * 2f);
        }

        private static float CalculatePropertyControlHeight(SerializedProperty property)
        {
            if (property == null)
            {
                return EditorGUIUtility.singleLineHeight;
            }

            if (IsTableExpandableProperty(property))
            {
                return CalculateTablePropertyTreeHeight(property);
            }

            return EditorGUI.GetPropertyHeight(property, GUIContent.none, includeChildren: true);
        }

        private static float CalculateTablePropertyTreeHeight(SerializedProperty property)
        {
            float height = GetTablePropertyLineHeight(property);
            if (!property.isExpanded)
            {
                return height;
            }

            SerializedProperty child = property.Copy();
            SerializedProperty end = property.GetEndProperty();
            bool enterChildren = true;
            while (child.NextVisible(enterChildren) && !SerializedProperty.EqualContents(child, end))
            {
                height += EditorGUIUtility.standardVerticalSpacing + GetTablePropertyLineHeight(child);
                enterChildren = ShouldDrawTablePropertyTree(child);
            }

            return height;
        }

        private static VisibleRowRange CalculateVisibleRowRange(
            GridRowLayout rowLayout,
            float scrollY,
            float viewportHeight)
        {
            int rowCount = rowLayout.Count;
            if (rowCount <= 0)
            {
                return new VisibleRowRange(0, 0);
            }

            float viewportTop = Mathf.Max(0f, scrollY);
            float viewportBottom = viewportTop + Mathf.Max(TableRowHeight, viewportHeight);
            int startIndex = 0;
            while (startIndex < rowCount - 1 && rowLayout.GetBottom(startIndex) < viewportTop)
            {
                startIndex++;
            }

            startIndex = Mathf.Max(0, startIndex - 1);
            int endIndex = startIndex;
            while (endIndex < rowCount && rowLayout.GetY(endIndex) <= viewportBottom)
            {
                endIndex++;
            }

            endIndex = Mathf.Clamp(endIndex + 1, startIndex + 1, rowCount);
            return new VisibleRowRange(startIndex, endIndex);
        }

        private static float CalculateGridRowPitch()
        {
            return TableRowHeight + EditorGUIUtility.standardVerticalSpacing;
        }

        private static float CalculateGridRowY(int rowIndex)
        {
            return rowIndex * CalculateGridRowPitch();
        }

        private static float CalculateDataGridViewportWidth(float frozenWidth)
        {
            float availableWidth = Mathf.Max(0f, EditorGUIUtility.currentViewWidth - 32f);
            return Mathf.Max(DefaultColumnWidth, availableWidth - frozenWidth);
        }

        private static bool ShouldUseHorizontalGridScroll(float dataWidth, float dataViewportWidth)
        {
            return dataWidth > dataViewportWidth;
        }

        private static string NormalizeTypeName(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return string.Empty;
            }

            string trimmed = typeName.Trim();
            int lastDot = trimmed.LastIndexOf('.');
            if (lastDot >= 0 && lastDot + 1 < trimmed.Length)
            {
                trimmed = trimmed.Substring(lastDot + 1);
            }

            return trimmed.ToLowerInvariant();
        }

        private static int ParseInt(string value)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
                ? result
                : 0;
        }

        private static long ParseLong(string value)
        {
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long result)
                ? result
                : 0L;
        }

        private static float ParseFloat(string value)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result)
                ? result
                : 0f;
        }

        private static double ParseDouble(string value)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result)
                ? result
                : 0d;
        }

        private static bool ParseBool(string value)
        {
            if (bool.TryParse(value, out bool result))
            {
                return result;
            }

            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
                && Math.Abs(number) > double.Epsilon;
        }

        private static string GetFriendlyTypeName(Type type)
        {
            if (type == null)
            {
                return null;
            }

            if (type == typeof(int))
            {
                return "int";
            }

            if (type == typeof(float))
            {
                return "float";
            }

            if (type == typeof(string))
            {
                return "string";
            }

            if (type == typeof(bool))
            {
                return "bool";
            }

            if (type == typeof(double))
            {
                return "double";
            }

            if (type == typeof(long))
            {
                return "long";
            }

            if (type == typeof(short))
            {
                return "short";
            }

            if (type == typeof(byte))
            {
                return "byte";
            }

            if (type == typeof(uint))
            {
                return "uint";
            }

            if (type == typeof(ulong))
            {
                return "ulong";
            }

            if (type == typeof(ushort))
            {
                return "ushort";
            }

            if (type == typeof(sbyte))
            {
                return "sbyte";
            }

            if (type == typeof(decimal))
            {
                return "decimal";
            }

            if (type.IsArray)
            {
                return $"{GetFriendlyTypeName(type.GetElementType()) ?? type.GetElementType()?.Name}[]";
            }

            if (type.IsGenericType)
            {
                string typeName = type.Name;
                int arityIndex = typeName.IndexOf('`');
                if (arityIndex >= 0)
                {
                    typeName = typeName.Substring(0, arityIndex);
                }

                Type[] arguments = type.GetGenericArguments();
                string[] argumentNames = new string[arguments.Length];
                for (int i = 0; i < arguments.Length; i++)
                {
                    argumentNames[i] = GetFriendlyTypeName(arguments[i]) ?? arguments[i].Name;
                }

                return $"{typeName}<{string.Join(", ", argumentNames)}>";
            }

            return type.Name;
        }

        private static string GetSerializedPropertyTypeName(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return "int";
                case SerializedPropertyType.Boolean:
                    return "bool";
                case SerializedPropertyType.Float:
                    return "float";
                case SerializedPropertyType.String:
                    return "string";
                case SerializedPropertyType.Color:
                    return "Color";
                case SerializedPropertyType.AnimationCurve:
                    return "AnimationCurve";
                case SerializedPropertyType.ObjectReference:
                    return string.IsNullOrEmpty(property.type) ? "Object" : property.type;
                case SerializedPropertyType.LayerMask:
                    return "LayerMask";
                case SerializedPropertyType.Enum:
                    return string.IsNullOrEmpty(property.type) ? "enum" : property.type;
                case SerializedPropertyType.Vector2:
                    return "Vector2";
                case SerializedPropertyType.Vector3:
                    return "Vector3";
                case SerializedPropertyType.Vector4:
                    return "Vector4";
                case SerializedPropertyType.Rect:
                    return "Rect";
                case SerializedPropertyType.Bounds:
                    return "Bounds";
                case SerializedPropertyType.Quaternion:
                    return "Quaternion";
                case SerializedPropertyType.Vector2Int:
                    return "Vector2Int";
                case SerializedPropertyType.Vector3Int:
                    return "Vector3Int";
                case SerializedPropertyType.RectInt:
                    return "RectInt";
                case SerializedPropertyType.BoundsInt:
                    return "BoundsInt";
                case SerializedPropertyType.ManagedReference:
                    return string.IsNullOrEmpty(property.managedReferenceFullTypename)
                        ? "managed"
                        : property.managedReferenceFullTypename;
                default:
                    return property.type;
            }
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

        private static List<SpreadAssetFormulaState> GetFormulaList(SpreadAssetSheetState sheetState)
        {
            if (sheetState.Formulas == null)
            {
                sheetState.Formulas = Array.Empty<SpreadAssetFormulaState>();
            }

            return new List<SpreadAssetFormulaState>(sheetState.Formulas);
        }

        private static bool EnsureFormulaIds(SpreadAssetSheetState sheetState)
        {
            if (sheetState.Formulas == null)
            {
                sheetState.Formulas = Array.Empty<SpreadAssetFormulaState>();
                return false;
            }

            bool changed = false;
            for (int i = 0; i < sheetState.Formulas.Length; i++)
            {
                SpreadAssetFormulaState formula = sheetState.Formulas[i];
                if (formula == null)
                {
                    formula = new SpreadAssetFormulaState();
                    sheetState.Formulas[i] = formula;
                    changed = true;
                }

                if (string.IsNullOrEmpty(formula.Id))
                {
                    formula.Id = CreateFormulaId();
                    changed = true;
                }
            }

            return changed;
        }

        private string GetFormulaDraft(SpreadAssetFormulaState formula)
        {
            if (formula == null)
            {
                return string.Empty;
            }

            if (string.IsNullOrEmpty(formula.Id))
            {
                formula.Id = CreateFormulaId();
            }

            if (!_formulaDrafts.TryGetValue(formula.Id, out string draft))
            {
                draft = formula.Expression ?? string.Empty;
                _formulaDrafts[formula.Id] = draft;
            }

            return draft;
        }

        private bool CommitFormulaDraftsIfFocusLost(SpreadAssetSheetState sheetState)
        {
            if (Event.current.type != EventType.Repaint || IsFormulaTextFocused())
            {
                return false;
            }

            return CommitFormulaDrafts(sheetState);
        }

        private bool CommitFormulaDrafts(SpreadAssetSheetState sheetState)
        {
            if (sheetState?.Formulas == null)
            {
                return false;
            }

            bool changed = false;
            foreach (SpreadAssetFormulaState formula in sheetState.Formulas)
            {
                if (formula == null)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(formula.Id))
                {
                    formula.Id = CreateFormulaId();
                    changed = true;
                }

                if (!_formulaDrafts.TryGetValue(formula.Id, out string draft))
                {
                    continue;
                }

                draft ??= string.Empty;
                if (formula.Expression != draft)
                {
                    formula.Expression = draft;
                    changed = true;
                }
            }

            return changed;
        }

        private bool CommitAllFormulaDrafts()
        {
            bool changed = false;
            if (_document?.Sheets == null)
            {
                return false;
            }

            foreach (SpreadAssetSheetState sheet in _document.Sheets)
            {
                if (CommitFormulaDrafts(sheet))
                {
                    InvalidateSheetFormulaCache(sheet, markDirty: true);
                    changed = true;
                }
            }

            if (changed)
            {
                _isDocumentDirty = true;
            }

            return changed;
        }

        private static string GetFormulaControlName(SpreadAssetFormulaState formula)
        {
            string id = string.IsNullOrEmpty(formula?.Id) ? "Unknown" : formula.Id;
            return $"SpreadAsset Editor.Formula.{id}";
        }

        private static string CreateFormulaId()
        {
            return Guid.NewGuid().ToString("N");
        }

        private static void ClearTextFieldFocus()
        {
            GUI.FocusControl(null);
            GUIUtility.keyboardControl = 0;
            EditorGUIUtility.editingTextField = false;
        }

        private void FocusPendingFormulaControl(string controlName)
        {
            if (string.IsNullOrEmpty(_pendingFormulaFocusControlName)
                || _pendingFormulaFocusControlName != controlName
                || Event.current.type != EventType.Repaint)
            {
                return;
            }

            EditorGUI.FocusTextInControl(controlName);
            _pendingFormulaFocusControlName = string.Empty;
        }

        private static bool IsFormulaTextFocused()
        {
            string focusedControl = GUI.GetNameOfFocusedControl();
            return !string.IsNullOrEmpty(focusedControl)
                && focusedControl.StartsWith("SpreadAsset Editor.Formula.", StringComparison.Ordinal);
        }

        private static List<string> GetColumnPropertyNames(List<TableColumn> columns)
        {
            List<string> propertyNames = new List<string>(columns.Count);
            foreach (TableColumn column in columns)
            {
                propertyNames.Add(column.PropertyName);
            }

            return propertyNames;
        }

        private HashSet<string> GetFormulaTargetKeys(
            SpreadAssetSheetState sheetState,
            int rowCount,
            List<TableColumn> columns)
        {
            if (sheetState?.Formulas == null)
            {
                return new HashSet<string>();
            }

            FormulaSheetCache cache = GetOrCreateFormulaSheetCache(
                sheetState,
                rowCount,
                columns,
                forceRecompile: false,
                out string error);
            if (cache == null)
            {
                _formulaError = error;
                return new HashSet<string>();
            }

            return cache.TargetKeys;
        }

        private static string GetCellKey(int rowIndex, int columnIndex)
        {
            return rowIndex + ":" + columnIndex;
        }

        private sealed class KeyValidationResult
        {
            public readonly List<KeyValidationIssue> Issues = new List<KeyValidationIssue>();
            public readonly HashSet<string> InvalidCellKeys = new HashSet<string>(StringComparer.Ordinal);

            public bool HasErrors => Issues.Count > 0;

            public void Add(KeyValidationResult result)
            {
                if (result == null)
                {
                    return;
                }

                Issues.AddRange(result.Issues);
                foreach (string cellKey in result.InvalidCellKeys)
                {
                    InvalidCellKeys.Add(cellKey);
                }
            }

            public void AddIssue(KeyValidationIssue issue)
            {
                if (issue == null)
                {
                    return;
                }

                Issues.Add(issue);
                foreach (int row in issue.Rows)
                {
                    InvalidCellKeys.Add(GetCellKey(row, issue.ColumnIndex));
                }
            }
        }

        private sealed class KeyValidationIssue
        {
            public readonly KeyValidationIssueKind Kind;
            public readonly string SheetName;
            public readonly string ColumnName;
            public readonly int ColumnIndex;
            public readonly string KeyValue;
            public readonly List<int> Rows;

            public KeyValidationIssue(
                KeyValidationIssueKind kind,
                string sheetName,
                string columnName,
                int columnIndex,
                string keyValue,
                List<int> rows)
            {
                Kind = kind;
                SheetName = sheetName ?? string.Empty;
                ColumnName = columnName ?? string.Empty;
                ColumnIndex = columnIndex;
                KeyValue = keyValue ?? string.Empty;
                Rows = rows ?? new List<int>();
            }
        }

        private enum KeyValidationIssueKind
        {
            Duplicate,
            Empty,
            Unsupported
        }

        private sealed class DesignFormulaCellStore : ISpreadAssetFormulaCellStore
        {
            private readonly SpreadAssetSheetState _sheetState;
            private readonly List<TableColumn> _columns;

            public DesignFormulaCellStore(SpreadAssetSheetState sheetState, List<TableColumn> columns)
            {
                _sheetState = sheetState;
                _columns = columns;
            }

            public bool IsVirtualColumn(int column)
            {
                return column >= 0
                    && column < _columns.Count
                    && _columns[column].IsDesignField;
            }

            public bool TryGetCellValue(
                SpreadAssetCellAddress address,
                out string value,
                out string typeName,
                out string error)
            {
                value = string.Empty;
                typeName = string.Empty;
                error = string.Empty;

                if (!IsVirtualColumn(address.Column))
                {
                    error = $"cell {address} is not a design field.";
                    return false;
                }

                TableColumn column = _columns[address.Column];
                value = GetDesignCellValue(_sheetState, address.Row, column);
                typeName = column.TypeName;
                return true;
            }

            public bool TrySetCellValue(
                SpreadAssetCellAddress address,
                string value,
                out bool changed,
                out string error)
            {
                changed = false;
                error = string.Empty;

                if (!IsVirtualColumn(address.Column))
                {
                    error = $"cell {address} is not a design field.";
                    return false;
                }

                TableColumn column = _columns[address.Column];
                string previousValue = GetDesignCellValue(_sheetState, address.Row, column);
                string nextValue = value ?? string.Empty;
                changed = previousValue != nextValue;
                if (changed)
                {
                    SetDesignCellValue(_sheetState, address.Row, column, nextValue);
                }

                return true;
            }
        }

        private enum SearchDirection
        {
            First,
            Next,
            Previous
        }

        private readonly struct GridRowLayout
        {
            private readonly float[] _rowYs;
            private readonly float[] _rowHeights;

            public readonly float ContentHeight;

            public GridRowLayout(float[] rowYs, float[] rowHeights, float contentHeight)
            {
                _rowYs = rowYs ?? Array.Empty<float>();
                _rowHeights = rowHeights ?? Array.Empty<float>();
                ContentHeight = Mathf.Max(TableRowHeight + TableLayoutPadding, contentHeight);
            }

            public int Count => _rowHeights?.Length ?? 0;

            public float GetY(int rowIndex)
            {
                return rowIndex >= 0 && rowIndex < Count ? _rowYs[rowIndex] : 0f;
            }

            public float GetHeight(int rowIndex)
            {
                return rowIndex >= 0 && rowIndex < Count ? _rowHeights[rowIndex] : TableRowHeight;
            }

            public float GetBottom(int rowIndex)
            {
                return GetY(rowIndex) + GetHeight(rowIndex);
            }
        }

        private readonly struct VisibleRowRange
        {
            public readonly int StartIndex;
            public readonly int EndIndex;

            public VisibleRowRange(int startIndex, int endIndex)
            {
                StartIndex = startIndex;
                EndIndex = endIndex;
            }
        }

        private readonly struct CsvColumnBinding
        {
            public readonly string Header;
            public readonly int ColumnIndex;

            public CsvColumnBinding(string header, int columnIndex)
            {
                Header = header ?? string.Empty;
                ColumnIndex = columnIndex;
            }
        }

        private readonly struct CsvImportColumn
        {
            public readonly int SourceColumnIndex;
            public readonly int TargetColumnIndex;
            public readonly string Header;

            public CsvImportColumn(int sourceColumnIndex, int targetColumnIndex, string header)
            {
                SourceColumnIndex = sourceColumnIndex;
                TargetColumnIndex = targetColumnIndex;
                Header = header ?? string.Empty;
            }
        }

        private readonly struct CsvImportPlan
        {
            public readonly int HeaderRowIndex;
            public readonly int DataStartRowIndex;
            public readonly int DataEndRowIndex;
            public readonly List<CsvImportColumn> Columns;
            public readonly int UnknownHeaderCount;

            public CsvImportPlan(
                int headerRowIndex,
                int dataStartRowIndex,
                int dataEndRowIndex,
                List<CsvImportColumn> columns,
                int unknownHeaderCount)
            {
                HeaderRowIndex = headerRowIndex;
                DataStartRowIndex = dataStartRowIndex;
                DataEndRowIndex = dataEndRowIndex;
                Columns = columns ?? new List<CsvImportColumn>();
                UnknownHeaderCount = unknownHeaderCount;
            }

            public int RowCount => Mathf.Max(0, DataEndRowIndex - DataStartRowIndex);
        }

        private readonly struct CsvImportSummary
        {
            public readonly int RowCount;
            public readonly int UnknownHeaderCount;
            public readonly int UnsupportedCellCount;

            public CsvImportSummary(int rowCount, int unknownHeaderCount, int unsupportedCellCount)
            {
                RowCount = rowCount;
                UnknownHeaderCount = unknownHeaderCount;
                UnsupportedCellCount = unsupportedCellCount;
            }
        }

        private sealed class FormulaSheetCache
        {
            private readonly int _rowCount;
            private readonly string _columnSignature;
            private readonly string _formulaSignature;

            public FormulaSheetCache(
                int rowCount,
                string columnSignature,
                string formulaSignature,
                List<string> columnPropertyNames,
                SpreadAssetFormulaEngine.CompiledFormulaSet compiledFormulas)
            {
                _rowCount = rowCount;
                _columnSignature = columnSignature ?? string.Empty;
                _formulaSignature = formulaSignature ?? string.Empty;
                ColumnPropertyNames = columnPropertyNames;
                CompiledFormulas = compiledFormulas;
                TargetKeys = new HashSet<string>(compiledFormulas?.TargetKeys ?? Array.Empty<string>());
            }

            public List<string> ColumnPropertyNames { get; }

            public SpreadAssetFormulaEngine.CompiledFormulaSet CompiledFormulas { get; }

            public HashSet<string> TargetKeys { get; }

            public bool Matches(int rowCount, string columnSignature, string formulaSignature)
            {
                return _rowCount == rowCount
                    && string.Equals(_columnSignature, columnSignature ?? string.Empty, StringComparison.Ordinal)
                    && string.Equals(_formulaSignature, formulaSignature ?? string.Empty, StringComparison.Ordinal);
            }
        }

        private readonly struct TableColumn
        {
            public readonly string PropertyName;
            public readonly string DisplayName;
            public readonly string TypeName;
            public readonly string HeaderLabel;
            public readonly float Width;
            public readonly string SchemaName;
            public readonly bool IsDesignField;
            public readonly bool IsKeyField;
            public readonly string FieldId;

            public TableColumn(
                string propertyName,
                string displayName,
                string typeName,
                string headerLabel,
                float width,
                string schemaName,
                bool isDesignField,
                bool isKeyField,
                string fieldId)
            {
                PropertyName = propertyName;
                DisplayName = displayName;
                TypeName = typeName;
                HeaderLabel = headerLabel;
                Width = width;
                SchemaName = schemaName;
                IsDesignField = isDesignField;
                IsKeyField = isKeyField;
                FieldId = fieldId ?? string.Empty;
            }

            public TableColumn WithWidth(float width)
            {
                return new TableColumn(
                    PropertyName,
                    DisplayName,
                    TypeName,
                    HeaderLabel,
                    width,
                    SchemaName,
                    IsDesignField,
                    IsKeyField,
                    FieldId);
            }
        }
    }
}

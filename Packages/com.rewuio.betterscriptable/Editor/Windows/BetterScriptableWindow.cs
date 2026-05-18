using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BetterScriptable.Editor
{
    public sealed class BetterScriptableWindow : EditorWindow
    {
        private const string WindowTitle = "BetterScriptable Editor";
        private const float RowNumberWidth = 44f;
        private const float RowButtonWidth = 28f;
        private const float MinimumColumnWidth = 100f;
        private const float MaximumColumnWidth = 360f;
        private const float DefaultColumnWidth = MinimumColumnWidth;
        private const float FormulaRowHeight = 22f;
        private const float TableRowHeight = 22f;
        private const float TableHeaderHeight = TableRowHeight * 2f;
        private const float HorizontalScrollbarPadding = 18f;
        private const float TableLayoutPadding = 6f;

        private IMGUIContainer _imguiContainer;
        private string _documentPath;
        private string _loadError;
        private BetterScriptableDocument _document;
        private UnityEngine.Object _targetAsset;
        private ScriptableObject _workingCopy;
        private SerializedObject _serializedObject;
        private readonly List<string> _arrayPropertyPaths = new List<string>();
        private Vector2 _propertyScroll;
        private Vector2 _tableScroll;
        private int _selectedArrayIndex;
        private bool _showAssetFields = true;
        private bool _showFormulaList = true;
        private bool _isDocumentDirty;
        private string _formulaError;
        private string _pendingFormulaFocusControlName;
        private string _pendingCellFocusControlName;
        private readonly Dictionary<string, string> _formulaDrafts = new Dictionary<string, string>();

        [MenuItem("Tools/BetterScriptable/Open")]
        public static BetterScriptableWindow Open()
        {
            BetterScriptableWindow window = GetWindow<BetterScriptableWindow>(WindowTitle);
            window.titleContent = new GUIContent(WindowTitle);
            window.Show();
            return window;
        }

        public static void OpenDocument(string documentPath)
        {
            BetterScriptableWindow window = Open();
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
            rootVisualElement.AddToClassList("better-scriptable-window");
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
            if (!BetterScriptableDocumentIO.IsDocumentPath(selectedPath))
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
            _pendingFormulaFocusControlName = string.Empty;
            _pendingCellFocusControlName = string.Empty;
            _formulaDrafts.Clear();

            if (!BetterScriptableDocumentIO.TryRead(documentPath, out _document, out _loadError))
            {
                return;
            }

            _targetAsset = BetterScriptableDocumentIO.LoadLinkedAsset(_document);
            if (_targetAsset == null)
            {
                _loadError = "Linked .asset could not be found.";
                return;
            }

            bool documentChanged = BetterScriptableDocumentSync.EnsureDocumentData(_document, _targetAsset);
            documentChanged |= RefreshDocumentSchemaFromGeneratedFactory(_document, _targetAsset);
            documentChanged |= EnsureDocumentSheetsForSchema(_document);

            if (documentChanged)
            {
                BetterScriptableDocumentIO.Write(_documentPath, _document);
                AssetDatabase.ImportAsset(_documentPath);
            }

            _workingCopy = BetterScriptableDocumentSync.CreateWorkingCopy(_document, _targetAsset);
            if (_workingCopy == null)
            {
                _loadError = "Could not create a working copy from the BetterScriptable document.";
                return;
            }

            _serializedObject = new SerializedObject(_workingCopy);
            RefreshArrayPropertyPaths();
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

            BetterScriptableDocumentIO.Write(_documentPath, _document);
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

            EditorGUI.BeginChangeCheck();
            DrawHeader();
            DrawNonArrayProperties();
            EditorGUILayout.Space(8);

            _propertyScroll = EditorGUILayout.BeginScrollView(_propertyScroll);
            DrawSelectedArrayTable();
            EditorGUILayout.EndScrollView();

            DrawArrayTabs();
            DrawFooter();

            EditorGUI.EndChangeCheck();
            bool serializedChanged = _serializedObject.ApplyModifiedProperties();
            bool formulaChanged = ApplySelectedSheetFormulas();
            if (serializedChanged || formulaChanged)
            {
                _isDocumentDirty = true;
                RefreshArrayPropertyPaths();
            }
        }

        private void DrawEmptyState()
        {
            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField(WindowTitle, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Select a .betterscriptable file in the Project window to edit its linked ScriptableObject data.",
                MessageType.Info);

            if (!string.IsNullOrEmpty(_loadError))
            {
                EditorGUILayout.HelpBox(_loadError, MessageType.Error);
            }

            if (GUILayout.Button("Open Generator", GUILayout.Width(160)))
            {
                BetterScriptableGeneratorWindow.Open();
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
                "Edits are applied to the .betterscriptable source document first. Save & Export writes the data portion to the linked .asset.",
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

            List<TableColumn> columns = GetColumns(arrayProperty);
            BetterScriptableSheetState sheetState = GetOrCreateSheetState(arrayProperty);
            DrawFormulaPanel(arrayProperty, columns, sheetState);
            DrawArrayToolbar(arrayProperty, sheetState);
            DrawArrayGrid(arrayProperty, columns, sheetState);
        }

        private void DrawFormulaPanel(
            SerializedProperty arrayProperty,
            List<TableColumn> columns,
            BetterScriptableSheetState sheetState)
        {
            if (EnsureFormulaIds(sheetState))
            {
                _isDocumentDirty = true;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                List<BetterScriptableFormulaState> formulaList = GetFormulaList(sheetState);
                using (new EditorGUILayout.HorizontalScope())
                {
                    _showFormulaList = EditorGUILayout.Foldout(
                        _showFormulaList,
                        $"Formulas ({formulaList.Count})",
                        true);
                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button("Add Formula", GUILayout.Width(104)))
                    {
                        List<BetterScriptableFormulaState> formulas = GetFormulaList(sheetState);
                        BetterScriptableFormulaState newFormula = new BetterScriptableFormulaState
                        {
                            Id = CreateFormulaId(),
                            Expression = string.Empty
                        };
                        formulas.Add(newFormula);
                        sheetState.Formulas = formulas.ToArray();
                        _isDocumentDirty = true;
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
                        }

                        ApplyFormulas(arrayProperty, columns, sheetState, markDirty: true);
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
                    ApplyFormulas(arrayProperty, columns, sheetState, markDirty: true);
                }
            }
        }

        private bool DrawFormulaRows(
            SerializedProperty arrayProperty,
            List<TableColumn> columns,
            BetterScriptableSheetState sheetState,
            List<BetterScriptableFormulaState> formulaList)
        {
            bool changed = false;
            for (int i = 0; i < formulaList.Count; i++)
            {
                BetterScriptableFormulaState formula = formulaList[i];
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
                        ClearTextFieldFocus();
                        ApplyFormulas(arrayProperty, columns, sheetState, markDirty: true);
                        Repaint();
                        GUIUtility.ExitGUI();
                    }

                    FocusPendingFormulaControl(controlName);
                }
            }

            return changed;
        }

        private void DrawArrayToolbar(SerializedProperty arrayProperty, BetterScriptableSheetState sheetState)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(arrayProperty.displayName, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Add Row", GUILayout.Width(92)))
                {
                    AddArrayRow(arrayProperty);
                }

                if (GUILayout.Button("Clear", GUILayout.Width(72))
                    && EditorUtility.DisplayDialog("Clear rows?", $"Remove every row from {arrayProperty.displayName}?", "Clear", "Cancel"))
                {
                    arrayProperty.ClearArray();
                    ClearSheetCells(sheetState);
                    _isDocumentDirty = true;
                }
            }

            int previousSize = arrayProperty.arraySize;
            int size = Mathf.Max(0, EditorGUILayout.IntField("Rows", arrayProperty.arraySize));
            if (size != arrayProperty.arraySize)
            {
                arrayProperty.arraySize = size;
                if (size < previousSize)
                {
                    TrimSheetCells(sheetState, size);
                    _isDocumentDirty = true;
                }
            }
        }

        private void DrawArrayGrid(
            SerializedProperty arrayProperty,
            List<TableColumn> columns,
            BetterScriptableSheetState sheetState)
        {
            bool rowStructureChanged = false;
            HashSet<string> formulaTargetKeys = GetFormulaTargetKeys(sheetState, arrayProperty.arraySize, columns);
            float gridWidth = CalculateGridWidth(columns);
            bool useHorizontalScroll = ShouldUseHorizontalGridScroll(gridWidth);
            if (useHorizontalScroll)
            {
                _tableScroll.y = 0f;
                float gridHeight = CalculateGridContentHeight(arrayProperty.arraySize, includesHorizontalScrollbar: true);
                _tableScroll = GUILayout.BeginScrollView(
                    _tableScroll,
                    false,
                    false,
                    GUI.skin.horizontalScrollbar,
                    GUIStyle.none,
                    GUILayout.Height(gridHeight));
                _tableScroll.y = 0f;
            }
            else
            {
                _tableScroll = Vector2.zero;
            }

            using (new EditorGUILayout.VerticalScope(GUILayout.MinWidth(gridWidth)))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("#", EditorStyles.boldLabel, GUILayout.Width(RowNumberWidth));
                    GUILayout.Space(RowButtonWidth * 2f + 6f);

                    for (int columnIndex = 0; columnIndex < columns.Count; columnIndex++)
                    {
                        TableColumn column = columns[columnIndex];
                        GUILayout.Label(GetColumnName(columnIndex), EditorStyles.boldLabel, GUILayout.Width(column.Width));
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(RowNumberWidth + RowButtonWidth * 2f + 6f);

                    foreach (TableColumn column in columns)
                    {
                        GUILayout.Label(column.HeaderLabel, EditorStyles.miniLabel, GUILayout.Width(column.Width));
                    }
                }

                if (arrayProperty.arraySize == 0)
                {
                    EditorGUILayout.HelpBox("No rows. Use Add Row to start entering data.", MessageType.None);
                }

                for (int row = 0; row < arrayProperty.arraySize; row++)
                {
                    SerializedProperty element = arrayProperty.GetArrayElementAtIndex(row);
                    bool stopDrawingRows = false;
                    using (new EditorGUILayout.HorizontalScope(GUILayout.Height(TableRowHeight)))
                    {
                        GUILayout.Label((row + 1).ToString(), GUILayout.Width(RowNumberWidth));

                        if (GUILayout.Button("+", GUILayout.Width(RowButtonWidth)))
                        {
                            InsertArrayRow(arrayProperty, row);
                            ShiftSheetCellsForInsert(sheetState, row);
                            rowStructureChanged = true;
                            stopDrawingRows = true;
                        }

                        using (new EditorGUI.DisabledScope(stopDrawingRows))
                        {
                            if (GUILayout.Button("-", GUILayout.Width(RowButtonWidth)))
                            {
                                DeleteArrayRow(arrayProperty, row);
                                ShiftSheetCellsForDelete(sheetState, row);
                                rowStructureChanged = true;
                                stopDrawingRows = true;
                            }
                        }

                        if (!stopDrawingRows)
                        {
                            DrawRowCells(arrayProperty, element, columns, row, formulaTargetKeys, sheetState);
                        }
                    }

                    if (stopDrawingRows)
                    {
                        break;
                    }
                }
            }

            if (useHorizontalScroll)
            {
                EditorGUILayout.EndScrollView();
            }

            if (rowStructureChanged)
            {
                _serializedObject.ApplyModifiedProperties();
                _isDocumentDirty = true;
                ApplyFormulas(arrayProperty, columns, sheetState, markDirty: true);
                RefreshArrayPropertyPaths();
                Repaint();
            }
        }

        private void DrawRowCells(
            SerializedProperty arrayProperty,
            SerializedProperty element,
            List<TableColumn> columns,
            int rowIndex,
            HashSet<string> formulaTargetKeys,
            BetterScriptableSheetState sheetState)
        {
            if (columns.Count == 0)
            {
                EditorGUILayout.PropertyField(element, GUIContent.none, true, GUILayout.Width(DefaultColumnWidth));
                return;
            }

            for (int columnIndex = 0; columnIndex < columns.Count; columnIndex++)
            {
                TableColumn column = columns[columnIndex];
                bool formulaControlled = formulaTargetKeys.Contains(GetCellKey(rowIndex, columnIndex));
                string controlName = GetCellControlName(arrayProperty.propertyPath, rowIndex, columnIndex);
                HandleCellNavigationKey(arrayProperty, columns, rowIndex, columnIndex, formulaTargetKeys, controlName);
                GUI.SetNextControlName(controlName);

                if (column.IsDesignField)
                {
                    using (new EditorGUI.DisabledScope(formulaControlled))
                    {
                        DrawDesignCell(sheetState, rowIndex, column);
                    }

                    FocusPendingCellControl(controlName);
                    continue;
                }

                SerializedProperty cell = GetCellProperty(element, column);
                if (cell == null)
                {
                    GUILayout.Label("-", GUILayout.Width(column.Width));
                    continue;
                }

                using (new EditorGUI.DisabledScope(formulaControlled))
                {
                    EditorGUILayout.PropertyField(cell, GUIContent.none, true, GUILayout.Width(column.Width));
                }

                FocusPendingCellControl(controlName);
            }
        }

        private void DrawDesignCell(BetterScriptableSheetState sheetState, int rowIndex, TableColumn column)
        {
            string currentValue = GetDesignCellValue(sheetState, rowIndex, column);
            EditorGUI.BeginChangeCheck();
            string nextValue = DrawDesignCellValue(column.TypeName, currentValue, column.Width);
            if (EditorGUI.EndChangeCheck())
            {
                SetDesignCellValue(sheetState, rowIndex, column, nextValue);
                _isDocumentDirty = true;
            }
        }

        private static string DrawDesignCellValue(string typeName, string value, float width)
        {
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
                    intValue = EditorGUILayout.IntField(intValue, GUILayout.Width(width));
                    return intValue.ToString(CultureInfo.InvariantCulture);
                case "long":
                case "ulong":
                    long longValue = ParseLong(value);
                    longValue = EditorGUILayout.LongField(longValue, GUILayout.Width(width));
                    return longValue.ToString(CultureInfo.InvariantCulture);
                case "float":
                    float floatValue = ParseFloat(value);
                    floatValue = EditorGUILayout.FloatField(floatValue, GUILayout.Width(width));
                    return floatValue.ToString(CultureInfo.InvariantCulture);
                case "double":
                case "decimal":
                    double doubleValue = ParseDouble(value);
                    doubleValue = EditorGUILayout.DoubleField(doubleValue, GUILayout.Width(width));
                    return doubleValue.ToString(CultureInfo.InvariantCulture);
                case "bool":
                case "boolean":
                    bool boolValue = ParseBool(value);
                    boolValue = EditorGUILayout.Toggle(boolValue, GUILayout.Width(width));
                    return boolValue.ToString();
                default:
                    return EditorGUILayout.TextField(value ?? string.Empty, GUILayout.Width(width));
            }
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
                        out string targetControlName))
                {
                    RequestCellFocus(targetControlName);
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
                        out string targetControlName))
                {
                    RequestCellFocus(targetControlName);
                    current.Use();
                }
            }
        }

        private void RequestCellFocus(string controlName)
        {
            _pendingCellFocusControlName = controlName;
            ClearTextFieldFocus();
            Repaint();
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
            _pendingCellFocusControlName = string.Empty;
        }

        private static bool TryFindEditableCellInRow(
            SerializedProperty arrayProperty,
            List<TableColumn> columns,
            int rowIndex,
            int startColumnIndex,
            int direction,
            HashSet<string> formulaTargetKeys,
            out string controlName)
        {
            controlName = string.Empty;
            for (int columnIndex = startColumnIndex;
                columnIndex >= 0 && columnIndex < columns.Count;
                columnIndex += direction)
            {
                if (IsEditableCell(arrayProperty, columns, rowIndex, columnIndex, formulaTargetKeys))
                {
                    controlName = GetCellControlName(arrayProperty.propertyPath, rowIndex, columnIndex);
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
            out string controlName)
        {
            controlName = string.Empty;
            for (int rowIndex = startRowIndex;
                rowIndex >= 0 && rowIndex < arrayProperty.arraySize;
                rowIndex += direction)
            {
                if (IsEditableCell(arrayProperty, columns, rowIndex, columnIndex, formulaTargetKeys))
                {
                    controlName = GetCellControlName(arrayProperty.propertyPath, rowIndex, columnIndex);
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
            return $"BSE.Cell.{arrayPropertyPath}.{rowIndex}.{columnIndex}";
        }

        private static string GetDesignCellValue(
            BetterScriptableSheetState sheetState,
            int rowIndex,
            TableColumn column)
        {
            BetterScriptableCellState cell = FindSheetCell(sheetState, rowIndex, column.SchemaName);
            return cell?.Value ?? string.Empty;
        }

        private static void SetDesignCellValue(
            BetterScriptableSheetState sheetState,
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
                sheetState.Cells = Array.Empty<BetterScriptableCellState>();
            }

            BetterScriptableCellState cell = FindSheetCell(sheetState, rowIndex, column.SchemaName);
            if (cell == null)
            {
                cell = new BetterScriptableCellState
                {
                    Row = rowIndex,
                    ColumnName = column.SchemaName
                };

                Array.Resize(ref sheetState.Cells, sheetState.Cells.Length + 1);
                sheetState.Cells[sheetState.Cells.Length - 1] = cell;
            }

            cell.Value = value ?? string.Empty;
        }

        private static BetterScriptableCellState FindSheetCell(
            BetterScriptableSheetState sheetState,
            int rowIndex,
            string columnName)
        {
            if (sheetState?.Cells == null || string.IsNullOrEmpty(columnName))
            {
                return null;
            }

            foreach (BetterScriptableCellState cell in sheetState.Cells)
            {
                if (cell == null)
                {
                    continue;
                }

                if (cell.Row == rowIndex && string.Equals(cell.ColumnName, columnName, StringComparison.Ordinal))
                {
                    return cell;
                }
            }

            return null;
        }

        private static void ClearSheetCells(BetterScriptableSheetState sheetState)
        {
            if (sheetState != null)
            {
                sheetState.Cells = Array.Empty<BetterScriptableCellState>();
            }
        }

        private static void TrimSheetCells(BetterScriptableSheetState sheetState, int rowCount)
        {
            if (sheetState?.Cells == null || sheetState.Cells.Length == 0)
            {
                return;
            }

            List<BetterScriptableCellState> cells = new List<BetterScriptableCellState>();
            foreach (BetterScriptableCellState cell in sheetState.Cells)
            {
                if (cell != null && cell.Row >= 0 && cell.Row < rowCount)
                {
                    cells.Add(cell);
                }
            }

            sheetState.Cells = cells.ToArray();
        }

        private static void ShiftSheetCellsForInsert(BetterScriptableSheetState sheetState, int rowIndex)
        {
            if (sheetState?.Cells == null)
            {
                return;
            }

            foreach (BetterScriptableCellState cell in sheetState.Cells)
            {
                if (cell != null && cell.Row >= rowIndex)
                {
                    cell.Row++;
                }
            }
        }

        private static void ShiftSheetCellsForDelete(BetterScriptableSheetState sheetState, int rowIndex)
        {
            if (sheetState?.Cells == null || sheetState.Cells.Length == 0)
            {
                return;
            }

            List<BetterScriptableCellState> cells = new List<BetterScriptableCellState>();
            foreach (BetterScriptableCellState cell in sheetState.Cells)
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

        private void DrawArrayTabs()
        {
            if (_arrayPropertyPaths.Count <= 1)
            {
                return;
            }

            string[] labels = new string[_arrayPropertyPaths.Count];
            for (int i = 0; i < _arrayPropertyPaths.Count; i++)
            {
                SerializedProperty property = _serializedObject.FindProperty(_arrayPropertyPaths[i]);
                labels[i] = property?.displayName ?? _arrayPropertyPaths[i];
            }

            EditorGUILayout.Space(4);
            _selectedArrayIndex = GUILayout.Toolbar(_selectedArrayIndex, labels);
        }

        private void DrawFooter()
        {
            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Import Asset", EditorStyles.toolbarButton, GUILayout.Width(96)))
                {
                    ImportFromLinkedAsset();
                }

                if (GUILayout.Button("Ping Asset", EditorStyles.toolbarButton, GUILayout.Width(88)))
                {
                    EditorGUIUtility.PingObject(_targetAsset);
                }

                GUILayout.FlexibleSpace();
                GUILayout.Label(_isDocumentDirty ? "Unsaved" : "Saved", EditorStyles.miniLabel);
                GUILayout.Space(8);
                GUILayout.Label(BetterScriptableDocumentIO.ResolveLinkedAssetPath(_document), EditorStyles.miniLabel);
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

            BetterScriptableDocumentSync.CaptureWorkingCopy(_document, _workingCopy);
            BetterScriptableDocumentSync.EnsureDocumentData(_document, _targetAsset);
            BetterScriptableDocumentIO.Write(_documentPath, _document);
            AssetDatabase.ImportAsset(_documentPath);

            if (exportToAsset)
            {
                BetterScriptableDocumentSync.ExportToLinkedAsset(_document, _targetAsset);
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
                "This replaces the source data inside the .betterscriptable document with the current linked .asset data.",
                "Import",
                "Cancel");

            if (!import)
            {
                return;
            }

            BetterScriptableDocumentSync.ImportFromLinkedAsset(_document, _targetAsset);
            BetterScriptableDocumentSync.EnsureDocumentData(_document, _targetAsset);
            RefreshDocumentSchemaFromGeneratedFactory(_document, _targetAsset);
            EnsureDocumentSheetsForSchema(_document);
            BetterScriptableDocumentIO.Write(_documentPath, _document);
            AssetDatabase.ImportAsset(_documentPath);
            RecreateWorkingCopy();
            _isDocumentDirty = false;
        }

        private void RecreateWorkingCopy()
        {
            DestroyWorkingCopy();
            _workingCopy = BetterScriptableDocumentSync.CreateWorkingCopy(_document, _targetAsset);
            _serializedObject = _workingCopy == null ? null : new SerializedObject(_workingCopy);
            RefreshArrayPropertyPaths();
        }

        private static bool RefreshDocumentSchemaFromGeneratedFactory(
            BetterScriptableDocument document,
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

            if (!BetterScriptableGeneratedClassLoader.TryLoadFromRuntimeScript(
                    runtimeScriptPath,
                    out BetterScriptableGenerationRequest request,
                    out _,
                    out _)
                || request?.Schema == null)
            {
                return false;
            }

            if (AreSchemasEqual(document.Schema, request.Schema))
            {
                return false;
            }

            document.Schema = request.Schema;
            return true;
        }

        private static bool EnsureDocumentSheetsForSchema(BetterScriptableDocument document)
        {
            if (document?.Schema?.Tables == null)
            {
                return false;
            }

            if (document.Sheets == null)
            {
                document.Sheets = Array.Empty<BetterScriptableSheetState>();
            }

            bool changed = false;
            List<BetterScriptableSheetState> sheets = new List<BetterScriptableSheetState>(document.Sheets);
            foreach (BetterScriptableSchemaTable table in document.Schema.Tables)
            {
                if (table == null || string.IsNullOrEmpty(table.FieldName))
                {
                    continue;
                }

                BetterScriptableSheetState sheet = FindSheetByName(sheets, table.FieldName);
                if (sheet == null)
                {
                    sheets.Add(new BetterScriptableSheetState
                    {
                        ArrayFieldName = table.FieldName,
                        Formulas = Array.Empty<BetterScriptableFormulaState>(),
                        Cells = Array.Empty<BetterScriptableCellState>()
                    });
                    changed = true;
                    continue;
                }

                if (sheet.Formulas == null)
                {
                    sheet.Formulas = Array.Empty<BetterScriptableFormulaState>();
                    changed = true;
                }

                if (sheet.Cells == null)
                {
                    sheet.Cells = Array.Empty<BetterScriptableCellState>();
                    changed = true;
                }
            }

            if (changed)
            {
                document.Sheets = sheets.ToArray();
            }

            return changed;
        }

        private static BetterScriptableSheetState FindSheetByName(
            List<BetterScriptableSheetState> sheets,
            string arrayFieldName)
        {
            foreach (BetterScriptableSheetState sheet in sheets)
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

        private static bool AreSchemasEqual(
            BetterScriptableDocumentSchema left,
            BetterScriptableDocumentSchema right)
        {
            return JsonUtility.ToJson(left ?? new BetterScriptableDocumentSchema())
                == JsonUtility.ToJson(right ?? new BetterScriptableDocumentSchema());
        }

        private bool ApplySelectedSheetFormulas()
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
            BetterScriptableSheetState sheetState = GetOrCreateSheetState(arrayProperty);
            return ApplyFormulas(arrayProperty, columns, sheetState, markDirty: false);
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
                BetterScriptableSheetState sheetState = GetOrCreateSheetState(arrayProperty);
                if (!TryApplyFormulaSet(arrayProperty, columns, sheetState, out _, out error))
                {
                    _formulaError = error;
                    return false;
                }
            }

            _serializedObject.ApplyModifiedProperties();
            _formulaError = string.Empty;
            return true;
        }

        private bool ApplyFormulas(
            SerializedProperty arrayProperty,
            List<TableColumn> columns,
            BetterScriptableSheetState sheetState,
            bool markDirty)
        {
            if (!TryApplyFormulaSet(arrayProperty, columns, sheetState, out bool changed, out string error))
            {
                _formulaError = error;
                return false;
            }

            _formulaError = string.Empty;
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

        private static bool TryApplyFormulaSet(
            SerializedProperty arrayProperty,
            List<TableColumn> columns,
            BetterScriptableSheetState sheetState,
            out bool changed,
            out string error)
        {
            changed = false;
            error = string.Empty;
            if (sheetState?.Formulas == null || sheetState.Formulas.Length == 0)
            {
                return true;
            }

            changed = BetterScriptableFormulaEngine.TryApply(
                arrayProperty,
                GetColumnPropertyNames(columns),
                sheetState.Formulas,
                new DesignFormulaCellStore(sheetState, columns),
                out error);
            return string.IsNullOrEmpty(error);
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

        private BetterScriptableSheetState GetOrCreateSheetState(SerializedProperty arrayProperty)
        {
            if (_document.Sheets == null)
            {
                _document.Sheets = Array.Empty<BetterScriptableSheetState>();
            }

            string sheetKey = GetSheetKey(arrayProperty);
            for (int i = 0; i < _document.Sheets.Length; i++)
            {
                BetterScriptableSheetState sheet = _document.Sheets[i];
                if (sheet == null)
                {
                    continue;
                }

                if (IsMatchingSheet(sheet, arrayProperty, sheetKey))
                {
                    if (sheet.Formulas == null)
                    {
                        sheet.Formulas = Array.Empty<BetterScriptableFormulaState>();
                    }

                    if (sheet.Cells == null)
                    {
                        sheet.Cells = Array.Empty<BetterScriptableCellState>();
                    }

                    return sheet;
                }
            }

            BetterScriptableSheetState newSheet = new BetterScriptableSheetState
            {
                ArrayFieldName = sheetKey,
                Formulas = Array.Empty<BetterScriptableFormulaState>(),
                Cells = Array.Empty<BetterScriptableCellState>()
            };

            Array.Resize(ref _document.Sheets, _document.Sheets.Length + 1);
            _document.Sheets[_document.Sheets.Length - 1] = newSheet;
            _isDocumentDirty = true;
            return newSheet;
        }

        private static bool IsMatchingSheet(
            BetterScriptableSheetState sheet,
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
            return BetterScriptableNameUtility.ToPascalCase(arrayProperty.name.TrimStart('_'));
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

        private List<TableColumn> GetColumns(SerializedProperty arrayProperty)
        {
            BetterScriptableSchemaTable schemaTable = FindSchemaTable(arrayProperty);
            if (schemaTable?.Fields != null && schemaTable.Fields.Length > 0)
            {
                return GetColumnsFromSchema(schemaTable);
            }

            FieldInfo arrayField = FindField(_targetAsset.GetType(), arrayProperty.propertyPath);
            Type elementType = GetElementType(arrayField?.FieldType);
            if (arrayProperty.arraySize > 0)
            {
                return GetColumnsFromFirstElement(arrayProperty.GetArrayElementAtIndex(0), elementType);
            }

            return GetColumnsFromReflection(elementType);
        }

        private BetterScriptableSchemaTable FindSchemaTable(SerializedProperty arrayProperty)
        {
            if (_document?.Schema?.Tables == null || arrayProperty == null)
            {
                return null;
            }

            string sheetKey = GetSheetKey(arrayProperty);
            foreach (BetterScriptableSchemaTable table in _document.Schema.Tables)
            {
                if (table == null || string.IsNullOrEmpty(table.FieldName))
                {
                    continue;
                }

                string serializedFieldName = BetterScriptableNameUtility.ToSerializedFieldName(table.FieldName);
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

        private static List<TableColumn> GetColumnsFromSchema(BetterScriptableSchemaTable table)
        {
            List<TableColumn> columns = new List<TableColumn>();
            foreach (BetterScriptableSchemaField field in table.Fields)
            {
                if (field == null || string.IsNullOrWhiteSpace(field.Name))
                {
                    continue;
                }

                string propertyName = BetterScriptableNameUtility.ToSerializedFieldName(field.Name);
                string displayName = ObjectNames.NicifyVariableName(field.Name);
                columns.Add(CreateTableColumn(
                    propertyName,
                    displayName,
                    field.TypeName,
                    field.Name,
                    field.IsDesignField));
            }

            return columns;
        }

        private static List<TableColumn> GetColumnsFromFirstElement(SerializedProperty element, Type elementType)
        {
            List<TableColumn> columns = new List<TableColumn>();
            if (element.propertyType != SerializedPropertyType.Generic)
            {
                string typeName = GetFriendlyTypeName(elementType) ?? GetSerializedPropertyTypeName(element);
                columns.Add(CreateTableColumn(string.Empty, element.displayName, typeName));
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
                columns.Add(CreateTableColumn(child.name, child.displayName, typeName));
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
                columns.Add(CreateTableColumn(field.Name, displayName, GetFriendlyTypeName(field.FieldType)));
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
            bool isDesignField = false)
        {
            string typeLabel = isDesignField && !string.IsNullOrEmpty(typeName)
                ? $"{typeName}, design"
                : typeName;
            string headerLabel = string.IsNullOrEmpty(typeLabel) ? displayName : $"{displayName}({typeLabel})";
            return new TableColumn(
                propertyName,
                displayName,
                typeName,
                headerLabel,
                CalculateColumnWidth(headerLabel),
                schemaName,
                isDesignField);
        }

        private static float CalculateGridWidth(List<TableColumn> columns)
        {
            float width = RowNumberWidth + RowButtonWidth * 2f + 6f;
            foreach (TableColumn column in columns)
            {
                width += column.Width;
            }

            return width;
        }

        private static float CalculateColumnWidth(string label)
        {
            return Mathf.Clamp(EditorStyles.label.CalcSize(new GUIContent(label)).x + 48f, MinimumColumnWidth, MaximumColumnWidth);
        }

        private static float CalculateGridContentHeight(int rowCount, bool includesHorizontalScrollbar)
        {
            float height = TableHeaderHeight + Mathf.Max(rowCount, 1) * TableRowHeight + TableLayoutPadding;
            int verticalItemCount = 2 + Mathf.Max(rowCount, 1);
            height += Mathf.Max(0, verticalItemCount - 1) * EditorGUIUtility.standardVerticalSpacing;
            return includesHorizontalScrollbar ? height + HorizontalScrollbarPadding : height;
        }

        private static bool ShouldUseHorizontalGridScroll(float gridWidth)
        {
            float availableWidth = Mathf.Max(0f, EditorGUIUtility.currentViewWidth - 32f);
            return gridWidth > availableWidth;
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

        private static List<BetterScriptableFormulaState> GetFormulaList(BetterScriptableSheetState sheetState)
        {
            if (sheetState.Formulas == null)
            {
                sheetState.Formulas = Array.Empty<BetterScriptableFormulaState>();
            }

            return new List<BetterScriptableFormulaState>(sheetState.Formulas);
        }

        private static bool EnsureFormulaIds(BetterScriptableSheetState sheetState)
        {
            if (sheetState.Formulas == null)
            {
                sheetState.Formulas = Array.Empty<BetterScriptableFormulaState>();
                return false;
            }

            bool changed = false;
            for (int i = 0; i < sheetState.Formulas.Length; i++)
            {
                BetterScriptableFormulaState formula = sheetState.Formulas[i];
                if (formula == null)
                {
                    formula = new BetterScriptableFormulaState();
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

        private string GetFormulaDraft(BetterScriptableFormulaState formula)
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

        private bool CommitFormulaDraftsIfFocusLost(BetterScriptableSheetState sheetState)
        {
            if (Event.current.type != EventType.Repaint || IsFormulaTextFocused())
            {
                return false;
            }

            return CommitFormulaDrafts(sheetState);
        }

        private bool CommitFormulaDrafts(BetterScriptableSheetState sheetState)
        {
            if (sheetState?.Formulas == null)
            {
                return false;
            }

            bool changed = false;
            foreach (BetterScriptableFormulaState formula in sheetState.Formulas)
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

            foreach (BetterScriptableSheetState sheet in _document.Sheets)
            {
                changed |= CommitFormulaDrafts(sheet);
            }

            if (changed)
            {
                _isDocumentDirty = true;
            }

            return changed;
        }

        private static string GetFormulaControlName(BetterScriptableFormulaState formula)
        {
            string id = string.IsNullOrEmpty(formula?.Id) ? "Unknown" : formula.Id;
            return $"BSE.Formula.{id}";
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
                && focusedControl.StartsWith("BSE.Formula.", StringComparison.Ordinal);
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

        private static HashSet<string> GetFormulaTargetKeys(
            BetterScriptableSheetState sheetState,
            int rowCount,
            List<TableColumn> columns)
        {
            HashSet<string> keys = new HashSet<string>();
            if (sheetState?.Formulas == null)
            {
                return keys;
            }

            List<string> columnPropertyNames = GetColumnPropertyNames(columns);
            foreach (BetterScriptableFormulaState formula in sheetState.Formulas)
            {
                if (formula == null
                    || !formula.Enabled
                    || !BetterScriptableFormulaEngine.TryCollectTargetCells(
                        formula.Expression,
                        columnPropertyNames,
                        rowCount,
                        out List<BetterScriptableCellAddress> targets))
                {
                    continue;
                }

                foreach (BetterScriptableCellAddress target in targets)
                {
                    keys.Add(GetCellKey(target.Row, target.Column));
                }
            }

            return keys;
        }

        private static string GetCellKey(int rowIndex, int columnIndex)
        {
            return rowIndex + ":" + columnIndex;
        }

        private sealed class DesignFormulaCellStore : IBetterScriptableFormulaCellStore
        {
            private readonly BetterScriptableSheetState _sheetState;
            private readonly List<TableColumn> _columns;

            public DesignFormulaCellStore(BetterScriptableSheetState sheetState, List<TableColumn> columns)
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
                BetterScriptableCellAddress address,
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
                BetterScriptableCellAddress address,
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

        private readonly struct TableColumn
        {
            public readonly string PropertyName;
            public readonly string DisplayName;
            public readonly string TypeName;
            public readonly string HeaderLabel;
            public readonly float Width;
            public readonly string SchemaName;
            public readonly bool IsDesignField;

            public TableColumn(
                string propertyName,
                string displayName,
                string typeName,
                string headerLabel,
                float width,
                string schemaName,
                bool isDesignField)
            {
                PropertyName = propertyName;
                DisplayName = displayName;
                TypeName = typeName;
                HeaderLabel = headerLabel;
                Width = width;
                SchemaName = schemaName;
                IsDesignField = isDesignField;
            }
        }
    }
}

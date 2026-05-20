using System;
using System.Collections.Generic;
using System.Globalization;
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
        private const float FormulaRowHeight = 22f;
        private const float TableRowHeight = 24f;
        private const float TableHeaderHeight = TableRowHeight * 2f;
        private const float HorizontalScrollbarHeight = 16f;
        private const float HorizontalWheelScrollSpeed = 24f;
        private const float TableLayoutPadding = 6f;
        private const float CellControlHorizontalPadding = 4f;
        private const string CellControlPrefix = "SpreadAsset Editor.Cell.";

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
        private bool _isDocumentDirty;
        private string _formulaError;
        private string _pendingFormulaFocusControlName;
        private string _pendingCellFocusControlName;
        private float _tableRowViewportHeight;
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
            _pendingFormulaFocusControlName = string.Empty;
            _pendingCellFocusControlName = string.Empty;
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
            List<TableColumn> columns = GetColumns(arrayProperty);
            SpreadAssetSheetState sheetState = GetOrCreateSheetState(arrayProperty);
            DrawFormulaPanel(arrayProperty, columns, sheetState);
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
            SpreadAssetSheetState sheetState = GetOrCreateSheetState(arrayProperty);
            DrawArrayToolbar(arrayProperty, sheetState);
            DrawArrayGrid(arrayProperty, columns, sheetState);
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

        private void DrawArrayGrid(
            SerializedProperty arrayProperty,
            List<TableColumn> columns,
            SpreadAssetSheetState sheetState)
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
                useHorizontalScroll);
        }

        private void DrawFrozenGridHeader(
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
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(frozenWidth), GUILayout.Height(headerHeight)))
            {
                using (new EditorGUILayout.HorizontalScope(GUILayout.Height(TableRowHeight)))
                {
                    GUILayout.Label("#", EditorStyles.boldLabel, GUILayout.Width(RowNumberWidth));
                    GUILayout.Space(RowButtonWidth * 2f + 6f);
                }

                GUILayout.Space(EditorGUIUtility.standardVerticalSpacing);

                using (new EditorGUILayout.HorizontalScope(GUILayout.Height(TableRowHeight)))
                {
                    GUILayout.Space(frozenWidth);
                }
            }
        }

        private void DrawColumnHeaderCells(
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
                    DrawColumnHeaderContent(columns, dataWidth, headerHeight, focusedColumnIndex);
                });
            }
            else
            {
                _tableScroll = Vector2.zero;
                DrawColumnHeaderContent(columns, dataWidth, headerHeight, focusedColumnIndex);
            }
        }

        private static void DrawColumnHeaderContent(
            List<TableColumn> columns,
            float dataWidth,
            float headerHeight,
            int focusedColumnIndex)
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(dataWidth), GUILayout.Height(headerHeight)))
            {
                using (new EditorGUILayout.HorizontalScope(GUILayout.Height(TableRowHeight)))
                {
                    for (int columnIndex = 0; columnIndex < columns.Count; columnIndex++)
                    {
                        TableColumn column = columns[columnIndex];
                        Rect headerRect = GUILayoutUtility.GetRect(
                            column.Width,
                            TableRowHeight,
                            GUILayout.Width(column.Width),
                            GUILayout.Height(TableRowHeight));
                        DrawFocusedColumnHeaderBackground(headerRect, columnIndex == focusedColumnIndex);
                        GUI.Label(headerRect, GetColumnName(columnIndex), EditorStyles.boldLabel);
                    }
                }

                using (new EditorGUILayout.HorizontalScope(GUILayout.Height(TableRowHeight)))
                {
                    for (int columnIndex = 0; columnIndex < columns.Count; columnIndex++)
                    {
                        TableColumn column = columns[columnIndex];
                        Rect headerRect = GUILayoutUtility.GetRect(
                            column.Width,
                            TableRowHeight,
                            GUILayout.Width(column.Width),
                            GUILayout.Height(TableRowHeight));
                        DrawFocusedColumnHeaderBackground(headerRect, columnIndex == focusedColumnIndex);
                        GUI.Label(headerRect, column.HeaderLabel, EditorStyles.miniLabel);
                    }
                }
            }
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
            bool useHorizontalScroll)
        {
            float rowContentHeight = CalculateGridRowContentHeight(arrayProperty.arraySize);
            float dataHeight = rowContentHeight;

            Rect scrollViewRect = GUILayoutUtility.GetRect(
                0f,
                100000f,
                0f,
                100000f,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true));
            _tableRowViewportHeight = scrollViewRect.height;
            HandleHorizontalScrollWheel(scrollViewRect, dataWidth, dataViewportWidth, useHorizontalScroll);

            _propertyScroll.x = 0f;
            Rect contentRect = new Rect(0f, 0f, frozenWidth + dataViewportWidth, dataHeight);
            _propertyScroll = GUI.BeginScrollView(
                scrollViewRect,
                _propertyScroll,
                contentRect,
                false,
                false,
                GUIStyle.none,
                GUI.skin.verticalScrollbar);
            _propertyScroll.x = 0f;
            VisibleRowRange visibleRows = CalculateVisibleRowRange(
                arrayProperty.arraySize,
                _propertyScroll.y,
                scrollViewRect.height);

            GUILayout.BeginArea(contentRect);
            using (new EditorGUILayout.HorizontalScope(GUILayout.Height(dataHeight)))
            {
                DrawFrozenRowHeaders(arrayProperty, sheetState, frozenWidth, rowContentHeight, visibleRows);
                DrawScrollableRowCells(
                    arrayProperty,
                    columns,
                    formulaTargetKeys,
                    sheetState,
                    dataWidth,
                    dataViewportWidth,
                    dataHeight,
                    useHorizontalScroll,
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
                float y = CalculateGridRowY(row);
                Rect rowHeaderRect = new Rect(0f, y, frozenWidth, TableRowHeight);
                DrawFocusedRowHeaderBackground(rowHeaderRect, hasFocusedCell && row == focusedRowIndex);
                GUI.Label(new Rect(0f, y, RowNumberWidth, TableRowHeight), (row + 1).ToString());

                if (GUI.Button(new Rect(RowNumberWidth, y, RowButtonWidth, TableRowHeight), "+"))
                {
                    InsertArrayRow(arrayProperty, row);
                    ShiftSheetCellsForInsert(sheetState, row);
                    CompleteRowStructureChange(arrayProperty, columnsChanged: false, sheetState);
                }

                if (GUI.Button(new Rect(RowNumberWidth + RowButtonWidth, y, RowButtonWidth, TableRowHeight), "-"))
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
                    () => DrawDataRows(arrayProperty, columns, formulaTargetKeys, sheetState, dataWidth, visibleRows));
                return;
            }

            _tableScroll = Vector2.zero;
            Rect tableRect = GUILayoutUtility.GetRect(
                dataWidth,
                dataHeight,
                GUILayout.Width(dataWidth),
                GUILayout.Height(dataHeight));
            GUI.BeginGroup(tableRect);
            DrawDataRows(arrayProperty, columns, formulaTargetKeys, sheetState, dataWidth, visibleRows);
            GUI.EndGroup();
        }

        private void DrawDataRows(
            SerializedProperty arrayProperty,
            List<TableColumn> columns,
            HashSet<string> formulaTargetKeys,
            SpreadAssetSheetState sheetState,
            float dataWidth,
            VisibleRowRange visibleRows)
        {
            for (int row = visibleRows.StartIndex; row < visibleRows.EndIndex; row++)
            {
                SerializedProperty element = arrayProperty.GetArrayElementAtIndex(row);
                DrawRowCells(
                    new Rect(0f, CalculateGridRowY(row), dataWidth, TableRowHeight),
                    arrayProperty,
                    element,
                    columns,
                    row,
                    formulaTargetKeys,
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
            SpreadAssetSheetState sheetState)
        {
            bool hasFocusedCell = TryGetFocusedCellPosition(
                arrayProperty.propertyPath,
                out int focusedRowIndex,
                out int focusedColumnIndex);

            if (columns.Count == 0)
            {
                Rect fallbackCellRect = new Rect(rowRect.x, rowRect.y, DefaultColumnWidth, TableRowHeight);
                DrawFocusedCellBackground(fallbackCellRect, hasFocusedCell && rowIndex == focusedRowIndex, false);
                EditorGUI.PropertyField(
                    GetCenteredCellControlRect(fallbackCellRect),
                    element,
                    GUIContent.none,
                    true);
                DrawCellTooltip(fallbackCellRect, rowIndex, 0);
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
                    TableRowHeight);
                Rect cellRect = GetCenteredCellControlRect(cellAreaRect);
                bool isFocusedRow = hasFocusedCell && rowIndex == focusedRowIndex;
                bool isFocusedColumn = hasFocusedCell && columnIndex == focusedColumnIndex;
                DrawFocusedCellBackground(cellAreaRect, isFocusedRow, isFocusedColumn);

                if (column.IsDesignField)
                {
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
                    GUI.Label(cellRect, "-");
                    DrawCellTooltip(cellAreaRect, rowIndex, columnIndex);
                    x += column.Width;
                    continue;
                }

                using (new EditorGUI.DisabledScope(formulaControlled))
                {
                    EditorGUI.PropertyField(cellRect, cell, GUIContent.none, true);
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
                cellAreaRect.y + Mathf.Max(0f, (TableRowHeight - EditorGUIUtility.singleLineHeight) * 0.5f),
                Mathf.Max(1f, cellAreaRect.width - horizontalPadding * 2f),
                EditorGUIUtility.singleLineHeight);
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
            _pendingCellFocusControlName = controlName;
            ClearTextFieldFocus();
            Repaint();
        }

        private void RequestCellFocus(string controlName, int rowIndex)
        {
            _pendingCellFocusControlName = controlName;
            ScrollRowIntoView(rowIndex);
            ClearTextFieldFocus();
            Repaint();
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

        private static bool TryGetFocusedCellPosition(
            string arrayPropertyPath,
            out int rowIndex,
            out int columnIndex)
        {
            rowIndex = -1;
            columnIndex = -1;

            string focusedControl = GUI.GetNameOfFocusedControl();
            if (string.IsNullOrEmpty(focusedControl))
            {
                return false;
            }

            string prefix = CellControlPrefix + arrayPropertyPath + ".";
            if (!focusedControl.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            string suffix = focusedControl.Substring(prefix.Length);
            int separatorIndex = suffix.IndexOf('.');
            if (separatorIndex <= 0 || separatorIndex >= suffix.Length - 1)
            {
                return false;
            }

            return int.TryParse(
                    suffix.Substring(0, separatorIndex),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out rowIndex)
                && int.TryParse(
                    suffix.Substring(separatorIndex + 1),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out columnIndex);
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

            SpreadAssetDocumentSync.CaptureWorkingCopy(_document, _workingCopy);
            SpreadAssetDocumentSync.EnsureDocumentData(_document, _targetAsset);
            SpreadAssetDocumentIO.Write(_documentPath, _document);
            AssetDatabase.ImportAsset(_documentPath);

            if (exportToAsset)
            {
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
                    column.IsDesignField ? "1" : "0"));
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

                    return sheet;
                }
            }

            SpreadAssetSheetState newSheet = new SpreadAssetSheetState
            {
                ArrayFieldName = sheetKey,
                Formulas = Array.Empty<SpreadAssetFormulaState>(),
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
            SpreadAssetSchemaTable schemaTable = FindSchemaTable(arrayProperty);
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

        private SpreadAssetSchemaTable FindSchemaTable(SerializedProperty arrayProperty)
        {
            if (_document?.Schema?.Tables == null || arrayProperty == null)
            {
                return null;
            }

            string sheetKey = GetSheetKey(arrayProperty);
            foreach (SpreadAssetSchemaTable table in _document.Schema.Tables)
            {
                if (table == null || string.IsNullOrEmpty(table.FieldName))
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
            string fieldId = "")
        {
            string displayTypeName = GetColumnTypeDisplayName(typeName);
            string typeLabel = isDesignField && !string.IsNullOrEmpty(displayTypeName)
                ? $"{displayTypeName}, design"
                : displayTypeName;
            string headerLabel = string.IsNullOrEmpty(typeLabel) ? displayName : $"{displayName}({typeLabel})";
            return new TableColumn(
                propertyName,
                displayName,
                typeName,
                headerLabel,
                CalculateColumnWidth(headerLabel),
                schemaName,
                isDesignField,
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
            if (columns.Count == 0)
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

        private static float CalculateColumnWidth(string label)
        {
            return Mathf.Clamp(EditorStyles.label.CalcSize(new GUIContent(label)).x + 48f, MinimumColumnWidth, MaximumColumnWidth);
        }

        private static float CalculateGridHeaderHeight()
        {
            return TableHeaderHeight + EditorGUIUtility.standardVerticalSpacing;
        }

        private static VisibleRowRange CalculateVisibleRowRange(int rowCount, float scrollY, float viewportHeight)
        {
            if (rowCount <= 0)
            {
                return new VisibleRowRange(0, 0);
            }

            float rowPitch = CalculateGridRowPitch();
            int startIndex = Mathf.FloorToInt(Mathf.Max(0f, scrollY) / rowPitch) - 1;
            startIndex = Mathf.Clamp(startIndex, 0, rowCount - 1);

            int visibleCount = Mathf.CeilToInt(Mathf.Max(TableRowHeight, viewportHeight) / rowPitch) + 3;
            int endIndex = Mathf.Clamp(startIndex + visibleCount, startIndex + 1, rowCount);
            return new VisibleRowRange(startIndex, endIndex);
        }

        private static float CalculateGridRowContentHeight(int rowCount)
        {
            int visibleRows = Mathf.Max(rowCount, 1);
            float height = visibleRows * TableRowHeight + TableLayoutPadding;
            height += Mathf.Max(0, visibleRows - 1) * EditorGUIUtility.standardVerticalSpacing;
            return height;
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
            public readonly string FieldId;

            public TableColumn(
                string propertyName,
                string displayName,
                string typeName,
                string headerLabel,
                float width,
                string schemaName,
                bool isDesignField,
                string fieldId)
            {
                PropertyName = propertyName;
                DisplayName = displayName;
                TypeName = typeName;
                HeaderLabel = headerLabel;
                Width = width;
                SchemaName = schemaName;
                IsDesignField = isDesignField;
                FieldId = fieldId ?? string.Empty;
            }
        }
    }
}

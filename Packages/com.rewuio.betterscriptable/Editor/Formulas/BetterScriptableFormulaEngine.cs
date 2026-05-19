using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;

namespace BetterScriptable.Editor
{
    internal static class BetterScriptableFormulaEngine
    {
        public static bool TryParseTarget(string formula, out BetterScriptableCellAddress target)
        {
            target = default;
            if (string.IsNullOrWhiteSpace(formula))
            {
                return false;
            }

            int equalsIndex = formula.IndexOf('=');
            string targetText = equalsIndex >= 0 ? formula.Substring(0, equalsIndex).Trim() : formula.Trim();
            return TryParseCellAddress(targetText, out target);
        }

        public static bool TryCollectTargetCells(
            string formula,
            IReadOnlyList<string> columnPropertyNames,
            int rowCount,
            out List<BetterScriptableCellAddress> targets)
        {
            targets = new List<BetterScriptableCellAddress>();
            ColumnLookup columnLookup = new ColumnLookup(columnPropertyNames);
            if (!TrySplitFormula(formula, columnLookup, out FormulaTarget target, out _))
            {
                return false;
            }

            if (target.IsColumn)
            {
                if (!columnLookup.IsColumnInRange(target.Column))
                {
                    return false;
                }

                for (int row = 0; row < rowCount; row++)
                {
                    targets.Add(new BetterScriptableCellAddress(row, target.Column));
                }

                return true;
            }

            if (target.Cell.Row < 0
                || target.Cell.Row >= rowCount
                || !columnLookup.IsColumnInRange(target.Cell.Column))
            {
                return false;
            }

            targets.Add(target.Cell);
            return true;
        }

        public static bool TryApply(
            SerializedProperty arrayProperty,
            IReadOnlyList<string> columnPropertyNames,
            IReadOnlyList<BetterScriptableFormulaState> formulas,
            out string error)
        {
            return TryApply(arrayProperty, columnPropertyNames, formulas, null, out error);
        }

        public static bool TryApply(
            SerializedProperty arrayProperty,
            IReadOnlyList<string> columnPropertyNames,
            IReadOnlyList<BetterScriptableFormulaState> formulas,
            IBetterScriptableFormulaCellStore cellStore,
            out string error)
        {
            if (arrayProperty == null)
            {
                error = string.Empty;
                return false;
            }

            if (!TryCompile(columnPropertyNames, formulas, arrayProperty.arraySize, out CompiledFormulaSet compiledFormulas, out error))
            {
                return false;
            }

            return TryApplyCompiled(arrayProperty, columnPropertyNames, compiledFormulas, cellStore, out error);
        }

        public static bool TryCompile(
            IReadOnlyList<string> columnPropertyNames,
            IReadOnlyList<BetterScriptableFormulaState> formulas,
            int rowCount,
            out CompiledFormulaSet compiledFormulas,
            out string error)
        {
            compiledFormulas = CompiledFormulaSet.Empty;
            error = string.Empty;
            if (formulas == null || formulas.Count == 0)
            {
                return true;
            }

            ColumnLookup columnLookup = new ColumnLookup(columnPropertyNames);
            List<ParsedFormula> parsedFormulas = new List<ParsedFormula>();
            HashSet<string> definedCellTargets = new HashSet<string>();
            HashSet<int> definedColumnTargets = new HashSet<int>();
            HashSet<string> targetKeys = new HashSet<string>();

            for (int i = 0; i < formulas.Count; i++)
            {
                BetterScriptableFormulaState formula = formulas[i];
                if (formula == null || !formula.Enabled || string.IsNullOrWhiteSpace(formula.Expression))
                {
                    continue;
                }

                if (!TrySplitFormula(formula.Expression, columnLookup, out FormulaTarget target, out string expression))
                {
                    error = $"Formula {i + 1}: expected format like C = A + B or C1 = A1 + B1.";
                    return false;
                }

                if (target.IsColumn)
                {
                    if (!columnLookup.IsColumnInRange(target.Column))
                    {
                        error = $"Formula {i + 1}: target column {FormatColumnName(target.Column)} is outside the table.";
                        return false;
                    }

                    if (!definedColumnTargets.Add(target.Column))
                    {
                        error = $"Formula {i + 1}: target column {FormatColumnName(target.Column)} is already defined by another formula.";
                        return false;
                    }

                    parsedFormulas.Add(new ParsedFormula(i + 1, target, expression));
                    for (int row = 0; row < rowCount; row++)
                    {
                        targetKeys.Add(GetTargetKey(new BetterScriptableCellAddress(row, target.Column)));
                    }

                    continue;
                }

                if (!IsCellInRange(rowCount, columnLookup.Count, target.Cell))
                {
                    error = $"Formula {i + 1}: target cell {target.Cell} is outside the table.";
                    return false;
                }

                string targetKey = GetTargetKey(target.Cell);
                if (!definedCellTargets.Add(targetKey))
                {
                    error = $"Formula {i + 1}: target cell {target.Cell} is already defined by another formula.";
                    return false;
                }

                targetKeys.Add(targetKey);
                parsedFormulas.Add(new ParsedFormula(i + 1, target, expression));
            }

            compiledFormulas = new CompiledFormulaSet(
                columnLookup,
                parsedFormulas,
                definedCellTargets,
                targetKeys);
            return true;
        }

        public static bool TryApplyCompiled(
            SerializedProperty arrayProperty,
            IReadOnlyList<string> columnPropertyNames,
            CompiledFormulaSet compiledFormulas,
            IBetterScriptableFormulaCellStore cellStore,
            out string error)
        {
            error = string.Empty;
            if (arrayProperty == null || compiledFormulas == null || compiledFormulas.IsEmpty)
            {
                return false;
            }

            bool changed = false;
            foreach (ParsedFormula formula in compiledFormulas.Formulas)
            {
                if (formula.Target.IsColumn)
                {
                    for (int row = 0; row < arrayProperty.arraySize; row++)
                    {
                        BetterScriptableCellAddress cell = new BetterScriptableCellAddress(row, formula.Target.Column);
                        if (compiledFormulas.IsSpecificCellTarget(cell))
                        {
                            continue;
                        }

                        if (!TryApplyCellFormula(
                                arrayProperty,
                                columnPropertyNames,
                                compiledFormulas.ColumnLookup,
                                cellStore,
                                formula.Expression,
                                cell,
                                out bool cellChanged,
                                out string formulaError))
                        {
                            error = $"Formula {formula.SourceIndex}: {formulaError}";
                            return changed;
                        }

                        changed |= cellChanged;
                    }

                    continue;
                }

                if (!TryApplyCellFormula(
                        arrayProperty,
                        columnPropertyNames,
                        compiledFormulas.ColumnLookup,
                        cellStore,
                        formula.Expression,
                        formula.Target.Cell,
                        out bool changedByFormula,
                        out string cellFormulaError))
                {
                    error = $"Formula {formula.SourceIndex}: {cellFormulaError}";
                    return changed;
                }

                changed |= changedByFormula;
            }

            return changed;
        }

        public static bool TryParseCellAddress(string text, out BetterScriptableCellAddress address)
        {
            address = default;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string trimmed = text.Trim().ToUpperInvariant();
            int index = 0;
            int column = 0;

            while (index < trimmed.Length && trimmed[index] >= 'A' && trimmed[index] <= 'Z')
            {
                column = column * 26 + trimmed[index] - 'A' + 1;
                index++;
            }

            if (column == 0 || index >= trimmed.Length)
            {
                return false;
            }

            int row = 0;
            while (index < trimmed.Length && char.IsDigit(trimmed[index]))
            {
                row = row * 10 + trimmed[index] - '0';
                index++;
            }

            if (row <= 0 || index != trimmed.Length)
            {
                return false;
            }

            address = new BetterScriptableCellAddress(row - 1, column - 1);
            return true;
        }

        private static bool TrySplitFormula(
            string formula,
            ColumnLookup columnLookup,
            out FormulaTarget target,
            out string expression)
        {
            target = default;
            expression = string.Empty;

            int equalsIndex = formula.IndexOf('=');
            if (equalsIndex < 0)
            {
                return false;
            }

            string targetText = formula.Substring(0, equalsIndex).Trim();
            expression = formula.Substring(equalsIndex + 1).Trim();
            return !string.IsNullOrWhiteSpace(expression)
                && TryParseFormulaTarget(targetText, columnLookup, out target);
        }

        private static bool TryParseFormulaTarget(
            string targetText,
            ColumnLookup columnLookup,
            out FormulaTarget target)
        {
            target = default;
            if (TryParseCellAddress(targetText, out BetterScriptableCellAddress cellAddress))
            {
                target = FormulaTarget.ForCell(cellAddress);
                return true;
            }

            if (columnLookup.TryGetColumn(targetText, out int column))
            {
                target = FormulaTarget.ForColumn(column);
                return true;
            }

            return false;
        }

        private static bool IsCellInRange(
            SerializedProperty arrayProperty,
            IReadOnlyList<string> columnPropertyNames,
            BetterScriptableCellAddress address)
        {
            return IsCellInRange(arrayProperty.arraySize, columnPropertyNames.Count, address);
        }

        private static bool IsCellInRange(int rowCount, int columnCount, BetterScriptableCellAddress address)
        {
            return address.Row >= 0
                && address.Row < rowCount
                && address.Column >= 0
                && address.Column < columnCount;
        }

        private static SerializedProperty GetCellProperty(
            SerializedProperty arrayProperty,
            IReadOnlyList<string> columnPropertyNames,
            BetterScriptableCellAddress address)
        {
            SerializedProperty element = arrayProperty.GetArrayElementAtIndex(address.Row);
            string propertyName = columnPropertyNames[address.Column];
            return string.IsNullOrEmpty(propertyName) ? element : element.FindPropertyRelative(propertyName);
        }

        private static bool TryGetFormulaValue(
            SerializedProperty arrayProperty,
            IReadOnlyList<string> columnPropertyNames,
            IBetterScriptableFormulaCellStore cellStore,
            BetterScriptableCellAddress address,
            HashSet<string> applyingTargets,
            out FormulaValue value,
            out string error)
        {
            value = default;
            error = string.Empty;

            if (!IsCellInRange(arrayProperty, columnPropertyNames, address))
            {
                error = $"cell {address} is outside the table.";
                return false;
            }

            if (applyingTargets.Contains(GetTargetKey(address)))
            {
                error = $"cell {address} is currently being calculated.";
                return false;
            }

            if (cellStore != null && cellStore.IsVirtualColumn(address.Column))
            {
                if (!cellStore.TryGetCellValue(address, out string cellValue, out string typeName, out error))
                {
                    return false;
                }

                return TryGetFormulaValue(cellValue, typeName, out value, out error);
            }

            SerializedProperty property = GetCellProperty(arrayProperty, columnPropertyNames, address);
            if (property == null)
            {
                error = $"cell {address} could not be found.";
                return false;
            }

            return TryGetFormulaValue(property, out value, out error);
        }

        private static bool TryGetFormulaValue(SerializedProperty property, out FormulaValue value, out string error)
        {
            value = default;
            error = string.Empty;

            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    value = FormulaValue.FromNumber(property.longValue);
                    return true;
                case SerializedPropertyType.Float:
                    value = FormulaValue.FromNumber(property.floatValue);
                    return true;
                case SerializedPropertyType.Boolean:
                    value = FormulaValue.FromNumber(property.boolValue ? 1d : 0d);
                    return true;
                case SerializedPropertyType.Enum:
                    value = FormulaValue.FromNumber(property.enumValueIndex);
                    return true;
                case SerializedPropertyType.String:
                    value = FormulaValue.FromString(property.stringValue ?? string.Empty);
                    return true;
                default:
                    error = $"cell type {property.propertyType} is not supported in formulas.";
                    return false;
            }
        }

        private static bool TryGetFormulaValue(
            string rawValue,
            string typeName,
            out FormulaValue value,
            out string error)
        {
            value = default;
            error = string.Empty;

            string normalizedType = NormalizeTypeName(typeName);
            string text = rawValue ?? string.Empty;
            switch (normalizedType)
            {
                case "byte":
                case "sbyte":
                case "short":
                case "ushort":
                case "int":
                case "uint":
                case "long":
                case "ulong":
                case "float":
                case "double":
                case "decimal":
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        value = FormulaValue.FromNumber(0d);
                        return true;
                    }

                    if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                    {
                        error = $"cell value \"{text}\" is not numeric.";
                        return false;
                    }

                    value = FormulaValue.FromNumber(number);
                    return true;
                case "bool":
                case "boolean":
                    if (bool.TryParse(text, out bool boolValue))
                    {
                        value = FormulaValue.FromNumber(boolValue ? 1d : 0d);
                        return true;
                    }

                    if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double boolNumber))
                    {
                        value = FormulaValue.FromNumber(Math.Abs(boolNumber) > double.Epsilon ? 1d : 0d);
                        return true;
                    }

                    error = $"cell value \"{text}\" is not boolean.";
                    return false;
                default:
                    value = FormulaValue.FromString(text);
                    return true;
            }
        }

        private static bool TrySetFormulaValue(SerializedProperty property, FormulaValue value, out bool changed, out string error)
        {
            changed = false;
            error = string.Empty;
            if (property == null)
            {
                error = "target cell could not be found.";
                return false;
            }

            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    if (!value.TryGetNumber(out double integerNumber))
                    {
                        error = "target cell type Integer requires a numeric formula result.";
                        return false;
                    }

                    long longValue = Convert.ToInt64(Math.Round(integerNumber));
                    changed = property.longValue != longValue;
                    property.longValue = longValue;
                    return true;
                case SerializedPropertyType.Float:
                    if (!value.TryGetNumber(out double floatNumber))
                    {
                        error = "target cell type Float requires a numeric formula result.";
                        return false;
                    }

                    float floatValue = (float)floatNumber;
                    changed = Math.Abs(property.floatValue - floatValue) > 0.000001f;
                    property.floatValue = floatValue;
                    return true;
                case SerializedPropertyType.Boolean:
                    if (!value.TryGetNumber(out double boolNumber))
                    {
                        error = "target cell type Boolean requires a numeric formula result.";
                        return false;
                    }

                    bool boolValue = Math.Abs(boolNumber) > double.Epsilon;
                    changed = property.boolValue != boolValue;
                    property.boolValue = boolValue;
                    return true;
                case SerializedPropertyType.Enum:
                    if (!value.TryGetNumber(out double enumNumber))
                    {
                        error = "target cell type Enum requires a numeric formula result.";
                        return false;
                    }

                    int enumValueIndex = Math.Max(0, Convert.ToInt32(Math.Round(enumNumber)));
                    if (property.enumDisplayNames.Length > 0)
                    {
                        enumValueIndex = Math.Min(enumValueIndex, property.enumDisplayNames.Length - 1);
                    }

                    changed = property.enumValueIndex != enumValueIndex;
                    property.enumValueIndex = enumValueIndex;
                    return true;
                case SerializedPropertyType.String:
                    string stringValue = value.ToText();
                    changed = property.stringValue != stringValue;
                    property.stringValue = stringValue;
                    return true;
                default:
                    error = $"target cell type {property.propertyType} is not supported by formulas.";
                    return false;
            }
        }

        private static bool TryApplyCellFormula(
            SerializedProperty arrayProperty,
            IReadOnlyList<string> columnPropertyNames,
            ColumnLookup columnLookup,
            IBetterScriptableFormulaCellStore cellStore,
            string expression,
            BetterScriptableCellAddress target,
            out bool changed,
            out string error)
        {
            changed = false;
            error = string.Empty;

            HashSet<string> currentTarget = new HashSet<string> { GetTargetKey(target) };
            FormulaParser parser = new FormulaParser(
                arrayProperty,
                columnPropertyNames,
                columnLookup,
                cellStore,
                expression,
                target.Row,
                currentTarget);

            if (!parser.TryEvaluate(out FormulaValue value, out string parseError))
            {
                error = parseError;
                return false;
            }

            if (cellStore != null && cellStore.IsVirtualColumn(target.Column))
            {
                if (!TrySetVirtualFormulaValue(cellStore, target, value, out changed, out string virtualSetError))
                {
                    error = virtualSetError;
                    return false;
                }

                return true;
            }

            SerializedProperty targetProperty = GetCellProperty(arrayProperty, columnPropertyNames, target);
            if (!TrySetFormulaValue(targetProperty, value, out changed, out string setError))
            {
                error = setError;
                return false;
            }

            return true;
        }

        private static bool TrySetVirtualFormulaValue(
            IBetterScriptableFormulaCellStore cellStore,
            BetterScriptableCellAddress target,
            FormulaValue value,
            out bool changed,
            out string error)
        {
            changed = false;
            error = string.Empty;
            if (!cellStore.TryGetCellValue(target, out _, out string typeName, out error))
            {
                return false;
            }

            if (!TryFormatFormulaValue(value, typeName, out string serializedValue, out error))
            {
                return false;
            }

            return cellStore.TrySetCellValue(target, serializedValue, out changed, out error);
        }

        private static bool TryFormatFormulaValue(
            FormulaValue value,
            string typeName,
            out string serializedValue,
            out string error)
        {
            serializedValue = string.Empty;
            error = string.Empty;

            string normalizedType = NormalizeTypeName(typeName);
            switch (normalizedType)
            {
                case "byte":
                case "sbyte":
                case "short":
                case "ushort":
                case "int":
                case "uint":
                case "long":
                case "ulong":
                    if (!value.TryGetNumber(out double integerNumber))
                    {
                        error = "target cell requires a numeric formula result.";
                        return false;
                    }

                    serializedValue = Convert.ToInt64(Math.Round(integerNumber)).ToString(CultureInfo.InvariantCulture);
                    return true;
                case "float":
                case "double":
                case "decimal":
                    if (!value.TryGetNumber(out double floatNumber))
                    {
                        error = "target cell requires a numeric formula result.";
                        return false;
                    }

                    serializedValue = floatNumber.ToString(CultureInfo.InvariantCulture);
                    return true;
                case "bool":
                case "boolean":
                    if (!value.TryGetNumber(out double boolNumber))
                    {
                        error = "target cell requires a numeric formula result.";
                        return false;
                    }

                    serializedValue = (Math.Abs(boolNumber) > double.Epsilon).ToString();
                    return true;
                default:
                    serializedValue = value.ToText();
                    return true;
            }
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

        private static string GetTargetKey(BetterScriptableCellAddress address)
        {
            return address.Row + ":" + address.Column;
        }

        private static string FormatColumnName(int zeroBasedIndex)
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

        private static string NormalizeColumnName(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string trimmed = text.Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[trimmed.Length - 1] == ']')
            {
                trimmed = trimmed.Substring(1, trimmed.Length - 2);
            }

            char[] buffer = new char[trimmed.Length];
            int length = 0;
            for (int i = 0; i < trimmed.Length; i++)
            {
                char character = trimmed[i];
                if (char.IsLetterOrDigit(character))
                {
                    buffer[length] = char.ToLowerInvariant(character);
                    length++;
                }
            }

            return new string(buffer, 0, length);
        }

        private static bool TryParseColumnAddress(string text, out int column)
        {
            column = -1;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string trimmed = text.Trim().ToUpperInvariant();
            int value = 0;
            for (int i = 0; i < trimmed.Length; i++)
            {
                char character = trimmed[i];
                if (character < 'A' || character > 'Z')
                {
                    return false;
                }

                value = value * 26 + character - 'A' + 1;
            }

            if (value <= 0)
            {
                return false;
            }

            column = value - 1;
            return true;
        }

        private readonly struct FormulaValue
        {
            private readonly double _number;
            private readonly string _text;

            private FormulaValue(double number, string text, bool isString)
            {
                _number = number;
                _text = text;
                IsString = isString;
            }

            public bool IsString { get; }

            public static FormulaValue FromNumber(double value)
            {
                return new FormulaValue(value, string.Empty, false);
            }

            public static FormulaValue FromString(string value)
            {
                return new FormulaValue(0d, value ?? string.Empty, true);
            }

            public bool TryGetNumber(out double value)
            {
                if (!IsString)
                {
                    value = _number;
                    return true;
                }

                return double.TryParse(_text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            }

            public string ToText()
            {
                return IsString ? _text : _number.ToString(CultureInfo.InvariantCulture);
            }

            public static bool TryAdd(FormulaValue left, FormulaValue right, out FormulaValue value, out string error)
            {
                error = string.Empty;
                if (left.IsString || right.IsString)
                {
                    value = FromString(left.ToText() + right.ToText());
                    return true;
                }

                value = FromNumber(left._number + right._number);
                return true;
            }

            public static bool TrySubtract(FormulaValue left, FormulaValue right, out FormulaValue value, out string error)
            {
                return TryNumericOperator(left, right, out value, out error, (leftNumber, rightNumber) => leftNumber - rightNumber, "-");
            }

            public static bool TryMultiply(FormulaValue left, FormulaValue right, out FormulaValue value, out string error)
            {
                return TryNumericOperator(left, right, out value, out error, (leftNumber, rightNumber) => leftNumber * rightNumber, "*");
            }

            public static bool TryDivide(FormulaValue left, FormulaValue right, out FormulaValue value, out string error)
            {
                value = default;
                error = string.Empty;
                if (!left.TryGetNumber(out double leftNumber) || !right.TryGetNumber(out double rightNumber))
                {
                    error = "operator / requires numeric values.";
                    return false;
                }

                if (Math.Abs(rightNumber) <= double.Epsilon)
                {
                    error = "division by zero.";
                    return false;
                }

                value = FromNumber(leftNumber / rightNumber);
                return true;
            }

            public static bool TryNegate(FormulaValue source, out FormulaValue value, out string error)
            {
                value = default;
                if (!source.TryGetNumber(out double number))
                {
                    error = "unary - requires a numeric value.";
                    return false;
                }

                error = string.Empty;
                value = FromNumber(-number);
                return true;
            }

            public static bool TryFormat(
                FormulaValue source,
                string format,
                out FormulaValue value,
                out string error)
            {
                value = default;
                error = string.Empty;

                if (!source.TryGetNumber(out double number))
                {
                    error = "TEXT/FORMAT requires a numeric value.";
                    return false;
                }

                try
                {
                    value = FromString(number.ToString(format ?? string.Empty, CultureInfo.InvariantCulture));
                    return true;
                }
                catch (FormatException)
                {
                    error = $"invalid format string \"{format}\".";
                    return false;
                }
            }

            private static bool TryNumericOperator(
                FormulaValue left,
                FormulaValue right,
                out FormulaValue value,
                out string error,
                Func<double, double, double> operation,
                string operatorName)
            {
                value = default;
                if (!left.TryGetNumber(out double leftNumber) || !right.TryGetNumber(out double rightNumber))
                {
                    error = $"operator {operatorName} requires numeric values.";
                    return false;
                }

                error = string.Empty;
                value = FromNumber(operation(leftNumber, rightNumber));
                return true;
            }
        }

        public sealed class CompiledFormulaSet
        {
            public static readonly CompiledFormulaSet Empty = new CompiledFormulaSet(
                new ColumnLookup(Array.Empty<string>()),
                new List<ParsedFormula>(),
                new HashSet<string>(),
                new HashSet<string>());

            private readonly ColumnLookup _columnLookup;
            private readonly List<ParsedFormula> _formulas;
            private readonly HashSet<string> _definedCellTargets;
            private readonly HashSet<string> _targetKeys;

            internal CompiledFormulaSet(
                ColumnLookup columnLookup,
                List<ParsedFormula> formulas,
                HashSet<string> definedCellTargets,
                HashSet<string> targetKeys)
            {
                _columnLookup = columnLookup;
                _formulas = formulas;
                _definedCellTargets = definedCellTargets;
                _targetKeys = targetKeys;
            }

            public bool IsEmpty => _formulas.Count == 0;

            public IReadOnlyCollection<string> TargetKeys => _targetKeys;

            internal ColumnLookup ColumnLookup => _columnLookup;

            internal IReadOnlyList<ParsedFormula> Formulas => _formulas;

            internal bool IsSpecificCellTarget(BetterScriptableCellAddress address)
            {
                return _definedCellTargets.Contains(GetTargetKey(address));
            }
        }

        internal readonly struct ParsedFormula
        {
            public readonly int SourceIndex;
            public readonly FormulaTarget Target;
            public readonly string Expression;

            public ParsedFormula(int sourceIndex, FormulaTarget target, string expression)
            {
                SourceIndex = sourceIndex;
                Target = target;
                Expression = expression;
            }
        }

        internal readonly struct FormulaTarget
        {
            public readonly bool IsColumn;
            public readonly BetterScriptableCellAddress Cell;
            public readonly int Column;

            private FormulaTarget(bool isColumn, BetterScriptableCellAddress cell, int column)
            {
                IsColumn = isColumn;
                Cell = cell;
                Column = column;
            }

            public static FormulaTarget ForCell(BetterScriptableCellAddress cell)
            {
                return new FormulaTarget(false, cell, cell.Column);
            }

            public static FormulaTarget ForColumn(int column)
            {
                return new FormulaTarget(true, default, column);
            }
        }

        internal sealed class ColumnLookup
        {
            private readonly Dictionary<string, int> _columnsByName = new Dictionary<string, int>();

            public ColumnLookup(IReadOnlyList<string> columnPropertyNames)
            {
                Count = columnPropertyNames?.Count ?? 0;
                if (columnPropertyNames == null)
                {
                    return;
                }

                for (int i = 0; i < columnPropertyNames.Count; i++)
                {
                    string propertyName = columnPropertyNames[i];
                    AddName(propertyName, i);
                    AddName(propertyName?.TrimStart('_'), i);
                    AddName(ObjectNames.NicifyVariableName(propertyName?.TrimStart('_') ?? string.Empty), i);
                    AddName(BetterScriptableNameUtility.ToPascalCase(propertyName?.TrimStart('_') ?? string.Empty), i);
                }
            }

            public int Count { get; }

            public bool IsColumnInRange(int column)
            {
                return column >= 0 && column < Count;
            }

            public bool TryGetColumn(string text, out int column)
            {
                column = -1;
                if (TryParseColumnAddress(text, out int addressColumn) && IsColumnInRange(addressColumn))
                {
                    column = addressColumn;
                    return true;
                }

                string normalized = NormalizeColumnName(text);
                return !string.IsNullOrEmpty(normalized) && _columnsByName.TryGetValue(normalized, out column);
            }

            private void AddName(string name, int column)
            {
                string normalized = NormalizeColumnName(name);
                if (string.IsNullOrEmpty(normalized) || _columnsByName.ContainsKey(normalized))
                {
                    return;
                }

                _columnsByName.Add(normalized, column);
            }
        }

        private sealed class FormulaParser
        {
            private readonly SerializedProperty _arrayProperty;
            private readonly IReadOnlyList<string> _columnPropertyNames;
            private readonly ColumnLookup _columnLookup;
            private readonly IBetterScriptableFormulaCellStore _cellStore;
            private readonly string _expression;
            private readonly int _currentRow;
            private readonly HashSet<string> _applyingTargets;
            private int _position;

            public FormulaParser(
                SerializedProperty arrayProperty,
                IReadOnlyList<string> columnPropertyNames,
                ColumnLookup columnLookup,
                IBetterScriptableFormulaCellStore cellStore,
                string expression,
                int currentRow,
                HashSet<string> applyingTargets)
            {
                _arrayProperty = arrayProperty;
                _columnPropertyNames = columnPropertyNames;
                _columnLookup = columnLookup;
                _cellStore = cellStore;
                _expression = expression;
                _currentRow = currentRow;
                _applyingTargets = applyingTargets;
            }

            public bool TryEvaluate(out FormulaValue value, out string error)
            {
                value = default;
                error = string.Empty;

                if (!TryParseExpression(out value, out error))
                {
                    return false;
                }

                SkipWhitespace();
                if (_position < _expression.Length)
                {
                    error = $"unexpected token near \"{_expression.Substring(_position)}\".";
                    return false;
                }

                return true;
            }

            private bool TryParseExpression(out FormulaValue value, out string error)
            {
                if (!TryParseTerm(out value, out error))
                {
                    return false;
                }

                while (true)
                {
                    SkipWhitespace();
                    if (Match('+'))
                    {
                        if (!TryParseTerm(out FormulaValue right, out error))
                        {
                            return false;
                        }

                        if (!FormulaValue.TryAdd(value, right, out value, out error))
                        {
                            return false;
                        }

                        continue;
                    }

                    if (Match('-'))
                    {
                        if (!TryParseTerm(out FormulaValue right, out error))
                        {
                            return false;
                        }

                        if (!FormulaValue.TrySubtract(value, right, out value, out error))
                        {
                            return false;
                        }

                        continue;
                    }

                    return true;
                }
            }

            private bool TryParseTerm(out FormulaValue value, out string error)
            {
                if (!TryParseFactor(out value, out error))
                {
                    return false;
                }

                while (true)
                {
                    SkipWhitespace();
                    if (Match('*'))
                    {
                        if (!TryParseFactor(out FormulaValue right, out error))
                        {
                            return false;
                        }

                        if (!FormulaValue.TryMultiply(value, right, out value, out error))
                        {
                            return false;
                        }

                        continue;
                    }

                    if (Match('/'))
                    {
                        if (!TryParseFactor(out FormulaValue right, out error))
                        {
                            return false;
                        }

                        if (!FormulaValue.TryDivide(value, right, out value, out error))
                        {
                            return false;
                        }

                        continue;
                    }

                    return true;
                }
            }

            private bool TryParseFactor(out FormulaValue value, out string error)
            {
                value = default;
                error = string.Empty;
                SkipWhitespace();

                if (Match('+'))
                {
                    return TryParseFactor(out value, out error);
                }

                if (Match('-'))
                {
                    if (!TryParseFactor(out value, out error))
                    {
                        return false;
                    }

                    return FormulaValue.TryNegate(value, out value, out error);
                }

                if (Match('('))
                {
                    if (!TryParseExpression(out value, out error))
                    {
                        return false;
                    }

                    SkipWhitespace();
                    if (!Match(')'))
                    {
                        error = "missing closing parenthesis.";
                        return false;
                    }

                    return true;
                }

                if (TryReadFunctionCall(out value, out error))
                {
                    return true;
                }

                if (!string.IsNullOrEmpty(error))
                {
                    return false;
                }

                if (TryReadReference(out BetterScriptableCellAddress address))
                {
                    return TryGetFormulaValue(
                        _arrayProperty,
                        _columnPropertyNames,
                        _cellStore,
                        address,
                        _applyingTargets,
                        out value,
                        out error);
                }

                if (TryReadStringLiteral(out value, out error))
                {
                    return true;
                }

                if (!string.IsNullOrEmpty(error))
                {
                    return false;
                }

                return TryReadNumber(out value, out error);
            }

            private bool TryReadFunctionCall(out FormulaValue value, out string error)
            {
                value = default;
                error = string.Empty;
                SkipWhitespace();

                int start = _position;
                if (!TryReadIdentifier(out string functionName))
                {
                    return false;
                }

                SkipWhitespace();
                if (!Match('('))
                {
                    _position = start;
                    return false;
                }

                if (!IsFormatFunction(functionName))
                {
                    error = $"unsupported function \"{functionName}\".";
                    return false;
                }

                if (!TryParseExpression(out FormulaValue sourceValue, out error))
                {
                    return false;
                }

                if (!Match(','))
                {
                    error = $"function {functionName} expects a comma before the format string.";
                    return false;
                }

                if (!TryReadStringLiteral(out FormulaValue formatValue, out error))
                {
                    if (string.IsNullOrEmpty(error))
                    {
                        error = $"function {functionName} expects a single-quoted format string.";
                    }

                    return false;
                }

                if (!Match(')'))
                {
                    error = $"function {functionName} is missing a closing parenthesis.";
                    return false;
                }

                return FormulaValue.TryFormat(
                    sourceValue,
                    formatValue.ToText(),
                    out value,
                    out error);
            }

            private bool TryReadNumber(out FormulaValue value, out string error)
            {
                value = default;
                error = string.Empty;
                SkipWhitespace();

                int start = _position;
                bool hasDigit = false;
                while (_position < _expression.Length
                    && (char.IsDigit(_expression[_position]) || _expression[_position] == '.'))
                {
                    hasDigit = true;
                    _position++;
                }

                if (!hasDigit)
                {
                    error = $"expected number, string literal, cell reference, or column reference near \"{_expression.Substring(_position)}\".";
                    return false;
                }

                string numberText = _expression.Substring(start, _position - start);
                if (!double.TryParse(numberText, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                {
                    error = $"invalid number \"{numberText}\".";
                    return false;
                }

                value = FormulaValue.FromNumber(number);
                return true;
            }

            private bool TryReadStringLiteral(out FormulaValue value, out string error)
            {
                value = default;
                error = string.Empty;
                SkipWhitespace();

                if (_position >= _expression.Length || _expression[_position] != '\'')
                {
                    return false;
                }

                _position++;
                string text = string.Empty;
                while (_position < _expression.Length)
                {
                    char character = _expression[_position];
                    if (character == '\'')
                    {
                        if (_position + 1 < _expression.Length && _expression[_position + 1] == '\'')
                        {
                            text += '\'';
                            _position += 2;
                            continue;
                        }

                        _position++;
                        value = FormulaValue.FromString(text);
                        return true;
                    }

                    if (character == '\\' && _position + 1 < _expression.Length)
                    {
                        char escaped = _expression[_position + 1];
                        switch (escaped)
                        {
                            case '\'':
                                text += '\'';
                                break;
                            case '\\':
                                text += '\\';
                                break;
                            case 'n':
                                text += '\n';
                                break;
                            case 't':
                                text += '\t';
                                break;
                            default:
                                text += escaped;
                                break;
                        }

                        _position += 2;
                        continue;
                    }

                    text += character;
                    _position++;
                }

                error = "missing closing quote for string literal.";
                return false;
            }

            private bool TryReadIdentifier(out string identifier)
            {
                identifier = string.Empty;
                SkipWhitespace();

                if (_position >= _expression.Length
                    || (!char.IsLetter(_expression[_position]) && _expression[_position] != '_'))
                {
                    return false;
                }

                int start = _position;
                _position++;
                while (_position < _expression.Length
                    && (char.IsLetterOrDigit(_expression[_position]) || _expression[_position] == '_'))
                {
                    _position++;
                }

                identifier = _expression.Substring(start, _position - start);
                return true;
            }

            private static bool IsFormatFunction(string functionName)
            {
                return string.Equals(functionName, "TEXT", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(functionName, "FORMAT", StringComparison.OrdinalIgnoreCase);
            }

            private bool TryReadReference(out BetterScriptableCellAddress address)
            {
                address = default;
                SkipWhitespace();
                int start = _position;
                string referenceText;

                if (_position < _expression.Length && _expression[_position] == '[')
                {
                    _position++;
                    int nameStart = _position;
                    while (_position < _expression.Length && _expression[_position] != ']')
                    {
                        _position++;
                    }

                    if (_position >= _expression.Length)
                    {
                        _position = start;
                        return false;
                    }

                    referenceText = _expression.Substring(nameStart, _position - nameStart);
                    _position++;
                }
                else
                {
                    if (_position >= _expression.Length
                        || (!char.IsLetter(_expression[_position]) && _expression[_position] != '_'))
                    {
                        return false;
                    }

                    _position++;
                    while (_position < _expression.Length
                        && (char.IsLetterOrDigit(_expression[_position]) || _expression[_position] == '_'))
                    {
                        _position++;
                    }

                    referenceText = _expression.Substring(start, _position - start);
                }

                if (TryParseCellAddress(referenceText, out address))
                {
                    return true;
                }

                if (_columnLookup.TryGetColumn(referenceText, out int column))
                {
                    address = new BetterScriptableCellAddress(_currentRow, column);
                    return true;
                }

                _position = start;
                return false;
            }

            private bool Match(char expected)
            {
                SkipWhitespace();
                if (_position >= _expression.Length || _expression[_position] != expected)
                {
                    return false;
                }

                _position++;
                return true;
            }

            private void SkipWhitespace()
            {
                while (_position < _expression.Length && char.IsWhiteSpace(_expression[_position]))
                {
                    _position++;
                }
            }
        }
    }

    internal readonly struct BetterScriptableCellAddress
    {
        public readonly int Row;
        public readonly int Column;

        public BetterScriptableCellAddress(int row, int column)
        {
            Row = row;
            Column = column;
        }

        public override string ToString()
        {
            return GetColumnName(Column) + (Row + 1).ToString(CultureInfo.InvariantCulture);
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
    }

    internal interface IBetterScriptableFormulaCellStore
    {
        bool IsVirtualColumn(int column);

        bool TryGetCellValue(
            BetterScriptableCellAddress address,
            out string value,
            out string typeName,
            out string error);

        bool TrySetCellValue(
            BetterScriptableCellAddress address,
            string value,
            out bool changed,
            out string error);
    }
}

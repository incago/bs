using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SpreadAsset.Editor
{
    internal static class SpreadAssetCsvUtility
    {
        public static void WriteFile(string path, IReadOnlyList<IReadOnlyList<string>> rows)
        {
            File.WriteAllText(path, Format(rows), Encoding.UTF8);
        }

        public static bool TryReadFile(string path, out List<List<string>> rows, out string error)
        {
            rows = null;
            error = string.Empty;

            try
            {
                return TryParse(File.ReadAllText(path, Encoding.UTF8), out rows, out error);
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        public static string Format(IReadOnlyList<IReadOnlyList<string>> rows)
        {
            StringBuilder builder = new StringBuilder();
            if (rows == null)
            {
                return string.Empty;
            }

            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                IReadOnlyList<string> row = rows[rowIndex];
                if (row != null)
                {
                    for (int columnIndex = 0; columnIndex < row.Count; columnIndex++)
                    {
                        if (columnIndex > 0)
                        {
                            builder.Append(',');
                        }

                        AppendField(builder, row[columnIndex]);
                    }
                }

                if (rowIndex < rows.Count - 1)
                {
                    builder.AppendLine();
                }
            }

            return builder.ToString();
        }

        public static bool TryParse(string text, out List<List<string>> rows, out string error)
        {
            rows = new List<List<string>>();
            error = string.Empty;

            if (string.IsNullOrEmpty(text))
            {
                return true;
            }

            List<string> row = new List<string>();
            StringBuilder field = new StringBuilder();
            bool inQuotes = false;
            bool hasContent = false;
            bool lastWasComma = false;

            for (int i = 0; i < text.Length; i++)
            {
                char current = text[i];

                if (inQuotes)
                {
                    if (current == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            field.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }

                        continue;
                    }

                    field.Append(current);
                    continue;
                }

                if (current == '"')
                {
                    if (field.Length > 0)
                    {
                        field.Append(current);
                    }
                    else
                    {
                        inQuotes = true;
                        hasContent = true;
                    }

                    lastWasComma = false;
                    continue;
                }

                if (current == ',')
                {
                    AddField(row, field);
                    hasContent = true;
                    lastWasComma = true;
                    continue;
                }

                if (current == '\r' || current == '\n')
                {
                    AddField(row, field);
                    rows.Add(row);
                    row = new List<string>();
                    hasContent = false;
                    lastWasComma = false;

                    if (current == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }

                    continue;
                }

                field.Append(current);
                hasContent = true;
                lastWasComma = false;
            }

            if (inQuotes)
            {
                error = "CSV has an unterminated quoted field.";
                return false;
            }

            if (hasContent || field.Length > 0 || row.Count > 0 || lastWasComma)
            {
                AddField(row, field);
                rows.Add(row);
            }

            return true;
        }

        private static void AppendField(StringBuilder builder, string value)
        {
            value ??= string.Empty;
            bool quote = value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
            if (!quote)
            {
                builder.Append(value);
                return;
            }

            builder.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] == '"')
                {
                    builder.Append('"');
                }

                builder.Append(value[i]);
            }

            builder.Append('"');
        }

        private static void AddField(List<string> row, StringBuilder field)
        {
            row.Add(field.ToString());
            field.Length = 0;
        }
    }
}

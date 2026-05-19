using System;
using System.Collections.Generic;
using System.Globalization;

namespace BetterScriptable.Editor
{
    internal static class BetterScriptableSchemaUtility
    {
        private const string FieldIdPrefix = "field_";
        private const ulong FnvOffsetBasis = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        public static bool EnsureFieldIds(BetterScriptableDocumentSchema schema)
        {
            if (schema == null)
            {
                return false;
            }

            bool changed = false;
            HashSet<string> usedIds = new HashSet<string>(StringComparer.Ordinal);
            changed |= EnsureFieldIds(schema.Fields, CreateAssetFieldScope(schema), usedIds);

            BetterScriptableSchemaTable[] tables = schema.Tables ?? Array.Empty<BetterScriptableSchemaTable>();
            foreach (BetterScriptableSchemaTable table in tables)
            {
                if (table == null)
                {
                    continue;
                }

                changed |= EnsureFieldIds(table.Fields, CreateTableFieldScope(schema, table), usedIds);
            }

            return changed;
        }

        public static string CreateNewFieldId()
        {
            return FieldIdPrefix + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        }

        public static string CreateFieldId(string scope, string fieldName, string typeName)
        {
            string seed = string.Join("|",
                scope ?? string.Empty,
                BetterScriptableNameUtility.ToPascalCase(fieldName ?? string.Empty),
                (typeName ?? string.Empty).Trim());

            ulong hash = FnvOffsetBasis;
            for (int i = 0; i < seed.Length; i++)
            {
                hash ^= char.ToUpperInvariant(seed[i]);
                hash *= FnvPrime;
            }

            return FieldIdPrefix + hash.ToString("x16", CultureInfo.InvariantCulture);
        }

        public static bool HasFieldId(BetterScriptableSchemaField field)
        {
            return !string.IsNullOrWhiteSpace(field?.Id);
        }

        public static bool AreSameFieldId(BetterScriptableSchemaField left, BetterScriptableSchemaField right)
        {
            return HasFieldId(left)
                && HasFieldId(right)
                && string.Equals(left.Id.Trim(), right.Id.Trim(), StringComparison.Ordinal);
        }

        private static bool EnsureFieldIds(
            BetterScriptableSchemaField[] fields,
            string scope,
            HashSet<string> usedIds)
        {
            if (fields == null)
            {
                return false;
            }

            bool changed = false;
            for (int i = 0; i < fields.Length; i++)
            {
                BetterScriptableSchemaField field = fields[i];
                if (field == null)
                {
                    continue;
                }

                string fieldId = (field.Id ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(fieldId))
                {
                    fieldId = CreateFieldId(scope, field.Name, field.TypeName);
                }

                fieldId = MakeUniqueFieldId(fieldId, usedIds);
                if (!string.Equals(field.Id, fieldId, StringComparison.Ordinal))
                {
                    field.Id = fieldId;
                    changed = true;
                }
            }

            return changed;
        }

        private static string CreateAssetFieldScope(BetterScriptableDocumentSchema schema)
        {
            return "asset:" + (schema.AssetClassName ?? string.Empty);
        }

        private static string CreateTableFieldScope(
            BetterScriptableDocumentSchema schema,
            BetterScriptableSchemaTable table)
        {
            return string.Join(":",
                "table",
                schema.AssetClassName ?? string.Empty,
                table.FieldName ?? string.Empty,
                table.RowTypeName ?? string.Empty);
        }

        private static string MakeUniqueFieldId(string fieldId, HashSet<string> usedIds)
        {
            string uniqueFieldId = fieldId;
            int suffix = 2;
            while (usedIds.Contains(uniqueFieldId))
            {
                uniqueFieldId = fieldId + "_" + suffix.ToString(CultureInfo.InvariantCulture);
                suffix++;
            }

            usedIds.Add(uniqueFieldId);
            return uniqueFieldId;
        }
    }
}

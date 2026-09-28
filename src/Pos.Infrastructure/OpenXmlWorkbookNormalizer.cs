using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using OxmlColumn = DocumentFormat.OpenXml.Spreadsheet.Column;
using OxmlColumns = DocumentFormat.OpenXml.Spreadsheet.Columns;

namespace Pos.Infrastructure;

internal static class OpenXmlWorkbookNormalizer
{
    private const uint MaximumColumnNumber = 16_384;

    public static void NormalizeOverlappingColumnRanges(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var document = SpreadsheetDocument.Open(path, isEditable: true);
        var workbookPart = document.WorkbookPart
            ?? throw new InvalidDataException("The workbook package has no workbook part.");

        foreach (var worksheetPart in workbookPart.WorksheetParts)
        {
            var changed = false;
            foreach (var columns in worksheetPart.Worksheet.Elements<OxmlColumns>())
            {
                changed |= CanonicalizeColumnRanges(columns);
            }

            if (changed)
            {
                worksheetPart.Worksheet.Save();
            }
        }
    }

    private static bool CanonicalizeColumnRanges(OxmlColumns columns)
    {
        var definitions = columns.Elements<OxmlColumn>()
            .Select((column, index) => CreateDefinition(column, index))
            .ToArray();
        if (definitions.Length < 2)
        {
            return false;
        }

        var boundaries = definitions
            .SelectMany(definition => new[] { definition.Min, definition.Max + 1 })
            .Distinct()
            .Order()
            .ToArray();
        var canonical = new List<OxmlColumn>();
        var hasOverlap = false;

        for (var index = 0; index < boundaries.Length - 1; index++)
        {
            var min = boundaries[index];
            var max = boundaries[index + 1] - 1;
            var matches = definitions
                .Where(definition => definition.Min <= min && definition.Max >= max)
                .OrderBy(definition => definition.Order)
                .Select(definition => definition.Column)
                .ToArray();
            if (matches.Length == 0)
            {
                continue;
            }

            hasOverlap |= matches.Length > 1;
            var resolved = ResolveColumnRange(min, max, matches);
            if (canonical.Count > 0
                && canonical[^1].Max!.Value + 1 == resolved.Min!.Value
                && HaveSameFormatting(canonical[^1], resolved))
            {
                canonical[^1].Max = resolved.Max!.Value;
            }
            else
            {
                canonical.Add(resolved);
            }
        }

        if (!hasOverlap)
        {
            return false;
        }

        columns.RemoveAllChildren<OxmlColumn>();
        foreach (var column in canonical)
        {
            columns.AppendChild(column);
        }

        return true;
    }

    private static ColumnDefinition CreateDefinition(OxmlColumn column, int order)
    {
        var min = column.Min?.Value;
        var max = column.Max?.Value;
        if (!min.HasValue
            || !max.HasValue
            || min.Value == 0
            || min.Value > max.Value
            || max.Value > MaximumColumnNumber)
        {
            throw new InvalidDataException("The workbook contains an invalid worksheet column range.");
        }

        return new ColumnDefinition(column, min.Value, max.Value, order);
    }

    private static OxmlColumn ResolveColumnRange(
        uint min,
        uint max,
        IReadOnlyList<OxmlColumn> matches)
    {
        var result = new OxmlColumn();
        // ClosedXML reads overlapping ranges in document order. Replay that order so
        // canonical non-overlapping ranges keep the same effective width and styling.
        foreach (var source in matches)
        {
            foreach (var attribute in source.GetAttributes()
                         .Where(attribute => attribute.LocalName is not "min" and not "max"))
            {
                result.SetAttribute(attribute);
            }
        }

        var last = matches[^1];
        result.Min = min;
        result.Max = max;
        result.Width = last.Width is null ? null : new DoubleValue(last.Width.Value);

        var style = matches.LastOrDefault(column => column.Style is not null)?.Style;
        result.Style = style is null ? null : new UInt32Value(style.Value);

        var outlineLevel = matches.LastOrDefault(column => column.OutlineLevel is not null)?.OutlineLevel;
        result.OutlineLevel = outlineLevel is null ? null : new ByteValue(outlineLevel.Value);

        result.Hidden = matches.Any(column => column.Hidden?.Value == true)
            ? new BooleanValue(true)
            : null;
        result.Collapsed = matches.Any(column => column.Collapsed?.Value == true)
            ? new BooleanValue(true)
            : null;
        return result;
    }

    private static bool HaveSameFormatting(OxmlColumn left, OxmlColumn right)
    {
        return GetFormattingKey(left).SequenceEqual(GetFormattingKey(right));
    }

    private static (string NamespaceUri, string LocalName, string Value)[] GetFormattingKey(OxmlColumn column)
    {
        return column.GetAttributes()
            .Where(attribute => attribute.LocalName is not "min" and not "max")
            .Select(attribute => (
                NamespaceUri: attribute.NamespaceUri ?? string.Empty,
                LocalName: attribute.LocalName,
                Value: attribute.Value ?? string.Empty))
            .OrderBy(attribute => attribute.NamespaceUri, StringComparer.Ordinal)
            .ThenBy(attribute => attribute.LocalName, StringComparer.Ordinal)
            .ThenBy(attribute => attribute.Value, StringComparer.Ordinal)
            .ToArray();
    }

    private sealed record ColumnDefinition(OxmlColumn Column, uint Min, uint Max, int Order);
}

using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using PPG.GuessData.Models;

namespace PPG.GuessData;

public sealed class ExcelReaderService : IExcelReaderService
{
    public Task<string?> ReadSourceUrlAsync(
        Stream workbookStream,
        CancellationToken cancellationToken = default)
    {
        ValidateWorkbookStream(workbookStream);

        cancellationToken.ThrowIfCancellationRequested();

        workbookStream.Position = 0;
        using var document = SpreadsheetDocument.Open(workbookStream, false);
        var workbookPart = document.WorkbookPart
            ?? throw new InvalidDataException("The Excel workbook is missing its workbook part.");
        var sharedStrings = GetSharedStrings(workbookPart);

        var sourceSheet = workbookPart.Workbook.Sheets?
            .Elements<Sheet>()
            .FirstOrDefault(sheet => string.Equals(
                sheet.Name?.Value?.Trim(),
                "Source",
                StringComparison.OrdinalIgnoreCase));
        if (sourceSheet?.Id?.Value is not { Length: > 0 } relationshipId
            || workbookPart.GetPartById(relationshipId) is not WorksheetPart worksheetPart)
        {
            return Task.FromResult<string?>(null);
        }

        var rows = worksheetPart.Worksheet.GetFirstChild<SheetData>()?.Elements<Row>() ?? [];
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var cells = ReadRow(row, sharedStrings).OrderBy(cell => cell.ColumnIndex).ToArray();
            var labelIndex = Array.FindIndex(
                cells,
                cell => string.Equals(cell.Value.Trim(), "Source URL", StringComparison.OrdinalIgnoreCase));
            if (labelIndex < 0)
            {
                continue;
            }

            var sourceUrl = cells
                .Skip(labelIndex + 1)
                .Select(cell => cell.Value.Trim())
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

            return Task.FromResult(IsSafeSourceUrl(sourceUrl) ? sourceUrl : null);
        }

        return Task.FromResult<string?>(null);
    }

    public Task<PanelWorkbook> ReadPanelsAsync(
        Stream workbookStream,
        CancellationToken cancellationToken = default)
    {
        ValidateWorkbookStream(workbookStream);

        cancellationToken.ThrowIfCancellationRequested();

        workbookStream.Position = 0;
        using var document = SpreadsheetDocument.Open(workbookStream, false);
        var workbookPart = document.WorkbookPart
            ?? throw new InvalidDataException("The Excel workbook is missing its workbook part.");

        var sharedStrings = GetSharedStrings(workbookPart);

        var (rows, headersByColumn, availableDays) = FindPanelWorksheet(workbookPart, sharedStrings);

        var panels = new List<Panel>(Math.Max(0, rows.Count - 1));
        foreach (var row in rows.Skip(1))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var cellsByColumn = ReadRow(row, sharedStrings)
                .ToDictionary(cell => cell.ColumnIndex, cell => cell.Value);
            var values = headersByColumn.ToDictionary(
                header => header.Value,
                header => cellsByColumn.GetValueOrDefault(header.Key, string.Empty),
                StringComparer.OrdinalIgnoreCase);

            if (values.Values.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            panels.Add(MapPanel(values));
        }

        return Task.FromResult(new PanelWorkbook
        {
            AvailableDays = availableDays,
            Panels = panels
        });
    }

    private static (
        IReadOnlyList<Row> Rows,
        IReadOnlyDictionary<int, string> HeadersByColumn,
        IReadOnlyList<string> AvailableDays)
        FindPanelWorksheet(WorkbookPart workbookPart, IReadOnlyList<string> sharedStrings)
    {
        var sheets = workbookPart.Workbook.Sheets?.Elements<Sheet>() ?? [];
        foreach (var sheet in sheets)
        {
            if (sheet.Id?.Value is not { Length: > 0 } relationshipId
                || workbookPart.GetPartById(relationshipId) is not WorksheetPart worksheetPart)
            {
                continue;
            }

            var rows = worksheetPart.Worksheet.GetFirstChild<SheetData>()?.Elements<Row>().ToList()
                ?? [];
            if (rows.Count == 0)
            {
                continue;
            }

            var headersByColumn = ReadRow(rows[0], sharedStrings)
                .Where(cell => !string.IsNullOrWhiteSpace(cell.Value))
                .ToDictionary(
                    cell => cell.ColumnIndex,
                    cell => NormalizeHeader(cell.Value),
                    EqualityComparer<int>.Default);

            var availableDays = GetAvailableDays(headersByColumn);
            if (headersByColumn.Values.Contains("WEEK_DATE", StringComparer.OrdinalIgnoreCase)
                && availableDays.Count > 0)
            {
                return (rows, headersByColumn, availableDays);
            }
        }

        throw new InvalidDataException(
            "No worksheet contains WEEK_DATE and at least one complete day column group (OPEN, pair, CLOSE).");
    }

    private static IEnumerable<(int ColumnIndex, string Value)> ReadRow(
        Row row,
        IReadOnlyList<string> sharedStrings)
    {
        foreach (var cell in row.Elements<Cell>())
        {
            yield return (GetColumnIndex(cell.CellReference?.Value), GetCellValue(cell, sharedStrings));
        }
    }

    private static void ValidateWorkbookStream(Stream workbookStream)
    {
        ArgumentNullException.ThrowIfNull(workbookStream);
        if (!workbookStream.CanRead || !workbookStream.CanSeek)
        {
            throw new ArgumentException(
                "The Excel workbook stream must be readable and seekable.",
                nameof(workbookStream));
        }
    }

    private static IReadOnlyList<string> GetSharedStrings(WorkbookPart workbookPart) =>
        workbookPart.SharedStringTablePart?.SharedStringTable
            .Elements<SharedStringItem>()
            .Select(item => item.InnerText)
            .ToArray() ?? [];

    private static bool IsSafeSourceUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string GetCellValue(Cell cell, IReadOnlyList<string> sharedStrings)
    {
        if (cell.DataType?.Value == CellValues.InlineString)
        {
            return cell.InlineString?.InnerText ?? string.Empty;
        }

        var rawValue = cell.CellValue?.InnerText ?? cell.InnerText ?? string.Empty;
        if (cell.DataType?.Value == CellValues.SharedString
            && int.TryParse(rawValue, out var sharedStringIndex)
            && sharedStringIndex >= 0
            && sharedStringIndex < sharedStrings.Count)
        {
            return sharedStrings[sharedStringIndex];
        }

        return rawValue;
    }

    private static int GetColumnIndex(string? cellReference)
    {
        if (string.IsNullOrWhiteSpace(cellReference))
        {
            throw new InvalidDataException("A worksheet cell is missing its reference.");
        }

        var columnIndex = 0;
        foreach (var character in cellReference)
        {
            if (!char.IsLetter(character))
            {
                break;
            }

            columnIndex = (columnIndex * 26) + (char.ToUpperInvariant(character) - 'A' + 1);
        }

        return columnIndex - 1;
    }

    private static IReadOnlyList<string> GetAvailableDays(
        IReadOnlyDictionary<int, string> headersByColumn)
    {
        var actualHeaders = headersByColumn.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return headersByColumn
            .OrderBy(header => header.Key)
            .Select(header => header.Value)
            .Where(header => !header.EndsWith("_OPEN", StringComparison.OrdinalIgnoreCase))
            .Where(header => !header.EndsWith("_CLOSE", StringComparison.OrdinalIgnoreCase))
            .Where(header => actualHeaders.Contains($"{header}_OPEN"))
            .Where(header => actualHeaders.Contains($"{header}_CLOSE"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeHeader(string header) => header.Trim().ToUpperInvariant();

    private static Panel MapPanel(IReadOnlyDictionary<string, string> values)
    {
        var headers = values.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var panelValues = values
            .Where(value => !string.Equals(value.Key, "WEEK_DATE", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                value => value.Key,
                value => (object?)NormalizePanelValue(value.Key, value.Value, headers),
                StringComparer.OrdinalIgnoreCase);

        return new Panel
        {
            WeekDate = values.GetValueOrDefault("WEEK_DATE", string.Empty),
            Values = panelValues
        };
    }

    private static string NormalizePanelValue(
        string header,
        string value,
        IReadOnlySet<string> headers)
    {
        var isPairColumn = headers.Contains($"{header}_OPEN")
            && headers.Contains($"{header}_CLOSE");

        return isPairColumn && value.Length == 1 && char.IsDigit(value[0])
            ? value.PadLeft(2, '0')
            : value;
    }
}

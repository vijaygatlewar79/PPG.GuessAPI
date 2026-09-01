using System.Net;
using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using HtmlAgilityPack;
using PPG.GuessData.Models;

namespace PPG.GuessData;

public sealed class ChartExcelService : IChartExcelService
{
    private readonly HttpClient _httpClient;
    private readonly IPanelFileStorage _fileStorage;

    public ChartExcelService(
        HttpClient httpClient,
        IPanelFileStorage fileStorage)
    {
        _httpClient = httpClient;
        _fileStorage = fileStorage;
    }

    public async Task<ChartExcelResult> GenerateExcelAsync(
        ChartExcelRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        ValidateUrl(request.Url);
        var fileName = BuildFileName(request.FileName);
        var existingFileNames = await _fileStorage.ListExcelFileNamesAsync(cancellationToken);
        fileName = existingFileNames.FirstOrDefault(candidate => string.Equals(
                       candidate,
                       fileName,
                       StringComparison.Ordinal))
                   ?? existingFileNames.FirstOrDefault(candidate => string.Equals(
                       candidate,
                       fileName,
                       StringComparison.OrdinalIgnoreCase))
                   ?? fileName;
        var html = await GetHtmlAsync(request.Url, cancellationToken);
        var (file, rowCount) = GenerateWorkbook(html, request.Url);

        await _fileStorage.SaveExcelFileAsync(
            fileName,
            file,
            ExcelFileBackupAction.Update,
            cancellationToken);

        return new ChartExcelResult
        {
            FileName = fileName,
            RowCount = rowCount
        };
    }

    private async Task<string> GetHtmlAsync(
        string url,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/151 Safari/537.36");
        request.Headers.TryAddWithoutValidation(
            "Accept",
            "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static (byte[] File, int RowCount) GenerateWorkbook(
        string html,
        string sourceUrl)
    {
        var document = new HtmlDocument
        {
            OptionFixNestedTags = true,
            OptionAutoCloseOnEnd = true
        };
        document.LoadHtml(html);

        var table = document.DocumentNode.SelectSingleNode("//table[contains(@class,'pchart')]")
            ?? throw new InvalidOperationException("Chart table was not found on the page.");
        // The source switches between td and th cells and omits some closing tr tags.
        // Reading every cell in document order keeps week boundaries recoverable.
        var cells = table.SelectNodes(".//th | .//td");
        if (cells is null || cells.Count == 0)
        {
            throw new InvalidOperationException("No chart data was found on the page.");
        }

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Data");

        for (var index = 0; index < Headers.Length; index++)
        {
            worksheet.Cell(1, index + 1).Value = Headers[index];
        }

        var expectedValueCount = GetExpectedPanelValueCount(cells);
        var excelRow = 2;
        for (var cellIndex = 0; cellIndex < cells.Count; cellIndex++)
        {
            var weekDate = GetDateValue(cells[cellIndex]);
            if (!WeekDatePattern.IsMatch(weekDate))
            {
                continue;
            }

            SetTextValue(worksheet, excelRow, 1, weekDate);

            var rawValues = new List<string>(Headers.Length - 1);
            for (var offset = 1; offset < Headers.Length && cellIndex + offset < cells.Count; offset++)
            {
                var sourceCell = cells[cellIndex + offset];
                if (WeekDatePattern.IsMatch(GetDateValue(sourceCell)))
                {
                    break;
                }

                rawValues.Add(GetChartValue(sourceCell));
            }

            var chartValues = ExpandChartValues(rawValues, expectedValueCount);
            for (var valueIndex = 0; valueIndex < chartValues.Count; valueIndex++)
            {
                SetTextValue(worksheet, excelRow, valueIndex + 2, chartValues[valueIndex]);
            }

            excelRow++;
            cellIndex += rawValues.Count;
        }

        var rowCount = excelRow - 2;
        if (rowCount == 0)
        {
            throw new InvalidOperationException("No chart rows could be imported from the page.");
        }

        FormatWorksheet(worksheet, excelRow - 1, Headers.Length);
        AddSourceWorksheet(
            workbook,
            sourceUrl,
            rowCount,
            worksheet.Cell(2, 1).GetString(),
            worksheet.Cell(excelRow - 1, 1).GetString());

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return (stream.ToArray(), rowCount);
    }

    private static string GetDateValue(HtmlNode cell)
    {
        var values = cell.DescendantsAndSelf()
            .Where(node => node.NodeType == HtmlNodeType.Text)
            .Select(node => WebUtility.HtmlDecode(node.InnerText).Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value));

        return Regex.Replace(string.Join(' ', values), @"\s+", " ").Trim();
    }

    private static string GetChartValue(HtmlNode cell)
    {
        var values = cell.DescendantsAndSelf()
            .Where(node => node.NodeType == HtmlNodeType.Text)
            .Select(node => WebUtility.HtmlDecode(node.InnerText).Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value));

        return string.Concat(values);
    }

    private static IReadOnlyList<string> ExpandChartValues(
        IReadOnlyList<string> rawValues,
        int expectedValueCount)
    {
        var values = new List<string>(Headers.Length - 1);
        expectedValueCount = Math.Clamp(
            expectedValueCount,
            PanelValueLengths.Length,
            Headers.Length - 1);

        foreach (var rawValue in rawValues)
        {
            if (values.Count >= expectedValueCount)
            {
                break;
            }

            var remaining = rawValue;
            if (string.IsNullOrEmpty(remaining))
            {
                values.Add(string.Empty);
                continue;
            }

            var maximumRemainingLength = Enumerable
                .Range(values.Count, expectedValueCount - values.Count)
                .Sum(index => PanelValueLengths[index % PanelValueLengths.Length]);
            if (!IsPanelToken(remaining) || remaining.Length > maximumRemainingLength)
            {
                // Malformed chart markup can make a td contain scripts or the rest of the page.
                // It is not panel data and must never be written into an Excel cell.
                values.Add(string.Empty);
                continue;
            }

            while (values.Count < expectedValueCount)
            {
                var expectedLength = PanelValueLengths[values.Count % PanelValueLengths.Length];
                if (remaining.Length <= expectedLength)
                {
                    values.Add(remaining);
                    break;
                }

                values.Add(remaining[..expectedLength]);
                remaining = remaining[expectedLength..];
            }
        }

        while (values.Count < Headers.Length - 1)
        {
            values.Add(string.Empty);
        }

        return values;
    }

    private static int GetExpectedPanelValueCount(IEnumerable<HtmlNode> cells)
    {
        var operatingDayCount = cells
            .Select(GetDateValue)
            .Select(value => WeekDatePattern.Match(value))
            .Where(match => match.Success)
            .Select(match => GetInclusiveDayCount(match.Groups[1].Value, match.Groups[2].Value))
            .Where(dayCount => dayCount is >= 1 and <= 7)
            .GroupBy(dayCount => dayCount)
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Key)
            .Select(group => group.Key)
            .FirstOrDefault();

        return operatingDayCount == 0
            ? Headers.Length - 1
            : operatingDayCount * PanelValueLengths.Length;
    }

    private static int GetInclusiveDayCount(string startValue, string endValue)
    {
        if (!TryParseChartDate(startValue, out var startDate)
            || !TryParseChartDate(endValue, out var endDate))
        {
            return 0;
        }

        return endDate.DayNumber - startDate.DayNumber + 1;
    }

    private static bool TryParseChartDate(string value, out DateOnly date) =>
        DateOnly.TryParseExact(
            value,
            ["d/M/yyyy", "dd/MM/yyyy", "d/M/yy", "dd/MM/yy"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);

    private static bool IsPanelToken(string value) =>
        value.All(character => char.IsDigit(character) || character == '*');

    private static void SetTextValue(
        IXLWorksheet worksheet,
        int row,
        int column,
        string value)
    {
        var cell = worksheet.Cell(row, column);
        cell.Style.NumberFormat.Format = "@";
        cell.Value = ToExcelCellText(value);
    }

    private static string ToExcelCellText(string value) =>
        value.Length <= ExcelCellCharacterLimit
            ? value
            : value[..ExcelCellCharacterLimit];

    private static void FormatWorksheet(
        IXLWorksheet worksheet,
        int lastRow,
        int lastColumn)
    {
        worksheet.SheetView.FreezeRows(1);
        worksheet.Row(1).Style.Font.Bold = true;

        var range = worksheet.Range(1, 1, lastRow, lastColumn);
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        range.Style.Border.TopBorder = XLBorderStyleValues.Thin;
        range.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        range.Style.Border.LeftBorder = XLBorderStyleValues.Thin;
        range.Style.Border.RightBorder = XLBorderStyleValues.Thin;

        worksheet.Column(1).Width = 28;
        for (var column = 2; column <= lastColumn; column++)
        {
            worksheet.Column(column).Width = 12;
        }

        range.SetAutoFilter();
    }

    private static void AddSourceWorksheet(
        XLWorkbook workbook,
        string sourceUrl,
        int rowCount,
        string firstRecord,
        string lastRecord)
    {
        var source = workbook.Worksheets.Add("Source");
        var sourceFile = Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri)
            ? Path.GetFileName(uri.AbsolutePath)
            : string.Empty;

        source.Cell("A1").Value = "Field";
        source.Cell("B1").Value = "Value";
        source.Cell("A2").Value = "Source file";
        source.Cell("B2").Value = ToExcelCellText(sourceFile);
        source.Cell("A3").Value = "Source URL";
        source.Cell("B3").Value = ToExcelCellText(sourceUrl);
        source.Cell("A4").Value = "Rows imported";
        source.Cell("B4").Value = rowCount;
        source.Cell("A5").Value = "First record";
        source.Cell("B5").Value = ToExcelCellText(firstRecord);
        source.Cell("A6").Value = "Last record";
        source.Cell("B6").Value = ToExcelCellText(lastRecord);

        source.Range("A1:B1").Style.Font.Bold = true;
        source.Range("A1:B1").Style.Fill.BackgroundColor = XLColor.FromHtml("#1F4E78");
        source.Range("A1:B1").Style.Font.FontColor = XLColor.White;
        source.Range("A1:B6").Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        source.Range("A1:B6").Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        source.Range("B2:B6").Style.NumberFormat.Format = "@";
        source.Column(1).Width = 24;
        source.Column(2).Width = 90;
        source.SheetView.FreezeRows(1);
    }

    private static string BuildFileName(string requestedFileName)
    {
        if (string.IsNullOrWhiteSpace(requestedFileName))
        {
            throw new ArgumentException("File name is required.", nameof(requestedFileName));
        }

        var fileName = requestedFileName.Trim();
        if (!fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            fileName += ".xlsx";
        }

        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalidCharacter, '_');
        }

        fileName = fileName.Replace('/', '_').Replace('\\', '_');

        return fileName;
    }

    private static void ValidateUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("URL is required.", nameof(url));
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Enter a valid HTTP or HTTPS URL.", nameof(url));
        }

        if (!AllowedDomains.Contains(uri.Host))
        {
            throw new ArgumentException($"Domain '{uri.Host}' is not allowed.", nameof(url));
        }
    }

    private static readonly HashSet<string> AllowedDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "sattamatkadpboss.mobi",
        "www.sattamatkadpboss.mobi",
        "sattakalyanmatka.net",
        "www.sattakalyanmatka.net"
    };

    private static readonly Regex WeekDatePattern = new(
        @"^(\d{1,2}/\d{1,2}/\d{2,4})\s+to\s+(\d{1,2}/\d{1,2}/\d{2,4})$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly int[] PanelValueLengths = [3, 2, 3];

    private const int ExcelCellCharacterLimit = 32_767;

    private static readonly string[] Headers =
    [
        "WEEK_DATE",
        "MON_OPEN", "MON", "MON_CLOSE",
        "TUE_OPEN", "TUE", "TUE_CLOSE",
        "WED_OPEN", "WED", "WED_CLOSE",
        "THU_OPEN", "THU", "THU_CLOSE",
        "FRI_OPEN", "FRI", "FRI_CLOSE",
        "SAT_OPEN", "SAT", "SAT_CLOSE",
        "SUN_OPEN", "SUN", "SUN_CLOSE"
    ];
}

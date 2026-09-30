namespace PPG.GuessData.Models;

public sealed class TripleSumAnalysisRequest
{
    public string FileName { get; init; } = string.Empty;

    public PanelNumberType NumberType { get; init; } = PanelNumberType.Open;

    public int Number { get; init; }

    // Zero means all available Excel history.
    public int PeriodMonths { get; init; }
}

public sealed class TripleSumAnalysisResult
{
    public int Number { get; init; }

    public PanelNumberType NumberType { get; init; }

    public int TotalCount { get; init; }

    public IReadOnlyList<TripleSumAnalysisRow> Numbers { get; init; } = [];

    public IReadOnlyList<TripleNumberSummaryRow> Summary { get; init; } = [];
}

public sealed class TripleSumAnalysisRow
{
    public string TripleNumber { get; init; } = string.Empty;

    public int Count { get; init; }
}

public sealed class TripleNumberSummaryRow
{
    public int Number { get; init; }

    public IReadOnlyList<TripleSumAnalysisRow> Triples { get; init; } = [];

    public IReadOnlyList<TripleSumAnalysisRow> MissingTriples { get; init; } = [];
}

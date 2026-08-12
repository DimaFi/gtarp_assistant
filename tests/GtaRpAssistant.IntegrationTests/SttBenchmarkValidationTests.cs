namespace GtaRpAssistant.IntegrationTests;

public sealed class SttBenchmarkValidationTests
{
    [Fact]
    public void CompleteDataset_IsAccepted()
    {
        var dataset = Dataset([Case("one")]);

        SttDatasetValidation.Validate(dataset);
    }

    [Fact]
    public void DuplicateCaseIds_AreRejected()
    {
        var dataset = Dataset([Case("same"), Case("SAME")]);

        var error = Assert.Throws<InvalidDataException>(() => SttDatasetValidation.Validate(dataset));

        Assert.Contains("Duplicate", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidQualityThreshold_IsRejected()
    {
        var dataset = Dataset([Case("one")]) with
        {
            Gate = new SttGate(1, MaximumAverageWordErrorRate: 1.1),
        };

        var error = Assert.Throws<InvalidDataException>(() => SttDatasetValidation.Validate(dataset));

        Assert.Contains("between 0 and 1", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Би Пи", "БПишки")]
    [InlineData("BP", "бонус-поинты")]
    [InlineData("Ди Пи", "ДПишки")]
    [InlineData("DP", "донатная валюта")]
    public void CurrencyPronunciations_AreEquivalentForScoring(string reference, string transcript)
    {
        Assert.Equal(0, SttTextMetrics.WordErrorRate(reference, transcript));
        Assert.Equal(1, SttTextMetrics.TermRecall([reference], transcript));
    }

    [Fact]
    public void BonusPointsAndDonatePoints_AreNotEquivalentForScoring()
    {
        Assert.Equal(1, SttTextMetrics.WordErrorRate("BP", "DP"));
        Assert.Equal(0, SttTextMetrics.TermRecall(["BP"], "DP"));
    }

    private static SttDataset Dataset(IReadOnlyList<SttCase> cases) =>
        new("dataset", new SttGate(MinimumCases: 1), cases);

    private static SttCase Case(string id) => new(id, $"audio/{id}.wav", "Тестовая фраза", []);
}

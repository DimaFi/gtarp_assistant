using GtaRpAssistant.Core;

namespace GtaRpAssistant.Core.Tests;

public sealed class ScreenContextTests
{
    [Fact]
    public void FrameDiffer_IgnoresSmallPixelNoise()
    {
        var previous = Frame(16, 12, 100);
        var current = Frame(16, 12, 110);
        var diff = new GridScreenFrameDiffer().Compare(previous, current);
        Assert.Equal(0, diff.ChangedRatio);
        Assert.Empty(diff.ChangedRegions);
    }

    [Fact]
    public void FrameDiffer_ReportsChangedGridRegions()
    {
        var previous = Frame(16, 12, 0);
        var current = Frame(16, 12, 0);
        Array.Fill(current.GrayscalePixels, (byte)255, 0, 48);
        var diff = new GridScreenFrameDiffer().Compare(previous, current);
        Assert.Equal(.25, diff.ChangedRatio, 3);
        Assert.NotEmpty(diff.ChangedRegions);
    }

    [Fact]
    public void Store_DropsExpiredSnapshot()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new ScreenContextStore();
        store.Publish(new(now, KnownScreenKind.Dialog, .8, [], [], [], now.AddSeconds(1)));
        Assert.NotNull(store.GetFresh(now));
        Assert.Null(store.GetFresh(now.AddSeconds(2)));
    }

    [Theory]
    [InlineData("Магазин купить цена", KnownScreenKind.Shop)]
    [InlineData("Инвентарь использовать", KnownScreenKind.Inventory)]
    [InlineData("Квест награда", KnownScreenKind.Quest)]
    public void Recognizer_UsesKnownScreenAnchors(string text, KnownScreenKind expected)
    {
        var result = KnownScreenRecognizer.Recognize([new("text", text, .9, ScreenRegion.Full)]);
        Assert.Equal(expected, result.Kind);
        Assert.True(result.Confidence > .7);
    }

    [Theory]
    [InlineData("Что написано на экране?", true)]
    [InlineData("Как начать рыбалку?", false)]
    public void QuestionClassifier_LimitsScreenContextToRelevantQuestions(string question, bool expected) =>
        Assert.Equal(expected, ScreenQuestionClassifier.NeedsScreenContext(question));

    private static ScreenFrame Frame(int width, int height, byte value) => new(width, height, Enumerable.Repeat(value, width * height).ToArray(), DateTimeOffset.UtcNow);
}

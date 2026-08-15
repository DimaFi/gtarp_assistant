using GtaRpAssistant.Infrastructure.Windows;

namespace GtaRpAssistant.IntegrationTests;

public sealed class ScreenOcrTests
{
    [Fact]
    public void TesseractTsv_IsNormalizedAndLowConfidenceTextIsDropped()
    {
        const string tsv = "level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext\n"
            + "5\t1\t1\t1\t1\t1\t100\t200\t400\t50\t91.5\tМагазин\n"
            + "5\t1\t1\t1\t1\t2\t0\t0\t10\t10\t12\tшум\n";

        var result = TesseractScreenOcr.ParseTsv(tsv, 1000, 1000);

        var field = Assert.Single(result.Fields);
        Assert.Equal("Магазин", field.Text);
        Assert.Equal(.1, field.Bounds.X, 3);
        Assert.Equal(.2, field.Bounds.Y, 3);
        Assert.Equal(.4, field.Bounds.Width, 3);
    }
}

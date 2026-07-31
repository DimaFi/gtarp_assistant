using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Documents;
using GtaRpAssistant.App.DesignSystem.Controls;

namespace GtaRpAssistant.App.Tests;

public sealed class MarkdownTextBlockTests
{
    [Fact]
    public void Renderer_UsesSafeNativeInlinesForSupportedMarkdown()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var block = new MarkdownTextBlock
                {
                    Markdown = "# Заголовок\n- **важно** и `код`\n<a href='https://example.test'>ссылка</a>",
                };
                var runs = block.Inlines.OfType<Run>().ToArray();
                var text = string.Concat(runs.Select(x => x.Text));

                Assert.Contains("Заголовок", text);
                Assert.Contains("• ", text);
                Assert.Contains("важно", text);
                Assert.Contains("код", text);
                Assert.Contains("<a href=", text);
                Assert.Contains(runs, run => run.Text == "важно" && run.FontWeight == FontWeights.SemiBold);
                Assert.Contains(runs, run => run.Text == "код" && run.FontFamily.Source == "Consolas");
                Assert.DoesNotContain(block.Inlines, inline => inline is Hyperlink);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}

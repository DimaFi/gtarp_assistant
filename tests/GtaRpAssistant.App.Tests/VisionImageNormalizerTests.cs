using GtaRpAssistant.App;
using System.IO;

namespace GtaRpAssistant.App.Tests;

public sealed class VisionImageNormalizerTests
{
    [Fact]
    public void ValidPng_IsDecodedAndNormalized()
    {
        var source = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        var png = VisionImageNormalizer.NormalizeToPng(source);
        Assert.True(png.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
    }

    [Fact]
    public void InvalidImage_IsRejected()
    {
        Assert.Throws<InvalidDataException>(() => VisionImageNormalizer.NormalizeToPng([1, 2, 3, 4]));
    }
}

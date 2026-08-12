using System.Buffers.Binary;
using System.Text;
using SherpaOnnx;

if (args.Length != 3 || !int.TryParse(args[2], out var threads) || threads is < 1 or > 8)
{
    Console.Error.WriteLine("Usage: GtaRpAssistant.SttHost <model.onnx> <tokens.txt> <threads>");
    return 2;
}

var config = new OfflineRecognizerConfig();
config.FeatConfig.SampleRate = 16_000;
config.FeatConfig.FeatureDim = 64;
config.ModelConfig.Tokens = Path.GetFullPath(args[1]);
config.ModelConfig.NeMoCtc.Model = Path.GetFullPath(args[0]);
config.ModelConfig.NumThreads = threads;
config.ModelConfig.Provider = "cpu";
config.ModelConfig.Debug = 0;
config.DecodingMethod = "greedy_search";

using var recognizer = new OfflineRecognizer(config);
var input = Console.OpenStandardInput();
var output = Console.OpenStandardOutput();
Console.Error.WriteLine("READY");

var header = new byte[5];
while (await ReadExactlyOrEofAsync(input, header))
{
    var command = header[0];
    var byteCount = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(1));
    if (byteCount < 0 || byteCount > 16_000 * 2 * 180) return 3;
    var payload = new byte[byteCount];
    await input.ReadExactlyAsync(payload);
    try
    {
        var text = command switch
        {
            0 when byteCount == 0 => "READY",
            1 when byteCount > 0 && byteCount % 2 == 0 => Recognize(recognizer, payload),
            _ => throw new InvalidDataException("Invalid STT host request."),
        };
        await WriteResponseAsync(output, 0, text);
    }
    catch (Exception exception)
    {
        await WriteResponseAsync(output, 1, exception.Message);
    }
}
return 0;

static string Recognize(OfflineRecognizer recognizer, byte[] pcm)
{
    var samples = new float[pcm.Length / 2];
    for (var i = 0; i < samples.Length; i++)
        samples[i] = BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(i * 2, 2)) / 32768f;
    using var stream = recognizer.CreateStream();
    stream.AcceptWaveform(16_000, samples);
    recognizer.Decode(stream);
    return stream.Result.Text.Trim();
}

static async Task<bool> ReadExactlyOrEofAsync(Stream stream, byte[] buffer)
{
    var offset = 0;
    while (offset < buffer.Length)
    {
        var read = await stream.ReadAsync(buffer.AsMemory(offset));
        if (read == 0) return offset == 0 ? false : throw new EndOfStreamException();
        offset += read;
    }
    return true;
}

static async Task WriteResponseAsync(Stream output, byte status, string value)
{
    var payload = Encoding.UTF8.GetBytes(value);
    var header = new byte[5];
    header[0] = status;
    BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(1), payload.Length);
    await output.WriteAsync(header);
    await output.WriteAsync(payload);
    await output.FlushAsync();
}

using System.Buffers.Binary;
using System.Text.Json;
using GtaRpAssistant.Core;
using GtaRpAssistant.Infrastructure.Windows;

public static class SttDatasetRecorder
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static int ListDevices()
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Microphone discovery is available only on Windows.");
            return 1;
        }
        var devices = WasapiDeviceCatalog.GetActiveMicrophones();
        if (devices.Count == 0)
        {
            Console.Error.WriteLine("No active microphone was found.");
            return 2;
        }
        foreach (var device in devices)
        {
            Console.WriteLine(device.IsDefault ? "* default" : "  device");
            Console.WriteLine($"  {device.DisplayName}");
            Console.WriteLine($"  {device.Id}");
        }
        return 0;
    }

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length is < 1 or > 3)
        {
            Console.Error.WriteLine("Usage: GtaRpAssistant.SttBenchmark record <dataset.json> [device-id] [--overwrite]");
            return 1;
        }
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Microphone recording is available only on Windows.");
            return 1;
        }
        if (Console.IsInputRedirected)
        {
            Console.Error.WriteLine("Recording requires an interactive console and explicit user input.");
            return 1;
        }

        var datasetPath = Path.GetFullPath(args[0]);
        var overwrite = args.Any(value => value.Equals("--overwrite", StringComparison.OrdinalIgnoreCase));
        var requestedDevice = args.Skip(1).FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
        var dataset = JsonSerializer.Deserialize<SttDataset>(await File.ReadAllTextAsync(datasetPath), Json)
            ?? throw new InvalidDataException("STT dataset is empty.");
        SttDatasetValidation.Validate(dataset);
        var devices = WasapiDeviceCatalog.GetActiveMicrophones();
        if (devices.Count == 0) throw new InvalidOperationException("No active microphone was found.");
        var device = ResolveDevice(devices, requestedDevice);
        var datasetDirectory = Path.GetDirectoryName(datasetPath)!;

        Console.WriteLine($"Dataset: {dataset.Id}; phrases: {dataset.Cases.Count}");
        Console.WriteLine($"Microphone: {device.DisplayName}");
        Console.WriteLine("Audio is saved only into this developer dataset after you press Enter. No network is used.");
        Console.WriteLine("For each phrase: Enter = record, S = skip, Q = stop. Press Enter again to finish (maximum 30 seconds).\n");

        var recorded = 0;
        foreach (var item in dataset.Cases)
        {
            var outputPath = SafeDatasetPath(datasetDirectory, item.AudioFile);
            if (File.Exists(outputPath) && !overwrite)
            {
                Console.WriteLine($"[{item.Id}] already exists; skipped.");
                continue;
            }

            Console.WriteLine($"[{item.Id}] Read exactly: {item.Reference}");
            Console.Write("Enter/S/Q: ");
            var command = Console.ReadLine()?.Trim();
            if (command?.Equals("q", StringComparison.OrdinalIgnoreCase) == true) break;
            if (command?.Equals("s", StringComparison.OrdinalIgnoreCase) == true) continue;

            var pcm = await CaptureAsync(device.Id);
            if (pcm.Length < 16_000)
            {
                Console.WriteLine("Recording is shorter than 0.5 seconds and was not saved. Try this phrase again later.\n");
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            WriteWaveAtomically(outputPath, pcm);
            recorded++;
            Console.WriteLine($"Saved {pcm.Length / 2d / 16_000d:F1} s: {outputPath}\n");
        }

        var present = dataset.Cases.Count(item => File.Exists(SafeDatasetPath(datasetDirectory, item.AudioFile)));
        Console.WriteLine($"Recorded now: {recorded}; dataset files present: {present}/{dataset.Cases.Count}.");
        return present >= dataset.Gate.MinimumCases ? 0 : 2;
    }

    private static MicrophoneDeviceInfo ResolveDevice(IReadOnlyList<MicrophoneDeviceInfo> devices, string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested)) return devices.FirstOrDefault(device => device.IsDefault) ?? devices[0];
        return devices.FirstOrDefault(device => device.Id.Equals(requested, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Requested microphone is not active: {requested}");
    }

    private static async Task<byte[]> CaptureAsync(string deviceId)
    {
        var frames = new List<short[]>();
        var sync = new object();
        await using var capture = new WasapiMicrophoneCaptureService(deviceId);
        capture.FrameCaptured += OnFrame;
        try
        {
            await capture.StartAsync(CancellationToken.None);
            Console.Write("Recording... press Enter to stop: ");
            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
            while (DateTimeOffset.UtcNow < deadline)
            {
                if (Console.KeyAvailable && Console.ReadKey(intercept: true).Key == ConsoleKey.Enter) break;
                await Task.Delay(50);
            }
            await capture.StopAsync(CancellationToken.None);
        }
        finally { capture.FrameCaptured -= OnFrame; }

        short[][] snapshot;
        lock (sync) snapshot = frames.ToArray();
        var sampleCount = snapshot.Sum(frame => frame.Length);
        var pcm = new byte[checked(sampleCount * sizeof(short))];
        var offset = 0;
        foreach (var frame in snapshot)
        {
            foreach (var sample in frame)
            {
                BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(offset, 2), sample);
                offset += 2;
            }
        }
        return pcm;

        void OnFrame(object? _, AudioFrameEventArgs frame)
        {
            if (frame.SampleRate != 16_000 || frame.Source != AudioSourceKind.UserMicrophone) return;
            lock (sync) frames.Add(frame.Samples.ToArray());
        }
    }

    private static string SafeDatasetPath(string directory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new InvalidDataException("Dataset audio paths must be relative.");
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Dataset audio path escapes the dataset directory.");
        return path;
    }

    private static void WriteWaveAtomically(string path, ReadOnlySpan<byte> pcm)
    {
        var wave = new byte[44 + pcm.Length];
        "RIFF"u8.CopyTo(wave); BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(4), 36 + pcm.Length);
        "WAVEfmt "u8.CopyTo(wave.AsSpan(8)); BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(20), 1); BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(22), 1);
        BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(24), 16_000); BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(28), 32_000);
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(32), 2); BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(34), 16);
        "data"u8.CopyTo(wave.AsSpan(36)); BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(40), pcm.Length); pcm.CopyTo(wave.AsSpan(44));
        var partial = path + ".partial";
        try
        {
            File.WriteAllBytes(partial, wave);
            File.Move(partial, path, overwrite: true);
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }
}

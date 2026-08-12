using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using GtaRpAssistant.Core;

namespace GtaRpAssistant.Infrastructure.Windows;

public sealed class LmStudioBootstrapInstaller : ILocalAiBootstrapInstaller
{
    internal static readonly Uri InstallerUri = new("https://lmstudio.ai/install.ps1");
    private const int MaximumScriptBytes = 1024 * 1024;
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(15);

    public async Task<LocalAiBootstrapInstallResult> InstallAsync(string installHome, CancellationToken cancellationToken)
    {
        var home = ValidateInstallHome(installHome);
        Directory.CreateDirectory(home);

        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true })
        {
            Timeout = TimeSpan.FromSeconds(45),
        };
        using var response = await client.GetAsync(InstallerUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var finalUri = response.RequestMessage?.RequestUri;
        if (finalUri is null || finalUri.Scheme != Uri.UriSchemeHttps || !string.Equals(finalUri.Host, InstallerUri.Host, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Официальный установщик перенаправил запрос на недоверенный адрес.");
        if (response.Content.Headers.ContentLength is > MaximumScriptBytes)
            throw new InvalidOperationException("Официальный установочный сценарий имеет неожиданный размер.");

        var script = await ReadBoundedAsync(response.Content, MaximumScriptBytes, cancellationToken);
        ValidateInstallerScript(script);
        var hash = Convert.ToHexString(SHA256.HashData(script)).ToLowerInvariant();
        var scriptPath = Path.Combine(home, $"lmstudio-installer-{Guid.NewGuid():N}.ps1");
        await File.WriteAllBytesAsync(scriptPath, script, cancellationToken);

        try
        {
            using var process = new Process { StartInfo = CreateStartInfo(scriptPath, home), EnableRaisingEvents = true };
            if (!process.Start()) throw new InvalidOperationException("Не удалось запустить официальный установщик LM Studio Core.");
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            using var timeout = new CancellationTokenSource(InstallTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            try
            {
                await process.WaitForExitAsync(linked.Token);
            }
            catch (OperationCanceledException)
            {
                TryKillTree(process);
                if (cancellationToken.IsCancellationRequested) throw;
                throw new TimeoutException("Установка LM Studio Core не завершилась за 15 минут.");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Установщик LM Studio Core завершился с кодом {process.ExitCode}: {LastUsefulLine(stderr, stdout)}");

            var cliPath = Path.Combine(home, ".lmstudio", "bin", "lms.exe");
            if (!File.Exists(cliPath))
                throw new FileNotFoundException("Установка завершилась, но lms.exe не найден в выбранной папке.", cliPath);
            return new(cliPath, home, hash);
        }
        finally
        {
            try { File.Delete(scriptPath); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    internal static string ValidateInstallHome(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Выберите папку установки.", nameof(value));
        var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(value.Trim().Trim('"')));
        if (Path.GetPathRoot(fullPath)?.TrimEnd(Path.DirectorySeparatorChar) == fullPath.TrimEnd(Path.DirectorySeparatorChar))
            throw new ArgumentException("Нельзя устанавливать движок прямо в корень диска.", nameof(value));
        return fullPath;
    }

    internal static void ValidateInstallerScript(ReadOnlySpan<byte> script)
    {
        var text = Encoding.UTF8.GetString(script);
        if (!text.Contains("$APP_NAME = 'llmster'", StringComparison.Ordinal)
            || !text.Contains("Test-Checksum", StringComparison.Ordinal)
            || !text.Contains("Install-Llmster", StringComparison.Ordinal))
            throw new InvalidDataException("Полученный файл не похож на официальный установщик LM Studio Core.");
    }

    internal static ProcessStartInfo CreateStartInfo(string scriptPath, string home)
    {
        var info = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add("-NoLogo");
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-NonInteractive");
        info.ArgumentList.Add("-ExecutionPolicy");
        info.ArgumentList.Add("Bypass");
        info.ArgumentList.Add("-File");
        info.ArgumentList.Add(scriptPath);
        info.ArgumentList.Add("--quiet");
        info.ArgumentList.Add("--no-modify-path");
        info.Environment["HOME"] = home;
        info.Environment["LMS_NO_MODIFY_PATH"] = "1";
        return info;
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, int maximumBytes, CancellationToken cancellationToken)
    {
        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        using var destination = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (destination.Length + read > maximumBytes) throw new InvalidOperationException("Официальный установочный сценарий имеет неожиданный размер.");
            destination.Write(buffer, 0, read);
        }
        return destination.ToArray();
    }

    private static string LastUsefulLine(params string[] outputs) => outputs
        .SelectMany(x => x.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        .LastOrDefault() ?? "диагностика отсутствует";

    private static void TryKillTree(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
    }
}

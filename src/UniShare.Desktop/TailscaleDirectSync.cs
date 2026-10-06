using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using UniShare.Infrastructure;

namespace UniShare.Desktop;

internal static class TailscaleDirectSync
{
    public static async Task<string?> TryConfigureAsync(
        DirectSyncServer server,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var serve = await RunAsync(
                ["serve", "--bg", "--https=8443", server.Configuration.Port.ToString(CultureInfo.InvariantCulture)],
                cancellationToken);
            if (serve.ExitCode != 0)
            {
                return "Tailscale no pudo publicar UniShare: " + UsefulError(serve);
            }

            var status = await RunAsync(["status", "--json"], cancellationToken);
            if (status.ExitCode != 0)
            {
                return "Tailscale no pudo obtener la dirección privada: " + UsefulError(status);
            }

            using var document = JsonDocument.Parse(status.StandardOutput);
            var dnsName = document.RootElement.GetProperty("Self").GetProperty("DNSName")
                .GetString()?.Trim().TrimEnd('.');
            if (string.IsNullOrWhiteSpace(dnsName))
            {
                return "Tailscale no informó el nombre privado de este ordenador.";
            }

            server.UpdatePublicEndpoint($"https://{dnsName}:8443");
            return null;
        }
        catch (OperationCanceledException)
        {
            return "La configuración automática de Tailscale agotó el tiempo de espera.";
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return "No se pudo configurar Tailscale automáticamente: " + error.Message;
        }
    }

    private static async Task<ProcessResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var startInfo = new ProcessStartInfo
        {
            FileName = "tailscale.exe",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Tailscale no se inició.");
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            throw;
        }
        return new ProcessResult(process.ExitCode, await output, await error);
    }

    private static string UsefulError(ProcessResult result) =>
        string.IsNullOrWhiteSpace(result.StandardError)
            ? string.IsNullOrWhiteSpace(result.StandardOutput) ? $"código {result.ExitCode}" : result.StandardOutput.Trim()
            : result.StandardError.Trim();

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}

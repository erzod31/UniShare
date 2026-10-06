using System.Text.Json;
using UniShare.Application;
using UniShare.Infrastructure;

namespace UniShare.Backup;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args.Contains("--help", StringComparer.OrdinalIgnoreCase))
            {
                PrintUsage();
                return args.Length == 0 ? 2 : 0;
            }

            var command = args[0].ToLowerInvariant();
            object summary = command switch
            {
                "export" => await ExportAsync(args),
                "inspect" => await InspectAsync(args),
                "restore" => await RestoreAsync(args),
                _ => throw new ArgumentException($"Comando desconocido: {args[0]}"),
            };
            Console.WriteLine(JsonSerializer.Serialize(summary, JsonOptions));
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
            return 1;
        }
    }

    private static Task<BackupSummary> ExportAsync(string[] args)
    {
        var profile = RequiredOption(args, "--profile");
        var output = RequiredOption(args, "--output");
        return new PortableBackupService(profile).ExportAsync(output);
    }

    private static Task<BackupSummary> InspectAsync(string[] args)
    {
        var input = RequiredOption(args, "--input");
        return new PortableBackupService(Directory.GetCurrentDirectory()).InspectAsync(input);
    }

    private static Task<BackupSummary> RestoreAsync(string[] args)
    {
        var input = RequiredOption(args, "--input");
        var profile = RequiredOption(args, "--profile");
        return new PortableBackupService(Directory.GetCurrentDirectory())
            .RestoreToNewProfileAsync(input, profile);
    }

    private static string RequiredOption(string[] args, string name)
    {
        for (var index = 1; index < args.Length; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                {
                    break;
                }

                return Path.GetFullPath(args[index + 1]);
            }
        }

        throw new ArgumentException($"Falta la opción obligatoria {name}.");
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            UniShare Backup

            export  --profile <ruta> --output <respaldo.unishare.zip>
            inspect --input <respaldo.unishare.zip>
            restore --input <respaldo.unishare.zip> --profile <nueva-ruta>
            restore nunca sobrescribe un perfil existente.
            """);
    }
}

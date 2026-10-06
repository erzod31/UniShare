using System.Net;
using UniShare.Application;
using UniShare.Infrastructure;

if (args.Length != 2 || !Guid.TryParse(args[1], out var jobId))
{
    Console.Error.WriteLine("Uso: UniShare.DownloadCrashHarness <perfil> <job-id>");
    return 2;
}

var profileRoot = Path.GetFullPath(args[0]);
var repository = new SqliteLibraryRepository(Path.Combine(profileRoot, "library.db"));
await repository.InitializeAsync();
if (await repository.GetDownloadAsync(jobId) is null)
{
    Console.Error.WriteLine("La descarga indicada no existe.");
    return 3;
}

using var downloads = new DurableDownloadService(
    repository,
    new FileBlobStore(profileRoot),
    new FixedAddressPolicy(IPAddress.Loopback),
    profileRoot,
    TimeProvider.System);
await downloads.RunAsync(jobId);
return 0;

internal sealed class FixedAddressPolicy(IPAddress address) : IRemoteEndpointPolicy
{
    public Task<IReadOnlyList<IPAddress>> ResolveAndValidateAsync(
        Uri uri,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<IPAddress>>([address]);
}

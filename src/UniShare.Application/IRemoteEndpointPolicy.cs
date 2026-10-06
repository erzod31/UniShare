using System.Net;

namespace UniShare.Application;

public interface IRemoteEndpointPolicy
{
    Task<IReadOnlyList<IPAddress>> ResolveAndValidateAsync(
        Uri uri,
        CancellationToken cancellationToken = default);
}

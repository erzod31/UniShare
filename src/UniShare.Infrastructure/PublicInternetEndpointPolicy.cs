using System.Net;
using System.Net.Sockets;
using UniShare.Application;
using UniShare.Domain;

namespace UniShare.Infrastructure;

public sealed class PublicInternetEndpointPolicy : IRemoteEndpointPolicy
{
    public async Task<IReadOnlyList<IPAddress>> ResolveAndValidateAsync(
        Uri uri,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        _ = UrlCanonicalizer.ParseHttpUrl(uri.AbsoluteUri);
        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, cancellationToken);
        }
        catch (SocketException exception)
        {
            throw new InvalidOperationException("No se pudo resolver el host remoto.", exception);
        }

        if (addresses.Length == 0 || addresses.Any(IsProhibited))
        {
            throw new InvalidOperationException("La URL resuelve a una red local, privada o no permitida.");
        }

        return addresses;
    }

    private static bool IsProhibited(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.None) ||
            address.Equals(IPAddress.IPv6None))
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] switch
            {
                0 or 10 or 127 => true,
                100 when bytes[1] is >= 64 and <= 127 => true,
                169 when bytes[1] == 254 => true,
                172 when bytes[1] is >= 16 and <= 31 => true,
                192 when bytes[1] == 0 && (bytes[2] == 0 || bytes[2] == 2) => true,
                192 when bytes[1] == 168 => true,
                198 when bytes[1] is 18 or 19 => true,
                198 when bytes[1] == 51 && bytes[2] == 100 => true,
                203 when bytes[1] == 0 && bytes[2] == 113 => true,
                >= 224 => true,
                _ => false,
            };
        }

        var isDocumentationV6 = bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8;
        return address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal ||
            (bytes[0] & 0xfe) == 0xfc || isDocumentationV6;
    }
}

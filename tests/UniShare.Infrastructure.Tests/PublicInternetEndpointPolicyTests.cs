using System.Net;
using UniShare.Infrastructure;

namespace UniShare.Infrastructure.Tests;

public sealed class PublicInternetEndpointPolicyTests
{
    [Theory]
    [InlineData("http://127.0.0.1/resource")]
    [InlineData("http://10.1.2.3/resource")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://[::1]/resource")]
    [InlineData("http://[fc00::1]/resource")]
    public async Task ValidateRejectsPrivateLocalAndMetadataAddresses(string value)
    {
        var policy = new PublicInternetEndpointPolicy();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policy.ResolveAndValidateAsync(new Uri(value), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ValidateAcceptsPublicLiteralAddress()
    {
        var policy = new PublicInternetEndpointPolicy();

        var addresses = await policy.ResolveAndValidateAsync(new Uri("https://93.184.216.34/resource"), TestContext.Current.CancellationToken);

        Assert.Equal(IPAddress.Parse("93.184.216.34"), Assert.Single(addresses));
    }
}

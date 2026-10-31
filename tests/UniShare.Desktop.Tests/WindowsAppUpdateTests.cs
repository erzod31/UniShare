using System.Diagnostics;
using UniShare.Application;
using UniShare.Desktop;

namespace UniShare.Desktop.Tests;

public sealed class WindowsAppUpdateTests
{
    private static readonly AppUpdateCandidate Candidate = new("erzod31/UniShare", "0.6.25", "windows-x64",
        "UniShare-Windows-Setup-0.6.25.exe", new Uri("https://github.com/erzod31/UniShare/releases/download/v0.6.25/UniShare-Windows-Setup-0.6.25.exe"), 1, new string('a', 64));

    [Theory]
    [InlineData("product")]
    [InlineData("version")]
    [InlineData("architecture")]
    [InlineData("signature")]
    [InlineData("publisher")]
    [InlineData("unsigned-downgrade")]
    public async Task PackageIdentityAndExistingPublisherAreMandatory(string failure)
    {
        var identity = new WindowsInstallerIdentity("UniShare", "UniShare Setup", "0.6.25", 0x014c, true, true, "abc");
        identity = failure switch
        {
            "product" => identity with { Product = "Other" },
            "version" => identity with { Version = "0.6.16" },
            "architecture" => identity with { Machine = 0x01c4 },
            "signature" => identity with { SignatureValid = false },
            "publisher" => identity with { CertificateSha256 = "other" },
            _ => identity with { Signed = false, CertificateSha256 = null },
        };
        var verifier = new WindowsUpdatePackageVerifier("abc", _ => identity);
        await Assert.ThrowsAsync<System.IO.InvalidDataException>(() => verifier.VerifyAsync("unused", Candidate, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LegacyUnsignedPolicyIsExplicitAndValidSignedPackageIsAccepted()
    {
        var identity = new WindowsInstallerIdentity("UniShare", "UniShare Setup", "0.6.25", 0x014c, false, false, null);
        Assert.Equal("legacy-unsigned", await new WindowsUpdatePackageVerifier(null, _ => identity).VerifyAsync("unused", Candidate, TestContext.Current.CancellationToken));
        Assert.Equal("verified", await new WindowsUpdatePackageVerifier("abc", _ => identity with
        { Signed = true, SignatureValid = true, CertificateSha256 = "abc" }).VerifyAsync("unused", Candidate, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NativeHandoffIsMockedAndShutdownOnlyFollowsSuccessfulLaunch()
    {
        ProcessStartInfo? request = null;
        var shutdowns = 0;
        var installer = new WindowsUpdateInstaller("C:\\synthetic profile", () => { shutdowns++; return Task.CompletedTask; },
            info => { request = info; return true; });
        await installer.RequestAsync(new(Candidate, "C:\\synthetic\\package.exe", "synthetic"), TestContext.Current.CancellationToken);
        Assert.Equal(1, shutdowns);
        Assert.NotNull(request);
        Assert.True(request.UseShellExecute);
        Assert.Contains("/UNISHAREUPDATE=1", request.ArgumentList);
        Assert.Contains("/UNISHAREPROFILE=C:\\synthetic profile", request.ArgumentList);
        Assert.DoesNotContain(request.ArgumentList, item => item.Contains("SILENT", StringComparison.OrdinalIgnoreCase));
        var rejected = new WindowsUpdateInstaller("C:\\synthetic profile", () => { shutdowns++; return Task.CompletedTask; }, _ => false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => rejected.RequestAsync(new(Candidate, "unused", "synthetic"), TestContext.Current.CancellationToken));
        Assert.Equal(1, shutdowns);
    }

    [Fact]
    public void MalformedEmbeddedCertificateNeverBecomesLegacyUnsigned()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "unishare-bad-signature-" + Guid.NewGuid().ToString("N") + ".exe");
        try
        {
            // Copy a real PE, append an invalid WIN_CERTIFICATE and point its security directory
            // to that block. This is inspected by WinVerifyTrust; the file is never executed.
            System.IO.File.Copy(typeof(WindowsUpdatePackageVerifier).Assembly.Location, path);
            using (var stream = System.IO.File.Open(path, System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite))
            using (var reader = new System.IO.BinaryReader(stream, System.Text.Encoding.UTF8, true))
            using (var writer = new System.IO.BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            {
                stream.Position = 0x3c;
                var pe = reader.ReadInt32();
                stream.Position = pe + 24;
                var magic = reader.ReadUInt16();
                var directory = pe + 24 + (magic == 0x20b ? 112 : 96) + 32;
                var certificateOffset = checked((uint)((stream.Length + 7) & ~7L));
                stream.SetLength(certificateOffset + 16);
                stream.Position = certificateOffset;
                writer.Write(16U);
                writer.Write((ushort)0x200);
                writer.Write((ushort)2);
                writer.Write(new byte[8]);
                stream.Position = directory;
                writer.Write(certificateOffset);
                writer.Write(16U);
            }
            var identity = WindowsUpdatePackageVerifier.Inspect(path);
            Assert.True(identity.Signed);
            Assert.False(identity.SignatureValid);
        }
        finally { System.IO.File.Delete(path); }
    }
}

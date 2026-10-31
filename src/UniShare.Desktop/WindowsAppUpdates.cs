using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using UniShare.Application;

namespace UniShare.Desktop;

internal sealed record WindowsInstallerIdentity(string Product, string Description, string Version,
    ushort Machine, bool Signed, bool SignatureValid, string? CertificateSha256);

internal sealed class WindowsUpdatePackageVerifier(
    string? requiredCertificateSha256 = null,
    Func<string, WindowsInstallerIdentity>? inspect = null) : IAppUpdatePackageVerifier
{
    public Task<string> VerifyAsync(string path, AppUpdateCandidate candidate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var identity = (inspect ?? Inspect)(path);
        if (candidate.Platform != "windows-x64" || identity.Product != "UniShare" ||
            identity.Description != "UniShare Setup" || identity.Version != candidate.Version || identity.Machine is not (0x014c or 0x8664))
        {
            throw new InvalidDataException("Installer product, platform or version does not match the release.");
        }
        if ((identity.Signed && !identity.SignatureValid) ||
            (requiredCertificateSha256 is not null &&
                (!identity.Signed || !string.Equals(identity.CertificateSha256, requiredCertificateSha256, StringComparison.OrdinalIgnoreCase))))
        {
            throw new InvalidDataException("Installer signature or publisher is not trusted.");
        }
        return Task.FromResult(identity.Signed ? "verified" : "legacy-unsigned");
    }

    public static WindowsUpdatePackageVerifier ForCurrentApplication()
    {
        var current = Environment.ProcessPath ?? throw new InvalidOperationException("Application path unavailable.");
        var identity = Inspect(current);
        if (identity.Signed && !identity.SignatureValid) { throw new InvalidDataException("Current application signature is invalid."); }
        return new(identity.CertificateSha256);
    }

    internal static WindowsInstallerIdentity Inspect(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (reader.ReadUInt16() != 0x5a4d) { throw new InvalidDataException("Not a PE executable."); }
        stream.Position = 0x3c;
        var offset = reader.ReadInt32();
        if (offset < 0 || offset > stream.Length - 6) { throw new InvalidDataException("Invalid PE header."); }
        stream.Position = offset;
        if (reader.ReadUInt32() != 0x4550) { throw new InvalidDataException("Invalid PE signature."); }
        var machine = reader.ReadUInt16();
        var version = FileVersionInfo.GetVersionInfo(path);
        var status = Authenticode.Verify(path);
        var certificateDirectory = ReadCertificateDirectory(reader, offset);
        // WinVerifyTrust can report NOSIGNATURE for a malformed embedded signature. A present
        // certificate table must never fall back to the historical unsigned-package policy.
        var signed = status != unchecked((int)0x800b0100) || certificateDirectory.Offset != 0 || certificateDirectory.Size != 0;
        string? certificateHash = null;
        if (signed && status == 0)
        {
            certificateHash = ReadSignerHash(reader, offset);
        }
        return new(version.ProductName?.Trim() ?? string.Empty, version.FileDescription?.Trim() ?? string.Empty,
            version.ProductVersion?.Trim() ?? string.Empty, machine, signed, status == 0, certificateHash);
    }

    private static string ReadSignerHash(BinaryReader reader, int peOffset)
    {
        var stream = reader.BaseStream;
        var (certificateOffset, certificateSize) = ReadCertificateDirectory(reader, peOffset);
        if (certificateSize is < 8 or > 16 * 1024 * 1024 || certificateOffset > stream.Length - certificateSize)
        {
            throw new InvalidDataException("Invalid Authenticode certificate table.");
        }
        stream.Position = certificateOffset;
        var length = reader.ReadUInt32();
        _ = reader.ReadUInt16();
        var type = reader.ReadUInt16();
        if (type != 2 || length < 8 || length > certificateSize) { throw new InvalidDataException("Invalid Authenticode signature."); }
        var cms = new SignedCms();
        cms.Decode(reader.ReadBytes(checked((int)length - 8)));
        if (cms.SignerInfos.Count != 1 || cms.SignerInfos[0].Certificate is not { } certificate)
        {
            throw new InvalidDataException("Ambiguous Authenticode signer.");
        }
        return Convert.ToHexString(SHA256.HashData(certificate.RawData));
    }

    private static (uint Offset, uint Size) ReadCertificateDirectory(BinaryReader reader, int peOffset)
    {
        var stream = reader.BaseStream;
        var optional = peOffset + 24;
        stream.Position = optional;
        var magic = reader.ReadUInt16();
        var directories = magic switch { 0x10b => 96, 0x20b => 112, _ => throw new InvalidDataException("Invalid PE format.") };
        stream.Position = optional + directories + 32; // IMAGE_DIRECTORY_ENTRY_SECURITY
        return (reader.ReadUInt32(), reader.ReadUInt32());
    }

    private static class Authenticode
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct FileInformation
        {
            public uint Size;
            [MarshalAs(UnmanagedType.LPWStr)] public string Path;
            public IntPtr File;
            public IntPtr KnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TrustData
        {
            public uint Size;
            public IntPtr PolicyCallback;
            public IntPtr SipCallback;
            public uint UiChoice;
            public uint RevocationChecks;
            public uint UnionChoice;
            public IntPtr FileInformation;
            public uint StateAction;
            public IntPtr StateData;
            public IntPtr UrlReference;
            public uint ProviderFlags;
            public uint UiContext;
            public IntPtr SignatureSettings;
        }

        [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);

        internal static int Verify(string path)
        {
            var file = new FileInformation { Size = (uint)Marshal.SizeOf<FileInformation>(), Path = path };
            var memory = Marshal.AllocHGlobal(Marshal.SizeOf<FileInformation>());
            Marshal.StructureToPtr(file, memory, false);
            var data = new TrustData
            {
                Size = (uint)Marshal.SizeOf<TrustData>(),
                UiChoice = 2,
                UnionChoice = 1,
                FileInformation = memory,
                StateAction = 1,
                ProviderFlags = 0x00000080, // Whole-chain revocation, never suppress trust failures.
            };
            var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
            try { return WinVerifyTrust(new IntPtr(-1), ref action, ref data); }
            finally
            {
                data.StateAction = 2;
                _ = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
                Marshal.DestroyStructure<FileInformation>(memory);
                Marshal.FreeHGlobal(memory);
            }
        }
    }
}

internal sealed class WindowsUpdateInstaller(
    string profileRoot, Func<Task> shutdown,
    Func<ProcessStartInfo, bool>? start = null) : IAppUpdateInstaller
{
    public async Task RequestAsync(VerifiedAppUpdate update, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var info = new ProcessStartInfo(update.FilePath) { UseShellExecute = true };
        info.ArgumentList.Add("/UNISHAREUPDATE=1");
        info.ArgumentList.Add("/UNISHAREPROFILE=" + Path.GetFullPath(profileRoot));
        info.ArgumentList.Add("/NORESTART");
        var accepted = start is not null ? start(info) : StartInstaller(info);
        if (!accepted) { throw new InvalidOperationException("The native installer could not be opened."); }
        await shutdown();
    }

    private static bool StartInstaller(ProcessStartInfo info)
    {
        using var process = Process.Start(info);
        return process is not null;
    }
}

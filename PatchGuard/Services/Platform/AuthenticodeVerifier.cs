using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using PatchGuard.Services.Native;

namespace PatchGuard.Services.Platform;

/// <summary>
/// Verifies that an executable carries a valid Authenticode signature (chain
/// trusted by Windows) and, optionally, that the signing certificate subject
/// contains an expected publisher string. Used before launching bundled tools
/// so a planted/unsigned binary in a user-writable folder is never executed.
/// </summary>
public static class AuthenticodeVerifier
{
    public static bool IsTrusted(string filePath, string? expectedPublisher = null)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        if (!HasValidSignature(filePath))
        {
            return false;
        }

        if (string.IsNullOrEmpty(expectedPublisher))
        {
            return true;
        }

        return SignerSubjectContains(filePath, expectedPublisher);
    }

    private static bool HasValidSignature(string filePath)
    {
        var fileInfo = new NativeMethods.WINTRUST_FILE_INFO
        {
            cbStruct = (uint)Marshal.SizeOf<NativeMethods.WINTRUST_FILE_INFO>(),
            pcwszFilePath = filePath,
            hFile = IntPtr.Zero,
            pgKnownSubject = IntPtr.Zero
        };

        var pFile = Marshal.AllocHGlobal(Marshal.SizeOf(fileInfo));
        try
        {
            Marshal.StructureToPtr(fileInfo, pFile, false);

            var data = new NativeMethods.WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<NativeMethods.WINTRUST_DATA>(),
                dwUIChoice = NativeMethods.WTD_UI_NONE,
                fdwRevocationChecks = NativeMethods.WTD_REVOKE_NONE,
                dwUnionChoice = NativeMethods.WTD_CHOICE_FILE,
                pFile = pFile,
                dwStateAction = NativeMethods.WTD_STATEACTION_VERIFY,
                dwProvFlags = NativeMethods.WTD_SAFER_FLAG
            };

            var action = NativeMethods.WINTRUST_ACTION_GENERIC_VERIFY_V2;
            var result = NativeMethods.WinVerifyTrust(IntPtr.Zero, action, ref data);

            // Always release the state data WinVerifyTrust allocated.
            data.dwStateAction = NativeMethods.WTD_STATEACTION_CLOSE;
            NativeMethods.WinVerifyTrust(IntPtr.Zero, action, ref data);

            return result == 0; // 0 == ERROR_SUCCESS (trusted)
        }
        catch
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(pFile);
        }
    }

    private static bool SignerSubjectContains(string filePath, string expectedPublisher)
    {
        try
        {
            // CreateFromSignedFile remains the supported way to read the
            // Authenticode signer certificate embedded in a PE file.
#pragma warning disable SYSLIB0057
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(filePath));
#pragma warning restore SYSLIB0057
            return cert.Subject.Contains(expectedPublisher, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}

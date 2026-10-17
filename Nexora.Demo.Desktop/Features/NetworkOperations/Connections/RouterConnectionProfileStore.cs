using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Nexora.Demo.Desktop.Features.NetworkOperations.Connections;

public sealed record RouterConnectionProfile(string Address, string Username, string Password, DateTimeOffset SavedAt);

/// <summary>Stores router credentials in Windows Credential Manager, scoped to the current demo account key.</summary>
public static class RouterConnectionProfileStore
{
    private const uint CredentialTypeGeneric = 1;
    private const uint CredentialPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private const string TargetPrefix = "Nexora.RouterOS.";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredFree", SetLastError = false)]
    private static extern void CredFree(IntPtr buffer);

    public static RouterConnectionProfile? Load(string accountKey)
    {
        var target = GetTarget(accountKey);
        if (!CredRead(target, CredentialTypeGeneric, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound) return null;
            throw new Win32Exception(error, "تعذر قراءة بيانات اعتماد الراوتر من Windows Credential Manager.");
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
                return null;
            var json = Marshal.PtrToStringUni(credential.CredentialBlob);
            if (string.IsNullOrWhiteSpace(json)) return null;
            var profile = JsonSerializer.Deserialize<RouterConnectionProfile>(json);
            if (profile is null || string.IsNullOrWhiteSpace(profile.Address) ||
                string.IsNullOrWhiteSpace(profile.Username) || string.IsNullOrEmpty(profile.Password))
                return null;
            return profile;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("بيانات اتصال الراوتر المحفوظة غير صالحة.", ex);
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public static void Save(string accountKey, RouterConnectionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(profile.Address) || string.IsNullOrWhiteSpace(profile.Username) || string.IsNullOrEmpty(profile.Password))
            throw new ArgumentException("لا يمكن حفظ اتصال ناقص البيانات.", nameof(profile));

        var json = JsonSerializer.Serialize(profile);
        var blob = Encoding.Unicode.GetBytes(json + "\0");
        // Windows Credential Manager limits a generic credential blob to 5 KiB.
        if (blob.Length > 5120) throw new InvalidOperationException("بيانات الاتصال تتجاوز الحد المسموح به في مخزن Windows.");
        var blobPointer = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, blobPointer, blob.Length);
            var credential = new NativeCredential
            {
                Flags = 0,
                Type = CredentialTypeGeneric,
                TargetName = GetTarget(accountKey),
                Comment = "Nexora RouterOS connection profile",
                LastWritten = default,
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = blobPointer,
                Persist = CredentialPersistLocalMachine,
                AttributeCount = 0,
                Attributes = IntPtr.Zero,
                TargetAlias = null,
                UserName = profile.Username
            };

            if (!CredWrite(ref credential, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "تعذر حفظ بيانات الاتصال في Windows Credential Manager.");
        }
        finally
        {
            try { Marshal.Copy(new byte[blob.Length], 0, blobPointer, blob.Length); } catch { /* best-effort unmanaged buffer cleanup */ }
            CryptographicOperations.ZeroMemory(blob);
            Marshal.FreeHGlobal(blobPointer);
        }
    }

    public static void Delete(string accountKey)
    {
        if (CredDelete(GetTarget(accountKey), CredentialTypeGeneric, 0)) return;
        var error = Marshal.GetLastWin32Error();
        if (error != ErrorNotFound)
            throw new Win32Exception(error, "تعذر حذف بيانات اتصال الراوتر من Windows Credential Manager.");
    }

    private static string GetTarget(string accountKey)
    {
        if (string.IsNullOrWhiteSpace(accountKey)) throw new ArgumentException("مفتاح مساحة الشبكة مطلوب.", nameof(accountKey));
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(accountKey))).Substring(0, 32);
        return TargetPrefix + digest;
    }
}
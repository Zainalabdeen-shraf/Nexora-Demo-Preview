using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nexora.Demo.Desktop.Features.Networks.Models;

namespace Nexora.Demo.Desktop.Features.Networks.Services;

/// <summary>
/// Local demo persistence for network and authentication-source settings.
/// It does not connect to or configure a real RouterOS device.
/// </summary>
public sealed class NetworkSourceSettingsStore
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexoraProductDemo",
        "network-source-settings-v1.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private bool _loadFailed;
    private string? _loadError;

    public string StorageDescription => _loadFailed
        ? "تعذر قراءة الإعدادات؛ تم إيقاف الحفظ لحماية البيانات."
        : "إعدادات الشبكة محفوظة محليًا في الديمو.";

    public NetworkSourceSettingsSnapshot Load()
    {
        EnsureStorageIsWritable();

        if (!File.Exists(StorePath))
            return new NetworkSourceSettingsSnapshot();

        try
        {
            var json = File.ReadAllText(StorePath);

            var snapshot =
                JsonSerializer.Deserialize<NetworkSourceSettingsSnapshot>(
                    json, JsonOptions)
                ?? throw new InvalidDataException(
                    "ملف إعدادات الشبكة فارغ أو غير صالح.");

            ValidateStructure(snapshot);

            return snapshot;
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            JsonException or
            InvalidDataException)
        {
            MarkStorageFailed(ex);

            throw new InvalidDataException(
                _loadError ?? "تعذر قراءة إعدادات الشبكة.",
                ex);
        }
    }

    public void Save(NetworkSourceSettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        EnsureStorageIsWritable();

        // Never replace an existing settings file that cannot be read.
        VerifyExistingStoreBeforeOverwrite();
        ValidateSettings(snapshot);

        var directory = Path.GetDirectoryName(StorePath)
            ?? throw new InvalidOperationException(
                "تعذر تحديد مجلد إعدادات الشبكة.");

        Directory.CreateDirectory(directory);

        var temporaryPath = StorePath + ".tmp";

        try
        {
            var json = JsonSerializer.Serialize(snapshot, JsonOptions);

            File.WriteAllText(
                temporaryPath,
                json,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            File.Move(temporaryPath, StorePath, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch
            {
                // Preserve the original save exception.
            }

            throw;
        }
    }

    private void VerifyExistingStoreBeforeOverwrite()
    {
        if (!File.Exists(StorePath))
            return;

        try
        {
            var json = File.ReadAllText(StorePath);

            var existing =
                JsonSerializer.Deserialize<NetworkSourceSettingsSnapshot>(
                    json, JsonOptions)
                ?? throw new InvalidDataException(
                    "ملف إعدادات الشبكة غير صالح.");

            ValidateStructure(existing);
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            JsonException or
            InvalidDataException)
        {
            MarkStorageFailed(ex);

            throw new InvalidOperationException(
                _loadError ??
                "تعذر التحقق من ملف الإعدادات الحالي؛ لم يتم استبداله.",
                ex);
        }
    }

    private void ValidateSettings(NetworkSourceSettingsSnapshot settings)
    {
        ValidateStructure(settings);

        if (settings.SchemaVersion != 1)
        {
            throw new InvalidDataException(
                "إصدار نموذج إعدادات الشبكة غير مدعوم.");
        }

        foreach (var router in settings.Routers)
        {
            if (string.IsNullOrWhiteSpace(router.Name))
                throw new InvalidDataException(
                    "يجب إدخال اسم لكل اتصال راوتر.");

            if (router.IsEnabled &&
                string.IsNullOrWhiteSpace(router.HostAddress))
            {
                throw new InvalidDataException(
                    $"أدخل عنوان الراوتر «{router.Name}» أو عطّل ملف الاتصال.");
            }

            ValidatePort(router.Port, $"منفذ اتصال الراوتر «{router.Name}»");

            if (!Enum.IsDefined(router.Method))
                throw new InvalidDataException(
                    $"طريقة الاتصال غير معروفة للراوتر «{router.Name}».");
        }

        foreach (var profile in settings.HotSpot.Profiles)
{
    if (profile is null ||
        string.IsNullOrWhiteSpace(profile.Name))
    {
        throw new InvalidDataException(
            "يجب إدخال اسم لكل بروفايل HotSpot.");
    }

    if (profile.RadiusInterimUpdateSeconds < 0 ||
        profile.CookieTimeoutMinutes < 1 ||
        profile.ClientIdleTimeoutSeconds < 0 ||
        profile.KeepaliveTimeoutSeconds < 0 ||
        profile.SharedUsers < 1)
    {
        throw new InvalidDataException(
            $"إعدادات البروفايل «{profile.Name}» غير صالحة.");
    }

    if (profile.LoginMethods is null)
    {
        throw new InvalidDataException(
            $"طرق الدخول في البروفايل «{profile.Name}» غير موجودة.");
    }
}

    foreach (var profile in settings.UserManager.Profiles)
    {
        if (profile is null ||
            string.IsNullOrWhiteSpace(profile.Name))
        {
            throw new InvalidDataException(
                "يجب إدخال اسم لكل بروفايل User Manager.");
        }

        if (profile.DownloadLimitKbps < 0 ||
            profile.UploadLimitKbps < 0 ||
            profile.SharedUsers < 1)
        {
            throw new InvalidDataException(
                $"إعدادات البروفايل «{profile.Name}» غير صالحة.");
        }
    }

        if (!Enum.IsDefined(settings.HotSpot.AuthenticationMode))
            throw new InvalidDataException(
                "طريقة مصادقة HotSpot غير معروفة.");

        if (settings.HotSpot.IsEnabled &&
            (string.IsNullOrWhiteSpace(settings.HotSpot.ServerName) ||
             string.IsNullOrWhiteSpace(settings.HotSpot.ServerProfileName)))
        {
            throw new InvalidDataException(
                "أدخل اسم خادم HotSpot واسم ملف الخادم.");
        }

        if (settings.HotSpot.RadiusInterimUpdateSeconds < 0)
        {
            throw new InvalidDataException(
                "فترة تحديث محاسبة HotSpot لا يمكن أن تكون سالبة.");
        }

        ValidatePort(
            settings.UserManager.AuthenticationPort,
            "منفذ مصادقة User Manager");

        ValidatePort(
            settings.UserManager.AccountingPort,
            "منفذ محاسبة User Manager");

        if (settings.UserManager.InterimUpdateSeconds < 0)
        {
            throw new InvalidDataException(
                "فترة تحديث المحاسبة لا يمكن أن تكون سالبة.");
        }

        foreach (var client in settings.UserManager.RouterClients)
        {
            if (string.IsNullOrWhiteSpace(client.Name))
                throw new InvalidDataException(
                    "يجب إدخال اسم لكل عميل RADIUS.");

            if (client.IsEnabled &&
                string.IsNullOrWhiteSpace(client.Address))
            {
                throw new InvalidDataException(
                    $"أدخل عنوان عميل RADIUS «{client.Name}» أو عطّله.");
            }

            ValidatePort(client.CoaPort, $"منفذ CoA للعميل «{client.Name}»");

            if (!Enum.IsDefined(client.Protocol))
                throw new InvalidDataException(
                    $"بروتوكول RADIUS غير معروف للعميل «{client.Name}».");
        }
    }

    private static void ValidateStructure(
        NetworkSourceSettingsSnapshot? settings)
    {
        if (settings is null)
            throw new InvalidDataException(
                "بيانات إعدادات الشبكة غير موجودة.");

        if (settings.Routers is null ||
            settings.HotSpot is null ||
            settings.UserManager is null ||
            settings.UserManager.RouterClients is null ||
            settings.UserManager.Profiles is null ||
            settings.HotSpot.Profiles is null ||
            settings.HotSpot.LoginMethods is null)
        {
            throw new InvalidDataException(
                "ملف الإعدادات لا يحتوي على جميع القوائم والأقسام المطلوبة.");
        }

        if (settings.Routers.Any(router => router is null) ||
            settings.UserManager.RouterClients.Any(client => client is null))
        {
            throw new InvalidDataException(
                "ملف الإعدادات يحتوي على سجل اتصال فارغ.");
        }
    }

    private static void ValidatePort(int port, string fieldName)
    {
        if (port is < 1 or > 65535)
        {
            throw new InvalidDataException(
                $"{fieldName} يجب أن يكون بين 1 و65535.");
        }
    }

    private void EnsureStorageIsWritable()
    {
        if (_loadFailed)
        {
            throw new InvalidOperationException(
                _loadError ??
                "تم إيقاف تعديل الإعدادات لحماية البيانات الحالية.");
        }
    }

    private void MarkStorageFailed(Exception exception)
    {
        _loadFailed = true;
        _loadError =
            "تعذر قراءة ملف إعدادات الشبكة أو التحقق منه. " +
            "لم يتم استبداله حفاظًا على سلامة البيانات. " +
            exception.Message;
    }
}
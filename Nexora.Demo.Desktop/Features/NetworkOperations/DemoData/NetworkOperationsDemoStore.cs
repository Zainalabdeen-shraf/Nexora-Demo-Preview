using System.Collections.ObjectModel;
using Nexora.Demo.Desktop.Features.NetworkOperations.Models;

namespace Nexora.Demo.Desktop.Features.NetworkOperations.DemoData;

/// <summary>
/// Shared in-memory sample data for the UI prototype. Nothing here connects to a device or persists after the app closes.
/// </summary>
public static class NetworkOperationsDemoStore
{
    private static int _sequence = 1;

    public static ObservableCollection<NetworkSiteDemo> Sites { get; } = new();
    public static ObservableCollection<NetworkDeviceDemo> Devices { get; } = new();
    public static ObservableCollection<NetworkClientDemo> Clients { get; } = new();
    public static ObservableCollection<NetworkAlertDemo> Alerts { get; } = new();
    public static ObservableCollection<NetworkMaintenanceTaskDemo> MaintenanceTasks { get; } = new();
    public static ObservableCollection<NetworkUsageBreakdownDemo> UsageBySite { get; } = new();

    public static string? RequestedAlertId { get; set; }

    public static string NewId(string prefix) => $"{prefix}-{DateTime.Now:MMddHHmmss}-{_sequence++:000}";

    static NetworkOperationsDemoStore()
    {
        Sites.Add(new NetworkSiteDemo { Id = "SITE-01", Name = "الموقع الرئيسي", Area = "مركز الشبكة", SiteType = "مركز توزيع", Notes = "موقع الراوتر الرئيسي" });
        Sites.Add(new NetworkSiteDemo { Id = "SITE-02", Name = "برج المدرسة", Area = "الحي الشرقي", SiteType = "برج / نقطة بث", Notes = "قطاع تجريبي يخدم الحي الشرقي" });
        Sites.Add(new NetworkSiteDemo { Id = "SITE-03", Name = "موقع التعاون", Area = "حي التعاون", SiteType = "نقطة توزيع", Notes = "مجموعة عملاء تجريبية" });
        Sites.Add(new NetworkSiteDemo { Id = "SITE-04", Name = "الموقع الشمالي", Area = "المنطقة الشمالية", SiteType = "رابط لاسلكي", Notes = "رابط تجريبي لمنطقة أخرى" });

        Devices.Add(new NetworkDeviceDemo { Id = "RTR-MAIN", Name = "RTR-MAIN", Type = "راوتر رئيسي", SiteName = "الموقع الرئيسي", ParentDevice = "—", ManagementAddress = "192.168.88.1", Status = "متصل (تجريبي)", Signal = "—", ConnectedClients = 18, DownloadGb = 486.2m, LastSeen = "قبل دقيقة", Notes = "بوابة الشبكة الرئيسية في السيناريو التجريبي." });
        Devices.Add(new NetworkDeviceDemo { Id = "SW-MAIN", Name = "SW-MAIN", Type = "سويتش / PoE", SiteName = "الموقع الرئيسي", ParentDevice = "RTR-MAIN", ManagementAddress = "192.168.88.2", Status = "متصل (تجريبي)", Signal = "—", ConnectedClients = 4, DownloadGb = 318.5m, LastSeen = "قبل دقيقتين", Notes = "توزيع سلكي تجريبي." });
        Devices.Add(new NetworkDeviceDemo { Id = "AP-EAST-01", Name = "AP-EAST-01", Type = "نقطة بث (AP)", SiteName = "برج المدرسة", ParentDevice = "SW-MAIN", ManagementAddress = "192.168.88.21", Status = "متصل (تجريبي)", Signal = "—", ConnectedClients = 8, DownloadGb = 146.8m, LastSeen = "قبل دقيقة", Notes = "نقطة وصول تجريبية." });
        Devices.Add(new NetworkDeviceDemo { Id = "AP-EAST-02", Name = "AP-EAST-02", Type = "نقطة بث (AP)", SiteName = "برج المدرسة", ParentDevice = "SW-MAIN", ManagementAddress = "192.168.88.22", Status = "يحتاج مراجعة (تجريبي)", Signal = "-76 dBm (تجريبي)", ConnectedClients = 12, DownloadGb = 203.4m, LastSeen = "قبل 8 دقائق", Notes = "تدهور افتراضي في جودة بعض الوصلات." });
        Devices.Add(new NetworkDeviceDemo { Id = "CPE-017", Name = "CPE-017", Type = "جهاز استقبال (CPE)", SiteName = "الحي الشرقي", ParentDevice = "AP-EAST-02", ManagementAddress = "192.168.90.17", Status = "غير متصل (تجريبي)", Signal = "غير متاح", ConnectedClients = 0, DownloadGb = 18.7m, LastSeen = "قبل 14 دقيقة", Notes = "جهاز استقبال تمثيلي لانقطاع اتصال." });
        Devices.Add(new NetworkDeviceDemo { Id = "HOME-014", Name = "HOME-014", Type = "راوتر منزلي", SiteName = "الحي الشرقي", ParentDevice = "CPE-017", ManagementAddress = "غير متاح", Status = "غير معروف", Signal = "غير متاح", ConnectedClients = 3, DownloadGb = 18.7m, LastSeen = "لا توجد قراءة حديثة", Notes = "الأجهزة خلف الراوتر المنزلي ليست بالضرورة مرئية من الراوتر المركزي." });
        Devices.Add(new NetworkDeviceDemo { Id = "AP-COOP-01", Name = "AP-COOP-01", Type = "نقطة بث (AP)", SiteName = "موقع التعاون", ParentDevice = "RTR-MAIN", ManagementAddress = "192.168.88.31", Status = "متصل (تجريبي)", Signal = "—", ConnectedClients = 9, DownloadGb = 112.6m, LastSeen = "قبل دقيقة", Notes = "نقطة وصول تجريبية." });
        Devices.Add(new NetworkDeviceDemo { Id = "CPE-009", Name = "CPE-009", Type = "جهاز استقبال (CPE)", SiteName = "الموقع الشمالي", ParentDevice = "AP-NORTH-01", ManagementAddress = "192.168.93.9", Status = "يحتاج مراجعة (تجريبي)", Signal = "-71 dBm (تجريبي)", ConnectedClients = 1, DownloadGb = 26.1m, LastSeen = "قبل 4 دقائق", Notes = "مثال لمراجعة جودة الربط." });
        Devices.Add(new NetworkDeviceDemo { Id = "LINK-NORTH", Name = "LINK-NORTH", Type = "وصلة لاسلكية", SiteName = "الموقع الشمالي", ParentDevice = "RTR-MAIN", ManagementAddress = "192.168.88.40", Status = "متصل (تجريبي)", Signal = "-58 dBm (تجريبي)", ConnectedClients = 2, DownloadGb = 167.2m, LastSeen = "قبل دقيقة", Notes = "وصلة نقل تجريبية." });
        Devices.Add(new NetworkDeviceDemo { Id = "AP-NORTH-01", Name = "AP-NORTH-01", Type = "نقطة بث (AP)", SiteName = "الموقع الشمالي", ParentDevice = "LINK-NORTH", ManagementAddress = "192.168.93.1", Status = "متصل (تجريبي)", Signal = "—", ConnectedClients = 7, DownloadGb = 93.7m, LastSeen = "قبل 3 دقائق", Notes = "نقطة بث تجريبية تابعة للرابط الشمالي." });
        Devices.Add(new NetworkDeviceDemo { Id = "CPE-024", Name = "CPE-024", Type = "جهاز استقبال (CPE)", SiteName = "حي التعاون", ParentDevice = "AP-COOP-01", ManagementAddress = "192.168.91.24", Status = "متصل (تجريبي)", Signal = "-61 dBm (تجريبي)", ConnectedClients = 2, DownloadGb = 41.8m, LastSeen = "قبل دقيقتين", Notes = "جهاز استقبال تجريبي." });
        Devices.Add(new NetworkDeviceDemo { Id = "CPE-025", Name = "CPE-025", Type = "جهاز استقبال (CPE)", SiteName = "حي التعاون", ParentDevice = "AP-COOP-01", ManagementAddress = "192.168.91.25", Status = "متصل (تجريبي)", Signal = "-64 dBm (تجريبي)", ConnectedClients = 1, DownloadGb = 22.3m, LastSeen = "قبل دقيقتين", Notes = "جهاز استقبال تجريبي." });
        Devices.Add(new NetworkDeviceDemo { Id = "AP-WEST-01", Name = "AP-WEST-01", Type = "نقطة بث (AP)", SiteName = "الموقع الرئيسي", ParentDevice = "SW-MAIN", ManagementAddress = "192.168.88.51", Status = "غير معروف", Signal = "غير متاح", ConnectedClients = 0, DownloadGb = 0m, LastSeen = "لم تتم قراءته", Notes = "يحتاج ملف إدارة مناسبًا قبل توفر القراءات." });

        Clients.Add(new NetworkClientDemo { Id = "SUB-101", Name = "مشترك 101", Layer = "جلسة HotSpot", DeviceName = "RTR-MAIN", SiteName = "الحي الشرقي", IpAddress = "10.10.0.101", MacAddress = "غير متاح", Status = "نشطة (تجريبي)", DownloadGb = 12.4m, UploadGb = 1.2m, PackageName = "باقة 10 جيجا", LastSeen = "الآن (عرض)" });
        Clients.Add(new NetworkClientDemo { Id = "SUB-102", Name = "مشترك 102", Layer = "جلسة PPPoE", DeviceName = "RTR-MAIN", SiteName = "حي التعاون", IpAddress = "10.10.0.102", MacAddress = "غير متاح", Status = "نشطة (تجريبي)", DownloadGb = 31.8m, UploadGb = 3.1m, PackageName = "باقة شهرية", LastSeen = "الآن (عرض)" });
        Clients.Add(new NetworkClientDemo { Id = "CPE-017-CLIENT", Name = "منزل 017", Layer = "رابط CPE", DeviceName = "CPE-017", SiteName = "الحي الشرقي", IpAddress = "192.168.90.17", MacAddress = "AA:10:20:30:40:17", Status = "غير متصل (تجريبي)", DownloadGb = 18.7m, UploadGb = 0.9m, PackageName = "باقة منزلية", LastSeen = "قبل 14 دقيقة" });
        Clients.Add(new NetworkClientDemo { Id = "DEV-014-A", Name = "جهاز منزلي 014-A", Layer = "جهاز نهائي مكتشف", DeviceName = "HOME-014", SiteName = "الحي الشرقي", IpAddress = "غير متاح", MacAddress = "غير متاح", Status = "غير معروف", DownloadGb = 0m, UploadGb = 0m, PackageName = "—", LastSeen = "يتطلب بيانات الراوتر المنزلي" });
        Clients.Add(new NetworkClientDemo { Id = "SUB-103", Name = "مشترك 103", Layer = "جلسة HotSpot", DeviceName = "AP-EAST-01", SiteName = "الحي الشرقي", IpAddress = "10.10.0.103", MacAddress = "غير متاح", Status = "نشطة (تجريبي)", DownloadGb = 6.2m, UploadGb = 0.6m, PackageName = "باقة يومية", LastSeen = "الآن (عرض)" });
        Clients.Add(new NetworkClientDemo { Id = "CPE-009-CLIENT", Name = "منزل 009", Layer = "رابط CPE", DeviceName = "CPE-009", SiteName = "المنطقة الشمالية", IpAddress = "192.168.93.9", MacAddress = "AA:10:20:30:40:09", Status = "يحتاج مراجعة (تجريبي)", DownloadGb = 26.1m, UploadGb = 2.4m, PackageName = "باقة منزلية", LastSeen = "قبل 4 دقائق" });
        Clients.Add(new NetworkClientDemo { Id = "SUB-104", Name = "مشترك 104", Layer = "جلسة PPPoE", DeviceName = "RTR-MAIN", SiteName = "حي التعاون", IpAddress = "10.10.0.104", MacAddress = "غير متاح", Status = "نشطة (تجريبي)", DownloadGb = 44.6m, UploadGb = 4.8m, PackageName = "باقة شهرية", LastSeen = "الآن (عرض)" });
        Clients.Add(new NetworkClientDemo { Id = "DEV-024-A", Name = "جهاز خلف CPE-024", Layer = "جهاز نهائي مكتشف", DeviceName = "CPE-024", SiteName = "حي التعاون", IpAddress = "192.168.91.124", MacAddress = "AA:10:20:30:41:24", Status = "متصل (تجريبي)", DownloadGb = 4.1m, UploadGb = 0.2m, PackageName = "—", LastSeen = "قبل دقيقتين" });
        Clients.Add(new NetworkClientDemo { Id = "SUB-105", Name = "مشترك 105", Layer = "جلسة HotSpot", DeviceName = "AP-COOP-01", SiteName = "حي التعاون", IpAddress = "10.10.0.105", MacAddress = "غير متاح", Status = "نشطة (تجريبي)", DownloadGb = 9.7m, UploadGb = 0.7m, PackageName = "باقة يومية", LastSeen = "الآن (عرض)" });
        Clients.Add(new NetworkClientDemo { Id = "CPE-025-CLIENT", Name = "منزل 025", Layer = "رابط CPE", DeviceName = "CPE-025", SiteName = "حي التعاون", IpAddress = "192.168.91.25", MacAddress = "AA:10:20:30:40:25", Status = "متصل (تجريبي)", DownloadGb = 22.3m, UploadGb = 1.6m, PackageName = "باقة منزلية", LastSeen = "قبل دقيقتين" });
        Clients.Add(new NetworkClientDemo { Id = "SUB-106", Name = "مشترك 106", Layer = "جلسة PPPoE", DeviceName = "RTR-MAIN", SiteName = "المنطقة الشمالية", IpAddress = "10.10.0.106", MacAddress = "غير متاح", Status = "نشطة (تجريبي)", DownloadGb = 15.3m, UploadGb = 1.1m, PackageName = "باقة شهرية", LastSeen = "الآن (عرض)" });
        Clients.Add(new NetworkClientDemo { Id = "DEV-AP-02", Name = "عميل لاسلكي 02", Layer = "عميل Wi-Fi مباشر", DeviceName = "AP-EAST-02", SiteName = "الحي الشرقي", IpAddress = "192.168.92.32", MacAddress = "AA:10:20:30:40:32", Status = "يحتاج مراجعة (تجريبي)", DownloadGb = 7.2m, UploadGb = 0.8m, PackageName = "—", LastSeen = "قبل 8 دقائق" });

        Alerts.Add(new NetworkAlertDemo { Id = "ALT-104", Title = "تراجع جودة الاتصال اللاسلكي", DeviceName = "AP-EAST-02", SiteName = "الحي الشرقي · برج المدرسة", Severity = "تحذير", Age = "منذ 8 دقائق", Description = "تظهر بيانات السيناريو التجريبي تراجعًا في جودة الاتصال لدى بعض أجهزة الاستقبال. يوصى بمراجعة الإشارة والاتجاه والتداخل ومقارنة القراءات السابقة." });
        Alerts.Add(new NetworkAlertDemo { Id = "ALT-103", Title = "جهاز لا يستجيب", DeviceName = "CPE-017", SiteName = "الحي الشرقي · منزل 017", Severity = "مرتفع", Age = "منذ 14 دقيقة", Description = "هذه حالة انقطاع تمثيلية. يجب التحقق من الطاقة والوصلة ومسار الوصول قبل تحديد سبب العطل." });
        Alerts.Add(new NetworkAlertDemo { Id = "ALT-101", Title = "ارتفاع زمن الاستجابة", DeviceName = "RTR-MAIN", SiteName = "الموقع الرئيسي", Severity = "مراجعة", Age = "منذ 22 دقيقة", Description = "تظهر بيانات العرض ارتفاعًا افتراضيًا في زمن الاستجابة. يجب مقارنة القياسات بالوجهة والمسار وبالقراءات السابقة." });
        Alerts.Add(new NetworkAlertDemo { Id = "ALT-099", Title = "بيانات الجهاز قديمة", DeviceName = "AP-WEST-01", SiteName = "الموقع الرئيسي", Severity = "مراجعة", Age = "منذ 37 دقيقة", Description = "لم يجرِ تحديث معلومات هذا الجهاز في بيانات السيناريو. هذا لا يثبت انقطاعه؛ حالة الجهاز الفعلية غير معروفة." });

        MaintenanceTasks.Add(new NetworkMaintenanceTaskDemo { Id = "MNT-021", Title = "فحص جودة الوصلة", DeviceName = "AP-EAST-02", SiteName = "الحي الشرقي · برج المدرسة", Priority = "متوسطة", Status = "مجدولة", AssignedTo = "فني الشبكة", Notes = "مهمة عرض تجريبية." });
        MaintenanceTasks.Add(new NetworkMaintenanceTaskDemo { Id = "MNT-019", Title = "التحقق من انقطاع الجهاز", DeviceName = "CPE-009", SiteName = "المنطقة الشمالية", Priority = "عالية", Status = "قيد التنفيذ", AssignedTo = "غير معيّن", Notes = "مهمة عرض تجريبية." });
        MaintenanceTasks.Add(new NetworkMaintenanceTaskDemo { Id = "MNT-016", Title = "مراجعة توثيق الموقع", DeviceName = "AP-WEST-01", SiteName = "الموقع الرئيسي", Priority = "منخفضة", Status = "مكتملة", AssignedTo = "فني الشبكة", Notes = "مهمة عرض تجريبية." });

        UsageBySite.Add(new NetworkUsageBreakdownDemo { Name = "الحي الشرقي", DownloadGb = 482m, SharePercent = 84, Caption = "482 جيجابايت" });
        UsageBySite.Add(new NetworkUsageBreakdownDemo { Name = "حي التعاون", DownloadGb = 361m, SharePercent = 64, Caption = "361 جيجابايت" });
        UsageBySite.Add(new NetworkUsageBreakdownDemo { Name = "المنطقة الشمالية", DownloadGb = 278m, SharePercent = 49, Caption = "278 جيجابايت" });
        UsageBySite.Add(new NetworkUsageBreakdownDemo { Name = "الموقع الرئيسي", DownloadGb = 196m, SharePercent = 35, Caption = "196 جيجابايت" });
}

    public static NetworkMaintenanceTaskDemo CreateOrGetTaskFromAlert(NetworkAlertDemo alert)
    {
        var existing = MaintenanceTasks.FirstOrDefault(task => task.SourceAlertId == alert.Id);
        if (existing is not null)
            return existing;

        var priority = alert.Severity == "مرتفع" ? "عالية" : alert.Severity == "تحذير" ? "متوسطة" : "للمراجعة";
        var task = new NetworkMaintenanceTaskDemo
        {
            Id = NewId("MNT"),
            Title = $"فحص: {alert.Title}",
            DeviceName = alert.DeviceName,
            SiteName = alert.SiteName,
            Priority = priority,
            Status = "جديدة",
            AssignedTo = "غير معيّن",
            Notes = $"أُنشئت من التنبيه {alert.Id}. يلزم فحص الجهاز قبل تأكيد السبب.",
            SourceAlertId = alert.Id,
            CreatedAt = DateTime.Now
        };
        MaintenanceTasks.Insert(0, task);
        return task;
    }
}
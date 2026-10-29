using System.IO;
using System.Text.Json;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Nexora.Demo.Desktop.Features.Cards.Models;
using Nexora.Demo.Desktop.Features.Cards.Services;
using Nexora.Demo.Desktop.Features.Packages.Services;
using Nexora.Demo.Desktop.Features.Packages.Views;
using Nexora.Demo.Desktop.Features.Shared;

namespace Nexora.Demo.Desktop.Features.Cards.Views;

// Responsibility: Issuance lifecycle and batch-generation workflow.
public partial class CardIssuanceView
{
    private void CardIssuanceView_Loaded(object sender, RoutedEventArgs e)

    {

        if (!_isSubscribed)

        {

            _issuanceService.ScenarioChanged += IssuanceService_ScenarioChanged;

            _isSubscribed = true;

        }


        try

        {

            LoadDesignerTemplateDefinition();
            ApplyPrintTemplate();
            RefreshPackageCatalog(preserveSelection: true);

            // Refreshing the package catalog updates its suggested profile; restore the user's per-system

            // saved choices immediately afterward so reloads don't silently overwrite saved preferences.

            LoadPersistedOptions();

            LoadPersistedPreferences();
            ApplyTemplateAssociationForSelectedPackage();
            var initialTemplatePath = (PrintTemplateComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            if (!string.IsNullOrWhiteSpace(initialTemplatePath) && File.Exists(initialTemplatePath))
                LoadSelectedTemplate(initialTemplatePath, persistAssociation: false);

            RefreshExistingBatches();

            _recentBatchesView?.Refresh();

            RefreshRecentBatchesEmptyState();

            ApplyInitialModeStates();

            UpdatePreview();

        }

        catch (Exception ex)

        {

            SetStatus("تعذر تجهيز الشاشة بالكامل: " + ex.Message, true);

        }

    }
    private void CardIssuanceView_Unloaded(object sender, RoutedEventArgs e)

    {

        if (_isSubscribed)

        {

            _issuanceService.ScenarioChanged -= IssuanceService_ScenarioChanged;

            _isSubscribed = false;

        }

    }
    private void IssuanceService_ScenarioChanged(object? sender, EventArgs e)

    {

        Dispatcher.InvokeAsync(() =>

        {

            if (!IsLoaded)

                return;

            RefreshExistingBatches();

            _recentBatchesView?.Refresh();

            RecentBatchesGrid.Items.Refresh();

            RefreshRecentBatchesEmptyState();

            UpdatePreview();

        });

    }
    private void ApplyInitialModeStates()

    {

        UpdatePasswordControls();

        BatchMode_Changed(this, new RoutedEventArgs());

        ApplyPrintTemplate();


    }
    private async void GenerateBatchButton_Click(object sender, RoutedEventArgs e) => await ExecuteBatchWorkflowAsync("local");
    private async void GenerateAndPrintButton_Click(object sender, RoutedEventArgs e) => await ExecuteBatchWorkflowAsync("upload-print");
    private async void OnlineSaleButton_Click(object sender, RoutedEventArgs e) => await ExecuteBatchWorkflowAsync("online-sale");

    private async Task ExecuteBatchWorkflowAsync(string action)
    {
        if (_workflowRunning)
            return;

        _workflowRunning = true;
        IsEnabled = false;
        var owner = Window.GetWindow(this);
        var progress = WorkflowFeedbackWindow.ShowProgress(
            owner,
            action == "online-sale" ? "تجهيز كروت الشبكات" : action == "upload-print" ? "إنشاء الدفعة وتجهيز الطباعة" : "إنشاء دفعة الكروت",
            action == "online-sale"
                ? "يجري التحقق من إعدادات الإصدار ثم حفظ دفعة كروت الشبكات داخل Nexora. لن تتم طباعة هذه الدفعة تلقائيًا."
                : action == "upload-print"
                    ? "يجري التحقق من البيانات وتوليد الكروت وحفظ سجل الدفعة قبل فتح المعاينة."
                    : "يجري التحقق من البيانات وتوليد هويات فريدة وحفظ الدفعة محليًا.",
            "التحقق من الحقول والإعدادات…");

        try
        {
            // Give WPF time to render the branded progress surface before the local operation starts.
            await Task.Delay(180);
            progress.UpdateStage(action == "online-sale"
                ? "التحقق من الحقول وحفظ الدفعة المحلية؛ المزامنة الفعلية غير متاحة في هذه النسخة…"
                : "توليد الأسماء والتحقق من عدم تكرارها…");

            var batch = GenerateBatch(
                forceNewBatch: action == "online-sale",
                // The operation is called "كروت الشبكات"; the batch itself is identified
                // by its generated timestamp-based ID, not by a repeated display name.
                batchName: string.Empty);

            if (string.IsNullOrWhiteSpace(_lastGeneratedBatchId) || _lastGeneratedCards.Count == 0)
                throw new InvalidOperationException("لم يتم إنشاء أي كرت. راجع البيانات ثم أعد المحاولة.");

            var targetKey = _targetSystem == DemoAccountSystem.HotSpot ? "hotspot" : "user-manager";
            _workflowStore.RecordLocalGeneration(_lastGeneratedBatchId, targetKey, batch.Quantity, _lastGeneratedCards);
            _workflowStore.SetPreferredBatchFilter(targetKey, _lastGeneratedBatchId);
            progress.UpdateStage("تم حفظ الدفعة وسجل الكروت محليًا.");

            var price = batch.UnitPrice;
            var details = new List<KeyValuePair<string, string>>
            {
                new("رقم الدفعة", batch.BatchId),
                new("تاريخ الإنشاء", batch.CreatedAt.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture)),
                new("عدد الكروت في هذه العملية", _lastGeneratedCards.Count.ToString("N0", CultureInfo.CurrentCulture)),
                new("الباقة", batch.PackageName),
                new("سعر الكرت", $"{price:N0} ريال"),
                new("الإجمالي التقديري", $"{price * _lastGeneratedCards.Count:N0} ريال"),
                new("نقطة البيع", batch.PointOfSale),
                new("حالة السجل", "محفوظ محليًا داخل Nexora")
            };

            if (action == "upload-print")
            {
                _workflowStore.QueuePrintSelection(_lastGeneratedCards);
                SetStatus($"تم إنشاء الدفعة {batch.BatchId} — {_lastGeneratedCards.Count:N0} كرت. يجري فتح معاينة PDF.", false);
                progress.Close();
                IsEnabled = true;
                await ShowLocalPrintPreviewAsync(_lastGeneratedCards.ToList());
                return;
            }

            if (action == "online-sale")
            {
                SetStatus($"تم توليد {_lastGeneratedCards.Count:N0} كرت في دفعة «كروت الشبكات» محليًا. المزامنة الفعلية مع الراوتر غير منفذة في هذه النسخة.", true);
                details.Add(new("مزامنة الراوتر", "لم تُنفّذ — موصل RouterOS المتاح للقراءة فقط"));
                details.Add(new("PDF والطباعة", "لم يتم إنشاء PDF ولم تبدأ الطباعة"));
                progress.Close();
                IsEnabled = true;
                var choice = WorkflowFeedbackWindow.ShowNotice(
                    owner,
                    "تم توليد دفعة كروت الشبكات",
                    "نجح إنشاء الدفعة وحفظها في سجل Nexora. تنبيه مهم: لم يتم إرسال الحسابات إلى الراوتر؛ الموصل الموجود في المشروع لا يدعم كتابة حسابات HotSpot أو User Manager بعد. لذلك بقيت الدفعة بانتظار المزامنة ولم تتم طباعتها.",
                    WorkflowFeedbackKind.Warning,
                    details,
                    new WorkflowFeedbackAction("manage", "فتح إدارة الكروت", true),
                    new WorkflowFeedbackAction("close", "إغلاق"));
                if (choice == "manage")
                    NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs(targetKey, "إدارة الكروت"));
                return;
            }

            SetStatus($"تم إنشاء {_lastGeneratedCards.Count:N0} كرت في الدفعة {batch.BatchId}.", false);
            progress.Close();
            IsEnabled = true;
            var localChoice = WorkflowFeedbackWindow.ShowNotice(
                owner,
                "اكتمل إنشاء الدفعة",
                "تم حفظ الدفعة محليًا. يمكنك مراجعة PDF كامل أو متابعة العمل دون طباعة.",
                WorkflowFeedbackKind.Success,
                details,
                new WorkflowFeedbackAction("preview", "فتح PDF كامل", true),
                new WorkflowFeedbackAction("close", "إغلاق"));
            if (localChoice == "preview")
                await ShowLocalPrintPreviewAsync(_lastGeneratedCards.ToList());
        }
        catch (ArgumentException ex)
        {
            progress.Close();
            IsEnabled = true;
            SetStatus(ex.Message, true);
            WorkflowFeedbackWindow.ShowNotice(
                owner,
                "راجع بيانات الدفعة",
                ex.Message,
                WorkflowFeedbackKind.Warning,
                [new("النتيجة", "لم تكتمل العملية ولم يتم اعتماد دفعة جديدة.")],
                new WorkflowFeedbackAction("close", "تعديل البيانات", true));
        }
        catch (Exception ex)
        {
            progress.Close();
            IsEnabled = true;
            var partial = _lastGeneratedCards.Count > 0;
            var message = partial
                ? "تم إنشاء الكروت محليًا، لكن تعذر إكمال تسجيل بعض تفاصيل سير العمل. افحص سجل الدفعات قبل إعادة المحاولة."
                : "تعذر إكمال العملية. لم يتم تأكيد إنشاء دفعة جديدة.";
            SetStatus(message + " " + ex.Message, true);
            WorkflowFeedbackWindow.ShowNotice(
                owner,
                partial ? "الدفعة أُنشئت وتحتاج مراجعة" : "تعذر إكمال العملية",
                message,
                WorkflowFeedbackKind.Error,
                [new("التفاصيل", ex.Message), new("الإجراء التالي", "راجع سجل الدفعات قبل إعادة التنفيذ حتى لا تكرر العملية.")],
                new WorkflowFeedbackAction("close", "إغلاق", true));
        }
        finally
        {
            if (progress.IsVisible)
                progress.Close();
            IsEnabled = true;
            _workflowRunning = false;
        }
    }

    private bool MatchRecentBatch(object item)

    {

        if (item is not GeneratedCardBatch batch)

            return false;

        var targetName = _targetSystem == DemoAccountSystem.HotSpot ? "مستخدمو نقطة الاتصال" : "مدير المستخدمين";

        return string.Equals(batch.TargetSystem, targetName, StringComparison.Ordinal);

    }
    private void RefreshRecentBatchesEmptyState()

    {

        if (RecentBatchesGrid is null || RecentBatchesEmptyPanel is null)

            return;

        RecentBatchesEmptyPanel.Visibility = RecentBatchesGrid.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    }
    private GeneratedCardBatch GenerateBatch(bool forceNewBatch = false, string batchName = "")
    {
        _lastGeneratedBatchId = null;
        _lastGeneratedCards = [];

        // Named network-card batches always start a new batch, even if the regular issuance
        // form is currently set to append to a previous batch.
        var request = BuildRequest(batchName);
        string? existingBatchId = null;
        if (!forceNewBatch && AppendToPreviousBatchRadioButton.IsChecked == true)
        {
            if (ExistingBatchComboBox.SelectedItem is not GeneratedCardBatch selectedBatch)
                throw new ArgumentException("اختر الدفعة السابقة التي تريد الإضافة إليها.");
            existingBatchId = selectedBatch.BatchId;
        }

        var countAdded = request.Quantity;
        var batch = _issuanceService.GenerateBatch(request, existingBatchId);
        var targetName = _targetSystem == DemoAccountSystem.HotSpot ? "مستخدمو الهوتسبوت" : "مدير المستخدمين";
        _lastGeneratedBatchId = batch.BatchId;
        _lastGeneratedCards = _issuanceService.Cards
            .Where(card => card.BatchId == batch.BatchId && card.TargetSystem == targetName)
            .TakeLast(countAdded)
            .ToList();

        if (_lastGeneratedCards.Count != countAdded)
            throw new InvalidOperationException($"طلبت إنشاء {countAdded:N0} كرت، لكن سجل الدفعة أعاد {_lastGeneratedCards.Count:N0}. لا تعِد العملية قبل مراجعة سجل الدفعات.");

        _previewCard = _lastGeneratedCards[^1];
        OpenPrintQueueButton.IsEnabled = true;
        RefreshExistingBatches();
        _recentBatchesView?.Refresh();
        RecentBatchesGrid.Items.Refresh();
        RefreshRecentBatchesEmptyState();
        UpdatePreview();
        return batch;
    }

    private CardIssuanceRequest BuildRequest(string batchName = "")

    {
        if (!TryParseInteger(QuantityTextBox.Text, out var quantity))
            throw new ArgumentException("أدخل عددًا صحيحًا للكروت.");

        if (quantity < 1)
            throw new ArgumentException("عدد الكروت يجب أن يكون أكبر من صفر.");

        if (!TryParseInteger(CardNumberLengthTextBox.Text, out var cardNumberLength))

            throw new ArgumentException("أدخل الطول الكلي لرقم الكرت كعدد صحيح.");

        var usernameLength = cardNumberLength;


        var passwordMode = ReadTag(PasswordModeComboBox) switch

        {

            "Empty" => PasswordGenerationMode.Empty,

            "Generated" => PasswordGenerationMode.Generated,

            _ => PasswordGenerationMode.SameAsUsername

        };

        var passwordLength = 8;

        if (passwordMode == PasswordGenerationMode.Generated &&

            !TryParseInteger(PasswordLengthTextBox.Text, out passwordLength))

            throw new ArgumentException("أدخل طولًا صحيحًا لكلمة المرور.");


        if (PackageComboBox.SelectedItem is not DemoCardPackage package)

            throw new ArgumentException("اختر باقة مفعّلة لهذا النظام أولًا.");


        var cardPrefix = CardPrefixTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(cardPrefix))
            throw new ArgumentException("بادئة الكرت مطلوبة.");

        var cardSet = ParseCardCharacterSet(ReadTag(CardNumberCharacterSetComboBox));

        var usernameSet = cardSet switch
        {
            CardNumberCharacterSet.AlphaNumeric => CredentialCharacterSet.AlphaNumeric,
            CardNumberCharacterSet.LettersOnly => CredentialCharacterSet.LettersOnly,
            _ => CredentialCharacterSet.NumericOnly
        };

        var passwordSet = ParseCredentialCharacterSet(ReadTag(PasswordCharacterSetComboBox));


        var profile = RouterProfileTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(profile))

            profile = _targetSystem == DemoAccountSystem.HotSpot ? package.HotSpotProfile : package.UserManagerProfile;


        var sharedUsersTag = ReadTag(SharedUsersComboBox);

        var sharedUsers = int.TryParse(sharedUsersTag, out var parsedSharedUsers) ? parsedSharedUsers : 1;

        var managerAccount = _targetSystem == DemoAccountSystem.UserManager

            ? (ReadTag(ManagerUserComboBox) ?? "admin")

            : "غير منطبق";


        return new CardIssuanceRequest(

            _targetSystem,

            quantity,

            package,

            cardNumberLength,

            cardPrefix.ToUpperInvariant(),

            CardSuffixTextBox.Text.Trim().ToUpperInvariant(),

            cardSet,

            CardNumberGenerationMode.Sequential,

            "1",

            usernameLength,

            usernameSet,

            passwordMode,

            passwordLength,

            passwordSet,

            profile,

            (PointOfSaleComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty,

            CommentTextBox.Text.Trim(),

            managerAccount,

            sharedUsers,

            batchName);

    }
    private static bool TryParseInteger(string? value, out int result)

    {

        var normalized = (value ?? string.Empty)

            .Replace('٠', '0').Replace('١', '1').Replace('٢', '2').Replace('٣', '3')

            .Replace('٤', '4').Replace('٥', '5').Replace('٦', '6').Replace('٧', '7')

            .Replace('٨', '8').Replace('٩', '9')

            .Replace('۰', '0').Replace('۱', '1').Replace('۲', '2').Replace('۳', '3')

            .Replace('۴', '4').Replace('۵', '5').Replace('۶', '6').Replace('۷', '7')

            .Replace('۸', '8').Replace('۹', '9');

        return int.TryParse(normalized.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    }
    private static string? ReadTag(ComboBox comboBox) => (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
    private static CardNumberCharacterSet ParseCardCharacterSet(string? value) => value switch

    {

        "AlphaNumeric" => CardNumberCharacterSet.AlphaNumeric,

        "LettersOnly" => CardNumberCharacterSet.LettersOnly,

        _ => CardNumberCharacterSet.NumericOnly

    };
    private static CredentialCharacterSet ParseCredentialCharacterSet(string? value) => value switch

    {

        "AlphaNumeric" => CredentialCharacterSet.AlphaNumeric,

        "LettersOnly" => CredentialCharacterSet.LettersOnly,

        _ => CredentialCharacterSet.NumericOnly

    };
}

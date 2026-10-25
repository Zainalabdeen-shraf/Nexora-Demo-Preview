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
    private void GenerateBatchButton_Click(object sender, RoutedEventArgs e) => ExecuteBatchWorkflow("local");
    private void GenerateAndPrintButton_Click(object sender, RoutedEventArgs e) => ExecuteBatchWorkflow("upload-print");
    private void OnlineSaleButton_Click(object sender, RoutedEventArgs e) => ExecuteBatchWorkflow("online-sale");
    private void ExecuteBatchWorkflow(string action)

    {

        _lastGeneratedBatchId = null;

        _lastGeneratedCards = [];

        GenerateBatch();

        if (string.IsNullOrWhiteSpace(_lastGeneratedBatchId) || _lastGeneratedCards.Count == 0)

            return;


        try

        {

            var batch = _issuanceService.Batches.FirstOrDefault(item => item.BatchId == _lastGeneratedBatchId);

            var targetKey = _targetSystem == DemoAccountSystem.HotSpot ? "hotspot" : "user-manager";

            _workflowStore.RecordLocalGeneration(_lastGeneratedBatchId, targetKey, batch?.Quantity ?? _lastGeneratedCards.Count, _lastGeneratedCards);

            _workflowStore.SetPreferredBatchFilter(targetKey, _lastGeneratedBatchId);


            switch (action)

            {
case "upload-print":
                    _workflowStore.QueuePrintSelection(_lastGeneratedCards);
                    SetStatus($"تم إنشاء {_lastGeneratedCards.Count:N0} كرت محليًا في الدفعة {_lastGeneratedBatchId}. ستظهر الأرقام في معاينة الطباعة المنفصلة.", false);
                    ShowLocalPrintPreview(_lastGeneratedCards);
                    break;

                case "online-sale":

                    SetStatus($"تم إنشاء الدفعة {_lastGeneratedBatchId}. ستفتح إدارة الكروت لتحديد الحسابات المطلوبة للبيع الإلكتروني.", false);

                    NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs(targetKey, "إدارة الكروت"));

                    break;

                default:

                    SetStatus($"تم إنشاء {_lastGeneratedCards.Count:N0} كرت في الدفعة {_lastGeneratedBatchId}. يمكنك الرجوع إليها من إدارة الكروت والدفعات.", false);

                    break;

            }

        }

        catch (Exception ex)

        {

            SetStatus("تم إنشاء الكروت، لكن تعذر تسجيل بعض تفاصيل مسار الإجراء: " + ex.Message, true);

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
    private void GenerateBatch()

    {

        try

        {

            var request = BuildRequest();

            string? existingBatchId = null;

            if (AppendToPreviousBatchRadioButton.IsChecked == true)

            {

                if (ExistingBatchComboBox.SelectedItem is not GeneratedCardBatch selectedBatch)

                    throw new ArgumentException("اختر رقم الدفعة السابقة التي تريد الإضافة إليها.");

                existingBatchId = selectedBatch.BatchId;

            }


            var countAdded = request.Quantity;

            var batch = _issuanceService.GenerateBatch(request, existingBatchId);

            var targetName = _targetSystem == DemoAccountSystem.HotSpot ? "مستخدمو نقطة الاتصال" : "مدير المستخدمين";

            _lastGeneratedBatchId = batch.BatchId;

            _lastGeneratedCards = _issuanceService.Cards

                .Where(card => card.BatchId == batch.BatchId && card.TargetSystem == targetName)

                .TakeLast(countAdded)

                .ToList();

            if (_lastGeneratedCards.Count == 0)

                throw new InvalidOperationException("لم يعثر النظام على الكروت التي أُنشئت لهذه الدفعة.");

            _previewCard = _lastGeneratedCards[^1];
            SetStatus($"تم إنشاء {countAdded:N0} كرت في الدفعة {batch.BatchId}.", false);


            OpenPrintQueueButton.IsEnabled = true;

            RefreshExistingBatches();

            _recentBatchesView?.Refresh();

            RecentBatchesGrid.Items.Refresh();

            RefreshRecentBatchesEmptyState();

            UpdatePreview();
        }

        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException or OverflowException)

        {

            SetStatus(exception.Message, true);

        }

        catch (Exception exception)

        {

            SetStatus("حدث خطأ غير متوقع أثناء تجهيز الدفعة. لم يتم إرسال شيء إلى الراوتر. التفاصيل: " + exception.Message, true);

        }

    }
    private CardIssuanceRequest BuildRequest()

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

            CardNumberGenerationMode.Random,

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

            sharedUsers);

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

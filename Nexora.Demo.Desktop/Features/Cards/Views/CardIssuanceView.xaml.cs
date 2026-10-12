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



public partial class CardIssuanceView : UserControl

{

    private readonly DemoCardIssuanceService _issuanceService;

    private readonly DemoAccountSystem _targetSystem;

    private readonly DemoCardWorkflowStore _workflowStore = new();

    private ListCollectionView? _recentBatchesView;

    private bool _isApplyingPreferences;

    private string? _lastGeneratedBatchId;

    private List<GeneratedDemoCard> _lastGeneratedCards = [];

    private GeneratedDemoCard? _previewCard;

    private string _quantityBeforeSingleCard = "20";

    private bool _singleCardModeApplied;

    private bool _isSubscribed;
    private bool _isSyncingPrintTemplateControls;

    private string _printPaperSize = "A4";

    private int _printColumns = 3;

    private bool _printCutMarks = true;

    private static readonly CultureInfo UiCulture = CultureInfo.CurrentCulture;



    public CardIssuanceView(DemoCardIssuanceService issuanceService, DemoAccountSystem targetSystem)

    {

        InitializeComponent();

        _issuanceService = issuanceService;

        _targetSystem = targetSystem;
        IssuanceSettingsHeadingTextBlock.Text = targetSystem == DemoAccountSystem.UserManager
            ? "إعدادات إصدار كروت User Manager"
            : "إعدادات إصدار كروت HotSpot";
        CardNumberPatternLabelTextBlock.Text = "طول / نمط الرقم";
        CardNumberLengthTextBox.ToolTip = targetSystem == DemoAccountSystem.UserManager
            ? "في User Manager رقم الكرت هو اسم المستخدم؛ هذا الطول يحدد الاثنين."
            : "هذا الطول يحدد طول رقم الكرت في HotSpot.";

        _recentBatchesView = new ListCollectionView(_issuanceService.Batches) { Filter = MatchRecentBatch };

        RecentBatchesGrid.ItemsSource = _recentBatchesView;



        TargetSystemBadgeTextBlock.Text = targetSystem == DemoAccountSystem.HotSpot

            ? "HotSpot"

            : "User Manager";

        IssuanceModeSubtitleTextBlock.Text = targetSystem == DemoAccountSystem.HotSpot

            ? "أنشئ كروت الهوتسبوت من باقة فنية مرتبطة، وحدد بيانات الدخول والمشاركة."

            : "أنشئ كروت مدير المستخدمين من باقة مرتبطة، وحدد المستخدم والمجموعة وصيغة الدخول.";

        ManagerUserPanel.Visibility = targetSystem == DemoAccountSystem.UserManager

            ? Visibility.Visible : Visibility.Collapsed;

        SharedUsersHelpTextBlock.Text = targetSystem == DemoAccountSystem.UserManager

            ? "يحدد هذا الخيار عدد الجلسات المتزامنة المسموح بها للحساب وفق إعداد User Manager."

            : "يحدد هذا الخيار عدد الجلسات المتزامنة وفق ملف مستخدم HotSpot المرتبط.";



        try

        {

            RefreshPackageCatalog(preserveSelection: false);

        }

        catch (Exception ex)

        {

            PackageComboBox.ItemsSource = Array.Empty<DemoCardPackage>();

            SetStatus("تعذر تحميل الباقات: " + ex.Message, true);

        }



        LoadPersistedOptions();

        LoadPersistedPreferences();

        ManagerUserPanel.Visibility = targetSystem == DemoAccountSystem.UserManager ? Visibility.Visible : Visibility.Collapsed;

        UsernameSettingsPanel.Visibility = targetSystem == DemoAccountSystem.HotSpot ? Visibility.Visible : Visibility.Collapsed;

        PasswordModeLabelTextBlock.Text = targetSystem == DemoAccountSystem.UserManager ? "صيغة الكرت" : "صيغة الدخول";

        ShowUsernameCheckBox.Visibility = targetSystem == DemoAccountSystem.UserManager ? Visibility.Collapsed : Visibility.Visible;

        UpdatePasswordControls();

        RefreshExistingBatches();

        RefreshRecentBatchesEmptyState();

        ApplyPrintTemplate();



        Loaded += CardIssuanceView_Loaded;

        Unloaded += CardIssuanceView_Unloaded;

    }



    public event EventHandler<FeatureNavigationRequestedEventArgs>? NavigationRequested;



    private void CardIssuanceView_Loaded(object sender, RoutedEventArgs e)

    {

        if (!_isSubscribed)

        {

            _issuanceService.ScenarioChanged += IssuanceService_ScenarioChanged;

            _isSubscribed = true;

        }



        try

        {

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

        GenerationMode_Changed(this, new RoutedEventArgs());

        UpdatePasswordControls();

        BatchMode_Changed(this, new RoutedEventArgs());

        PrintMode_Changed(this, new RoutedEventArgs());

        SynchronizePrintTemplateControls(PrintTemplateComboBox, PreviewTemplateComboBox);

        ApplyPrintTemplate();

        PreviewOptionChanged(this, new RoutedEventArgs());

    }



    private void GenerateBatchButton_Click(object sender, RoutedEventArgs e) => ExecuteBatchWorkflow("local");

    private void GenerateAndPrintButton_Click(object sender, RoutedEventArgs e) => ExecuteBatchWorkflow("upload-print");

    private void UploadRouterButton_Click(object sender, RoutedEventArgs e) => ExecuteBatchWorkflow("upload");

    private void OnlineSaleButton_Click(object sender, RoutedEventArgs e) => ExecuteBatchWorkflow("online-sale");



    private void ExecuteBatchWorkflow(string action)

    {

        _lastGeneratedBatchId = null;

        _lastGeneratedCards = [];

        GenerateBatch(openPrintQueue: false);

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

                case "upload":

                    _workflowStore.RecordRouterUpload(_lastGeneratedCards);

                    SetStatus($"تم إنشاء {_lastGeneratedCards.Count:N0} كرت وتسجيل طلب المزامنة للدفعة.", false);

                    break;

                case "upload-print":

                    _workflowStore.RecordRouterUpload(_lastGeneratedCards);

                    _workflowStore.QueuePrintSelection(_lastGeneratedCards);

                    SetStatus($"تم إنشاء {_lastGeneratedCards.Count:N0} كرت وتسجيل طلب المزامنة وإضافة الكروت إلى قائمة الطباعة.", false);

                    NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("printing", "قائمة الطباعة"));

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

        var targetName = _targetSystem == DemoAccountSystem.HotSpot ? "مستخدمو الهوتسبوت" : "مدير المستخدمين";

        return string.Equals(batch.TargetSystem, targetName, StringComparison.Ordinal);

    }



    private void RefreshRecentBatchesEmptyState()

    {

        if (RecentBatchesGrid is null || RecentBatchesEmptyPanel is null)

            return;

        RecentBatchesEmptyPanel.Visibility = RecentBatchesGrid.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    }



    private void GenerateBatch(bool openPrintQueue)

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

            var targetName = _targetSystem == DemoAccountSystem.HotSpot ? "مستخدمو الهوتسبوت" : "مدير المستخدمين";

            _lastGeneratedBatchId = batch.BatchId;

            _lastGeneratedCards = _issuanceService.Cards

                .Where(card => card.BatchId == batch.BatchId && card.TargetSystem == targetName)

                .TakeLast(countAdded)

                .ToList();

            if (_lastGeneratedCards.Count == 0)

                throw new InvalidOperationException("لم يعثر النظام على الكروت التي أُنشئت لهذه الدفعة.");

            _previewCard = _lastGeneratedCards[^1];



            if (existingBatchId is null)

                PreviewBatchTextBlock.Text = $"تم إنشاء الدفعة {batch.BatchId}؛ عدد الكروت {batch.Quantity:N0}.";

            else

                PreviewBatchTextBlock.Text = $"تمت إضافة {countAdded:N0} كرت إلى الدفعة {batch.BatchId}. إجماليها الآن {batch.Quantity:N0}.";



            SetStatus(openPrintQueue

                ? $"نجح تجهيز {countAdded:N0} كرت. ستنتقل إلى قائمة الطباعة للمراجعة؛ لم تُرسل الكروت إلى الراوتر ولم تتم طباعة فعلية."

                : $"تم إنشاء {countAdded:N0} كرت في الدفعة {batch.BatchId}.",

                false);



            OpenGeneratedCardsButton.IsEnabled = true;

            OpenPrintQueueButton.IsEnabled = true;

            RefreshExistingBatches();

            _recentBatchesView?.Refresh();

            RecentBatchesGrid.Items.Refresh();

            RefreshRecentBatchesEmptyState();

            UpdatePreview();



            if (openPrintQueue)

                NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("printing", "قائمة الطباعة"));

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

        int quantity;

        if (SingleCardModeRadioButton.IsChecked == true)

        {

            quantity = 1;

        }

        else if (!TryParseInteger(QuantityTextBox.Text, out quantity))

        {

            throw new ArgumentException("أدخل عددًا صحيحًا للكروت.");

        }



        if (!TryParseInteger(CardNumberLengthTextBox.Text, out var cardNumberLength))

            throw new ArgumentException("أدخل الطول الكلي لرقم الكرت كعدد صحيح.");

        var usernameLength = cardNumberLength;

        if (_targetSystem == DemoAccountSystem.HotSpot && !TryParseInteger(UsernameLengthTextBox.Text, out usernameLength))

            throw new ArgumentException("أدخل طول اسم المستخدم كعدد صحيح.");



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



        var cardSet = ParseCardCharacterSet(ReadTag(CardNumberCharacterSetComboBox));

        var numberMode = SequentialGenerationRadioButton.IsChecked == true

            ? CardNumberGenerationMode.Sequential

            : CardNumberGenerationMode.Random;

        var usernameSet = _targetSystem == DemoAccountSystem.UserManager

            ? (cardSet switch

            {

                CardNumberCharacterSet.AlphaNumeric => CredentialCharacterSet.AlphaNumeric,

                CardNumberCharacterSet.LettersOnly => CredentialCharacterSet.LettersOnly,

                _ => CredentialCharacterSet.NumericOnly

            })

            : ParseCredentialCharacterSet(ReadTag(UsernameCharacterSetComboBox));

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

            CardPrefixTextBox.Text.Trim().ToUpperInvariant(),

            CardSuffixTextBox.Text.Trim().ToUpperInvariant(),

            cardSet,

            numberMode,

            SequenceStartTextBox.Text.Trim(),

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



    private void RefreshPackageCatalog(bool preserveSelection, string? preferredKey = null)

    {

        if (PackageComboBox is null)

            return;



        var oldKey = preserveSelection ? (PackageComboBox.SelectedItem as DemoCardPackage)?.Key : null;

        var packages = DemoCardPackageCatalog.Load(_targetSystem);

        PackageComboBox.ItemsSource = packages;



        var desiredKey = preferredKey ?? oldKey;

        var selected = !string.IsNullOrWhiteSpace(desiredKey)

            ? packages.FirstOrDefault(package => package.Key == desiredKey)

            : null;

        PackageComboBox.SelectedItem = selected ?? packages.ElementAtOrDefault(packages.Count > 1 ? 1 : 0);



        if (PackageComboBox.SelectedItem is null)

        {

            SetStatus("لا توجد باقات مفعّلة لهذا النظام. استخدم زر (+) لإنشاء باقة أو ربط باقة موجودة.", true);

            RouterProfileTextBox.Text = string.Empty;

        }

        else

        {

            UpdateProfileSuggestion();

        }

        RefreshExistingBatches();

    }



    private void PackageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)

    {

        if (PackageComboBox is null || !IsLoaded)

            return;

        _previewCard = null;

        UpdateProfileSuggestion();

        RefreshExistingBatches();

        UpdatePreview();

        PersistCurrentPreferences();

    }



    private void UpdateProfileSuggestion()

    {

        if (PackageComboBox?.SelectedItem is not DemoCardPackage package || RouterProfileTextBox is null)

            return;

        RouterProfileTextBox.Text = _targetSystem == DemoAccountSystem.HotSpot

            ? package.HotSpotProfile

            : package.UserManagerProfile;

    }



    private void FormSelectionChanged(object sender, SelectionChangedEventArgs e)

    {

        if (!IsLoaded)

            return;

        _previewCard = null;

        RefreshExistingBatches();

        UpdatePreview();

        PersistCurrentPreferences();

    }



    private void FormTextChanged(object sender, TextChangedEventArgs e)

    {

        if (!IsLoaded)

            return;

        _previewCard = null;

        RefreshExistingBatches();

        UpdatePreview();

        PersistCurrentPreferences();

    }



    private void GenerationMode_Changed(object sender, RoutedEventArgs e)

    {

        if (!IsLoaded || SequenceStartPanel is null || CardNumberCharacterSetComboBox is null ||

            SequentialGenerationRadioButton is null || SequenceStartTextBox is null)

            return;



        var sequential = SequentialGenerationRadioButton.IsChecked == true;

        SequenceStartPanel.Visibility = sequential ? Visibility.Visible : Visibility.Collapsed;

        if (sequential)

        {

            CardNumberCharacterSetComboBox.SelectedItem = CardNumberCharacterSetComboBox.Items

                .OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag?.ToString() == "NumericOnly");

            CardNumberCharacterSetComboBox.IsEnabled = false;

        }

        else

        {

            CardNumberCharacterSetComboBox.IsEnabled = true;

        }

        _previewCard = null;

        UpdatePreview();

    }



    private void PasswordModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)

    {

        if (!IsLoaded)

            return;

        _previewCard = null;

        UpdatePasswordControls();

        UpdatePreview();

        PersistCurrentPreferences();

    }



    private void UpdatePasswordControls()

    {

        if (PasswordModeComboBox is null || PasswordCharacterSetComboBox is null || PasswordLengthTextBox is null)

            return;

        var generated = ReadTag(PasswordModeComboBox) == "Generated";

        PasswordCharacterSetComboBox.IsEnabled = generated;

        PasswordLengthTextBox.IsEnabled = generated;

        if (PasswordGenerationPanel is not null)

            PasswordGenerationPanel.Visibility = generated ? Visibility.Visible : Visibility.Collapsed;

    }



    private void PreviewOptionChanged(object sender, RoutedEventArgs e)

    {

        if (!IsLoaded || CardPreviewBorder is null ||

            ShowCardNumberCheckBox is null || ShowUsernameCheckBox is null ||

            ShowPasswordCheckBox is null || ShowPackageCheckBox is null ||

            ShowPriceCheckBox is null || PreviewCardNumberTextBlock is null ||

            PreviewUsernameTextBlock is null || PreviewPasswordTextBlock is null ||

            PreviewPackageTextBlock is null || PackageComboBox is null)

            return;



        PreviewCardNumberTextBlock.Visibility = ShowCardNumberCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        PreviewUsernameTextBlock.Visibility = ShowUsernameCheckBox.IsChecked == true

            ? Visibility.Visible : Visibility.Collapsed;

        PreviewPasswordTextBlock.Visibility = ShowPasswordCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        var packageVisible = ShowPackageCheckBox.IsChecked == true || ShowPriceCheckBox.IsChecked == true;

        PreviewPackageTextBlock.Visibility = packageVisible ? Visibility.Visible : Visibility.Collapsed;

        UpdatePreview();

    }



    private void UpdatePreview()

    {

        if (!IsLoaded || PackageComboBox?.SelectedItem is not DemoCardPackage package)

            return;



        var prefix = CardPrefixTextBox.Text.Trim().ToUpperInvariant();

        var suffix = CardSuffixTextBox.Text.Trim().ToUpperInvariant();

        var totalLength = TryParseInteger(CardNumberLengthTextBox.Text, out var parsedLength) ? Math.Clamp(parsedLength, 5, 24) : 8;

        var bodyLength = Math.Max(1, totalLength - prefix.Length - suffix.Length);

        var sequential = SequentialGenerationRadioButton.IsChecked == true;

        var body = sequential

            ? (string.IsNullOrWhiteSpace(SequenceStartTextBox.Text) ? "1" : SequenceStartTextBox.Text.Trim()).PadLeft(bodyLength, '0')

            : new string('•', Math.Min(bodyLength, 16));

        PreviewCardNumberTextBlock.Text = _previewCard?.CardNumber ?? $"{prefix}{body}{suffix}";



        var usernameLength = _targetSystem == DemoAccountSystem.UserManager

            ? totalLength

            : (TryParseInteger(UsernameLengthTextBox.Text, out var uLength) ? Math.Clamp(uLength, 1, 20) : 6);

        var usernameSample = _previewCard?.Username ?? (_targetSystem == DemoAccountSystem.UserManager

            ? $"{prefix}{new string('•', Math.Min(bodyLength, 16))}{suffix}"

            : new string('•', Math.Min(usernameLength, 12)));

        PreviewUsernameTextBlock.Text = _targetSystem == DemoAccountSystem.UserManager

            ? $"اسم المستخدم ورقم الكرت: {usernameSample}"

            : $"اسم المستخدم: {usernameSample}";



        var mode = ReadTag(PasswordModeComboBox);

        var passwordText = _previewCard is not null

            ? _previewCard.PasswordDisplay

            : mode switch

            {

                "Empty" => "بدون كلمة مرور",

                "Generated" => new string('•', Math.Clamp(TryParseInteger(PasswordLengthTextBox.Text, out var pLength) ? pLength : 8, 1, 12)),

                _ => usernameSample

            };

        PreviewPasswordTextBlock.Text = $"كلمة المرور: {passwordText}";

        PreviewTimeTextBlock.Text = $"الوقت: {_previewCard?.TimeLimit ?? package.TimeLimit}";
        PreviewDataTextBlock.Text = $"التحميل: {_previewCard?.DataLimit ?? package.DataLimit}";
        PreviewSpeedTextBlock.Text = $"السرعة: {_previewCard?.RateLimit ?? package.RateLimit}";
        PreviewValidityTextBlock.Text = $"الصلاحية: {_previewCard?.PackageValidity ?? package.Validity}";

        var previewPrice = _previewCard?.UnitPriceLabel;
        if (string.IsNullOrWhiteSpace(previewPrice))
            previewPrice = $"{package.PrintPrice:N0} ريال";
        PreviewPriceTextBlock.Text = $"السعر: {previewPrice}";

        var packageParts = new List<string>();

        if (ShowPackageCheckBox.IsChecked == true)

            packageParts.Add(package.DisplayName.Split('—')[0].Trim());

        if (ShowPriceCheckBox.IsChecked == true)

            packageParts.Add($"{package.PrintPrice:N0} ريال");

        PreviewPackageTextBlock.Text = string.Join(" · ", packageParts);

        PreviewProfileTextBlock.Text = string.IsNullOrWhiteSpace(RouterProfileTextBox.Text) ? "info3" : RouterProfileTextBox.Text.Trim();



        PreviewCardNumberTextBlock.Visibility = ShowCardNumberCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        PreviewUsernameTextBlock.Visibility = ShowUsernameCheckBox.IsChecked == true

            ? Visibility.Visible : Visibility.Collapsed;

        PreviewPasswordTextBlock.Visibility = ShowPasswordCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        PreviewPackageTextBlock.Visibility = packageParts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;



        PreviewCredentialsCaptionTextBlock.Text = _targetSystem == DemoAccountSystem.UserManager

            ? (mode == "Empty" ? "رقم الكرت / اسم المستخدم — كلمة المرور فارغة" : "رقم الكرت / اسم المستخدم")

            : (mode == "Empty" ? "بيانات دخول دون كلمة مرور" : "بيانات الدخول");

        PreviewBatchTextBlock.Text = _previewCard is null

            ? $"معاينة متوقعة · صلاحية {package.Validity} · حد الوقت {package.TimeLimit} · {package.DataLimit} · {package.RateLimit}"

            : $"عينة من الدفعة {_previewCard.BatchId} · صلاحية {package.Validity} · حد الوقت {package.TimeLimit} · {package.DataLimit}";

    }



    private void ApplyPrintTemplate()

    {

        if (CardPreviewBorder is null || PrintTemplateComboBox is null)

            return;



        var tag = ReadTag(PrintTemplateComboBox);

        CardPreviewBorder.Height = 235;

        CardPreviewBorder.MinHeight = 215;

        CardPreviewBorder.MaxHeight = 250;

        CardPreviewBorder.Padding = tag == "compact" ? new Thickness(8) : new Thickness(10);



        if (tag == "compact")

        {

            CardPreviewBorder.Background = new SolidColorBrush(Color.FromRgb(249, 237, 237));

            CardPreviewBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(160, 24, 24));

            SetPreviewTextBrush(new SolidColorBrush(Color.FromRgb(32, 36, 42)), new SolidColorBrush(Color.FromRgb(109, 116, 128)));

        }

        else

        {

            // Reference design: white card face with TabCard's red accent, not a solid red card.

            CardPreviewBorder.Background = Brushes.White;

            CardPreviewBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(226, 199, 201));

            SetPreviewTextBrush(new SolidColorBrush(Color.FromRgb(32, 36, 42)), new SolidColorBrush(Color.FromRgb(109, 116, 128)));

        }

    }



    private void SetPreviewTextBrush(Brush primary, Brush secondary)

    {

        PreviewCredentialsCaptionTextBlock.Foreground = secondary;

        PreviewCardNumberTextBlock.Foreground = primary;

        PreviewUsernameTextBlock.Foreground = primary;

        PreviewPasswordTextBlock.Foreground = secondary;

        PreviewPackageTextBlock.Foreground = primary;

        PreviewProfileTextBlock.Foreground = secondary;

        PreviewTimeTextBlock.Foreground = primary;

        PreviewDataTextBlock.Foreground = primary;

        PreviewSpeedTextBlock.Foreground = primary;

        PreviewValidityTextBlock.Foreground = primary;

        PreviewPriceTextBlock.Foreground = primary;

    }



    private void PrintTemplateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)

    {

        if (!IsLoaded || _isSyncingPrintTemplateControls)

            return;

        SynchronizePrintTemplateControls(PrintTemplateComboBox, PreviewTemplateComboBox);

        ApplyPrintTemplate();

        UpdatePreview();

        PersistCurrentPreferences();

    }



    private void PreviewTemplateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)

    {

        if (!IsLoaded || _isSyncingPrintTemplateControls)

            return;

        SynchronizePrintTemplateControls(PreviewTemplateComboBox, PrintTemplateComboBox);

        ApplyPrintTemplate();

        UpdatePreview();

        PersistCurrentPreferences();

    }



    private void SynchronizePrintTemplateControls(ComboBox source, ComboBox target)

    {

        if (source is null || target is null || source.SelectedItem is not ComboBoxItem selected)

            return;



        var tag = selected.Tag?.ToString();

        if (string.IsNullOrWhiteSpace(tag))

            return;



        _isSyncingPrintTemplateControls = true;

        try

        {

            var targetItem = target.Items.OfType<ComboBoxItem>()

                .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase));



            if (targetItem is null)

            {

                var label = tag switch

                {

                    "nexora" => ReferenceEquals(target, PreviewTemplateComboBox) ? "info1" : "default",

                    "light" => ReferenceEquals(target, PreviewTemplateComboBox) ? "info2" : "قالب فاتح",

                    "compact" => ReferenceEquals(target, PreviewTemplateComboBox) ? "info3" : "قالب مضغوط",

                    _ when tag.StartsWith("custom-template:", StringComparison.OrdinalIgnoreCase) => tag["custom-template:".Length..],

                    _ => selected.Content?.ToString() ?? tag

                };

                targetItem = new ComboBoxItem { Content = label, Tag = tag };

                target.Items.Add(targetItem);

            }



            target.SelectedItem = targetItem;

        }

        finally

        {

            _isSyncingPrintTemplateControls = false;

        }

    }



    private void PrintMode_Changed(object sender, RoutedEventArgs e)

    {

        if (!IsLoaded || QuantityTextBox is null || SingleCardModeRadioButton is null)

            return;



        var single = SingleCardModeRadioButton.IsChecked == true;

        if (single && !_singleCardModeApplied)

        {

            _quantityBeforeSingleCard = QuantityTextBox.Text;

            QuantityTextBox.Text = "1";

            QuantityTextBox.IsEnabled = false;

            _singleCardModeApplied = true;

        }

        else if (!single && _singleCardModeApplied)

        {

            QuantityTextBox.IsEnabled = true;

            QuantityTextBox.Text = string.IsNullOrWhiteSpace(_quantityBeforeSingleCard) ? "20" : _quantityBeforeSingleCard;

            _singleCardModeApplied = false;

        }



        if (IssuanceStatusTextBlock is not null)

        {

            if (single)

                SetStatus("وضع كرت واحد: ستُنشأ بطاقة واحدة فقط. لا تُرسل مهمة طباعة فعلية.", false);

            else if (FastFilePrintModeRadioButton?.IsChecked == true)

                SetStatus("الطباعة السريعة عبر ملف مؤجلة حتى اكتمال الاستيراد والتحقق من الملف.", true);

            else if (SequentialPrintModeRadioButton?.IsChecked == true)

                SetStatus("تم اختيار مراجعة الكروت بالتتابع؛ هذا يحدد طريقة العرض فقط ولا يرسل مهمة إلى الطابعة.", false);

            else if (DefaultPrintModeRadioButton?.IsChecked == true)

                SetStatus("تم اختيار إعداد الطباعة الافتراضي للمعاينة.", false);

        }

        UpdatePreview();

        PersistCurrentPreferences();

    }



    private void BatchMode_Changed(object sender, RoutedEventArgs e)

    {

        if (!IsLoaded || ExistingBatchPanel is null || AppendToPreviousBatchRadioButton is null)

            return;



        ExistingBatchPanel.IsEnabled = AppendToPreviousBatchRadioButton.IsChecked == true;

        BatchRegistrationHintTextBlock.Text = AppendToPreviousBatchRadioButton.IsChecked == true

            ? "يُسمح بالإضافة إلى دفعة من نفس النظام والباقة فقط، حفاظًا على اتساق السجل."

            : "سيُنشأ رقم دفعة من النظام تلقائيًا.";

        RefreshExistingBatches();

        PersistCurrentPreferences();

    }



    private void ExistingBatchComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)

    {

        if (!IsLoaded || ExistingBatchComboBox.SelectedItem is not GeneratedCardBatch batch)

            return;

        BatchRegistrationHintTextBlock.Text = $"الدفعة المحددة: {batch.BatchId} · المسجل حاليًا {batch.Quantity:N0} كرت.";

    }



    private void RefreshExistingBatches()

    {

        if (ExistingBatchComboBox is null || PackageComboBox is null)

            return;



        var previousId = (ExistingBatchComboBox.SelectedItem as GeneratedCardBatch)?.BatchId;

        var targetName = _targetSystem == DemoAccountSystem.HotSpot ? "مستخدمو الهوتسبوت" : "مدير المستخدمين";

        var packageName = (PackageComboBox.SelectedItem as DemoCardPackage)?.DisplayName;

        var profile = RouterProfileTextBox?.Text.Trim() ?? string.Empty;

        var pointOfSale = (PointOfSaleComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;

        var managerAccount = _targetSystem == DemoAccountSystem.UserManager

            ? (ReadTag(ManagerUserComboBox) ?? "admin")

            : "غير منطبق";

        var sharedUsers = int.TryParse(ReadTag(SharedUsersComboBox), out var parsedSharedUsers) ? parsedSharedUsers : 1;

        var eligible = _issuanceService.Batches

            .Where(batch => batch.TargetSystem == targetName &&

                            !string.IsNullOrWhiteSpace(packageName) &&

                            batch.PackageName == packageName &&

                            batch.RouterProfile == profile &&

                            batch.PointOfSale == pointOfSale &&

                            batch.ManagerAccount == managerAccount &&

                            batch.SharedUsers == sharedUsers)

            .ToList();



        ExistingBatchComboBox.ItemsSource = eligible;

        ExistingBatchComboBox.SelectedItem = eligible.FirstOrDefault(batch => batch.BatchId == previousId)

            ?? eligible.FirstOrDefault();

        AppendToPreviousBatchRadioButton.IsEnabled = eligible.Count > 0;

        if (eligible.Count == 0 && AppendToPreviousBatchRadioButton.IsChecked == true)

            NewBatchRadioButton.IsChecked = true;

    }



    private void PickExistingBatchButton_Click(object sender, RoutedEventArgs e)

    {

        RefreshExistingBatches();

        if (ExistingBatchComboBox.Items.Count == 0)

        {

            SetStatus("لا توجد دفعات سابقة متوافقة مع النظام والباقة وملف الاستخدام الحالي.", true);

            return;

        }

        AppendToPreviousBatchRadioButton.IsChecked = true;

        ExistingBatchComboBox.Focus();

        ExistingBatchComboBox.IsDropDownOpen = true;

    }



    private void AddPrintTemplateButton_Click(object sender, RoutedEventArgs e) => AddNamedOption("template");

    private void AddPointOfSaleButton_Click(object sender, RoutedEventArgs e) => AddNamedOption("point-of-sale");



    private void AddNamedOption(string optionKind)

    {

        var isTemplate = optionKind == "template";

        var title = isTemplate ? "إضافة قالب طباعة" : "إضافة نقطة بيع";

        var prompt = isTemplate ? "اسم القالب" : "اسم نقطة البيع";

        var value = PromptForOption(title, prompt);

        if (string.IsNullOrWhiteSpace(value))

            return;



        try

        {

            _workflowStore.AddOption(GetCurrentSystemKey(), optionKind, value);

            var combo = isTemplate ? PrintTemplateComboBox : PointOfSaleComboBox;

            var tagPrefix = isTemplate ? "custom-template:" : "custom-pos:";

            var existing = combo.Items.OfType<ComboBoxItem>()

                .FirstOrDefault(item => string.Equals(item.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase));

            if (existing is null)

            {

                existing = new ComboBoxItem { Content = value, Tag = tagPrefix + value };

                combo.Items.Add(existing);

            }

            combo.SelectedItem = existing;

            PersistCurrentPreferences();

            SetStatus($"تم حفظ {(isTemplate ? "القالب" : "نقطة البيع")} «{value}».", false);

        }

        catch (Exception ex)

        {

            SetStatus("تعذر حفظ الخيار: " + ex.Message, true);

        }

    }



    private string? PromptForOption(string title, string prompt)

    {

        var editor = new TextBox

        {

            Height = 34,

            Margin = new Thickness(0, 7, 0, 11),

            Padding = new Thickness(8, 4, 8, 4),

            VerticalContentAlignment = VerticalAlignment.Center,

            FlowDirection = FlowDirection.RightToLeft

        };

        var saveButton = new Button

        {

            Content = "حفظ",

            MinWidth = 88,

            Height = 34,

            Padding = new Thickness(14, 0, 14, 0),

            Background = new SolidColorBrush(Color.FromRgb(160, 24, 24)),

            Foreground = Brushes.White,

            BorderBrush = new SolidColorBrush(Color.FromRgb(118, 13, 13)),

            IsDefault = true

        };

        var cancelButton = new Button { Content = "إلغاء", MinWidth = 88, Height = 34, Padding = new Thickness(14, 0, 14, 0), IsCancel = true };

        var dialog = new Window

        {

            Title = title,

            Width = 390,

            Height = 178,

            MinWidth = 350,

            ResizeMode = ResizeMode.NoResize,

            WindowStartupLocation = WindowStartupLocation.CenterOwner,

            FlowDirection = FlowDirection.RightToLeft,

            Background = new SolidColorBrush(Color.FromRgb(244, 245, 247)),

            Owner = Window.GetWindow(this)

        };

        var stack = new StackPanel { Margin = new Thickness(16) };

        stack.Children.Add(new TextBlock { Text = prompt, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(32, 36, 42)) });

        stack.Children.Add(editor);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };

        buttons.Children.Add(saveButton);

        buttons.Children.Add(cancelButton);

        stack.Children.Add(buttons);

        dialog.Content = stack;

        saveButton.Click += (_, _) =>

        {

            if (string.IsNullOrWhiteSpace(editor.Text))

            {

                MessageBox.Show(dialog, "اكتب اسمًا قبل الحفظ.", title, MessageBoxButton.OK, MessageBoxImage.Information);

                editor.Focus();

                return;

            }

            dialog.DialogResult = true;

            dialog.Close();

        };

        return dialog.ShowDialog() == true ? editor.Text.Trim() : null;

    }



    private string GetCurrentSystemKey() => _targetSystem == DemoAccountSystem.HotSpot ? "hotspot" : "user-manager";



    private void LoadPersistedOptions()

    {

        foreach (var value in _workflowStore.GetOptions(GetCurrentSystemKey(), "template"))

        {

            if (!PrintTemplateComboBox.Items.OfType<ComboBoxItem>().Any(item => string.Equals(item.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase)))

                PrintTemplateComboBox.Items.Add(new ComboBoxItem { Content = value, Tag = "custom-template:" + value });

        }

        foreach (var value in _workflowStore.GetOptions(GetCurrentSystemKey(), "point-of-sale"))

        {

            if (!PointOfSaleComboBox.Items.OfType<ComboBoxItem>().Any(item => string.Equals(item.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase)))

                PointOfSaleComboBox.Items.Add(new ComboBoxItem { Content = value, Tag = "custom-pos:" + value });

        }

    }



    private void LoadPersistedPreferences()

    {

        var values = _workflowStore.GetIssuancePreferences(GetCurrentSystemKey());

        if (values.Count == 0)

            return;

        _isApplyingPreferences = true;

        try

        {

            void SetText(string key, TextBox control)

            {

                if (values.TryGetValue(key, out var value) && value is not null)

                    control.Text = value;

            }

            void SetTag(string key, ComboBox control)

            {

                if (values.TryGetValue(key, out var value))

                    control.SelectedItem = control.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag?.ToString() == value)

                        ?? control.SelectedItem;

            }

            SetText("Quantity", QuantityTextBox);

            SetText("CardPrefix", CardPrefixTextBox);

            SetText("CardSuffix", CardSuffixTextBox);

            SetText("CardNumberLength", CardNumberLengthTextBox);

            SetText("UsernameLength", UsernameLengthTextBox);

            SetText("PasswordLength", PasswordLengthTextBox);

            SetText("SequenceStart", SequenceStartTextBox);

            SetText("RouterProfile", RouterProfileTextBox);

            SetText("Comment", CommentTextBox);

            SetTag("CardNumberCharacterSet", CardNumberCharacterSetComboBox);

            SetTag("UsernameCharacterSet", UsernameCharacterSetComboBox);

            SetTag("PasswordCharacterSet", PasswordCharacterSetComboBox);

            SetTag("PasswordMode", PasswordModeComboBox);

            SetTag("ManagerAccount", ManagerUserComboBox);

            SetTag("SharedUsers", SharedUsersComboBox);

            SetTag("PointOfSale", PointOfSaleComboBox);

            SetTag("PrintTemplate", PrintTemplateComboBox);

            if (values.TryGetValue("PackageKey", out var packageKey))

                PackageComboBox.SelectedItem = PackageComboBox.Items.OfType<DemoCardPackage>().FirstOrDefault(package => package.Key == packageKey)

                    ?? PackageComboBox.SelectedItem;

            if (values.TryGetValue("PrintMode", out var printMode))

            {

                DefaultPrintModeRadioButton.IsChecked = printMode == "default";

                SingleCardModeRadioButton.IsChecked = printMode == "single";

                SequentialPrintModeRadioButton.IsChecked = printMode == "sequential";

                FastFilePrintModeRadioButton.IsChecked = printMode == "file";

            }

            if (values.TryGetValue("NumberGenerationMode", out var numberMode))

            {

                SequentialGenerationRadioButton.IsChecked = numberMode == "sequential";

                RandomGenerationRadioButton.IsChecked = numberMode != "sequential";

            }

            if (values.TryGetValue("BatchMode", out var batchMode))

            {

                NewBatchRadioButton.IsChecked = batchMode == "new";

                AppendToPreviousBatchRadioButton.IsChecked = batchMode == "append";

            }

            if (values.TryGetValue("ShowCardNumber", out var showNumber)) ShowCardNumberCheckBox.IsChecked = showNumber != "false";

            if (values.TryGetValue("ShowUsername", out var showUsername)) ShowUsernameCheckBox.IsChecked = showUsername != "false";

            if (values.TryGetValue("ShowPassword", out var showPassword)) ShowPasswordCheckBox.IsChecked = showPassword != "false";

            if (values.TryGetValue("ShowPackage", out var showPackage)) ShowPackageCheckBox.IsChecked = showPackage != "false";

            if (values.TryGetValue("ShowPrice", out var showPrice)) ShowPriceCheckBox.IsChecked = showPrice != "false";

            if (values.TryGetValue("ExistingBatchId", out var existingBatchId))

            {

                RefreshExistingBatches();

                ExistingBatchComboBox.SelectedItem = ExistingBatchComboBox.Items.OfType<GeneratedCardBatch>()

                    .FirstOrDefault(batch => string.Equals(batch.BatchId, existingBatchId, StringComparison.OrdinalIgnoreCase))

                    ?? ExistingBatchComboBox.SelectedItem;

            }

            if (values.TryGetValue("PrintPaperSize", out var paperSize)) _printPaperSize = paperSize;

            if (values.TryGetValue("PrintColumns", out var columns) && int.TryParse(columns, out var parsedColumns)) _printColumns = Math.Clamp(parsedColumns, 1, 8);

            if (values.TryGetValue("PrintCutMarks", out var cutMarks)) _printCutMarks = cutMarks != "false";

        }

        finally

        {

            _isApplyingPreferences = false;

        }

    }



    private void PersistCurrentPreferences()

    {

        if (_isApplyingPreferences || !IsLoaded || PackageComboBox is null)

            return;

        try

        {

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)

            {

                ["Quantity"] = QuantityTextBox.Text,

                ["CardPrefix"] = CardPrefixTextBox.Text,

                ["CardSuffix"] = CardSuffixTextBox.Text,

                ["CardNumberLength"] = CardNumberLengthTextBox.Text,

                ["UsernameLength"] = UsernameLengthTextBox.Text,

                ["PasswordLength"] = PasswordLengthTextBox.Text,

                ["SequenceStart"] = SequenceStartTextBox.Text,

                ["RouterProfile"] = RouterProfileTextBox.Text,

                ["Comment"] = CommentTextBox.Text,

                ["CardNumberCharacterSet"] = ReadTag(CardNumberCharacterSetComboBox) ?? "NumericOnly",

                ["UsernameCharacterSet"] = ReadTag(UsernameCharacterSetComboBox) ?? "NumericOnly",

                ["PasswordCharacterSet"] = ReadTag(PasswordCharacterSetComboBox) ?? "NumericOnly",

                ["PasswordMode"] = ReadTag(PasswordModeComboBox) ?? "Empty",

                ["ManagerAccount"] = ReadTag(ManagerUserComboBox) ?? "admin",

                ["SharedUsers"] = ReadTag(SharedUsersComboBox) ?? "1",

                ["PointOfSale"] = ReadTag(PointOfSaleComboBox) ?? "main",

                ["PrintTemplate"] = ReadTag(PrintTemplateComboBox) ?? "nexora",

                ["PackageKey"] = (PackageComboBox.SelectedItem as DemoCardPackage)?.Key ?? string.Empty,

                ["PrintMode"] = SingleCardModeRadioButton.IsChecked == true ? "single" : SequentialPrintModeRadioButton.IsChecked == true ? "sequential" : FastFilePrintModeRadioButton.IsChecked == true ? "file" : "default",

                ["NumberGenerationMode"] = SequentialGenerationRadioButton.IsChecked == true ? "sequential" : "random",

                ["ExistingBatchId"] = (ExistingBatchComboBox.SelectedItem as GeneratedCardBatch)?.BatchId ?? string.Empty,

                ["BatchMode"] = AppendToPreviousBatchRadioButton.IsChecked == true ? "append" : "new",

                ["ShowCardNumber"] = ShowCardNumberCheckBox.IsChecked == true ? "true" : "false",

                ["ShowUsername"] = ShowUsernameCheckBox.IsChecked == true ? "true" : "false",

                ["ShowPassword"] = ShowPasswordCheckBox.IsChecked == true ? "true" : "false",

                ["ShowPackage"] = ShowPackageCheckBox.IsChecked == true ? "true" : "false",

                ["ShowPrice"] = ShowPriceCheckBox.IsChecked == true ? "true" : "false",

                ["PrintPaperSize"] = _printPaperSize,

                ["PrintColumns"] = _printColumns.ToString(CultureInfo.InvariantCulture),

                ["PrintCutMarks"] = _printCutMarks ? "true" : "false"

            };

            _workflowStore.SaveIssuancePreferences(GetCurrentSystemKey(), values);

        }

        catch (Exception ex)

        {

            SetStatus("تعذر حفظ تفضيلات الشاشة: " + ex.Message, true);

        }

    }



    private void AddPackageButton_Click(object sender, RoutedEventArgs e)

    {

        try

        {

            var providerKey = _targetSystem == DemoAccountSystem.HotSpot ? "hotspot" : "user-manager";

            var dialog = new PackageEditorWindow(providerKey);

            var owner = Window.GetWindow(this);

            if (owner is not null)

                dialog.Owner = owner;



            if (dialog.ShowDialog() == true)

            {

                var catalog = DemoPackageStore.Load();

                var newestPackage = catalog.Packages

                    .Where(package => !package.IsArchived)

                    .OrderByDescending(package => package.CreatedAt)

                    .FirstOrDefault();

                RefreshPackageCatalog(preserveSelection: false, preferredKey: newestPackage?.Id);

                SetStatus("تم حفظ الباقة وتحديث قائمة الباقات المتاحة للإصدار.", false);

            }

        }

        catch (Exception ex)

        {

            SetStatus("تعذر فتح أو حفظ شاشة الباقة: " + ex.Message, true);

        }

    }



    private void PrintOptionsButton_Click(object sender, RoutedEventArgs e)

    {

        var dialog = new IssuanceUtilityDialog(

            showOffers: false, currentPaperSize: _printPaperSize, currentColumns: _printColumns, showCutMarks: _printCutMarks);

        var owner = Window.GetWindow(this);

        if (owner is not null) dialog.Owner = owner;



        if (dialog.ShowDialog() == true)

        {

            _printPaperSize = dialog.SelectedPaperSize;

            _printColumns = dialog.SelectedColumns;

            _printCutMarks = dialog.SelectedCutMarks;

            PersistCurrentPreferences();

            SetStatus($"تم حفظ إعدادات المعاينة: {_printPaperSize} · {_printColumns} أعمدة · " +

                      (_printCutMarks ? "فواصل قص ظاهرة." : "دون فواصل قص."), false);

        }

    }



    private void OffersButton_Click(object sender, RoutedEventArgs e)

    {

        var dialog = new IssuanceUtilityDialog(showOffers: true, currentPaperSize: _printPaperSize, currentColumns: _printColumns, showCutMarks: _printCutMarks);

        var owner = Window.GetWindow(this);

        if (owner is not null) dialog.Owner = owner;

        dialog.ShowDialog();

    }



    private void OpenGeneratedCardsButton_Click(object sender, RoutedEventArgs e)

    {

        var section = _targetSystem == DemoAccountSystem.HotSpot ? "hotspot" : "user-manager";

        NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs(section, "إدارة الكروت"));

    }



    private void OpenPrintQueueButton_Click(object sender, RoutedEventArgs e) =>

        NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("printing", "قائمة الطباعة"));



    private void SetStatus(string message, bool isError)

    {

        if (IssuanceStatusTextBlock is null)

            return;

        IssuanceStatusTextBlock.Text = message;

        IssuanceStatusTextBlock.Foreground = new SolidColorBrush(isError ? Color.FromRgb(180, 35, 24) : Color.FromRgb(22, 138, 91));

    }

}

/// <summary>Displays a one-based row number in the local batch history table.</summary>
public sealed class OneBasedAlternationIndexConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int index ? index + 1 : 1;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

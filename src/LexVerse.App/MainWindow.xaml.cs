using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using LexVerse.Application.Modules;
using LexVerse.Application.Commerce;
using LexVerse.Application.Popup;
using LexVerse.Application.Product;
using LexVerse.Application.Privacy;
using LexVerse.Application.Realtime;
using LexVerse.App.Features.Popup;
using LexVerse.App.Features.Realtime;
using LexVerse.Core.Pipeline;
using LexVerse.Core.Product;
using LexVerse.Infrastructure.Windows;

namespace LexVerse.App;

public partial class MainWindow : Window
{
    private const int HotkeyId = 0x4C56;
    private const int WmHotkey = 0x0312;
    private const uint VkF6 = 0x75;
    private const uint ModNoRepeat = 0x4000;
    private const uint WdaExcludeFromCapture = 0x00000011;
    private readonly PopupFeatureModule _popupModule;
    private readonly RealtimeFeatureModule _realtimeModule;
    private readonly WindowsMonitorService _monitorService;
    private readonly IFeatureAccessService _featureAccessService;
    private readonly CommerceCoordinator _commerce;
    private readonly IPrivacyPreferencesService _privacyPreferences;
    private bool _isUpdatingPrivacyUi;
    private bool _regionAccessAllowed;
    private bool _fullScreenAccessAllowed;
    private CancellationTokenSource? _popupCancellation;
    private HwndSource? _hwndSource;
    private bool _hotkeyRegistered;
    private bool _isSelectingRegion;
    private bool _cleanupStarted;
    private bool _cleanupComplete;

    public MainWindow(
        PopupFeatureModule popupModule,
        RealtimeFeatureModule realtimeModule,
        FeatureModuleRegistry modules,
        WindowsMonitorService monitorService,
        IFeatureAccessService featureAccessService,
        CommerceCoordinator commerce,
        IPrivacyPreferencesService privacyPreferences)
    {
        ArgumentNullException.ThrowIfNull(popupModule);
        ArgumentNullException.ThrowIfNull(realtimeModule);
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(monitorService);
        ArgumentNullException.ThrowIfNull(featureAccessService);
        ArgumentNullException.ThrowIfNull(commerce);
        ArgumentNullException.ThrowIfNull(privacyPreferences);

        _popupModule = popupModule;
        _realtimeModule = realtimeModule;
        _monitorService = monitorService;
        _featureAccessService = featureAccessService;
        _commerce = commerce;
        _privacyPreferences = privacyPreferences;
        InitializeComponent();
        ApplyPrivacyPreferences(_privacyPreferences.Current);
        ModuleCountText.Text = $"{modules.Modules.Count} module registered by the composition root";
        _realtimeModule.Coordinator.StateChanged += RealtimeCoordinator_StateChanged;
        _realtimeModule.Coordinator.FrameProcessed += RealtimeCoordinator_FrameProcessed;
        _commerce.StateChanged += Commerce_StateChanged;
        _privacyPreferences.Changed += PrivacyPreferences_Changed;
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;
        try
        {
            await _commerce.InitializeAsync();
        }
        catch
        {
            StatusText.Text = "Account and plan information is temporarily unavailable.";
        }

        await RefreshFeatureAccessAsync();
    }

    private void PrivacyPreferences_Changed(object? sender, PrivacyPreferences preferences) =>
        Dispatcher.BeginInvoke(async () =>
        {
            ApplyPrivacyPreferences(preferences);
            await RefreshFeatureAccessAsync();
        });

    private void ApplyPrivacyPreferences(PrivacyPreferences preferences)
    {
        _isUpdatingPrivacyUi = true;
        try
        {
            RemoteProcessingCheckBox.IsChecked = preferences.AllowRemoteTextProcessing;
            LocalDiagnosticsCheckBox.IsChecked = preferences.AllowLocalDiagnostics;
        }
        finally
        {
            _isUpdatingPrivacyUi = false;
        }
    }

    private async void PrivacySetting_Changed(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingPrivacyUi)
        {
            return;
        }

        var current = _privacyPreferences.Current;
        var allowRemote = RemoteProcessingCheckBox.IsChecked == true;
        if (allowRemote && !current.AllowRemoteTextProcessing)
        {
            var confirmation = MessageBox.Show(
                "LexVerse will send selected, typed, or OCR text to the configured translation provider. Operational metrics never include that text. Continue?",
                "Allow external text processing",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (confirmation != MessageBoxResult.Yes)
            {
                ApplyPrivacyPreferences(current);
                return;
            }
        }

        try
        {
            var updated = new PrivacyPreferences(
                allowRemote,
                LocalDiagnosticsCheckBox.IsChecked == true);
            await _privacyPreferences.UpdateAsync(updated);
            if (!updated.AllowRemoteTextProcessing)
            {
                await _realtimeModule.Coordinator.StopAsync();
            }

            StatusText.Text = "Privacy preferences saved.";
        }
        catch
        {
            ApplyPrivacyPreferences(current);
            StatusText.Text = "Privacy preferences could not be saved.";
        }
    }

    private void Commerce_StateChanged(object? sender, CommerceState state) =>
        Dispatcher.BeginInvoke(() => RenderCommerceState(state));

    private void RenderCommerceState(CommerceState state)
    {
        var busy = state.Status == CommerceStatus.Loading;
        CommercePanel.Visibility = state.Status == CommerceStatus.Unconfigured
            ? Visibility.Collapsed
            : Visibility.Visible;
        AccountStatusText.Text = state.Status switch
        {
            CommerceStatus.Unconfigured => "FREE MODE",
            CommerceStatus.SignedOut => "SIGNED OUT",
            CommerceStatus.Ready => "SIGNED IN",
            CommerceStatus.CheckoutOpened => "CHECKOUT",
            CommerceStatus.Error => "RETRY",
            _ => "LOADING"
        };
        AccountDetailText.Text = state.Status switch
        {
            CommerceStatus.Unconfigured => "The production MVP is available without an account.",
            CommerceStatus.SignedOut => "Sign in to purchase, restore, or use paid features.",
            CommerceStatus.Ready => $"Signed in as {state.Account?.DisplayName}.",
            CommerceStatus.CheckoutOpened => "Checkout opened in your browser. Use Restore / refresh when complete.",
            CommerceStatus.Error => "Could not reach the account service. Check the network and retry.",
            _ => "Loading account and current plans..."
        };

        var selectedOfferId = (OfferBox.SelectedItem as ComboBoxItem)?.Tag as string;
        OfferBox.Items.Clear();
        var catalog = state.Catalog;
        var publicOffers = catalog?.Offers
            .Where(offer => offer.IsPublic)
            .OrderBy(offer => offer.Price.Amount)
            ?? Enumerable.Empty<ProductOffer>();
        foreach (var offer in publicOffers)
        {
            var plan = catalog?.FindPlan(offer.PlanId);
            if (plan is not { IsPublic: true })
            {
                continue;
            }

            var period = offer.BillingPeriod is null
                ? "one-time"
                : $"every {offer.BillingPeriod.Count} {offer.BillingPeriod.Unit.ToString().ToLowerInvariant()}";
            OfferBox.Items.Add(new ComboBoxItem
            {
                Content = $"{plan.DisplayName} · {offer.Price.Amount:N0} {offer.Price.CurrencyCode} · {period}",
                Tag = offer.Id
            });
        }

        OfferBox.SelectedItem = OfferBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, selectedOfferId, StringComparison.Ordinal))
            ?? OfferBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
        var signedIn = state.Account is not null;
        SignInButton.IsEnabled = !busy && state.Status != CommerceStatus.Unconfigured && !signedIn;
        SignOutButton.IsEnabled = !busy && signedIn;
        RestoreButton.IsEnabled = !busy && signedIn;
        OfferBox.IsEnabled = !busy && OfferBox.Items.Count > 0;
        BuyButton.IsEnabled = !busy && signedIn && OfferBox.SelectedItem is not null;
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e) =>
        await RunCommerceOperationAsync(_commerce.SignInAsync, "Opening secure sign-in in your browser...");

    private async void SignOut_Click(object sender, RoutedEventArgs e) =>
        await RunCommerceOperationAsync(_commerce.SignOutAsync, "Signing out...");

    private async void Restore_Click(object sender, RoutedEventArgs e) =>
        await RunCommerceOperationAsync(_commerce.RestorePurchasesAsync, "Restoring purchases and refreshing access...");

    private async void Buy_Click(object sender, RoutedEventArgs e)
    {
        if ((OfferBox.SelectedItem as ComboBoxItem)?.Tag is not string offerId)
        {
            return;
        }

        await RunCommerceOperationAsync(
            token => _commerce.StartCheckoutAsync(offerId, token),
            "Creating secure checkout...");
    }

    private void OfferBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        BuyButton.IsEnabled = _commerce.State.Account is not null && OfferBox.SelectedItem is not null;

    private async Task RunCommerceOperationAsync(
        Func<CancellationToken, Task> operation,
        string status)
    {
        StatusText.Text = status;
        try
        {
            await operation(CancellationToken.None);
            await RefreshFeatureAccessAsync();
            StatusText.Text = "Account and feature access updated.";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Account operation canceled.";
        }
        catch
        {
            StatusText.Text = "Account operation failed. Check the network and try again.";
        }
    }

    private async Task RefreshFeatureAccessAsync()
    {
        var region = await _featureAccessService.GetAccessAsync(ProductFeatures.RegionTranslation);
        var fullScreen = await _featureAccessService.GetAccessAsync(ProductFeatures.FullScreenTranslation);
        _regionAccessAllowed = region.IsAllowed;
        _fullScreenAccessAllowed = fullScreen.IsAllowed;
        var remoteProcessingAllowed = _privacyPreferences.Current.AllowRemoteTextProcessing;
        QuickTranslateButton.IsEnabled = remoteProcessingAllowed;
        RegionButton.IsEnabled = _regionAccessAllowed && remoteProcessingAllowed;
        FullScreenButton.IsEnabled = _fullScreenAccessAllowed && remoteProcessingAllowed;
        PopupStatusText.Text = remoteProcessingAllowed ? "ACTIVE" : "CONSENT";
        RegionButton.Content = !_regionAccessAllowed
            ? "Document region \u00B7 unavailable"
            : remoteProcessingAllowed ? "Select document region" : "Document region \u00B7 consent";
        FullScreenButton.Content = !_fullScreenAccessAllowed
            ? "Full screen \u00B7 locked"
            : remoteProcessingAllowed ? "Full screen" : "Full screen \u00B7 consent";
        RealtimeStatusText.Text = !remoteProcessingAllowed
            ? "CONSENT"
            : _regionAccessAllowed ? "READY" : "UNAVAILABLE";
        RealtimeStatusText.Foreground = (System.Windows.Media.Brush)FindResource(
            remoteProcessingAllowed && _regionAccessAllowed
                ? "SuccessBrush"
                : "AccentBrush");
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var handle = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(handle);
        _hwndSource?.AddHook(WndProc);
        _ = SetWindowDisplayAffinity(handle, WdaExcludeFromCapture);
        _hotkeyRegistered = RegisterHotKey(handle, HotkeyId, ModNoRepeat, VkF6);
        HotkeyStatusText.Text = _hotkeyRegistered ? "F6 ready" : "F6 unavailable";
        StatusText.Text = _hotkeyRegistered
            ? "Ready. Highlight text in another app and press F6."
            : "F6 is used by another app. Quick text remains available.";
    }

    private async void TranslateQuickText_Click(object sender, RoutedEventArgs e)
    {
        var text = QuickTextBox.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            StatusText.Text = "Enter text in Quick text first.";
            QuickTextBox.Focus();
            return;
        }

        await RunPopupAsync(
            token => _popupModule.UseCase.TranslateTextAsync(text, CreateRequest(), token),
            "Translating quick text…");
    }

    private void MinimizeForHotkey_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
        StatusText.Text = "Highlight text in another app and press F6.";
    }

    private async void StartRegionRealtime_Click(object sender, RoutedEventArgs e)
    {
        if (_isSelectingRegion)
        {
            return;
        }

        _isSelectingRegion = true;
        try
        {
            await _realtimeModule.Coordinator.StopAsync();
            WindowState = WindowState.Minimized;
            await Task.Delay(160);
            var selector = new SelectionOverlayWindow();
            var selected = selector.ShowDialog();
            WindowState = WindowState.Normal;
            Activate();

            if (selected != true || selector.Selection is not { } selection)
            {
                StatusText.Text = "Region selection canceled.";
                return;
            }

            await StartRealtimeAsync(RealtimeCaptureKind.Region, selection);
        }
        catch (Exception exception)
        {
            ShowRealtimeStartError(exception);
        }
        finally
        {
            _isSelectingRegion = false;
        }
    }

    private async void StartFullScreenRealtime_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            var monitorBounds = _monitorService.GetNearestMonitorBounds(handle);
            await StartRealtimeAsync(RealtimeCaptureKind.FullScreen, monitorBounds);
        }
        catch (Exception exception)
        {
            ShowRealtimeStartError(exception);
        }
    }

    private async void StopRealtime_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _realtimeModule.Coordinator.StopAsync();
            StatusText.Text = "Realtime translation stopped.";
        }
        catch
        {
            StatusText.Text = "Realtime cleanup did not finish normally.";
        }
    }

    private Task StartRealtimeAsync(RealtimeCaptureKind kind, LexVerse.Core.Geometry.ScreenRect bounds)
    {
        StatusText.Text = kind == RealtimeCaptureKind.Region
            ? "Starting selected-region translation…"
            : "Starting full-screen translation…";
        var request = new RealtimeTranslationRequest(
            kind,
            bounds,
            GetOcrLanguageTag(),
            CreateRealtimeOptions());
        return _realtimeModule.Coordinator.StartAsync(request);
    }

    private RealtimeTranslationOptions CreateRealtimeOptions()
    {
        var sourceLanguage = GetSelectedTag(SourceLanguageBox);

        return RealtimeTranslationOptions.Document with
        {
            SourceLanguage = sourceLanguage,
            TargetLanguage = GetSelectedTag(TargetLanguageBox)
        };
    }

    private string GetOcrLanguageTag() => GetSelectedTag(SourceLanguageBox) switch
    {
        "ja" => "ja-JP",
        "ko" => "ko-KR",
        "zh" => "zh-CN",
        "vi" => "vi-VN",
        _ => "en-US"
    };

    private void RealtimeCoordinator_StateChanged(object? sender, RealtimeCoordinatorState state)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var running = state.Status is RealtimeCoordinatorStatus.Starting or RealtimeCoordinatorStatus.Running;
            var remoteProcessingAllowed = _privacyPreferences.Current.AllowRemoteTextProcessing;
            RegionButton.IsEnabled = _regionAccessAllowed && remoteProcessingAllowed && !running && !_isSelectingRegion;
            FullScreenButton.IsEnabled = _fullScreenAccessAllowed && remoteProcessingAllowed && !running;
            StopRealtimeButton.IsEnabled = running;
            RealtimeStatusText.Text = state.Status.ToString().ToUpperInvariant();

            if (state.Status == RealtimeCoordinatorStatus.Faulted)
            {
                StatusText.Text = "Realtime pipeline stopped after an error. Check provider and OCR availability.";
            }
        });
    }

    private void RealtimeCoordinator_FrameProcessed(object? sender, RealtimeFrameUpdate update)
    {
        Dispatcher.BeginInvoke(() =>
        {
            StatusText.Text = update.UsedCachedTranslation
                ? $"Realtime: cached frame · {update.Timing.Total.TotalMilliseconds:0} ms"
                : $"Realtime: OCR {update.OcrBlockCount}, translated {update.TranslatedBlockCount} · {update.Timing.Total.TotalMilliseconds:0} ms";
            if (update.PerformanceBudgetExceeded)
            {
                StatusText.Text += " · slower than target";
            }
        });
    }

    private void ShowRealtimeStartError(Exception exception)
    {
        StatusText.Text = exception is FeatureAccessDeniedException denied
            ? $"Realtime access unavailable: {denied.Decision.DenialReason}."
            : "Could not start realtime translation. Check capture permission, OCR language, and provider setup.";
    }

    private async Task RunSelectedTextPopupAsync()
    {
        await RunPopupAsync(
            token => _popupModule.UseCase.TranslateSelectedTextAsync(CreateRequest(), token),
            "Reading highlighted text…");
    }

    private async Task RunPopupAsync(
        Func<CancellationToken, Task<PopupTranslationResult>> operation,
        string loadingStatus)
    {
        _popupCancellation?.Cancel();
        _popupCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _popupCancellation = cancellation;
        var cancellationToken = cancellation.Token;
        var popup = new TranslationPopupWindow();

        StatusText.Text = loadingStatus;
        QuickTranslateButton.IsEnabled = false;

        try
        {
            var operationTask = operation(cancellationToken);
            var loadingDelay = Task.Delay(TimeSpan.FromMilliseconds(260), cancellationToken);
            if (await Task.WhenAny(operationTask, loadingDelay) != operationTask)
            {
                popup.Show();
            }

            var result = await operationTask;
            if (!result.HasSelection || result.Translation is null || result.SourceText is null)
            {
                popup.Close();
                StatusText.Text = "No highlighted text found. Select text and try F6 again.";
                return;
            }

            if (!popup.IsVisible)
            {
                popup.Show();
            }

            popup.ShowResult(
                result.SourceText,
                result.Translation.TranslatedText,
                GetTargetLanguageLabel());
            StatusText.Text = "Translation complete.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            popup.Close();
        }
        catch (RemoteProcessingConsentRequiredException)
        {
            popup.Close();
            StatusText.Text = "Enable external text processing under Privacy before translating.";
        }
        catch (FeatureAccessDeniedException exception)
        {
            popup.Close();
            StatusText.Text = $"Popup access unavailable: {exception.Decision.DenialReason}.";
        }
        catch
        {
            if (!popup.IsVisible)
            {
                popup.Show();
            }

            popup.ShowError(
                "Translation unavailable",
                "Check your provider credentials and network connection, then try again.");
            StatusText.Text = "Translation provider unavailable.";
        }
        finally
        {
            QuickTranslateButton.IsEnabled = _privacyPreferences.Current.AllowRemoteTextProcessing;
            if (ReferenceEquals(_popupCancellation, cancellation))
            {
                _popupCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private PopupTranslationRequest CreateRequest()
    {
        var sourceLanguage = GetSelectedTag(SourceLanguageBox);
        return new PopupTranslationRequest(
            GetSelectedTag(TargetLanguageBox),
            sourceLanguage.Equals("auto", StringComparison.OrdinalIgnoreCase)
                ? null
                : sourceLanguage);
    }

    private string GetTargetLanguageLabel() =>
        TargetLanguageBox.SelectedItem is ComboBoxItem { Content: { } content }
            ? content.ToString() ?? "Translation"
            : "Translation";

    private static string GetSelectedTag(ComboBox comboBox) =>
        comboBox.SelectedItem is ComboBoxItem { Tag: { } tag }
            ? tag.ToString() ?? string.Empty
            : comboBox.Text.Trim();

    private IntPtr WndProc(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            _ = RunSelectedTextPopupAsync();
        }

        return IntPtr.Zero;
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_cleanupComplete)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        if (_cleanupStarted)
        {
            return;
        }

        _cleanupStarted = true;
        IsEnabled = false;
        StatusText.Text = "Stopping background work…";
        try
        {
            await _realtimeModule.Coordinator.StopAsync();
        }
        catch
        {
        }

        _cleanupComplete = true;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Normal, Close);
    }

    protected override void OnClosed(EventArgs e)
    {
        _popupCancellation?.Cancel();
        _popupCancellation?.Dispose();
        _popupCancellation = null;

        var handle = new WindowInteropHelper(this).Handle;
        if (_hotkeyRegistered)
        {
            UnregisterHotKey(handle, HotkeyId);
        }

        _hwndSource?.RemoveHook(WndProc);
        _realtimeModule.Coordinator.StateChanged -= RealtimeCoordinator_StateChanged;
        _realtimeModule.Coordinator.FrameProcessed -= RealtimeCoordinator_FrameProcessed;
        _commerce.StateChanged -= Commerce_StateChanged;
        _privacyPreferences.Changed -= PrivacyPreferences_Changed;
        base.OnClosed(e);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
}

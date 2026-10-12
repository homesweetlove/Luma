using System.Text.Json;
using Luma.Models;
using Luma.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Web.WebView2.Core;
using Windows.System;

namespace Luma;

public sealed partial class MainWindow : Window
{
    private const string HomeUrl = "https://www.google.com";

    private readonly Dictionary<TabViewItem, WebView2> _webViews = [];
    private readonly SnapshotService _snapshotService = new();
    private readonly MacroService _macroService = new();
    private readonly ForegroundWatchService _watchService = new();
    private readonly LocalIntentRouter _intentRouter = new();
    private readonly ApprovalPolicy _approvalPolicy = new();
    private readonly ProviderRegistry _providers = new();
    private readonly DispatcherTimer _watchTimer = new();

    private readonly string _dataRoot;
    private readonly AuditLogService _auditLog;
    private readonly SecureVaultService _vault;
    private CoreWebView2Environment? _webEnvironment;
    private bool _windowActive = true;
    private bool _watchTickRunning;
    private WebView2? _watchTab;
    private string? _watchOrigin;
    private DateTimeOffset _watchStarts;
    private DateTimeOffset _watchEnds;
    private string _watchText = string.Empty;
    private bool _watchAutoClick;
    private bool _watchReplay;
    private CancellationTokenSource? _automationCancellation;
    private readonly Dictionary<string, VisibleWebAiDelegate> _delegates = [];
    private VisibleWebAiDelegate? _lastDelegate;
    private bool _dialogOpen;
    private bool _closing;
    private WebView2? _recordingTab;
    private readonly HashSet<WebView2> _loadingTabs = [];
    private int _watchVersion;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    public MainWindow()
    {
        InitializeComponent();

        _dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Luma");
        Directory.CreateDirectory(_dataRoot);

        _auditLog = new AuditLogService(_dataRoot);
        _vault = new SecureVaultService(_dataRoot);

        _watchTimer.Interval = TimeSpan.FromSeconds(3);
        _watchTimer.Tick += WatchTimer_Tick;

        Activated += MainWindow_Activated;
        Closed += (_, _) =>
        {
            _closing = true;
            _watchTimer.Stop();
            _automationCancellation?.Cancel();
            foreach (var view in _webViews.Values) view.Close();
        };
        _delegates["Gemini"] = new VisibleWebAiDelegate("Gemini", "https://gemini.google.com", OpenDelegateTabAsync, CanExecute);
        _delegates["Claude"] = new VisibleWebAiDelegate("Claude", "https://claude.ai", OpenDelegateTabAsync, CanExecute);

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            await _auditLog.InitializeAsync();

            var profilePath = Path.Combine(_dataRoot, "WebView2Profile");
            Directory.CreateDirectory(profilePath);
            _webEnvironment = await CoreWebView2Environment.CreateWithOptionsAsync(null, profilePath, new CoreWebView2EnvironmentOptions());

            await CreateTabAsync(HomeUrl);
            SetStatus("Ready · WebView2 profile and local audit log initialized.");
        }
        catch (Exception ex)
        {
            SetStatus($"Startup error: {ex.Message}");
        }
    }

    private WebView2? ActiveWebView
    {
        get
        {
            if (BrowserTabs.SelectedItem is TabViewItem tab && _webViews.TryGetValue(tab, out var webView))
            {
                return webView;
            }

            return null;
        }
    }

    private async Task<WebView2> CreateTabAsync(string? url = null)
    {
        if (_webEnvironment is null)
        {
            throw new InvalidOperationException("Browser is still starting.");
        }

        var webView = new WebView2();
        var tab = new TabViewItem
        {
            Header = "New Tab",
            IconSource = new SymbolIconSource { Symbol = Symbol.World },
            Content = webView
        };

        _webViews[tab] = webView;
        BrowserTabs.TabItems.Add(tab);
        BrowserTabs.SelectedItem = tab;

        try
        {
            await webView.EnsureCoreWebView2Async(_webEnvironment);
            if (_closing || !_webViews.ContainsKey(tab)) throw new OperationCanceledException();
            await ConfigureWebViewAsync(tab, webView);
            webView.CoreWebView2.Navigate(NormalizeAddress(url ?? HomeUrl));
            return webView;
        }
        catch
        {
            _webViews.Remove(tab);
            BrowserTabs.TabItems.Remove(tab);
            webView.Close();
            throw;
        }
    }

    private async Task ConfigureWebViewAsync(TabViewItem tab, WebView2 webView)
    {
        var core = webView.CoreWebView2;
        if (core is null)
        {
            return;
        }

        core.Settings.AreDefaultContextMenusEnabled = true;
        core.Settings.AreDevToolsEnabled = true;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = true;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;

        await core.AddScriptToExecuteOnDocumentCreatedAsync(BrowserScripts.MacroBridgeScript);

        core.NavigationStarting += (_, args) =>
        {
            _loadingTabs.Add(webView);
            if (_watchTab == webView && OriginOf(args.Uri) != _watchOrigin) StopWatch("Watch stopped: site changed.");
            if (ReferenceEquals(BrowserTabs.SelectedItem, tab))
            {
                AddressBox.Text = args.Uri ?? string.Empty;
                NavigationProgress.Visibility = Visibility.Visible;
            }
        };

        core.NavigationCompleted += async (_, args) =>
        {
            _loadingTabs.Remove(webView);
            tab.Header = string.IsNullOrWhiteSpace(core.DocumentTitle) ? "New Tab" : core.DocumentTitle;
            if (ReferenceEquals(BrowserTabs.SelectedItem, tab))
            {
                AddressBox.Text = core.Source;
                UpdateNavigationButtons(webView);
                NavigationProgress.Visibility = Visibility.Collapsed;
                if (!args.IsSuccess) SetStatus($"Navigation failed: {args.WebErrorStatus}");
            }

            if (_recordingTab == webView) _macroService.CaptureNavigation(core.Source);
            RefreshMacroUi();
            try { await _auditLog.WriteAsync("browser", "navigate", OriginOf(core.Source)); }
            catch { SetStatus("Page loaded; local audit log could not be written."); }
        };

        core.DocumentTitleChanged += (_, _) =>
        {
            tab.Header = string.IsNullOrWhiteSpace(core.DocumentTitle) ? "New Tab" : core.DocumentTitle;
        };

        core.HistoryChanged += (_, _) =>
        {
            if (ReferenceEquals(BrowserTabs.SelectedItem, tab))
            {
                UpdateNavigationButtons(webView);
            }
        };

        core.WebMessageReceived += (_, args) =>
        {
            if (_recordingTab == webView && CanExecute(webView) && args.Source == core.Source &&
                _macroService.TryCaptureWebMessage(args.WebMessageAsJson))
            {
                RefreshMacroUi();
            }
        };
        core.NewWindowRequested += async (_, args) =>
        {
            args.Handled = true;
            if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
                await RunUiAsync(async () => { await CreateTabAsync(args.Uri); });
        };
    }

    private bool CanExecute(WebView2 view) => !_closing && _windowActive &&
        GetForegroundWindow() == WinRT.Interop.WindowNative.GetWindowHandle(this) && ActiveWebView == view &&
        _webViews.Values.Contains(view) && !_loadingTabs.Contains(view);

    private static string? OriginOf(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Scheme is "https" or "http" ? uri.GetLeftPart(UriPartial.Authority) : null;

    private static string NormalizeAddress(string input)
    {
        var value = (input ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return HomeUrl;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute) &&
            absolute.Scheme is "http" or "https")
        {
            return absolute.ToString();
        }

        if (!value.Contains(' ') && value.Contains('.'))
        {
            return "https://" + value;
        }

        return "https://www.google.com/search?q=" + Uri.EscapeDataString(value);
    }

    private void NavigateFromAddressBar()
    {
        var webView = ActiveWebView;
        if (webView?.CoreWebView2 is null)
        {
            return;
        }

        webView.CoreWebView2.Navigate(NormalizeAddress(AddressBox.Text));
    }

    private void UpdateNavigationButtons(WebView2 webView)
    {
        BackButton.IsEnabled = webView.CoreWebView2?.CanGoBack ?? false;
        ForwardButton.IsEnabled = webView.CoreWebView2?.CanGoForward ?? false;
    }

    private async Task CaptureSnapshotAsync()
    {
        var webView = ActiveWebView;
        if (webView is null)
        {
            return;
        }

        try
        {
            var items = await _snapshotService.CaptureAsync(webView);
            var preview = string.Join(" · ", items.Take(5).Select(item => $"{item.Ref} {item.Text}"));
            SetStatus(items.Count == 0
                ? "Snapshot: no visible interactive elements."
                : $"Snapshot: {items.Count} interactive elements. {preview}");
            await _auditLog.WriteAsync("agent", "snapshot", OriginOf(webView.CoreWebView2?.Source), $"items={items.Count}");
        }
        catch (Exception ex)
        {
            SetStatus($"Snapshot failed: {ex.Message}");
        }
    }

    private void StartRecording()
    {
        if (_automationCancellation is not null || _macroService.IsReplaying) { SetStatus("Stop automation before recording; a dispatched effect may still be finishing."); return; }
        _recordingTab = ActiveWebView;
        _macroService.Start(clearExisting: true);
        if (ActiveWebView?.CoreWebView2?.Source is { Length: > 0 } source)
        {
            _macroService.CaptureNavigation(source);
        }

        RecordMacroButton.Content = "■ Stop";
        SetStatus("Macro recording started. Password/card/OTP values are never recorded.");
        RefreshMacroUi();
    }

    private void StopRecording()
    {
        _macroService.Stop();
        _recordingTab = null;
        RecordMacroButton.Content = "● Record";
        SetStatus($"Macro recording stopped · {_macroService.Steps.Count} steps.");
        RefreshMacroUi();
    }

    private async Task ReplayMacroAsync()
    {
        if (_automationCancellation is not null || _macroService.IsReplaying) { SetStatus("A macro or its dispatched effect is still running."); return; }
        var webView = ActiveWebView;
        if (webView is null || _macroService.Steps.Count == 0)
        {
            SetStatus("There is no recorded macro to replay.");
            return;
        }

        try
        {
            StopRecording();
            StopWatch("Starting macro replay.");
            _automationCancellation = new CancellationTokenSource();
            ReplayMacroButton.IsEnabled = false;
            SetStatus("Replaying semantic macro…");
            await ReplayOnTabAsync(webView, _automationCancellation.Token);
            SetStatus("Macro replay finished.");
            await _auditLog.WriteAsync("macro", "replay", OriginOf(webView.CoreWebView2?.Source), $"steps={_macroService.Steps.Count}");
        }
        catch (Exception ex)
        {
            SetStatus($"Macro replay stopped: {ex.Message}");
        }
        finally
        {
            _automationCancellation?.Dispose();
            _automationCancellation = null;
            ReplayMacroButton.IsEnabled = true;
        }
    }

    private void StartWatch(string text)
    {
        if (_automationCancellation is not null || _macroService.IsReplaying) { StopWatch("Wait for macro replay and its dispatched effects before starting a watch."); return; }
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus("Enter visible button/link text to watch for first.");
            WatchToggleButton.IsChecked = false;
            return;
        }

        WatchTextBox.Text = text.Trim();
        _watchTab = ActiveWebView;
        _watchOrigin = OriginOf(_watchTab?.CoreWebView2?.Source);
        if (_watchTab is null || _watchOrigin is null) { StopWatch("Open a web page before watching."); return; }
        _watchText = text.Trim();
        _watchVersion++;
        _watchAutoClick = AutoClickCheckBox.IsChecked == true;
        _watchReplay = ReplayAfterWatchCheckBox.IsChecked == true;
        _watchStarts = DateTimeOffset.Now;
        _watchEnds = _watchStarts.AddMinutes(30);
        WatchToggleButton.IsChecked = true;
        WatchToggleButton.Content = "Stop watching";
        _watchTimer.Start();
        SetStatus($"Foreground Watch active for ‘{WatchTextBox.Text}’. Luma must remain active.");
    }

    private void StopWatch(string message = "Foreground Watch stopped.")
    {
        _watchTimer.Stop();
        WatchToggleButton.IsChecked = false;
        WatchToggleButton.Content = "Start watching";
        _watchTab = null;
        _watchVersion++;
        SetStatus(message);
    }

    private async void WatchTimer_Tick(object? sender, object e)
    {
        if (_watchTickRunning || !_windowActive || WatchToggleButton.IsChecked != true)
        {
            return;
        }

        if (DateTimeOffset.Now >= _watchEnds) { StopWatch("Watch expired."); return; }
        if (DateTimeOffset.Now < _watchStarts) { SetStatus($"Watch scheduled for {_watchStarts:g}; keep Luma open."); return; }
        var webView = _watchTab;
        if (webView is null || !CanExecute(webView))
        {
            return;
        }

        _watchTickRunning = true;
        var watchVersion = _watchVersion;
        var watchText = _watchText;
        try
        {
            var result = await _watchService.CheckAsync(
                webView,
                watchText,
                false);

            if (!result.Found)
            {
                SetStatus($"Watching ‘{_watchText}’ · waiting for one enabled exact match.");
                return;
            }

            if (watchVersion != _watchVersion || !CanExecute(webView) || _watchTab != webView || OriginOf(webView.CoreWebView2?.Source) != _watchOrigin) return;
            if (_watchAutoClick && await ConfirmAsync("Seat found", $"Click ‘{result.MatchedText}’ on {_watchOrigin}? This may reserve a seat.", "Click once"))
            {
                if (watchVersion != _watchVersion || !CanExecute(webView) || _watchTab != webView || DateTimeOffset.Now >= _watchEnds) return;
                result = await ClickWatchedSeatAsync(webView, watchText, result, watchVersion);
            }
            if (watchVersion != _watchVersion) return;
            var replayAfter = _watchReplay;
            await _auditLog.WriteAsync(
                "watch",
                result.Clicked ? "found-and-clicked" : "found",
                _watchOrigin);

            StopWatch(result.Clicked
                ? $"Found and clicked ‘{result.MatchedText}’."
                : $"Found ‘{result.MatchedText}’. No click performed. {result.Error}");
            if (result.Clicked && replayAfter && CanExecute(webView)) await ReplayMacroAsync();
        }
        catch (Exception ex)
        {
            if (watchVersion == _watchVersion) StopWatch($"Watch stopped after an error: {ex.Message}");
        }
        finally
        {
            _watchTickRunning = false;
        }
    }

    private async Task HandleCommandAsync()
    {
        var command = CommandBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        CommandBox.Text = string.Empty;
        var intent = _intentRouter.Parse(command);

        switch (intent.Kind)
        {
            case "snapshot":
                await CaptureSnapshotAsync();
                break;
            case "record":
                StartRecording();
                break;
            case "stop-recording":
                StopRecording();
                break;
            case "replay":
                await ReplayMacroAsync();
                break;
            case "watch":
                StartWatch(intent.Argument ?? string.Empty);
                break;
            default:
                SetStatus("Use local snapshot/record/replay/watch commands, or select Gemini / Claude for a visible task handoff. No model runs in the background.");
                break;
        }
    }

    private void RefreshMacroUi()
    {
        MacroSummaryText.Text = _macroService.IsRecording
            ? $"Recording · {_macroService.Steps.Count} steps"
            : $"{_macroService.Steps.Count} recorded steps";

        MacroStepsList.ItemsSource = _macroService.Steps.Select((step, index) => $"{index + 1}. {step}").ToList();
    }

    private void SetStatus(string message)
    {
        AgentStatusText.Text = message;
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        _windowActive = args.WindowActivationState != WindowActivationState.Deactivated;
        ForegroundStatusText.Text = _windowActive ? "Active · free-first" : "Paused · window inactive";
        if (!_windowActive && WatchToggleButton.IsChecked == true)
        {
            SetStatus("Foreground Watch paused because Luma is not the active window.");
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveWebView?.CoreWebView2?.CanGoBack == true)
        {
            ActiveWebView.CoreWebView2.GoBack();
        }
    }

    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveWebView?.CoreWebView2?.CanGoForward == true)
        {
            ActiveWebView.CoreWebView2.GoForward();
        }
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs e) => ActiveWebView?.CoreWebView2?.Reload();
    private void HomeButton_Click(object sender, RoutedEventArgs e) => ActiveWebView?.CoreWebView2?.Navigate(HomeUrl);
    private void GoButton_Click(object sender, RoutedEventArgs e) => NavigateFromAddressBar();
    private async void SnapshotButton_Click(object sender, RoutedEventArgs e) => await CaptureSnapshotAsync();

    private void AddressBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            NavigateFromAddressBar();
        }
    }

    private async void BrowserTabs_AddTabButtonClick(TabView sender, object args) => await RunUiAsync(async () => { await CreateTabAsync(HomeUrl); });

    private async void BrowserTabs_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Tab is not TabViewItem tab)
        {
            return;
        }

        if (_webViews.Remove(tab, out var webView))
        {
            if (_watchTab == webView) StopWatch("Watch stopped: target tab closed.");
            if (_recordingTab == webView) StopRecording();
            _automationCancellation?.Cancel();
            _loadingTabs.Remove(webView);
            webView.Close();
        }

        sender.TabItems.Remove(tab);
        if (sender.TabItems.Count == 0)
        {
            await RunUiAsync(async () => { await CreateTabAsync(HomeUrl); });
        }
    }

    private void BrowserTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ActiveWebView?.CoreWebView2 is { } core)
        {
            AddressBox.Text = core.Source;
            UpdateNavigationButtons(ActiveWebView);
            NavigationProgress.Visibility = _loadingTabs.Contains(ActiveWebView!) ? Visibility.Visible : Visibility.Collapsed;
        }
        if (_watchTab is not null && ActiveWebView != _watchTab) SetStatus("Watch paused: select its original tab to resume.");
        _ = UpdateTabResourcesAsync();
    }

    private void AgentToggleButton_Click(object sender, RoutedEventArgs e)
    {
        var visible = AgentPanel.Visibility == Visibility.Visible;
        AgentPanel.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        AgentColumn.Width = visible ? new GridLength(0) : new GridLength(360);
    }

    private void RecordMacroButton_Click(object sender, RoutedEventArgs e)
    {
        if (_macroService.IsRecording)
        {
            StopRecording();
        }
        else
        {
            StartRecording();
        }
    }

    private async void ReplayMacroButton_Click(object sender, RoutedEventArgs e) => await ReplayMacroAsync();

    private void WatchToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (WatchToggleButton.IsChecked == true)
        {
            StartWatch(WatchTextBox.Text);
        }
        else
        {
            StopWatch();
        }
    }

    private async void SendCommandButton_Click(object sender, RoutedEventArgs e) => await HandleCommandAsync();

    private async void CommandBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            await HandleCommandAsync();
        }
    }

    private async Task RunUiAsync(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { SetStatus("Stopped."); }
        catch (Exception ex) { SetStatus(ex.Message); }
    }

    private async Task<bool> ConfirmAsync(string title, string message, string primary, CancellationToken token = default)
    {
        if (_dialogOpen || _closing) return false;
        _dialogOpen = true;
        try
        {
            var dialog = new ContentDialog { XamlRoot = RootGrid.XamlRoot, Title = title,
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                PrimaryButtonText = primary, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
            using var registration = token.Register(() => DispatcherQueue.TryEnqueue(() => dialog.Hide()));
            token.ThrowIfCancellationRequested();
            var result = await dialog.ShowAsync();
            token.ThrowIfCancellationRequested();
            return result == ContentDialogResult.Primary;
        }
        finally { _dialogOpen = false; }
    }

    private async Task UpdateTabResourcesAsync()
    {
        foreach (var view in _webViews.Values.ToArray())
        {
            try
            {
                if (view.CoreWebView2 is not { } core) continue;
                var active = view == ActiveWebView;
                core.MemoryUsageTargetLevel = active ? CoreWebView2MemoryUsageTargetLevel.Normal : CoreWebView2MemoryUsageTargetLevel.Low;
                if (active) core.Resume();
                else if (!_loadingTabs.Contains(view))
                {
                    await core.TrySuspendAsync();
                    if (view == ActiveWebView) core.Resume();
                }
            }
            catch (Exception) { /* A tab can close while suspension is pending. */ }
        }
    }

    private async Task<WebView2> OpenDelegateTabAsync(string url, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return await CreateTabAsync(url);
    }

    private async Task StartDelegateAsync(string provider)
    {
        if (_dialogOpen) return;
        var taskBox = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 140,
            MaxLength = 8000, PlaceholderText = "Describe a bounded task. Do not paste credentials or personal data." };
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = "Only this task is shared. Review the prepared prompt next; no page or vault data is attached.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(taskBox);
        var dialog = new ContentDialog { XamlRoot = RootGrid.XamlRoot, Title = $"Visible {provider} delegate",
            Content = panel, PrimaryButtonText = "Prepare", CloseButtonText = "Cancel" };
        _dialogOpen = true;
        ContentDialogResult result;
        try { result = await dialog.ShowAsync(); } finally { _dialogOpen = false; }
        if (result != ContentDialogResult.Primary) return;
        var prompt = DelegatePrivacy.PreparePrompt(taskBox.Text);
        if (!await ConfirmAsync($"Review task for {provider}", prompt, "Open visible tab")) return;
        StopRecording();
        _lastDelegate = _delegates[provider];
        await _lastDelegate.DelegateVisibleAsync(taskBox.Text, null);
        // Clipboard is populated only after a second explicit user choice.
        if (await ConfirmAsync("Visible handoff", "Sign in manually if needed. Copy the reviewed task, then paste and send it in this tab. Read selected reply imports only the text you select.", "Copy task"))
        {
            var data = new Windows.ApplicationModel.DataTransfer.DataPackage();
            data.SetText(prompt);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
            Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
            SetStatus($"{provider} task copied. Paste and send manually in the visible tab.");
        }
        await _auditLog.WriteAsync("delegate", "opened", provider);
    }

    private async void DelegateGeminiButton_Click(object sender, RoutedEventArgs e) => await RunUiAsync(() => StartDelegateAsync("Gemini"));
    private async void DelegateClaudeButton_Click(object sender, RoutedEventArgs e) => await RunUiAsync(() => StartDelegateAsync("Claude"));

    private async void ReadDelegateButton_Click(object sender, RoutedEventArgs e) => await RunUiAsync(async () =>
    {
        if (_lastDelegate is null) { SetStatus("Open a delegate first."); return; }
        var reply = await _lastDelegate.ReadSelectedReplyAsync();
        if (string.IsNullOrWhiteSpace(reply)) { SetStatus("Select the reply text in the visible delegate tab first."); return; }
        await ConfirmAsync("Delegate reply · untrusted text", reply, "Done");
        SetStatus("Reply reviewed. External text never runs tools or modifies a macro automatically.");
    });

    private void CancelAutomationButton_Click(object sender, RoutedEventArgs e)
    {
        _automationCancellation?.Cancel();
        StopRecording();
        StopWatch("Automation stopped.");
    }

    private async Task ReplayOnTabAsync(WebView2 view, CancellationToken token)
    {
        _macroService.ExecutionGateAsync = (target, ct) =>
        { ct.ThrowIfCancellationRequested(); return Task.FromResult(CanExecute(target)); };
        _macroService.ApproveClickAsync = (step, ct) => ConfirmAsync("Macro click", $"Click {step.Selector} on {OriginOf(view.CoreWebView2?.Source)}?", "Click once", ct);
        await _macroService.ReplayAsync(view, token);
    }

    private Task<WatchResult> ClickWatchedSeatAsync(WebView2 view, string text, WatchResult approved, int version) => _watchService.ClickAsync(view, text,
        ct => Task.FromResult(version == _watchVersion && CanExecute(view) && _watchTab == view && DateTimeOffset.Now < _watchEnds &&
            OriginOf(view.CoreWebView2?.Source) == _watchOrigin), approvedSelector: approved.Selector,
            approvedSource: approved.Source, approvedDocumentId: approved.DocumentId);

    private async void ImportMacroButton_Click(object sender, RoutedEventArgs e) => await RunUiAsync(async () =>
    {
        if (_dialogOpen || _automationCancellation is not null) return;
        var box = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 131072,
            MinHeight = 220, PlaceholderText = "Paste a JSON array of steps: navigate, click, input, wait, retry, if, loop." };
        var dialog = new ContentDialog { XamlRoot = RootGrid.XamlRoot, Title = "Import semantic macro",
            Content = box, PrimaryButtonText = "Import", CloseButtonText = "Cancel" };
        _dialogOpen = true;
        ContentDialogResult choice;
        try { choice = await dialog.ShowAsync(); } finally { _dialogOpen = false; }
        if (choice != ContentDialogResult.Primary) return;
        StopRecording();
        _macroService.ImportJson(box.Text);
        RefreshMacroUi();
        SetStatus("Macro imported and validated. Replay stays on the selected foreground tab.");
    });

    private async void ExportMacroButton_Click(object sender, RoutedEventArgs e) => await RunUiAsync(async () =>
    {
        if (_dialogOpen) return;
        var box = new TextBox { Text = _macroService.ExportJson(), IsReadOnly = true, AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap, MinHeight = 220 };
        var dialog = new ContentDialog { XamlRoot = RootGrid.XamlRoot, Title = "Export semantic macro",
            Content = box, PrimaryButtonText = "Save locally", CloseButtonText = "Close" };
        _dialogOpen = true;
        ContentDialogResult result;
        try { result = await dialog.ShowAsync(); } finally { _dialogOpen = false; }
        if (result == ContentDialogResult.Primary)
        {
            await _macroService.SaveAsync(_dataRoot, "macro.json");
            SetStatus("Macro saved to %LOCALAPPDATA%\\Luma\\macro.json. It may contain ordinary input data; keep it private.");
        }
    });

    private void WatchScheduleButton_Click(object sender, RoutedEventArgs e)
    {
        if (!WatchSchedule.TryParse(WatchStartBox.Text.Trim(), WatchEndBox.Text.Trim(), DateTimeOffset.Now, out var start, out var end))
        { SetStatus("Enter valid start/end timestamps, including timezone. Maximum watch window: 24 hours."); return; }
        StartWatch(WatchTextBox.Text);
        if (_watchTab is null) return;
        _watchStarts = start; _watchEnds = end;
        SetStatus($"Watch scheduled: {start:g} → {end:g}. Active original tab only; no background wakeup.");
    }

    private void FocusAddress_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    { AddressBox.Focus(FocusState.Keyboard); AddressBox.SelectAll(); args.Handled = true; }
    private async void NewTab_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    { args.Handled = true; await RunUiAsync(async () => { await CreateTabAsync(HomeUrl); }); }
    private async void CloseTab_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (BrowserTabs.SelectedItem is not TabViewItem tab) return;
        if (_webViews.Remove(tab, out var view))
        {
            if (_watchTab == view) StopWatch();
            if (_recordingTab == view) StopRecording();
            _automationCancellation?.Cancel(); _loadingTabs.Remove(view); view.Close();
        }
        BrowserTabs.TabItems.Remove(tab);
        if (BrowserTabs.TabItems.Count == 0) await RunUiAsync(async () => { await CreateTabAsync(HomeUrl); });
    }
    private void Reload_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    { ActiveWebView?.CoreWebView2?.Reload(); args.Handled = true; }
}

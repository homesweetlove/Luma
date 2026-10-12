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
        Closed += (_, _) => _watchTimer.Stop();

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            await _auditLog.InitializeAsync();

            var profilePath = Path.Combine(_dataRoot, "WebView2Profile");
            Directory.CreateDirectory(profilePath);
            _webEnvironment = await CoreWebView2Environment.CreateAsync(null, profilePath);

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

    private async Task CreateTabAsync(string? url = null)
    {
        if (_webEnvironment is null)
        {
            return;
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

        await webView.EnsureCoreWebView2Async(_webEnvironment);
        ConfigureWebView(tab, webView);
        webView.CoreWebView2.Navigate(NormalizeAddress(url ?? HomeUrl));
    }

    private void ConfigureWebView(TabViewItem tab, WebView2 webView)
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

        core.AddScriptToExecuteOnDocumentCreatedAsync(BrowserScripts.MacroBridgeScript);

        core.NavigationStarting += (_, args) =>
        {
            if (BrowserTabs.SelectedItem == tab)
            {
                AddressBox.Text = args.Uri ?? string.Empty;
            }
        };

        core.NavigationCompleted += async (_, _) =>
        {
            tab.Header = string.IsNullOrWhiteSpace(core.DocumentTitle) ? "New Tab" : core.DocumentTitle;
            if (BrowserTabs.SelectedItem == tab)
            {
                AddressBox.Text = core.Source;
                UpdateNavigationButtons(webView);
            }

            _macroService.CaptureNavigation(core.Source);
            RefreshMacroUi();
            await _auditLog.WriteAsync("browser", "navigate", core.Source);
        };

        core.DocumentTitleChanged += (_, _) =>
        {
            tab.Header = string.IsNullOrWhiteSpace(core.DocumentTitle) ? "New Tab" : core.DocumentTitle;
        };

        core.HistoryChanged += (_, _) =>
        {
            if (BrowserTabs.SelectedItem == tab)
            {
                UpdateNavigationButtons(webView);
            }
        };

        core.WebMessageReceived += (_, args) =>
        {
            if (_macroService.TryCaptureWebMessage(args.WebMessageAsJson))
            {
                RefreshMacroUi();
            }
        };
    }

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
            await _auditLog.WriteAsync("agent", "snapshot", webView.CoreWebView2?.Source, $"items={items.Count}");
        }
        catch (Exception ex)
        {
            SetStatus($"Snapshot failed: {ex.Message}");
        }
    }

    private void StartRecording()
    {
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
        RecordMacroButton.Content = "● Record";
        SetStatus($"Macro recording stopped · {_macroService.Steps.Count} steps.");
        RefreshMacroUi();
    }

    private async Task ReplayMacroAsync()
    {
        var webView = ActiveWebView;
        if (webView is null || _macroService.Steps.Count == 0)
        {
            SetStatus("There is no recorded macro to replay.");
            return;
        }

        try
        {
            SetStatus("Replaying semantic macro…");
            await _macroService.ReplayAsync(webView);
            SetStatus("Macro replay finished.");
            await _auditLog.WriteAsync("macro", "replay", webView.CoreWebView2?.Source, $"steps={_macroService.Steps.Count}");
        }
        catch (Exception ex)
        {
            SetStatus($"Macro replay stopped: {ex.Message}. This is where a future self-healing AI hook can take over.");
        }
    }

    private void StartWatch(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus("Enter visible button/link text to watch for first.");
            WatchToggleButton.IsChecked = false;
            return;
        }

        WatchTextBox.Text = text.Trim();
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
        SetStatus(message);
    }

    private async void WatchTimer_Tick(object? sender, object e)
    {
        if (_watchTickRunning || !_windowActive || WatchToggleButton.IsChecked != true)
        {
            return;
        }

        var webView = ActiveWebView;
        if (webView is null || webView.Visibility != Visibility.Visible)
        {
            return;
        }

        _watchTickRunning = true;
        try
        {
            var result = await _watchService.CheckAsync(
                webView,
                WatchTextBox.Text,
                AutoClickCheckBox.IsChecked == true);

            if (!result.Found)
            {
                SetStatus($"Watching ‘{WatchTextBox.Text}’ · not available yet.");
                return;
            }

            await _auditLog.WriteAsync(
                "watch",
                result.Clicked ? "found-and-clicked" : "found",
                webView.CoreWebView2?.Source,
                result.MatchedText);

            StopWatch(result.Clicked
                ? $"Found and clicked ‘{result.MatchedText}’."
                : $"Found ‘{result.MatchedText}’. Auto-click is off.");
        }
        catch (Exception ex)
        {
            StopWatch($"Watch stopped after an error: {ex.Message}");
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
                SetStatus("No paid provider is configured yet. Deterministic browser tools are ready; provider/delegate adapters are the next layer.");
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

    private async void BrowserTabs_AddTabButtonClick(TabView sender, object args) => await CreateTabAsync(HomeUrl);

    private async void BrowserTabs_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Tab is not TabViewItem tab)
        {
            return;
        }

        if (_webViews.Remove(tab, out var webView))
        {
            webView.Close();
        }

        sender.TabItems.Remove(tab);
        if (sender.TabItems.Count == 0)
        {
            await CreateTabAsync(HomeUrl);
        }
    }

    private void BrowserTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ActiveWebView?.CoreWebView2 is { } core)
        {
            AddressBox.Text = core.Source;
            UpdateNavigationButtons(ActiveWebView);
        }
    }

    private void AgentToggleButton_Click(object sender, RoutedEventArgs e)
    {
        var visible = AgentPanel.Visibility == Visibility.Visible;
        AgentPanel.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        AgentColumn.Width = visible ? new GridLength(0) : new GridLength(340);
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
}

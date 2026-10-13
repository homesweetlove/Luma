using System.Text.Json;
using Luma.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Web.WebView2.Core;
using Windows.System;

namespace Luma;

public sealed partial class MainWindow
{
    private HomeDashboardService? _homeDashboard;
    private readonly HashSet<WebView2> _homePendingViews = [];
    private readonly HashSet<CoreWebView2> _homeConfiguredCores = [];
    private bool _glassHomeInitialized;
    private bool _initialHomeApplied;

    internal async Task InitializeGlassHomeAsync()
    {
        if (_glassHomeInitialized) return;
        _glassHomeInitialized = true;

        _homeDashboard = new HomeDashboardService(_dataRoot);
        await _homeDashboard.InitializeAsync();

        RewireHomeCommands();
        BrowserTabs.SelectionChanged += GlassHomeTabs_SelectionChanged;

        foreach (var view in _webViews.Values.ToArray()) AttachHomeBehavior(view);
        await OpenInitialHomeAsync();
    }

    private void RewireHomeCommands()
    {
        HomeButton.Click -= HomeButton_Click;
        HomeButton.Click += GlassHomeButton_Click;

        BrowserTabs.AddTabButtonClick -= BrowserTabs_AddTabButtonClick;
        BrowserTabs.AddTabButtonClick += GlassBrowserTabs_AddTabButtonClick;
        BrowserTabs.TabCloseRequested -= BrowserTabs_TabCloseRequested;
        BrowserTabs.TabCloseRequested += GlassBrowserTabs_TabCloseRequested;

        foreach (var accelerator in RootGrid.KeyboardAccelerators)
        {
            if (accelerator.Modifiers != VirtualKeyModifiers.Control) continue;
            if (accelerator.Key == VirtualKey.T)
            {
                accelerator.Invoked -= NewTab_Invoked;
                accelerator.Invoked += GlassNewTab_Invoked;
            }
            else if (accelerator.Key == VirtualKey.W)
            {
                accelerator.Invoked -= CloseTab_Invoked;
                accelerator.Invoked += GlassCloseTab_Invoked;
            }
        }
    }

    private async Task OpenInitialHomeAsync()
    {
        for (var attempt = 0; attempt < 60 && !_closing; attempt++)
        {
            var view = ActiveWebView;
            if (view?.CoreWebView2 is { } core)
            {
                ConfigureHomeCore(view, core);
                if (!_initialHomeApplied && IsDefaultGoogleHome(core.Source))
                {
                    _initialHomeApplied = true;
                    core.Navigate(HomeDashboardService.HomeUrl);
                }
                return;
            }
            await Task.Delay(100);
        }
    }

    private static bool IsDefaultGoogleHome(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return true;
        return Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
               uri.Scheme == "https" &&
               string.Equals(uri.Host, "www.google.com", StringComparison.OrdinalIgnoreCase) &&
               (string.IsNullOrEmpty(uri.AbsolutePath) || uri.AbsolutePath == "/") &&
               string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
    }

    private void GlassHomeTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ActiveWebView is { } view) AttachHomeBehavior(view);
    }

    private void AttachHomeBehavior(WebView2 view)
    {
        if (_homeDashboard is null) return;
        if (view.CoreWebView2 is { } core)
        {
            ConfigureHomeCore(view, core);
            return;
        }

        if (!_homePendingViews.Add(view)) return;
        view.CoreWebView2InitializationCompleted += (_, args) =>
        {
            _homePendingViews.Remove(view);
            if (args.IsSuccess && view.CoreWebView2 is { } initializedCore)
                ConfigureHomeCore(view, initializedCore);
        };
    }

    private void ConfigureHomeCore(WebView2 view, CoreWebView2 core)
    {
        if (_homeDashboard is null || !_homeConfiguredCores.Add(core)) return;

        _homeDashboard.Configure(core);
        core.WebMessageReceived += async (_, args) => await HandleHomeMessageAsync(view, core, args);
        core.NavigationCompleted += async (_, _) =>
        {
            if (_homeDashboard.IsHome(core.Source))
            {
                await HydrateHomeAsync(view);
                if (ReferenceEquals(view, ActiveWebView))
                    DispatcherQueue.TryEnqueue(() => AddressBox.Text = string.Empty);
            }
        };
    }

    private async Task HandleHomeMessageAsync(WebView2 view, CoreWebView2 core, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (_homeDashboard is null || !_homeDashboard.IsHome(core.Source) ||
            !string.Equals(OriginOf(args.Source), $"https://{HomeDashboardService.HostName}", StringComparison.OrdinalIgnoreCase))
            return;

        string? action;
        try
        {
            using var document = JsonDocument.Parse(args.WebMessageAsJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("kind", out var kind) || kind.GetString() != "luma-home" ||
                !root.TryGetProperty("action", out var actionProperty) || actionProperty.ValueKind != JsonValueKind.String)
                return;
            action = actionProperty.GetString();
        }
        catch (JsonException) { return; }

        switch (action)
        {
            case "hydrate":
                await HydrateHomeAsync(view);
                break;
            case "new-search":
                await RunUiAsync(async () =>
                {
                    await CreateTabAsync("https://www.google.com");
                    AddressBox.Focus(FocusState.Keyboard);
                    AddressBox.SelectAll();
                });
                break;
            case "record":
                await RunUiAsync(async () =>
                {
                    await CreateTabAsync("https://www.google.com");
                    StartRecording();
                    AddressBox.Focus(FocusState.Keyboard);
                    AddressBox.SelectAll();
                });
                break;
            case "watch":
                EnsureAgentPanelVisible();
                WatchTextBox.Focus(FocusState.Keyboard);
                break;
            case "vault":
                EnsureAgentPanelVisible();
                await RunUiAsync(ManageVaultAsync);
                break;
            case "ai":
                EnsureAgentPanelVisible();
                CommandBox.Focus(FocusState.Keyboard);
                break;
            case "stop":
                _automationCancellation?.Cancel();
                StopRecording();
                StopWatch("Automation stopped from Home.");
                await HydrateHomeAsync(view);
                break;
        }
    }

    private void EnsureAgentPanelVisible()
    {
        AgentPanel.Visibility = Visibility.Visible;
        AgentColumn.Width = new GridLength(360);
    }

    private async Task HydrateHomeAsync(WebView2 view)
    {
        if (_homeDashboard is null || view.CoreWebView2 is not { } core || !_homeDashboard.IsHome(core.Source)) return;

        try
        {
            var origins = await _auditLog.GetRecentNavigationTargetsAsync(6);
            var recent = origins.Select(origin => new
            {
                title = Uri.TryCreate(origin, UriKind.Absolute, out var uri) ? uri.Host.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase) : origin,
                url = origin
            }).ToArray();

            var payload = JsonSerializer.Serialize(new
            {
                recent,
                state = new
                {
                    recording = _macroService.IsRecording,
                    replaying = _macroService.IsReplaying,
                    watching = WatchToggleButton.IsChecked == true,
                    macroSteps = _macroService.Steps.Count,
                    message = AgentStatusText.Text
                }
            });
            await core.ExecuteScriptAsync($"window.lumaHydrate && window.lumaHydrate({payload});");
        }
        catch
        {
            // Home is cosmetic. A dashboard refresh must never break browsing.
        }
    }

    private void GlassHomeButton_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveWebView is not { } view || view.CoreWebView2 is not { } core) return;
        AttachHomeBehavior(view);
        core.Navigate(HomeDashboardService.HomeUrl);
    }

    private async void GlassBrowserTabs_AddTabButtonClick(TabView sender, object args) =>
        await RunUiAsync(async () => { await CreateTabAsync(HomeDashboardService.HomeUrl); });

    private async void GlassBrowserTabs_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Tab is not TabViewItem tab) return;

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
            await RunUiAsync(async () => { await CreateTabAsync(HomeDashboardService.HomeUrl); });
    }

    private async void GlassNewTab_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await RunUiAsync(async () => { await CreateTabAsync(HomeDashboardService.HomeUrl); });
    }

    private async void GlassCloseTab_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (BrowserTabs.SelectedItem is not TabViewItem tab) return;

        if (_webViews.Remove(tab, out var view))
        {
            if (_watchTab == view) StopWatch();
            if (_recordingTab == view) StopRecording();
            _automationCancellation?.Cancel();
            _loadingTabs.Remove(view);
            view.Close();
        }

        BrowserTabs.TabItems.Remove(tab);
        if (BrowserTabs.TabItems.Count == 0)
            await RunUiAsync(async () => { await CreateTabAsync(HomeDashboardService.HomeUrl); });
    }
}

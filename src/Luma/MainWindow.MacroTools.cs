using Luma.Models;
using Luma.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Luma;

public sealed partial class MainWindow
{
    private MacroEditorSession? _macroEditor;
    private MacroEditorSession.Entry? _editorSelection;
    private bool _editorRefreshing;

    private async void MacroLibraryButton_Click(object sender, RoutedEventArgs e) =>
        await RunUiAsync(ShowMacroLibraryAsync);

    private async Task ShowMacroLibraryAsync()
    {
        if (_dialogOpen || _automationCancellation is not null || _macroService.IsReplaying) return;
        if (_macroService.IsRecording) StopRecording();
        var presets = await _macroLibrary.ListAsync();
        var list = new ListView { ItemsSource = presets, MaxHeight = 220, MinHeight = 80,
            SelectionMode = ListViewSelectionMode.Single };
        if (presets.Count > 0) list.SelectedIndex = 0;
        var name = new TextBox { Header = "Name for saving the current macro", MaxLength = 60,
            PlaceholderText = "Example: Research articles or 코레일 조회" };
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock { Text = "Named macros remain only on this PC. Plaintext input values and unsafe URLs cannot be saved.",
            TextWrapping = TextWrapping.Wrap });
        content.Children.Add(list);
        content.Children.Add(name);
        var dialog = new ContentDialog { XamlRoot = RootGrid.XamlRoot, Title = "Local macro library",
            Content = content, PrimaryButtonText = "Load selected", SecondaryButtonText = "Save current",
            CloseButtonText = "Close", DefaultButton = ContentDialogButton.Close };
        _dialogOpen = true;
        ContentDialogResult result;
        try { result = await dialog.ShowAsync(); }
        finally { _dialogOpen = false; }

        if (result == ContentDialogResult.Primary)
        {
            if (list.SelectedItem is not SavedMacro selected)
                throw new InvalidOperationException("Choose a saved macro to load.");
            await _macroLibrary.LoadAsync(selected.Name, _macroService);
            DiscardMacroEditor();
            RefreshMacroUi();
            SetStatus($"Loaded '{selected.Name}'. Review its steps before replaying in the foreground tab.");
        }
        else if (result == ContentDialogResult.Secondary)
        {
            await _macroLibrary.SaveAsync(name.Text, _macroService);
            SetStatus($"Saved '{name.Text.Trim()}' in %LOCALAPPDATA%\\Luma\\Macros. No sensitive input values were stored.");
        }
    }

    private void OpenMacroEditorButton_Click(object sender, RoutedEventArgs e)
    {
        if (_automationCancellation is not null || _macroService.IsReplaying)
        { SetStatus("Wait until the current macro finishes before editing."); return; }
        if (_macroService.IsRecording) StopRecording();
        _macroEditor = new MacroEditorSession(_macroService.Steps);
        _editorSelection = null;
        MacroEditorExpander.IsExpanded = true;
        RebuildMacroEditor(_macroEditor.Steps.FirstOrDefault());
        EditorMessageText.Text = "Working copy ready. Add a root step or select a step to edit.";
    }

    private static string Choice(ComboBox combo, string fallback) =>
        (combo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? fallback;

    private static void SelectChoice(ComboBox combo, string? value)
    {
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Content?.ToString(), value, StringComparison.Ordinal));
    }

    private static int NumberOr(NumberBox box, int defaultValue) =>
        double.IsFinite(box.Value) ? (int)box.Value : defaultValue;

    private void SaveEditorFields()
    {
        if (_editorRefreshing || _editorSelection is null) return;
        var step = _editorSelection.Step;
        step.Kind = Choice(EditorKindBox, "wait");
        step.Selector = string.IsNullOrWhiteSpace(EditorSelectorBox.Text) ? null : EditorSelectorBox.Text.Trim();
        step.Url = string.IsNullOrWhiteSpace(EditorUrlBox.Text) ? null : EditorUrlBox.Text.Trim();
        step.Value = step.Kind == "input" ? EditorValueBox.Password : null;
        step.Condition = Choice(EditorConditionBox, "ready");
        step.LoopCount = NumberOr(EditorLoopBox, 1);
        step.RetryCount = NumberOr(EditorRetryBox, 0);
        step.TimeoutMs = NumberOr(EditorTimeoutBox, step.Kind == "click" ? 30000 : 5000);
    }

    private void ShowEditorFields()
    {
        _editorRefreshing = true;
        try
        {
            var step = _editorSelection?.Step;
            SelectChoice(EditorKindBox, step?.Kind ?? "wait");
            EditorSelectorBox.Text = step?.Selector ?? string.Empty;
            EditorUrlBox.Text = step?.Url ?? string.Empty;
            EditorValueBox.Password = step?.Kind == "input" ? step.Value ?? string.Empty : string.Empty;
            SelectChoice(EditorConditionBox, step?.Condition ?? "ready");
            EditorLoopBox.Value = step?.LoopCount ?? 1;
            EditorRetryBox.Value = step?.RetryCount ?? 0;
            EditorTimeoutBox.Value = step?.TimeoutMs ?? 5000;
        }
        finally { _editorRefreshing = false; }
    }

    private void RebuildMacroEditor(MacroStep? focus)
    {
        if (_macroEditor is null) return;
        var items = _macroEditor.Flatten();
        _editorRefreshing = true;
        try
        {
            EditorStepsList.ItemsSource = items;
            _editorSelection = items.FirstOrDefault(item => ReferenceEquals(item.Step, focus)) ?? items.FirstOrDefault();
            EditorStepsList.SelectedItem = _editorSelection;
        }
        finally { _editorRefreshing = false; }
        ShowEditorFields();
    }

    private void EditorStepsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_editorRefreshing || _macroEditor is null) return;
        SaveEditorFields();
        _editorSelection = EditorStepsList.SelectedItem as MacroEditorSession.Entry;
        ShowEditorFields();
    }

    private void EditorUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_macroEditor is null || _editorSelection is null) return;
        SaveEditorFields();
        var selected = _editorSelection.Step;
        RebuildMacroEditor(selected);
        EditorMessageText.Text = "Step updated in the working copy. Apply macro to activate changes.";
    }

    private void EditTree(Action action, MacroStep? focus = null)
    {
        if (_macroEditor is null) return;
        try
        {
            SaveEditorFields();
            action();
            RebuildMacroEditor(focus ?? _editorSelection?.Step);
            EditorMessageText.Text = "Working copy changed. Press Apply macro to validate the whole tree.";
        }
        catch (Exception ex) { EditorMessageText.Text = ex.Message; }
    }

    private void EditorAddRoot_Click(object sender, RoutedEventArgs e)
    {
        if (_macroEditor is null) return;
        MacroStep? inserted = null;
        EditTree(() => inserted = _macroEditor.AddRoot());
        if (inserted is not null) RebuildMacroEditor(inserted);
    }

    private void EditorAddChild_Click(object sender, RoutedEventArgs e)
    {
        if (_macroEditor is null || _editorSelection is null) return;
        MacroStep? inserted = null;
        var selected = _editorSelection;
        EditTree(() => inserted = _macroEditor.AddChild(selected));
        if (inserted is not null) RebuildMacroEditor(inserted);
    }

    private void EditorAddElse_Click(object sender, RoutedEventArgs e)
    {
        if (_macroEditor is null || _editorSelection is null) return;
        MacroStep? inserted = null;
        var selected = _editorSelection;
        EditTree(() => inserted = _macroEditor.AddChild(selected, elseBranch: true));
        if (inserted is not null) RebuildMacroEditor(inserted);
    }

    private void EditorRemove_Click(object sender, RoutedEventArgs e)
    {
        if (_macroEditor is null || _editorSelection is null) return;
        var selected = _editorSelection;
        EditTree(() => _macroEditor.Delete(selected), focus: null);
    }

    private void EditorMoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (_macroEditor is null || _editorSelection is null) return;
        var selected = _editorSelection;
        EditTree(() => _macroEditor.Move(selected, -1));
    }

    private void EditorMoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (_macroEditor is null || _editorSelection is null) return;
        var selected = _editorSelection;
        EditTree(() => _macroEditor.Move(selected, 1));
    }

    private void EditorDiscard_Click(object sender, RoutedEventArgs e)
    {
        DiscardMacroEditor();
        SetStatus("Visual editor changes discarded. The active macro is unchanged.");
    }

    private void DiscardMacroEditor()
    {
        _macroEditor = null;
        _editorSelection = null;
        MacroEditorExpander.IsExpanded = false;
        EditorStepsList.ItemsSource = null;
    }

    private void EditorApply_Click(object sender, RoutedEventArgs e)
    {
        if (_macroEditor is null) return;
        if (_automationCancellation is not null || _macroService.IsReplaying || _macroService.IsRecording)
        { EditorMessageText.Text = "Stop running or recording before applying the edited macro."; return; }
        try
        {
            SaveEditorFields();
            _macroEditor.ValidateStructure();
            _macroService.ReplaceSteps(_macroEditor.Steps);
            var count = _macroService.Steps.Count;
            DiscardMacroEditor();
            RefreshMacroUi();
            SetStatus($"Validated {count} top-level steps. Replay remains a separate, foreground-only action.");
        }
        catch (Exception ex) { EditorMessageText.Text = "Cannot apply: " + ex.Message; }
    }

    private async void PlanGemini_Click(object sender, RoutedEventArgs e) =>
        await RunUiAsync(() => PrepareAiPlanAsync("Gemini"));

    private async void PlanClaude_Click(object sender, RoutedEventArgs e) =>
        await RunUiAsync(() => PrepareAiPlanAsync("Claude"));

    private async Task PrepareAiPlanAsync(string provider)
    {
        if (_dialogOpen || _automationCancellation is not null || _macroService.IsReplaying) return;
        var request = AiMacroPlanner.BuildRequest(AiPlanTaskBox.Text);
        if (!await ConfirmLongTextAsync($"Review {provider} macro request", request, "Open provider")) return;
        if (_macroService.IsRecording) StopRecording();
        _lastDelegate = _delegates[provider];
        await _lastDelegate.DelegateVisibleAsync(request, null);
        if (await ConfirmAsync("Visible AI plan handoff", "Copy the reviewed request and paste/send it yourself. Select ONLY the JSON reply, then click Review selected AI plan in Luma.", "Copy request"))
        {
            var data = new DataPackage();
            data.SetText(request);
            Clipboard.SetContent(data);
            Clipboard.Flush();
            SetStatus($"{provider} macro request copied. Send it manually; Luma will not execute the reply.");
        }
        await _auditLog.WriteAsync("delegate", "macro-plan-request", provider);
    }

    private async void ReviewAiPlan_Click(object sender, RoutedEventArgs e) =>
        await RunUiAsync(ReviewAiPlanAsync);

    private async Task ReviewAiPlanAsync()
    {
        if (_dialogOpen || _automationCancellation is not null || _macroService.IsReplaying) return;
        if (_lastDelegate is null) { SetStatus("Start a Gemini or Claude plan and select its JSON reply first."); return; }
        var selected = await _lastDelegate.ReadSelectedReplyAsync();
        if (string.IsNullOrWhiteSpace(selected)) { SetStatus("Select only the generated JSON plan on the visible AI page."); return; }
        var validated = AiMacroPlanner.ValidateReply(selected);
        if (!await ConfirmLongTextAsync("Review proposed macro · no execution", validated, "Load for editing")) return;
        if (_macroService.IsRecording) StopRecording();
        _macroService.ImportJson(validated);
        DiscardMacroEditor();
        RefreshMacroUi();
        SetStatus("AI plan validated and loaded. Open the visual editor to review selectors; Replay requires a separate action and click approvals.");
        await _auditLog.WriteAsync("delegate", "macro-plan-loaded", _lastDelegate.Name);
    }

    private async Task<bool> ConfirmLongTextAsync(string title, string text, string primary)
    {
        if (_dialogOpen || _closing) return false;
        var box = new TextBox { Text = text, IsReadOnly = true, AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap, MinHeight = 220, MaxHeight = 420 };
        var dialog = new ContentDialog { XamlRoot = RootGrid.XamlRoot, Title = title,
            Content = box, PrimaryButtonText = primary, CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close };
        _dialogOpen = true;
        try { return await dialog.ShowAsync() == ContentDialogResult.Primary; }
        finally { _dialogOpen = false; }
    }
}

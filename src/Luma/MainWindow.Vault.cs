using Luma.Models;
using Luma.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Luma;

public sealed partial class MainWindow
{
    private async void VaultButton_Click(object sender, RoutedEventArgs e) => await RunUiAsync(ManageVaultAsync);

    private async Task ManageVaultAsync()
    {
        if (_dialogOpen || _automationCancellation is not null || _macroService.IsReplaying) return;
        var view = ActiveWebView;
        var entries = await _vault.ListAsync();
        var slots = new ComboBox { Header = "Saved slots", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var entry in entries)
            slots.Items.Add(new ComboBoxItem { Content = entry switch
            {
                LoginVaultEntry => $"Login · {entry.Label} · {entry.Origin}",
                AddressVaultEntry => $"Address · {entry.Label} · {entry.Origin}",
                PaymentVaultEntry p => $"Payment · {p.Brand} •••• {p.Last4} · {entry.Origin}",
                _ => entry.Label
            }, Tag = entry });
        if (slots.Items.Count > 0) slots.SelectedIndex = 0;
        var remove = new CheckBox { Content = "Delete selected slot instead of filling" };
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock { Text = "Local Vault · exact HTTPS site only. Fill never submits a form. Payment slots hold only name/expiry and masked metadata; card number and CVV/CVC stay manual.", TextWrapping = TextWrapping.Wrap });
        content.Children.Add(slots); content.Children.Add(remove);
        var dialog = new ContentDialog { XamlRoot = RootGrid.XamlRoot, Title = "Local Vault",
            Content = content, PrimaryButtonText = "Fill selected", SecondaryButtonText = "Add slot", CloseButtonText = "Cancel",
            IsPrimaryButtonEnabled = entries.Count > 0, DefaultButton = ContentDialogButton.Close };
        remove.Checked += (_, _) => dialog.PrimaryButtonText = "Delete selected";
        remove.Unchecked += (_, _) => dialog.PrimaryButtonText = "Fill selected";
        _dialogOpen = true;
        ContentDialogResult result;
        try { result = await dialog.ShowAsync(); } finally { _dialogOpen = false; }
        if (result == ContentDialogResult.Secondary) { await AddVaultEntryAsync(); return; }
        if (result != ContentDialogResult.Primary || slots.SelectedItem is not ComboBoxItem { Tag: VaultEntry selected }) return;
        if (remove.IsChecked == true)
        {
            if (await ConfirmAsync("Delete vault slot", $"Delete ‘{selected.Label}’ and its saved login secret?", "Delete"))
            { await _vault.DeleteAsync(selected.Id); SetStatus("Vault slot deleted."); }
            return;
        }
        if (view is null || !CanExecute(view)) { SetStatus("Select the target tab in active Luma before filling."); return; }
        var fill = new VaultAutofillService(_vault);
        var fillResult = await fill.FillAsync(view, selected.Id, WinRT.Interop.WindowNative.GetWindowHandle(this), true,
            () => CanExecute(view), () =>
            {
                StopRecording();
                return new RecordingSuppression();
            });
        SetStatus(fillResult.Success ? $"Filled {fillResult.FilledFields} safe fields locally. Review and submit manually."
            : $"Vault refused to fill: {fillResult.RefusalReason}. No submission occurred.");
        await _auditLog.WriteAsync("vault", fillResult.Success ? "filled" : "refused", selected.Origin);
    }

    private async Task AddVaultEntryAsync()
    {
        var kind = new ComboBox { Header = "Slot type", HorizontalAlignment = HorizontalAlignment.Stretch };
        kind.Items.Add("Login"); kind.Items.Add("Address"); kind.Items.Add("Payment metadata"); kind.SelectedIndex = 0;
        var label = new TextBox { Header = "Label", MaxLength = 120 };
        var origin = new TextBox { Header = "Exact HTTPS site", Text = OriginOf(ActiveWebView?.CoreWebView2?.Source) ?? "https://", MaxLength = 2048 };
        var fields = new StackPanel { Spacing = 8 };
        var boxes = new Dictionary<string, TextBox>();
        var password = new PasswordBox { Header = "Password", MaxLength = 1280 };
        var credman = new CheckBox { Content = "Store login secret in Windows Credential Manager" };
        void Add(string name, int max = 320)
        { var box = new TextBox { Header = name, MaxLength = max }; boxes[name] = box; fields.Children.Add(box); }
        void Render()
        {
            boxes.Clear(); fields.Children.Clear(); password.Password = string.Empty;
            switch (kind.SelectedIndex)
            {
                case 0: Add("Username"); fields.Children.Add(password); fields.Children.Add(credman); break;
                case 1:
                    foreach (var key in new[] { "Full name", "Address line 1", "Address line 2", "City", "Region", "Postal code", "Country code", "Email", "Phone" }) Add(key);
                    break;
                case 2:
                    Add("Brand (Visa/Mastercard/Amex/Discover/JCB/UnionPay/Other)", 20);
                    Add("Last four digits", 4); Add("Card holder", 120); Add("Expiry month", 2); Add("Expiry year", 4);
                    break;
            }
        }
        kind.SelectionChanged += (_, _) => Render(); Render();
        var panel = new StackPanel { Spacing = 10 }; panel.Children.Add(kind); panel.Children.Add(label); panel.Children.Add(origin); panel.Children.Add(fields);
        panel.Children.Add(new TextBlock { Text = "Never enter CVV/CVC or a full card number. Metadata is protected with DPAPI for this Windows user.", TextWrapping = TextWrapping.Wrap });
        var dialog = new ContentDialog { XamlRoot = RootGrid.XamlRoot, Title = "Add local vault slot",
            Content = new ScrollViewer { Content = panel, MaxHeight = 520 }, PrimaryButtonText = "Save locally", CloseButtonText = "Cancel" };
        _dialogOpen = true;
        ContentDialogResult result;
        try { result = await dialog.ShowAsync(); } finally { _dialogOpen = false; }
        if (result != ContentDialogResult.Primary) { password.Password = string.Empty; return; }
        string Value(string key) => boxes[key].Text.Trim();
        try
        {
            var site = SecureVaultService.NormalizeOrigin(origin.Text.Trim());
            switch (kind.SelectedIndex)
            {
                case 0:
                    await _vault.SaveLoginAsync(new LoginVaultEntry { Label = label.Text.Trim(), Origin = site, Username = Value("Username"),
                        SecretStore = credman.IsChecked == true ? LoginSecretStore.WindowsCredentialManager : LoginSecretStore.Dpapi }, password.Password);
                    break;
                case 1:
                    await _vault.SaveAddressAsync(new AddressVaultEntry { Label = label.Text.Trim(), Origin = site,
                        FullName = Value("Full name"), AddressLine1 = Value("Address line 1"), AddressLine2 = Value("Address line 2"),
                        City = Value("City"), Region = Value("Region"), PostalCode = Value("Postal code"),
                        CountryCode = Value("Country code").ToUpperInvariant(), Email = Value("Email"), Phone = Value("Phone") });
                    break;
                case 2:
                    if (!int.TryParse(Value("Expiry month"), out var month) || !int.TryParse(Value("Expiry year"), out var year))
                        throw new ArgumentException("Enter numeric expiry month and year.");
                    await _vault.SavePaymentAsync(new PaymentVaultEntry { Label = label.Text.Trim(), Origin = site,
                        Brand = Value("Brand (Visa/Mastercard/Amex/Discover/JCB/UnionPay/Other)"), Last4 = Value("Last four digits"),
                        Holder = Value("Card holder"), ExpiryMonth = month, ExpiryYear = year });
                    break;
            }
            SetStatus("Vault slot saved locally. Open Local Vault to fill its matching site.");
        }
        finally { password.Password = string.Empty; }
    }

    private sealed class RecordingSuppression : IDisposable { public void Dispose() { } }
}

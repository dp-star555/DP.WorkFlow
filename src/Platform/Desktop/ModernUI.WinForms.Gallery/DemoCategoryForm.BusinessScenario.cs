using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using ModernUI.Localization;
using ModernUI.WinForms;

namespace ModernUI.WinForms.Gallery;

internal sealed partial class DemoCategoryForm
{
    private enum DeviceScenarioStatus { Ready, Saved, Restored, Testing, Connected, ValidationFailed, ConnectionFailed }

    private void BuildBusinessScenario()
    {
        var localizationManager = GalleryLocalization.CreateManager(CultureInfo.GetCultureInfo("zh-CN"));
        if (localizationManager is IDisposable disposableLocalization) Own(disposableLocalization);
        var context = localizationManager.Context;
        string T(string key, IReadOnlyDictionary<string, object?>? arguments = null) =>
            context.Text(GalleryLocalization.Key(key), arguments);

        var model = new DeviceSettingsModel();
        var saved = model.Capture();
        var source = Own(new BindingSource { DataSource = model });
        var validation = Own(new ModernValidationProvider { LocalizationContext = context });
        CancellationTokenSource? activeOperation = null;
        var scenarioDisposed = false;
        var synchronizingMode = false;
        var status = DeviceScenarioStatus.Ready;

        var name = new ModernInput { Width = 360, MaxLength = 40 };
        var address = new ModernInput { Width = 360, MaxLength = 45 };
        var port = new ModernInputNumber { Width = 220, Minimum = 1, Maximum = 65535, DecimalPlaces = 0, Increment = 1 };
        var timeout = new ModernInputNumber { Width = 220, Minimum = 100, Maximum = 30000, DecimalPlaces = 0, Increment = 100 };
        var enabled = new ModernSwitch();
        var modeItems = new BindingList<DemoOption>();
        var mode = new ModernSelect
        {
            Width = 360,
            DataSource = modeItems,
            DisplayMember = nameof(DemoOption.Name),
            ValueMember = nameof(DemoOption.Id)
        };
        var notes = new ModernTextArea { Width = 520, Height = 78, ScrollBars = ScrollBars.Vertical };

        Bind(name, nameof(ModernInput.Text), nameof(DeviceSettingsModel.Name));
        Bind(address, nameof(ModernInput.Text), nameof(DeviceSettingsModel.Address));
        Bind(port, nameof(ModernInputNumber.Value), nameof(DeviceSettingsModel.Port));
        Bind(timeout, nameof(ModernInputNumber.Value), nameof(DeviceSettingsModel.Timeout));
        Bind(enabled, nameof(ModernSwitch.Checked), nameof(DeviceSettingsModel.Enabled));
        Bind(notes, nameof(ModernTextArea.Text), nameof(DeviceSettingsModel.Notes));

        var labels = new Dictionary<string, Label>(StringComparer.Ordinal);
        var form = new BufferedTableLayoutPanel
        {
            Width = 720,
            Height = 362,
            ColumnCount = 2,
            RowCount = 7,
            Padding = new Padding(0)
        };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < 6; row++) form.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 98));
        AddRow("BusinessDeviceName", name, 0);
        AddRow("BusinessAddress", address, 1);
        AddRow("BusinessPort", port, 2);
        AddRow("BusinessMode", mode, 3);
        AddRow("BusinessTimeout", timeout, 4);
        AddRow("BusinessEnabled", enabled, 5);
        AddRow("BusinessNotes", notes, 6);

        var stateAlert = new ModernAlert { Width = 720, Height = 72, WrapText = true };
        var summary = new ModernValidationSummary { Width = 720, Height = 76, Provider = validation };
        var spinner = new ModernSpinner { Width = 720, Height = 34 };
        var commandBar = new ModernCommandBar { Width = 720, Height = 42 };

        ModernCommand saveCommand = null!;
        ModernCommand restoreCommand = null!;
        ModernCommand testCommand = null!;
        ModernCommand localeCommand = null!;

        saveCommand = new ModernCommand(() => _ = SaveAsync()) { Icon = ModernIconKind.Settings };
        restoreCommand = new ModernCommand(RestoreSaved) { Icon = ModernIconKind.Close, CanExecutePredicate = CanRestore };
        testCommand = new ModernCommand(() => _ = TestConnectionAsync()) { Icon = ModernIconKind.Play, CanExecutePredicate = () => activeOperation is null };
        localeCommand = new ModernCommand(() => _ = ToggleLocaleAsync()) { Icon = ModernIconKind.Info };
        commandBar.Commands.Add(saveCommand);
        commandBar.Commands.Add(restoreCommand);
        commandBar.Commands.Add(new ModernCommand { Kind = ModernCommandKind.Separator });
        commandBar.Commands.Add(testCommand);
        commandBar.Commands.Add(localeCommand);

        validation.SetAsyncValidator(name, _ => new ValueTask<ModernValidationOutcome>(
            string.IsNullOrWhiteSpace(model.Name)
                ? new ModernValidationOutcome(ModernValidationState.Error, T("BusinessNameRequired"))
                : new ModernValidationOutcome(ModernValidationState.None, string.Empty)));
        validation.SetAsyncValidator(address, async cancellationToken =>
        {
            await Task.Delay(120, cancellationToken);
            if (string.IsNullOrWhiteSpace(model.Address))
                return new ModernValidationOutcome(ModernValidationState.Error, T("BusinessAddressRequired"));
            return IPAddress.TryParse(model.Address, out var parsed) && parsed.AddressFamily == AddressFamily.InterNetwork
                ? new ModernValidationOutcome(ModernValidationState.None, string.Empty)
                : new ModernValidationOutcome(ModernValidationState.Error, T("BusinessAddressInvalid"));
        });

        mode.SelectedIndexChanged += (_, _) =>
        {
            if (synchronizingMode) return;
            if (mode.SelectedValue is string value) model.ModeId = value;
        };
        model.PropertyChanged += ModelChanged;
        // Own this after BindingSource/ValidationProvider. DemoCategoryForm disposes resources in
        // reverse registration order, so cancellation completes before those dependencies vanish.
        Own(new DeviceScenarioLifetime(() =>
        {
            scenarioDisposed = true;
            model.PropertyChanged -= ModelChanged;
            activeOperation?.Cancel();
            activeOperation?.Dispose();
            activeOperation = null;
        }));

        ApplyLocalizedText();
        SynchronizeModeSelection();
        ApplyStatus();
        ModernUiSettings.ApplyLocalization(commandBar, context);
        ModernUiSettings.ApplyLocalization(form, context);
        ModernUiSettings.ApplyLocalization(stateAlert, context);
        ModernUiSettings.ApplyLocalization(summary, context);
        ModernUiSettings.ApplyLocalization(spinner, context);

        AddSection("真实业务页面", "设备参数编辑、BindingSource、异步验证、命令、保存快照、连接测试、语言切换和释放生命周期。",
            Column(commandBar, stateAlert, form, summary, spinner));

        void Bind(Control control, string controlProperty, string modelProperty)
        {
            control.DataBindings.Add(controlProperty, source, modelProperty, true,
                DataSourceUpdateMode.OnPropertyChanged, null);
        }

        void AddRow(string key, Control editor, int row)
        {
            var label = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent,
                Tag = "secondary"
            };
            labels[key] = label;
            editor.Anchor = AnchorStyles.Left;
            form.Controls.Add(label, 0, row);
            form.Controls.Add(editor, 1, row);
        }

        void ModelChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DeviceSettingsModel.ModeId)) SynchronizeModeSelection();
            saveCommand.RaiseCanExecuteChanged();
            restoreCommand.RaiseCanExecuteChanged();
            Report($"设备参数已修改 · {e.PropertyName}");
        }

        bool CanRestore() => activeOperation is null && model.Capture() != saved;

        void SynchronizeModeSelection()
        {
            synchronizingMode = true;
            try { mode.SelectedValue = model.ModeId; }
            finally { synchronizingMode = false; }
        }

        async Task<bool> ValidateAsync(CancellationToken cancellationToken)
        {
            validation.LocalizationContext = context;
            var valid = await validation.ValidateAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (valid) return true;
            status = DeviceScenarioStatus.ValidationFailed;
            ApplyStatus();
            validation.FocusFirstInvalid();
            return false;
        }

        async Task SaveAsync()
        {
            if (activeOperation is not null) return;
            using var operation = new CancellationTokenSource();
            activeOperation = operation;
            SetBusy(true, "BusinessValidating");
            try
            {
                source.EndEdit();
                if (!await ValidateAsync(operation.Token)) return;
                saved = model.Capture();
                status = DeviceScenarioStatus.Saved;
                ApplyStatus();
                Report($"配置已保存 · {model.Address}:{model.Port:0}");
                restoreCommand.RaiseCanExecuteChanged();
            }
            catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
            finally
            {
                if (ReferenceEquals(activeOperation, operation)) activeOperation = null;
                if (!scenarioDisposed) SetBusy(false);
            }
        }

        void RestoreSaved()
        {
            if (activeOperation is not null) return;
            validation.Clear();
            model.Restore(saved);
            source.ResetBindings(false);
            SynchronizeModeSelection();
            status = DeviceScenarioStatus.Restored;
            ApplyStatus();
            restoreCommand.RaiseCanExecuteChanged();
            Report("已恢复上次保存的设备参数");
        }

        async Task TestConnectionAsync()
        {
            if (activeOperation is not null) return;
            using var operation = new CancellationTokenSource();
            activeOperation = operation;
            SetBusy(true, "BusinessTesting");
            status = DeviceScenarioStatus.Testing;
            ApplyStatus();
            try
            {
                source.EndEdit();
                if (!await ValidateAsync(operation.Token)) return;
                await Task.Delay(650, operation.Token);
                if (model.Address.EndsWith(".0", StringComparison.Ordinal))
                {
                    validation.SetValidation(address, ModernValidationState.Error, T("BusinessConnectionRejected"));
                    status = DeviceScenarioStatus.ConnectionFailed;
                }
                else
                {
                    validation.Clear(address);
                    status = DeviceScenarioStatus.Connected;
                }
                ApplyStatus();
                Report(status == DeviceScenarioStatus.Connected
                    ? $"连接成功 · {model.Address}:{model.Port:0}"
                    : $"连接失败 · {model.Address}:{model.Port:0}");
            }
            catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
            finally
            {
                if (ReferenceEquals(activeOperation, operation)) activeOperation = null;
                if (!scenarioDisposed) SetBusy(false);
            }
        }

        async Task ToggleLocaleAsync()
        {
            var nextEnglish = context.Current.Culture.Name != "en-US";
            await localizationManager.ChangeLocaleAsync(CultureInfo.GetCultureInfo(nextEnglish ? "en-US" : "zh-CN"));
            ApplyLocalizedText();
            ModernUiSettings.ApplyLocalization(commandBar, context);
            ModernUiSettings.ApplyLocalization(form, context);
            ModernUiSettings.ApplyLocalization(stateAlert, context);
            ModernUiSettings.ApplyLocalization(summary, context);
            ModernUiSettings.ApplyLocalization(spinner, context);
            await validation.ValidateAsync();
            if (scenarioDisposed) return;
            ApplyStatus();
            Report($"Language = {context.Current.Culture.Name}；业务值保持 {model.Address}:{model.Port:0}");
        }

        void ApplyLocalizedText()
        {
            foreach (var pair in labels) pair.Value.Text = T(pair.Key);
            name.AccessibleName = T("BusinessDeviceName");
            address.AccessibleName = T("BusinessAddress");
            port.AccessibleName = T("BusinessPort");
            mode.AccessibleName = T("BusinessMode");
            timeout.AccessibleName = T("BusinessTimeout");
            enabled.AccessibleName = T("BusinessEnabled");
            notes.AccessibleName = T("BusinessNotes");
            name.PlaceholderText = T("BusinessDeviceNamePlaceholder");
            address.PlaceholderText = T("BusinessAddressPlaceholder");
            notes.PlaceholderText = T("BusinessNotesPlaceholder");
            saveCommand.Text = T("BusinessSave");
            restoreCommand.Text = T("BusinessRestore");
            testCommand.Text = T("BusinessTestConnection");
            localeCommand.Text = T(context.Current.Culture.Name == "en-US"
                ? "BusinessSwitchToChinese" : "BusinessSwitchToEnglish");
            var selectedId = model.ModeId;
            var wasOpen = mode.DroppedDown;
            synchronizingMode = true;
            try
            {
                modeItems.Clear();
                modeItems.Add(new DemoOption("continuous", T("BusinessModeContinuous")));
                modeItems.Add(new DemoOption("triggered", T("BusinessModeTriggered")));
                modeItems.Add(new DemoOption("single", T("BusinessModeSingle")));
                mode.SelectedValue = selectedId;
            }
            finally { synchronizingMode = false; }
            if (wasOpen) mode.DroppedDown = true;
        }

        void SetBusy(bool busy, string? textKey = null)
        {
            spinner.Spinning = busy;
            spinner.Text = busy && textKey is not null ? T(textKey) : string.Empty;
            saveCommand.Enabled = !busy;
            localeCommand.Enabled = !busy;
            testCommand.RaiseCanExecuteChanged();
            restoreCommand.RaiseCanExecuteChanged();
        }

        void ApplyStatus()
        {
            stateAlert.Status = status switch
            {
                DeviceScenarioStatus.Saved or DeviceScenarioStatus.Connected => ModernVisualStatus.Success,
                DeviceScenarioStatus.ValidationFailed or DeviceScenarioStatus.ConnectionFailed => ModernVisualStatus.Error,
                DeviceScenarioStatus.Testing => ModernVisualStatus.Warning,
                _ => ModernVisualStatus.Primary
            };
            var prefix = status switch
            {
                DeviceScenarioStatus.Saved => "BusinessSaved",
                DeviceScenarioStatus.Restored => "BusinessRestored",
                DeviceScenarioStatus.Testing => "BusinessTesting",
                DeviceScenarioStatus.Connected => "BusinessConnected",
                DeviceScenarioStatus.ValidationFailed => "BusinessValidationFailed",
                DeviceScenarioStatus.ConnectionFailed => "BusinessConnectionFailed",
                _ => "BusinessReady"
            };
            var arguments = new Dictionary<string, object?>
            {
                ["address"] = model.Address,
                ["port"] = model.Port
            };
            stateAlert.Text = T(prefix + "Title", arguments);
            stateAlert.Description = T(prefix + "Description", arguments);
        }
    }
}

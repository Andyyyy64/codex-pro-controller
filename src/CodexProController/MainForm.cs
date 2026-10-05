using System.Collections.Concurrent;
using CodexProController.Core;
using Binding = CodexProController.Core.Binding;

namespace CodexProController;

internal sealed class MainForm : Form
{
    private readonly string settingsPath = Path.Combine(Program.DataDirectory, "config.json");
    private Settings settings = new();
    private ControllerDevice? device;
    private readonly InputRouter router = new();
    private readonly ConcurrentQueue<ControllerInput> inputs = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 40 };
    private readonly CheckBox armed = new() { Text = "コントローラー操作を有効にする", AutoSize = true };
    private readonly CheckBox rumble = new() { Text = "状態が変わったら振動", AutoSize = true };
    private readonly Label connection = new() { Text = "接続を確認しています…", AutoSize = true };
    private readonly Label inputLabel = new() { Text = "ボタン: —", AutoSize = true };
    private readonly Label statusLabel = new() { Text = "状態: 未取得（チャットを割り当ててフックを設定）", AutoSize = true };
    private readonly Label message = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly DataGridView slots = Grid();
    private readonly DataGridView bindings = Grid();
    private readonly ComboBox selected = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly ComboBox demo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly NumericUpDown deadZone = new() { DecimalPlaces = 2, Increment = 0.05m, Minimum = 0.1m, Maximum = 0.8m, Width = 65 };
    private readonly NotifyIcon tray = new() { Icon = SystemIcons.Application, Text = "Codex Pro Controller", Visible = true };
    private Task io = Task.CompletedTask;
    private DateTimeOffset nextConnect;
    private DateTimeOffset nextStatus;
    private string? disconnected;
    private string lastFeedback = "";
    private bool allowClose;
    private bool closing;

    public MainForm()
    {
        Text = "Codex Pro Controller · 0.1";
        Font = new Font("Yu Gothic UI", 10);
        ClientSize = new Size(950, 620); MinimumSize = new Size(820, 520);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), RowCount = 7, ColumnCount = 1 };
        layout.RowStyles.Add(new(SizeType.Absolute, 36)); layout.RowStyles.Add(new(SizeType.Absolute, 32));
        layout.RowStyles.Add(new(SizeType.Absolute, 42)); layout.RowStyles.Add(new(SizeType.Absolute, 45));
        layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.Absolute, 80)); layout.RowStyles.Add(new(SizeType.Absolute, 32));
        Controls.Add(layout);
        layout.Controls.Add(new Label { Text = "Codex Pro Controller", Font = new Font(Font.FontFamily, 18, FontStyle.Bold), AutoSize = true }, 0, 0);
        layout.Controls.Add(connection, 0, 1);
        var options = Row(); options.Controls.AddRange([armed, rumble, new Label { Text = "スティックの遊び", AutoSize = true, Padding = new Padding(10, 4, 0, 0) }, deadZone]);
        layout.Controls.Add(options, 0, 2);
        var observation = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        observation.Controls.AddRange([inputLabel, statusLabel]); layout.Controls.Add(observation, 0, 3);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var chatTab = new TabPage("6つのチャット"); var bindingTab = new TabPage("ボタン割り当て");
        tabs.TabPages.AddRange([chatTab, bindingTab]); layout.Controls.Add(tabs, 0, 4);
        slots.Columns.Add("button", "Proコン"); slots.Columns[0].ReadOnly = true; slots.Columns[0].FillWeight = 20;
        slots.Columns.Add("name", "名前"); slots.Columns[1].FillWeight = 35;
        slots.Columns.Add("id", "チャットID または codex://threads/…"); slots.Columns[2].FillWeight = 100;
        chatTab.Controls.Add(slots);
        bindings.Columns.Add("button", "ボタン"); bindings.Columns[0].ReadOnly = true; bindings.Columns[0].FillWeight = 20;
        bindings.Columns.Add(new DataGridViewComboBoxColumn { Name = "kind", HeaderText = "操作", DataSource = Enum.GetNames<ActionKind>(), FillWeight = 35 });
        bindings.Columns.Add("value", "ショートカット / スキルのプロンプト"); bindings.Columns[2].FillWeight = 100;
        bindingTab.Controls.Add(bindings);
        var footer = Row();
        footer.Controls.Add(Button("保存", Save)); footer.Controls.Add(Button("再読込", LoadSettings));
        selected.Items.AddRange(Enumerable.Range(1, 6).Select(i => (object)$"通知対象: Chat {i}").ToArray()); selected.SelectedIndex = 0;
        selected.SelectedIndexChanged += (_, _) => { lastFeedback = ""; nextStatus = DateTimeOffset.MinValue; };
        footer.Controls.Add(selected);
        footer.Controls.Add(Button("チャットを開く", () => { Save(); DesktopActions.OpenSlot(settings.Slots[selected.SelectedIndex]); }));
        demo.Items.AddRange(["Idle", "Working", "NeedsApproval", "Stopped"]); demo.SelectedIndex = 1;
        footer.Controls.Add(demo); footer.Controls.Add(Button("通知テスト", TestFeedback));
        footer.Controls.Add(Button("フック設定を生成", ExportHooks)); layout.Controls.Add(footer, 0, 5);
        layout.Controls.Add(message, 0, 6);
        armed.CheckedChanged += (_, _) => router.Reset();
        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("設定を開く", null, (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); });
        trayMenu.Items.Add("終了", null, (_, _) => Close()); tray.ContextMenuStrip = trayMenu;
        tray.DoubleClick += (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); };
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) Hide(); };
        LoadSettings();
        timer.Tick += (_, _) => Tick(); Shown += (_, _) => timer.Start();
        FormClosing += CloseAsync;
    }

    private static DataGridView Grid() => new() { Dock = DockStyle.Fill, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, BackgroundColor = SystemColors.Window,
        AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells, SelectionMode = DataGridViewSelectionMode.CellSelect };
    private static FlowLayoutPanel Row() => new() { Dock = DockStyle.Fill, WrapContents = true };
    private static Button Button(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, Padding = new Padding(3), Height = 32 };
        button.Click += (_, _) => { try { action(); } catch (Exception e) { MessageBox.Show(e.Message, text, MessageBoxButtons.OK, MessageBoxIcon.Warning); } };
        return button;
    }

    private void LoadSettings()
    {
        try { settings = Settings.Load(settingsPath); }
        catch (Exception e) when (e is IOException or ArgumentException or System.Text.Json.JsonException)
        {
            // Keep the broken file intact; recover explicitly through Save.
            message.Text = "設定を読み込めませんでした: " + e.Message; settings = new Settings();
        }
        slots.Rows.Clear(); bindings.Rows.Clear();
        string[] names = ["ZL + A", "ZL + B", "ZL + X", "ZL + Y", "ZL + L", "ZL + R"];
        for (var i = 0; i < 6; i++) slots.Rows.Add(names[i], settings.Slots[i].Name, settings.Slots[i].ThreadId);
        foreach (var binding in settings.Bindings) bindings.Rows.Add(binding.Button, binding.Kind.ToString(), binding.Value);
        rumble.Checked = settings.Rumble; deadZone.Value = (decimal)settings.DeadZone;
        router.Reset(); lastFeedback = "";
    }

    private void Save()
    {
        slots.EndEdit(); bindings.EndEdit();
        var updated = new Settings
        {
            DeadZone = (double)deadZone.Value, Rumble = rumble.Checked,
            Slots = slots.Rows.Cast<DataGridViewRow>().Select(r => new ChatSlot(Convert.ToString(r.Cells[1].Value) ?? "", Convert.ToString(r.Cells[2].Value) ?? "")).ToList(),
            Bindings = bindings.Rows.Cast<DataGridViewRow>().Select(r => new Binding(Convert.ToString(r.Cells[0].Value)!,
                Enum.Parse<ActionKind>(Convert.ToString(r.Cells[1].Value)!), Convert.ToString(r.Cells[2].Value) ?? "")).ToList()
        };
        updated.Save(settingsPath); settings = updated; router.Reset(); lastFeedback = "";
        message.Text = "保存しました。ZL＋A/B/X/Y/L/Rでチャット1〜6に切り替えます。";
    }

    private void Tick()
    {
        if (closing) return;
        var now = DateTimeOffset.UtcNow;
        if (disconnected is { } error && io.IsCompleted)
        {
            disconnected = null; device?.Dispose(); device = null; router.Reset();
            lastFeedback = ""; connection.Text = "切断: " + error; nextConnect = now.AddSeconds(3);
        }
        if (device is null && io.IsCompleted && now >= nextConnect) { nextConnect = now.AddSeconds(3); io = Connect(); }
        var active = armed.Checked && DesktopActions.IsCodexForeground() && device?.Connected == true;
        if (!active) router.Reset();
        while (inputs.TryDequeue(out var input))
        {
            inputLabel.Text = $"ボタン: {string.Join(" + ", input.Buttons)}　スティック: {input.ScrollY:0.00}";
            if (!active) continue;
            foreach (var routed in router.Route(input, now, settings.DeadZone))
            {
                try
                {
                    if (!DesktopActions.IsCodexForeground()) { router.Reset(); break; }
                    if (routed.Slot is { } slot)
                    {
                        if (settings.Slots[slot].ThreadId.Length == 0) { message.Text = $"Chat {slot + 1}は未割り当てです。"; continue; }
                        selected.SelectedIndex = slot; DesktopActions.OpenSlot(settings.Slots[slot]);
                    }
                    else if (routed.Button is { } name && settings.Bindings.FirstOrDefault(b => b.Button == name) is { } binding) DesktopActions.Run(binding);
                    else if (routed.Scroll != 0) DesktopActions.Scroll(routed.Scroll);
                }
                catch (Exception e) { message.Text = "操作エラー: " + e.Message; armed.Checked = false; }
            }
        }
        if (device?.Connected == true) connection.Text = $"Pro Controller接続済み · 電池 {device.Battery} · {(active ? "操作受付中" : "待機中（有効にしてCodexを前面へ）")}";
        if (now < nextStatus || !io.IsCompleted || device?.Connected != true) return;
        nextStatus = now.AddMilliseconds(500);
        var status = HookBridge.Read(Program.EventsDirectory, settings.Slots[selected.SelectedIndex].ThreadId);
        var state = status?.State ?? ChatState.Unknown;
        statusLabel.Text = status is null ? "状態: 未取得（フック設定・信頼とチャットIDを確認）" :
            $"最後に観測した状態: {state} · {status.UpdatedAt.ToLocalTime():MM/dd HH:mm:ss}";
        var key = $"{selected.SelectedIndex}:{state}";
        if (key == lastFeedback) return;
        // First observed state / switching slots does not replay an old completion vibration.
        var pulse = lastFeedback.StartsWith(selected.SelectedIndex + ":", StringComparison.Ordinal) && settings.Rumble &&
            state is ChatState.NeedsApproval or ChatState.Stopped;
        lastFeedback = key; io = Feedback(state, pulse);
    }

    private async Task Connect()
    {
        var candidate = new ControllerDevice();
        candidate.Input += input => { inputs.Enqueue(input); while (inputs.Count > 256) inputs.TryDequeue(out _); };
        candidate.Disconnected += error => disconnected = error;
        try { await candidate.ConnectAsync(); device = candidate; router.Reset(); lastFeedback = ""; }
        catch (Exception e) { candidate.Dispose(); connection.Text = e.Message; }
    }
    private async Task Feedback(ChatState state, bool pulse)
    {
        try { if (device is { } current) await current.FeedbackAsync(selected.SelectedIndex, state, pulse); }
        catch (Exception e) { lastFeedback = ""; message.Text = "通知エラー: " + e.Message; }
    }
    private void TestFeedback()
    {
        if (!io.IsCompleted || device?.Connected != true) { message.Text = "コントローラーの接続完了を待ってください。"; return; }
        nextStatus = DateTimeOffset.UtcNow.AddSeconds(5); lastFeedback = "";
        io = Feedback(Enum.Parse<ChatState>((string)demo.SelectedItem!), rumble.Checked);
        message.Text = "通知テストです。実際のチャット状態は変更しません。";
    }
    private void ExportHooks()
    {
        using var dialog = new SaveFileDialog { Filter = "JSON (*.json)|*.json", FileName = "codex-pro-controller.hooks.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (!File.Exists(Program.HookExecutable)) throw new IOException("CodexProController.Hooks.exeをアプリと同じフォルダに置いてください。");
        AtomicFile.Write(dialog.FileName, HookBridge.CreateConfig(Program.HookExecutable, Program.EventsDirectory));
        message.Text = "フック設定を生成しました。既存hooks.jsonへマージし、Codexの/hooksで信頼してください。";
    }
    private async void CloseAsync(object? sender, FormClosingEventArgs e)
    {
        if (allowClose) return;
        e.Cancel = true; if (closing) return;
        closing = true; armed.Checked = false; timer.Stop();
        await io;
        if (device?.Connected == true) await Feedback(ChatState.Unknown, false);
        device?.Dispose(); tray.Visible = false; tray.Dispose(); timer.Dispose();
        allowClose = true; Close();
    }
}

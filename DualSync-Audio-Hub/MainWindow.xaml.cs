using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using DualSync.Audio;

namespace DualSync
{
    public partial class MainWindow : Window
    {
        [DllImport("kernel32.dll")]
        private static extern bool GetSystemPowerStatus(out SystemPowerStatus sps);

        [StructLayout(LayoutKind.Sequential)]
        private struct SystemPowerStatus
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }

        private static readonly SolidColorBrush GreenBg = FreezeBrush("#ECFDF5");
        private static readonly SolidColorBrush GreenBorder = FreezeBrush("#A7F3D0");
        private static readonly SolidColorBrush GreenText = FreezeBrush("#047857");
        private static readonly SolidColorBrush GreenDot = FreezeBrush("#10B981");

        private static readonly SolidColorBrush AmberBg = FreezeBrush("#FFFBEB");
        private static readonly SolidColorBrush AmberBorder = FreezeBrush("#FDE68A");
        private static readonly SolidColorBrush AmberText = FreezeBrush("#B45309");
        private static readonly SolidColorBrush AmberDot = FreezeBrush("#F59E0B");

        private static readonly SolidColorBrush RedBg = FreezeBrush("#FEF2F2");
        private static readonly SolidColorBrush RedBorder = FreezeBrush("#FECACA");
        private static readonly SolidColorBrush RedText = FreezeBrush("#B91C1C");
        private static readonly SolidColorBrush RedDot = FreezeBrush("#EF4444");

        private static readonly SolidColorBrush BrandBlue = FreezeBrush("#2563EB");
        private static readonly SolidColorBrush SlateGray = FreezeBrush("#64748B");
        private static readonly SolidColorBrush DarkNavy = FreezeBrush("#0F172A");
        private static readonly SolidColorBrush CardBg = FreezeBrush("#F8FAFC");
        private static readonly SolidColorBrush CardBorder = FreezeBrush("#E2E8F0");

        private static readonly Geometry ShieldGeo = FreezeGeometry("M12 1L3 5v6c0 5.55 3.84 10.74 9 12 5.16-1.26 9-6.45 9-12V5l-9-4zm-2 16l-4-4 1.41-1.41L10 14.17l6.59-6.59L18 9l-8 8z");
        private static readonly Geometry WarningGeo = FreezeGeometry("M1 21h22L12 2 1 21zm12-3h-2v-2h2v2zm0-4h-2v-4h2v4z");
        private static readonly Geometry AlertGeo = FreezeGeometry("M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-2h2v2zm0-4h-2V7h2v6z");

        private static SolidColorBrush FreezeBrush(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        private static Geometry FreezeGeometry(string data)
        {
            var geo = Geometry.Parse(data);
            geo.Freeze();
            return geo;
        }

        private DualAudioEngine? audioEngine;
        private MMDevice? laptopSpeaker;
        private bool speakerWasMuted;

        private bool device1Muted;
        private bool device2Muted;

        private double device1PreviousVolume = 90;
        private double device2PreviousVolume = 85;

        private int lastLoggedVol1 = -1;
        private int lastLoggedVol2 = -1;

        private string? selectedDevice1Id;
        private string? selectedDevice2Id;

        private readonly float[] waveTelemetry1 = new float[256];
        private readonly float[] waveTelemetry2 = new float[256];

        private readonly DispatcherTimer connectionStatusTimer;
        private readonly DispatcherTimer streamDurationTimer;
        private readonly DispatcherTimer waveformTimer;
        private DispatcherTimer? refreshFeedbackTimer;
        private double animationPhase;
        private DateTime streamStartTime;
        private bool isCheckingConnection;
        private bool isUpdatingDelay;
        private double configuredDelayMs;
        private bool isInitializing = true;
        private const double ReminderIntervalMinutes = 120.0;
        private DateTime lastReminderAckTime = DateTime.MinValue;
        private Window? activeReminderWindow;
        private bool isReminderPopupOpen;

        public double ConfiguredDelayMs
        {
            get => configuredDelayMs;
            set => UpdateConfiguredDelay(value);
        }

        private readonly List<string> fullSessionLogs = new();
        private readonly List<TelemetryRow> consolidatedAuditRows = new();
        private readonly List<DiscoveredAudioDeviceItem> discoveredDeviceItems = new();

        public MainWindow()
        {
            InitializeComponent();

            UpdateHostLaptopProfile();

            Device1ComboBox.SelectionChanged += DeviceComboBox_SelectionChanged;
            Device2ComboBox.SelectionChanged += DeviceComboBox_SelectionChanged;

            LoadConfiguration();
            UpdateConfiguredDelay(configuredDelayMs, syncSlider: true);
            UpdateHearingSafetyIndicators();
            UpdateConsolidatedAuditTable();

            SetStatus("Ready", Brushes.Gray);

            connectionStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            connectionStatusTimer.Tick += ConnectionStatusTimer_Tick;

            streamDurationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            streamDurationTimer.Tick += (s, e) =>
            {
                if (audioEngine != null)
                {
                    TimeSpan elapsed = DateTime.Now - streamStartTime;
                    string formatted = elapsed.ToString(@"hh\:mm\:ss");
                    if (StreamDurationText != null) StreamDurationText.Text = formatted;
                    if (SummaryDurationText != null) SummaryDurationText.Text = formatted;
                    UpdateHearingSafetyIndicators();
                }
            };

            waveformTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            waveformTimer.Tick += (s, e) =>
            {
                animationPhase += 0.08;
                RenderWaveforms();
            };

            RenderWaveforms();

            AppendLog("DualSync v1.0 initialized. DualStream Core online.", "SYNC");
            isInitializing = false;
        }

        private void NavConsoleRadio_Checked(object sender, RoutedEventArgs e)
        {
            HideAllPages();
            if (Page1_Console != null)
            {
                Page1_Console.Visibility = Visibility.Visible;
                UpdateConfiguredDelay(configuredDelayMs, syncSlider: true);
                UpdateHearingSafetyIndicators();
            }
        }

        private void NavNodesRadio_Checked(object sender, RoutedEventArgs e)
        {
            HideAllPages();
            if (Page2_Nodes != null)
            {
                Page2_Nodes.Visibility = Visibility.Visible;
                UpdateNodesDisplay();
            }
        }

        private void NavReportsRadio_Checked(object sender, RoutedEventArgs e)
        {
            HideAllPages();
            if (Page3_Reports != null)
            {
                Page3_Reports.Visibility = Visibility.Visible;
                UpdateConfiguredDelay(configuredDelayMs, syncSlider: false);
                RenderWaveforms();
                if (audioEngine != null && !waveformTimer.IsEnabled)
                {
                    waveformTimer.Start();
                }
            }
        }

        private void NavExportRadio_Checked(object sender, RoutedEventArgs e)
        {
            HideAllPages();
            if (Page4_ExportSummary != null)
            {
                Page4_ExportSummary.Visibility = Visibility.Visible;
                UpdateConfiguredDelay(configuredDelayMs, syncSlider: false);
                UpdateConsolidatedAuditTable();
                UpdateHearingSafetyIndicators();
            }
            AppendLog("Export / Report Summary workspace loaded.", "SYNC");
        }

        private void HideAllPages()
        {
            if (Page1_Console != null) Page1_Console.Visibility = Visibility.Collapsed;
            if (Page2_Nodes != null) Page2_Nodes.Visibility = Visibility.Collapsed;
            if (Page3_Reports != null) Page3_Reports.Visibility = Visibility.Collapsed;
            if (Page4_ExportSummary != null) Page4_ExportSummary.Visibility = Visibility.Collapsed;
        }

        private static string GetHostBatteryStatus()
        {
            try
            {
                if (GetSystemPowerStatus(out var sps))
                {
                    string status = sps.ACLineStatus == 1 ? "Plugged In" : "Discharging";
                    if (sps.BatteryLifePercent <= 100)
                    {
                        return $"{sps.BatteryLifePercent}% ({status})";
                    }
                }
            }
            catch
            {
            }
            return "AC Power";
        }

        private void UpdateHostLaptopProfile()
        {
            try
            {
                string machine = Environment.MachineName;

                if (TopLaptopNameText != null) TopLaptopNameText.Text = machine;
                if (TopHoverLaptopName != null) TopHoverLaptopName.Text = machine;
                if (ProfileLaptopNameText != null) ProfileLaptopNameText.Text = machine;

                string batteryFormatted = GetHostBatteryStatus();

                if (ProfileBatteryText != null) ProfileBatteryText.Text = batteryFormatted;
                if (TopBatteryText != null) TopBatteryText.Text = batteryFormatted;

                int activeCount = (selectedDevice1Id != null ? 1 : 0) + (selectedDevice2Id != null ? 1 : 0);
                string nodeInfo = $"{activeCount} Active Sync";

                if (ProfileDevicesCountText != null) ProfileDevicesCountText.Text = nodeInfo;
                if (TopDevicesCountText != null) TopDevicesCountText.Text = nodeInfo;

                if (ProfileDelayText != null)
                {
                    ProfileDelayText.Text = FormatDelayValue(configuredDelayMs);
                }
            }
            catch
            {
                string fallback = Environment.MachineName;
                if (TopLaptopNameText != null) TopLaptopNameText.Text = fallback;
                if (ProfileLaptopNameText != null) ProfileLaptopNameText.Text = fallback;
                if (ProfileBatteryText != null) ProfileBatteryText.Text = "AC Power";
                if (TopBatteryText != null) TopBatteryText.Text = "AC Power";
            }
        }

        private void DeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isInitializing) return;

            if (sender == Device1ComboBox && Device1ComboBox.SelectedItem is MMDevice d1)
            {
                selectedDevice1Id = d1.ID;
                if (selectedDevice1Id == selectedDevice2Id)
                {
                    var alternate = Device2ComboBox.Items.OfType<MMDevice>().FirstOrDefault(d => d.ID != d1.ID);
                    Device2ComboBox.SelectedItem = alternate;
                    selectedDevice2Id = alternate?.ID;
                    AppendLog("Both slots cannot use the same device. Earbuds 2 selection updated.", "CONNECTION");
                }
                AppendLog($"Slot 1 mapped: {d1.FriendlyName}", "CONNECTION");
            }
            else if (sender == Device2ComboBox && Device2ComboBox.SelectedItem is MMDevice d2)
            {
                selectedDevice2Id = d2.ID;
                if (selectedDevice2Id == selectedDevice1Id)
                {
                    var alternate = Device1ComboBox.Items.OfType<MMDevice>().FirstOrDefault(d => d.ID != d2.ID);
                    Device1ComboBox.SelectedItem = alternate;
                    selectedDevice1Id = alternate?.ID;
                    AppendLog("Both slots cannot use the same device. Earbuds 1 selection updated.", "CONNECTION");
                }
                AppendLog($"Slot 2 mapped: {d2.FriendlyName}", "CONNECTION");
            }

            UpdateNodesDisplay();
            UpdateHostLaptopProfile();
            UpdateConsolidatedAuditTable();
        }

        private static string GetDeviceCategory(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Audio Output";
            string lower = name.ToLowerInvariant();
            if (lower.Contains("bluetooth") || lower.Contains("buds") || lower.Contains("airpods") ||
                lower.Contains("wireless") || lower.Contains("wh-") || lower.Contains("wf-") ||
                lower.Contains("freebuds") || lower.Contains("galaxy buds") || lower.Contains("earbuds") ||
                lower.Contains("tune") || lower.Contains("live") || lower.Contains("air"))
            {
                return "Bluetooth / Wireless Headset";
            }
            if (lower.Contains("headphone") || lower.Contains("headset") || lower.Contains("earphone"))
            {
                return "Headphones / Headset";
            }
            if (lower.Contains("realtek") || lower.Contains("speaker") || lower.Contains("lautsprecher") ||
                lower.Contains("high definition") || lower.Contains("internal") || lower.Contains("soundcard"))
            {
                return "Internal / Built-in Speakers";
            }
            if (lower.Contains("usb") || lower.Contains("dac") || lower.Contains("interface"))
            {
                return "USB Audio Device";
            }
            return "Audio Playback Device";
        }

        private void UpdateNodesDisplay()
        {
            var d1 = Device1ComboBox.SelectedItem as MMDevice;
            var d2 = Device2ComboBox.SelectedItem as MMDevice;

            if (Slot1NameText != null)
            {
                Slot1NameText.Text = d1?.FriendlyName ?? "No device selected";
            }
            if (Slot1DetailsText != null)
            {
                if (d1 != null)
                {
                    string cat = GetDeviceCategory(d1.FriendlyName);
                    Slot1DetailsText.Text = $"Type: {cat} • Active Windows Endpoint";
                }
                else
                {
                    Slot1DetailsText.Text = "Hardware endpoint query: Standby";
                }
            }

            if (Slot2NameText != null)
            {
                Slot2NameText.Text = d2?.FriendlyName ?? "No device selected";
            }
            if (Slot2DetailsText != null)
            {
                if (d2 != null)
                {
                    string cat = GetDeviceCategory(d2.FriendlyName);
                    Slot2DetailsText.Text = $"Type: {cat} • Active Windows Endpoint";
                }
                else
                {
                    Slot2DetailsText.Text = "Hardware endpoint query: Standby";
                }
            }

            UpdateDiscoveredDevicesDisplay();
        }

        private void UpdateDiscoveredDevicesDisplay()
        {
            var d1 = Device1ComboBox?.SelectedItem as MMDevice;
            var d2 = Device2ComboBox?.SelectedItem as MMDevice;

            foreach (var item in discoveredDeviceItems)
            {
                if (d1 != null && item.Id == d1.ID)
                {
                    item.SlotAssignment = "Earbuds 1 (Alpha)";
                    item.SlotBadgeForeground = BrandBlue;
                    item.SlotBadgeBackground = GreenBg;
                    item.SlotBadgeBorder = GreenBorder;
                }
                else if (d2 != null && item.Id == d2.ID)
                {
                    item.SlotAssignment = "Earbuds 2 (Beta)";
                    item.SlotBadgeForeground = AmberText;
                    item.SlotBadgeBackground = AmberBg;
                    item.SlotBadgeBorder = AmberBorder;
                }
                else
                {
                    item.SlotAssignment = "Unassigned";
                    item.SlotBadgeForeground = SlateGray;
                    item.SlotBadgeBackground = CardBg;
                    item.SlotBadgeBorder = CardBorder;
                }
            }

            if (DiscoveredDevicesItemsControl != null)
            {
                DiscoveredDevicesItemsControl.ItemsSource = null;
                DiscoveredDevicesItemsControl.ItemsSource = discoveredDeviceItems;
            }

            if (ReportEndpointsItemsControl != null)
            {
                ReportEndpointsItemsControl.ItemsSource = null;
                ReportEndpointsItemsControl.ItemsSource = discoveredDeviceItems;
            }
        }

        private void DeviceCardAssignSlot1_Click(object sender, RoutedEventArgs e)
        {
            if (audioEngine != null)
            {
                MessageBox.Show("Please stop DualSync before reassigning devices.", "DualSync", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if ((sender as FrameworkElement)?.DataContext is DiscoveredAudioDeviceItem item)
            {
                int idx = Device1ComboBox.Items.OfType<MMDevice>().ToList().FindIndex(d => d.ID == item.Id);
                if (idx >= 0)
                {
                    Device1ComboBox.SelectedIndex = idx;
                    selectedDevice1Id = item.Id;
                    if (selectedDevice1Id == selectedDevice2Id)
                    {
                        var alternate = Device2ComboBox.Items.OfType<MMDevice>().FirstOrDefault(d => d.ID != item.Id);
                        Device2ComboBox.SelectedItem = alternate;
                        selectedDevice2Id = alternate?.ID;
                    }
                    UpdateNodesDisplay();
                    UpdateHostLaptopProfile();
                    UpdateConsolidatedAuditTable();
                    AppendLog($"Earbuds 1 assigned: {item.FriendlyName}", "CONNECTION");
                }
            }
        }

        private void DeviceCardAssignSlot2_Click(object sender, RoutedEventArgs e)
        {
            if (audioEngine != null)
            {
                MessageBox.Show("Please stop DualSync before reassigning devices.", "DualSync", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if ((sender as FrameworkElement)?.DataContext is DiscoveredAudioDeviceItem item)
            {
                int idx = Device2ComboBox.Items.OfType<MMDevice>().ToList().FindIndex(d => d.ID == item.Id);
                if (idx >= 0)
                {
                    Device2ComboBox.SelectedIndex = idx;
                    selectedDevice2Id = item.Id;
                    if (selectedDevice2Id == selectedDevice1Id)
                    {
                        var alternate = Device1ComboBox.Items.OfType<MMDevice>().FirstOrDefault(d => d.ID != item.Id);
                        Device1ComboBox.SelectedItem = alternate;
                        selectedDevice1Id = alternate?.ID;
                    }
                    UpdateNodesDisplay();
                    UpdateHostLaptopProfile();
                    UpdateConsolidatedAuditTable();
                    AppendLog($"Earbuds 2 assigned: {item.FriendlyName}", "CONNECTION");
                }
            }
        }

        private void DeviceCardChime_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is DiscoveredAudioDeviceItem item)
            {
                PlaySyntheticChime(item.Id, 523.25);
                AppendLog($"Test chime sent to endpoint: {item.FriendlyName}", "SYNC");
            }
        }

        private void LoadAudioDevices(string? preferred1 = null, string? preferred2 = null)
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();

                Device1ComboBox.ItemsSource = devices;
                Device2ComboBox.ItemsSource = devices;

                Device1ComboBox.DisplayMemberPath = "FriendlyName";
                Device2ComboBox.DisplayMemberPath = "FriendlyName";

                if (DevicesCountBadge != null) DevicesCountBadge.Text = $"{devices.Count} Devices Found";

                string? target1 = preferred1 ?? selectedDevice1Id;
                string? target2 = preferred2 ?? selectedDevice2Id;

                int idx1 = -1;
                if (!string.IsNullOrWhiteSpace(target1))
                {
                    idx1 = devices.FindIndex(d => d.ID == target1);
                }
                if (idx1 < 0 && devices.Count > 0)
                {
                    idx1 = 0;
                }
                Device1ComboBox.SelectedIndex = idx1;

                int idx2 = -1;
                if (!string.IsNullOrWhiteSpace(target2))
                {
                    idx2 = devices.FindIndex(d => d.ID == target2 && (idx1 < 0 || d.ID != devices[idx1].ID));
                }
                if (idx2 < 0 && devices.Count > 1)
                {
                    idx2 = devices.FindIndex(d => idx1 < 0 || d.ID != devices[idx1].ID);
                }
                Device2ComboBox.SelectedIndex = idx2;

                selectedDevice1Id = (Device1ComboBox.SelectedItem as MMDevice)?.ID;
                selectedDevice2Id = (Device2ComboBox.SelectedItem as MMDevice)?.ID;

                discoveredDeviceItems.Clear();
                foreach (var d in devices)
                {
                    string cat = GetDeviceCategory(d.FriendlyName);
                    string icon = (cat.Contains("Headphone") || cat.Contains("Headset") || cat.Contains("Wireless") || cat.Contains("Bluetooth"))
                        ? "🎧"
                        : (cat.Contains("Speaker") ? "🔊" : "🎵");

                    discoveredDeviceItems.Add(new DiscoveredAudioDeviceItem
                    {
                        Id = d.ID,
                        FriendlyName = d.FriendlyName,
                        DeviceType = cat,
                        DeviceIcon = icon,
                        Status = "Active Endpoint",
                        Device = d
                    });
                }

                if (SummaryDeviceCountText != null)
                {
                    SummaryDeviceCountText.Text = $"{devices.Count} Devices";
                }

                UpdateNodesDisplay();
                if (ReportEndpointsItemsControl != null)
                {
                    ReportEndpointsItemsControl.ItemsSource = null;
                    ReportEndpointsItemsControl.ItemsSource = discoveredDeviceItems;
                }
            }
            catch (Exception ex)
            {
                SetStatus("Device Error", Brushes.Red);
                AppendLog($"Endpoint scan failure: {ex.Message}", "CONNECTION");
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            if (audioEngine != null)
            {
                MessageBox.Show("Please stop DualSync before refreshing hardware endpoints.", "DualSync", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            LoadAudioDevices(selectedDevice1Id, selectedDevice2Id);
            UpdateHostLaptopProfile();
            AppendLog("Hardware endpoints rescanned successfully.", "CONNECTION");

            SetStatus("Refreshed", GreenDot);

            refreshFeedbackTimer?.Stop();
            refreshFeedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            refreshFeedbackTimer.Tick += (s, args) =>
            {
                refreshFeedbackTimer.Stop();
                string status = audioEngine != null ? "Running" : "Ready";
                Brush brush = audioEngine != null ? GreenDot : Brushes.Gray;
                SetStatus(status, brush);
            };
            refreshFeedbackTimer.Start();
        }

        private void UpdateSyncModeDisplay(string state, string detail, Brush color)
        {
            if (SyncModeStatusText != null)
            {
                SyncModeStatusText.Text = state;
                SyncModeStatusText.Foreground = color;
            }
            if (SyncModeDetailText != null)
            {
                SyncModeDetailText.Text = detail;
            }
            if (StreamStatusSubText != null)
            {
                StreamStatusSubText.Text = audioEngine != null ? "Broadcast session active" : "Engine stopped";
            }
            if (SummaryEngineStateText != null)
            {
                SummaryEngineStateText.Text = state;
                SummaryEngineStateText.Foreground = color;
            }
            if (SummaryEngineDot != null)
            {
                SummaryEngineDot.Fill = color;
            }
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(selectedDevice1Id) || string.IsNullOrWhiteSpace(selectedDevice2Id))
                {
                    MessageBox.Show(
                        "DualSync requires two audio output devices to stream simultaneously.\n\nPlease connect two audio devices (such as Bluetooth earbuds or headphones) and select them in Earbuds 1 and Earbuds 2.",
                        "DualSync Audio Hub",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                if (selectedDevice1Id == selectedDevice2Id)
                {
                    MessageBox.Show(
                        "DualSync requires two different audio output devices. Both slots cannot use the same device.",
                        "DualSync Audio Hub",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                using var enumerator = new MMDeviceEnumerator();
                var activeDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();

                var dev1 = activeDevices.FirstOrDefault(d => d.ID == selectedDevice1Id);
                var dev2 = activeDevices.FirstOrDefault(d => d.ID == selectedDevice2Id);

                if (dev1 == null || dev2 == null)
                {
                    MessageBox.Show(
                        "One or both selected audio devices are no longer detected. Please click 'Refresh Devices' to scan available endpoints.",
                        "Device Not Available",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    LoadAudioDevices();
                    return;
                }

                UpdateSyncModeDisplay("Starting", "Initializing capture & playback pipelines", BrandBlue);

                laptopSpeaker = activeDevices.FirstOrDefault(d => d.FriendlyName.Contains("Realtek", StringComparison.OrdinalIgnoreCase));

                if (laptopSpeaker != null)
                {
                    if (laptopSpeaker.ID != selectedDevice1Id && laptopSpeaker.ID != selectedDevice2Id)
                    {
                        try
                        {
                            speakerWasMuted = laptopSpeaker.AudioEndpointVolume.Mute;
                            laptopSpeaker.AudioEndpointVolume.Mute = true;
                        }
                        catch { }
                    }
                    else
                    {
                        laptopSpeaker.Dispose();
                        laptopSpeaker = null;
                    }
                }

                audioEngine = new DualAudioEngine(dev1, dev2);
                audioEngine.Start();

                audioEngine.SetVolume1((float)(Device1VolumeSlider.Value / 100.0));
                audioEngine.SetVolume2((float)(Device2VolumeSlider.Value / 100.0));
                audioEngine.SetPhaseDelay(configuredDelayMs);
                if (EchoTestCheckBox.IsChecked == true)
                {
                    audioEngine.SetEchoTest(true);
                }

                StartButton.IsEnabled = false;
                StopButton.IsEnabled = true;
                if (RefreshButton != null) RefreshButton.IsEnabled = false;
                if (ScanBtn != null) ScanBtn.IsEnabled = false;

                streamStartTime = DateTime.Now;
                lastReminderAckTime = DateTime.Now;
                isReminderPopupOpen = false;
                streamDurationTimer.Start();
                connectionStatusTimer.Start();
                waveformTimer.Start();

                SetStatus("Running", GreenDot);
                AppendLog("DualStream synchronization broadcast started.", "SYNC");
                UpdateConnectionStatusAsync();
                UpdateHostLaptopProfile();
                UpdateHearingSafetyIndicators();
                UpdateConsolidatedAuditTable();
                RenderWaveforms();
            }
            catch (Exception ex)
            {
                audioEngine?.Dispose();
                audioEngine = null;
                RestoreLaptopSpeaker();
                SetStatus("Error", RedDot);
                UpdateSyncModeDisplay("Error", $"Start fault: {ex.Message}", RedDot);
                AppendLog($"Start failed: {ex.Message}", "SYNC");
                MessageBox.Show($"Unable to start audio synchronization:\n\n{ex.Message}", "DualSync Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (activeReminderWindow != null)
                {
                    try
                    {
                        var w = activeReminderWindow;
                        activeReminderWindow = null;
                        w.Close();
                    }
                    catch { }
                }
                isReminderPopupOpen = false;
                lastReminderAckTime = DateTime.MinValue;

                connectionStatusTimer.Stop();
                streamDurationTimer.Stop();
                waveformTimer.Stop();

                audioEngine?.Dispose();
                audioEngine = null;

                RestoreLaptopSpeaker();

                StartButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                if (RefreshButton != null) RefreshButton.IsEnabled = true;
                if (ScanBtn != null) ScanBtn.IsEnabled = true;

                SetDeviceConnectionStatus(Device1StatusDot, Device1StatusText, "Not connected", Brushes.Gray);
                SetDeviceConnectionStatus(Device2StatusDot, Device2StatusText, "Not connected", Brushes.Gray);

                UpdateSyncModeDisplay("Standby", "Audio engine stopped", Brushes.Gray);
                if (ActiveEndpointsSummaryText != null) ActiveEndpointsSummaryText.Text = "0 / 2 Active";

                SetStatus("Ready", Brushes.Gray);
                AppendLog("Audio broadcast stopped. Returned to standby.", "SYNC");
                UpdateHearingSafetyIndicators();
                UpdateConsolidatedAuditTable();
                RenderWaveforms();
            }
            catch (Exception ex)
            {
                SetStatus("Stop Error", RedDot);
                UpdateSyncModeDisplay("Error", $"Stop fault: {ex.Message}", RedDot);
                AppendLog($"Stop error: {ex.Message}", "SYNC");
            }
        }

        private void RestoreLaptopSpeaker()
        {
            if (laptopSpeaker != null)
            {
                try { laptopSpeaker.AudioEndpointVolume.Mute = speakerWasMuted; } catch { }
                try { laptopSpeaker.Dispose(); } catch { }
                laptopSpeaker = null;
            }
        }

        private void Device1VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (isInitializing) return;

            if (audioEngine != null && !device1Muted)
            {
                try { audioEngine.SetVolume1((float)(e.NewValue / 100.0)); } catch { }
            }
            device1PreviousVolume = e.NewValue;
            UpdateHearingSafetyIndicators();

            int volInt = (int)Math.Round(e.NewValue);
            if (Math.Abs(volInt - lastLoggedVol1) >= 2)
            {
                lastLoggedVol1 = volInt;
                AppendLog($"Node 1 Gain adjusted: {volInt}%", "VOLUME");
            }

            RenderWaveforms();
        }

        private void Device2VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (isInitializing) return;

            if (audioEngine != null && !device2Muted)
            {
                try { audioEngine.SetVolume2((float)(e.NewValue / 100.0)); } catch { }
            }
            device2PreviousVolume = e.NewValue;
            UpdateHearingSafetyIndicators();

            int volInt = (int)Math.Round(e.NewValue);
            if (Math.Abs(volInt - lastLoggedVol2) >= 2)
            {
                lastLoggedVol2 = volInt;
                AppendLog($"Node 2 Gain adjusted: {volInt}%", "VOLUME");
            }

            RenderWaveforms();
        }

        private void Device1MuteButton_Click(object sender, RoutedEventArgs e)
        {
            if (audioEngine == null) return;
            device1Muted = !device1Muted;

            if (device1Muted)
            {
                audioEngine.SetMute1(true);
                Device1MuteButton.Content = "🔊 Unmute";
                AppendLog("Node 1 Muted.", "VOLUME");
            }
            else
            {
                audioEngine.SetMute1(false);
                audioEngine.SetVolume1((float)(device1PreviousVolume / 100.0));
                Device1MuteButton.Content = "🔊 Mute";
                AppendLog("Node 1 Unmuted.", "VOLUME");
            }
            UpdateHearingSafetyIndicators();
            RenderWaveforms();
        }

        private void Device2MuteButton_Click(object sender, RoutedEventArgs e)
        {
            if (audioEngine == null) return;
            device2Muted = !device2Muted;

            if (device2Muted)
            {
                audioEngine.SetMute2(true);
                Device2MuteButton.Content = "🔊 Unmute";
                AppendLog("Node 2 Muted.", "VOLUME");
            }
            else
            {
                audioEngine.SetMute2(false);
                audioEngine.SetVolume2((float)(device2PreviousVolume / 100.0));
                Device2MuteButton.Content = "🔊 Mute";
                AppendLog("Node 2 Unmuted.", "VOLUME");
            }
            UpdateHearingSafetyIndicators();
            RenderWaveforms();
        }

        private void UpdateHearingSafetyIndicators()
        {
            int rawV1 = (int)Math.Round(Device1VolumeSlider.Value);
            int rawV2 = (int)Math.Round(Device2VolumeSlider.Value);
            int v1 = (device1Muted && audioEngine != null) ? 0 : rawV1;
            int v2 = (device2Muted && audioEngine != null) ? 0 : rawV2;

            UpdateEarbudSafetyBanner(
                rawV1,
                Device1SafetyBorder,
                Device1SafetyDot,
                Device1SafetyTitle,
                Device1SafetyValue,
                Device1SafetyIcon,
                Device1SafetyDetail,
                Vol1Label);

            UpdateEarbudSafetyBanner(
                rawV2,
                Device2SafetyBorder,
                Device2SafetyDot,
                Device2SafetyTitle,
                Device2SafetyValue,
                Device2SafetyIcon,
                Device2SafetyDetail,
                Vol2Label);

            UpdateReportHearingSafety(v1, v2);
        }

        private void UpdateEarbudSafetyBanner(
            int volume,
            Border? border,
            System.Windows.Shapes.Ellipse? dot,
            TextBlock? title,
            TextBlock? value,
            System.Windows.Shapes.Path? icon,
            TextBlock? detail,
            TextBlock? volLabel)
        {
            if (border == null) return;

            if (volume <= 75)
            {
                border.Background = GreenBg;
                border.BorderBrush = GreenBorder;
                if (dot != null) dot.Fill = GreenDot;
                if (title != null) { title.Text = "● Safe Volume"; title.Foreground = GreenText; }
                if (value != null) { value.Text = $"{volume}% / 80%"; value.Foreground = GreenText; }
                if (icon != null) { icon.Data = ShieldGeo; icon.Fill = GreenText; }
                if (detail != null) { detail.Text = "Within safe listening limit"; detail.Foreground = GreenText; }
                if (volLabel != null) volLabel.Foreground = BrandBlue;
            }
            else if (volume <= 80)
            {
                border.Background = AmberBg;
                border.BorderBrush = AmberBorder;
                if (dot != null) dot.Fill = AmberDot;
                if (title != null) { title.Text = "● Volume Limit Near"; title.Foreground = AmberText; }
                if (value != null) { value.Text = $"{volume}% / 80%"; value.Foreground = AmberText; }
                if (icon != null) { icon.Data = WarningGeo; icon.Fill = AmberText; }
                if (detail != null) { detail.Text = "Approaching safety limit"; detail.Foreground = AmberText; }
                if (volLabel != null) volLabel.Foreground = AmberText;
            }
            else
            {
                border.Background = RedBg;
                border.BorderBrush = RedBorder;
                if (dot != null) dot.Fill = RedDot;
                if (title != null) { title.Text = "● High Volume"; title.Foreground = RedText; }
                if (value != null) { value.Text = $"{volume}% / 80%"; value.Foreground = RedText; }
                if (icon != null) { icon.Data = AlertGeo; icon.Fill = RedText; }
                if (detail != null) { detail.Text = "Safety limit exceeded"; detail.Foreground = RedText; }
                if (volLabel != null) volLabel.Foreground = RedText;
            }
        }

        private void UpdateReportHearingSafety(int v1, int v2)
        {
            int maxVol = Math.Max(v1, v2);
            TimeSpan elapsed = audioEngine != null ? (DateTime.Now - streamStartTime) : TimeSpan.Zero;
            TimeSpan cycleElapsed = (audioEngine != null && lastReminderAckTime != DateTime.MinValue)
                ? (DateTime.Now - lastReminderAckTime)
                : TimeSpan.Zero;
            if (cycleElapsed < TimeSpan.Zero) cycleElapsed = TimeSpan.Zero;

            bool isBreakRecommended = audioEngine != null && cycleElapsed.TotalMinutes >= ReminderIntervalMinutes;

            if (isBreakRecommended && !isReminderPopupOpen)
            {
                TriggerBreakReminderPopup(elapsed, maxVol);
            }

            if (HearingSafetyVolText != null)
            {
                HearingSafetyVolText.Text = $"{maxVol}% / 80%";
                HearingSafetyVolText.Foreground = maxVol > 80 ? RedText : (maxVol > 75 ? AmberText : GreenText);
            }

            if (HearingSafetyVolProgress != null)
            {
                HearingSafetyVolProgress.Value = Math.Clamp(maxVol, 0, 100);
                HearingSafetyVolProgress.Foreground = maxVol > 80 ? RedDot : (maxVol > 75 ? AmberDot : GreenDot);
            }

            if (HearingSafetyDurationText != null)
            {
                string durationStr = (audioEngine != null ? cycleElapsed : TimeSpan.Zero).ToString(@"hh\:mm\:ss");
                HearingSafetyDurationText.Text = $"{durationStr} / 02:00:00";
                HearingSafetyDurationText.Foreground = isBreakRecommended ? AmberText : DarkNavy;
            }

            if (HearingSafetyDurationProgress != null)
            {
                double durationPct = audioEngine != null ? Math.Clamp((cycleElapsed.TotalMinutes / ReminderIntervalMinutes) * 100.0, 0, 100) : 0;
                HearingSafetyDurationProgress.Value = durationPct;
                HearingSafetyDurationProgress.Foreground = isBreakRecommended ? AmberDot : GreenDot;
            }

            if (HearingSafetyStateText != null && HearingSafetyDot != null && HearingSafetySubText != null)
            {
                if (maxVol > 80)
                {
                    HearingSafetyDot.Fill = RedDot;
                    HearingSafetyStateText.Text = "High Volume Alert";
                    HearingSafetyStateText.Foreground = RedText;
                    HearingSafetySubText.Text = isBreakRecommended ? "Safety limit exceeded & break recommended" : "Volume exceeds 80% listening threshold";
                }
                else if (isBreakRecommended)
                {
                    HearingSafetyDot.Fill = AmberDot;
                    HearingSafetyStateText.Text = "Break Recommended";
                    HearingSafetyStateText.Foreground = AmberText;
                    HearingSafetySubText.Text = "2-hour continuous listening limit reached";
                }
                else if (maxVol > 75)
                {
                    HearingSafetyDot.Fill = AmberDot;
                    HearingSafetyStateText.Text = "Volume Limit Near";
                    HearingSafetyStateText.Foreground = AmberText;
                    HearingSafetySubText.Text = "Approaching 80% safety limit";
                }
                else
                {
                    HearingSafetyDot.Fill = GreenDot;
                    HearingSafetyStateText.Text = "Safe Listening";
                    HearingSafetyStateText.Foreground = GreenText;
                    HearingSafetySubText.Text = "Volume and listening duration within safe limits";
                }
            }
        }

        private void TriggerBreakReminderPopup(TimeSpan elapsed, int currentVolume)
        {
            if (isReminderPopupOpen) return;
            isReminderPopupOpen = true;

            try
            {
                var win = new Window
                {
                    Title = "Listening Break Recommended",
                    Width = 480,
                    SizeToContent = SizeToContent.Height,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this,
                    ResizeMode = ResizeMode.NoResize,
                    Background = Brushes.White,
                    WindowStyle = WindowStyle.SingleBorderWindow,
                    FontFamily = new FontFamily("Segoe UI, Inter, sans-serif")
                };

                activeReminderWindow = win;

                var rootGrid = new Grid { Margin = new Thickness(24, 20, 24, 24) };
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var headerStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
                var iconPath = new System.Windows.Shapes.Path
                {
                    Width = 22,
                    Height = 22,
                    Stretch = Stretch.Uniform,
                    Fill = AmberDot,
                    Data = WarningGeo,
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                var titleText = new TextBlock
                {
                    Text = "Listening Break Recommended",
                    FontSize = 18,
                    FontWeight = FontWeights.Bold,
                    Foreground = DarkNavy,
                    VerticalAlignment = VerticalAlignment.Center
                };
                headerStack.Children.Add(iconPath);
                headerStack.Children.Add(titleText);
                Grid.SetRow(headerStack, 0);
                rootGrid.Children.Add(headerStack);

                var msgText = new TextBlock
                {
                    Text = "You have been listening continuously for 2 hours. Consider taking a short break.",
                    FontSize = 13,
                    Foreground = SlateGray,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 18),
                    LineHeight = 18
                };
                Grid.SetRow(msgText, 1);
                rootGrid.Children.Add(msgText);

                var card = new Border
                {
                    Background = CardBg,
                    BorderBrush = CardBorder,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(16, 12, 16, 12),
                    Margin = new Thickness(0, 0, 0, 20)
                };
                var cardGrid = new Grid();
                cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var durationCol = new StackPanel();
                durationCol.Children.Add(new TextBlock { Text = "SESSION DURATION", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = SlateGray, Margin = new Thickness(0, 0, 0, 4) });
                durationCol.Children.Add(new TextBlock { Text = $"{elapsed:hh\\:mm\\:ss}", FontSize = 15, FontWeight = FontWeights.ExtraBold, Foreground = DarkNavy });
                Grid.SetColumn(durationCol, 0);
                cardGrid.Children.Add(durationCol);

                var volumeCol = new StackPanel();
                volumeCol.Children.Add(new TextBlock { Text = "CURRENT VOLUME", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = SlateGray, Margin = new Thickness(0, 0, 0, 4) });
                volumeCol.Children.Add(new TextBlock { Text = $"{currentVolume}% / 80%", FontSize = 15, FontWeight = FontWeights.ExtraBold, Foreground = currentVolume > 80 ? RedText : (currentVolume > 75 ? AmberText : GreenText) });
                Grid.SetColumn(volumeCol, 1);
                cardGrid.Children.Add(volumeCol);

                card.Child = cardGrid;
                Grid.SetRow(card, 2);
                rootGrid.Children.Add(card);

                var btnGrid = new Grid();
                btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
                btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var continueBtn = new Button
                {
                    Content = "Continue Listening",
                    Height = 38,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Background = Brushes.White,
                    Foreground = DarkNavy,
                    BorderBrush = CardBorder,
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand
                };
                if (TryFindResource("PillButtonStyle") is Style pillStyle)
                {
                    continueBtn.Style = pillStyle;
                }
                continueBtn.Click += (s, args) =>
                {
                    lastReminderAckTime = DateTime.Now;
                    isReminderPopupOpen = false;
                    activeReminderWindow = null;
                    win.Close();
                    UpdateHearingSafetyIndicators();
                    AppendLog("Break reminder acknowledged. Extended for 120 minutes.", "SAFETY");
                };
                Grid.SetColumn(continueBtn, 0);
                btnGrid.Children.Add(continueBtn);

                var stopBtn = new Button
                {
                    Content = "Take a Break — Stop Audio",
                    Height = 38,
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Background = BrandBlue,
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand
                };
                if (TryFindResource("PrimaryPillButtonStyle") is Style primaryStyle)
                {
                    stopBtn.Style = primaryStyle;
                }
                stopBtn.Click += (s, args) =>
                {
                    isReminderPopupOpen = false;
                    activeReminderWindow = null;
                    win.Close();
                    StopButton_Click(this, new RoutedEventArgs());
                    AppendLog("User chose to take a break. DualSync audio stopped.", "SAFETY");
                };
                Grid.SetColumn(stopBtn, 2);
                btnGrid.Children.Add(stopBtn);

                Grid.SetRow(btnGrid, 3);
                rootGrid.Children.Add(btnGrid);

                win.Content = rootGrid;

                win.Closed += (s, args) =>
                {
                    if (isReminderPopupOpen)
                    {
                        lastReminderAckTime = DateTime.Now;
                        isReminderPopupOpen = false;
                        activeReminderWindow = null;
                        UpdateHearingSafetyIndicators();
                    }
                };

                win.Show();
            }
            catch (Exception ex)
            {
                isReminderPopupOpen = false;
                AppendLog($"Failed to display break reminder popup: {ex.Message}", "SAFETY");
            }
        }

        private void Device1ChimeButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(selectedDevice1Id))
            {
                PlaySyntheticChime(selectedDevice1Id, 523.25);
                AppendLog("Test Chime sent to Earbuds 1.", "SYNC");
            }
        }

        private void Device2ChimeButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(selectedDevice2Id))
            {
                PlaySyntheticChime(selectedDevice2Id, 659.25);
                AppendLog("Test Chime sent to Earbuds 2.", "SYNC");
            }
        }

        private void PlaySyntheticChime(string deviceId, double frequency)
        {
            Task.Run(() =>
            {
                try
                {
                    using var enumerator = new MMDeviceEnumerator();
                    var active = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
                    var device = active.FirstOrDefault(d => d.ID == deviceId);
                    if (device == null)
                    {
                        Dispatcher.Invoke(() => AppendLog("Test chime cancelled: Endpoint is not active.", "SYNC"));
                        return;
                    }

                    using var waveOut = new WasapiOut(device, AudioClientShareMode.Shared, false, 50);

                    int sampleRate = device.AudioClient.MixFormat.SampleRate;
                    if (sampleRate <= 0) sampleRate = 48000;

                    var format = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);
                    int sampleCount = sampleRate / 2;
                    var buffer = new float[sampleCount * 2];

                    for (int i = 0; i < sampleCount; i++)
                    {
                        float sample = (float)(Math.Sin(2 * Math.PI * frequency * i / (double)sampleRate) * Math.Exp(-4.0 * i / (double)sampleCount));
                        buffer[i * 2] = sample * 0.3f;
                        buffer[i * 2 + 1] = sample * 0.3f;
                    }

                    var stream = new MemoryStream();
                    using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
                    {
                        foreach (var f in buffer) writer.Write(f);
                    }
                    stream.Position = 0;

                    using var rawStream = new RawSourceWaveStream(stream, format);
                    waveOut.Init(rawStream);
                    waveOut.Play();
                    while (waveOut.PlaybackState == PlaybackState.Playing)
                    {
                        System.Threading.Thread.Sleep(50);
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => AppendLog($"Chime playback fault: {ex.Message}", "SYNC"));
                }
            });
        }

        private static string FormatDelayValue(double delayMs)
        {
            if (Math.Abs(delayMs) < 0.05)
            {
                return "0.0 ms";
            }
            return delayMs > 0 ? $"+{delayMs:0.0} ms" : $"{delayMs:0.0} ms";
        }

        public void UpdateConfiguredDelay(double delayMs, bool syncSlider = true)
        {
            double clamped = Math.Clamp(delayMs, -20.0, 20.0);
            configuredDelayMs = Math.Abs(clamped) < 0.05 ? 0.0 : clamped;

            if (syncSlider && DelayRangeSlider != null)
            {
                isUpdatingDelay = true;
                try
                {
                    if (Math.Abs(DelayRangeSlider.Value - configuredDelayMs) > 0.01)
                    {
                        DelayRangeSlider.Value = configuredDelayMs;
                    }
                }
                finally
                {
                    isUpdatingDelay = false;
                }
            }

            audioEngine?.SetPhaseDelay(configuredDelayMs);

            string formatted = FormatDelayValue(configuredDelayMs);

            if (DelayLabel != null) DelayLabel.Text = formatted;
            if (ProfileDelayText != null) ProfileDelayText.Text = formatted;
            if (DelayStatusBadge != null)
            {
                if (Math.Abs(configuredDelayMs) < 0.05)
                {
                    DelayStatusBadge.Text = "Neutral (0.0 ms)";
                }
                else if (configuredDelayMs > 0)
                {
                    DelayStatusBadge.Text = $"Earbuds 2 delayed +{configuredDelayMs:0.1} ms";
                }
                else
                {
                    DelayStatusBadge.Text = $"Earbuds 1 delayed +{-configuredDelayMs:0.1} ms";
                }
            }

            if (MetricDelayDisplay != null) MetricDelayDisplay.Text = formatted;
            if (PhaseBadgeText != null)
            {
                if (EchoTestCheckBox?.IsChecked == true)
                {
                    PhaseBadgeText.Text = "Audible Echo Test Active (+50ms offset)";
                    PhaseBadgeText.Foreground = AmberDot;
                }
                else
                {
                    PhaseBadgeText.Text = $"Configured Delay: {formatted}";
                    PhaseBadgeText.Foreground = GreenDot;
                }
            }

            if (SummaryDelayText != null) SummaryDelayText.Text = formatted;

            RenderWaveforms();
        }

        private void DelayRangeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (isUpdatingDelay || isInitializing) return;
            UpdateConfiguredDelay(e.NewValue, syncSlider: false);
        }

        private void QuickCalibrateButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateConfiguredDelay(0.0, syncSlider: true);
            AppendLog("Delay offset reset to neutral baseline (0.0 ms).", "SYNC");
            MessageBox.Show(
                "Delay offset reset to neutral baseline (0.0 ms).",
                "Reset Delay",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void EchoTestCheckBox_Click(object sender, RoutedEventArgs e)
        {
            bool isEcho = EchoTestCheckBox.IsChecked == true;
            audioEngine?.SetEchoTest(isEcho);

            if (isEcho)
            {
                PhaseBadgeText.Text = "Audible Echo Test Active (+50ms offset)";
                PhaseBadgeText.Foreground = AmberDot;
                if (JitterText != null) JitterText.Text = "Audible Echo Test Active (+50ms)";
                AppendLog("[ALERT] Audible Echo Test enabled: +50ms delay injected to Output 2.", "SYNC");
            }
            else
            {
                UpdateConfiguredDelay(configuredDelayMs, syncSlider: false);
                if (JitterText != null) JitterText.Text = "Visual Phase Representation";
                AppendLog("Audible Echo Test disabled: Restored configured delay.", "SYNC");
            }

            RenderWaveforms();
        }

        private void WaveZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (WaveZoomLabel != null)
            {
                WaveZoomLabel.Text = $"{(int)e.NewValue}%";
            }
            RenderWaveforms();
        }

        private void RenderWaveforms()
        {
            if (Page3_Reports == null || Page3_Reports.Visibility != Visibility.Visible) return;
            if (Wave1Path == null || Wave2Path == null || WaveCanvas == null) return;

            const double center1 = 37.0;
            const double center2 = 43.0;
            const double width = 1000.0;
            const double step = 4.0;

            if (audioEngine == null)
            {
                var idleG1 = new StreamGeometry();
                using (var ctx = idleG1.Open())
                {
                    ctx.BeginFigure(new Point(0, center1), false, false);
                    ctx.LineTo(new Point(width, center1), true, false);
                }
                idleG1.Freeze();
                Wave1Path.Data = idleG1;

                var idleG2 = new StreamGeometry();
                using (var ctx = idleG2.Open())
                {
                    ctx.BeginFigure(new Point(0, center2), false, false);
                    ctx.LineTo(new Point(width, center2), true, false);
                }
                idleG2.Freeze();
                Wave2Path.Data = idleG2;
                return;
            }

            audioEngine.GetWaveformTelemetry(waveTelemetry1, waveTelemetry2);

            float maxPeak = 0f;
            for (int i = 0; i < 256; i++)
            {
                float a1 = Math.Abs(waveTelemetry1[i]);
                if (a1 > maxPeak) maxPeak = a1;
                float a2 = Math.Abs(waveTelemetry2[i]);
                if (a2 > maxPeak) maxPeak = a2;
            }

            double zoomFactor = WaveZoomSlider != null ? WaveZoomSlider.Value / 100.0 : 1.0;
            if (zoomFactor < 0.3) zoomFactor = 0.3;

            double delayMs = configuredDelayMs;
            if (EchoTestCheckBox?.IsChecked == true)
            {
                delayMs += 50.0;
            }

            double vol1Factor = (Device1VolumeSlider != null ? Device1VolumeSlider.Value / 100.0 : 0.90);
            double vol2Factor = (Device2VolumeSlider != null ? Device2VolumeSlider.Value / 100.0 : 0.85);

            if (device1Muted) vol1Factor = 0.0;
            if (device2Muted) vol2Factor = 0.0;

            double baseZoomAmp = Math.Clamp(zoomFactor, 0.75, 1.25);
            double amplitude1 = 28.0 * baseZoomAmp * Math.Clamp(vol1Factor, 0.0, 1.0);
            double amplitude2 = 28.0 * baseZoomAmp * Math.Clamp(vol2Factor, 0.0, 1.0);

            var g1 = new StreamGeometry();
            var g2 = new StreamGeometry();

            if (maxPeak > 0.002f)
            {
                using (var ctx1 = g1.Open())
                {
                    double y0_1 = center1 - waveTelemetry1[0] * amplitude1;
                    ctx1.BeginFigure(new Point(0, y0_1), false, false);

                    for (double x = step; x <= width; x += step)
                    {
                        double normX = x / width;
                        int sIdx = Math.Clamp((int)((normX * 255.0) / zoomFactor), 0, 255);
                        double y = center1 - waveTelemetry1[sIdx] * amplitude1;
                        ctx1.LineTo(new Point(x, y), true, false);
                    }
                }

                using (var ctx2 = g2.Open())
                {
                    double y0_2 = center2 - waveTelemetry2[0] * amplitude2;
                    ctx2.BeginFigure(new Point(0, y0_2), false, false);

                    for (double x = step; x <= width; x += step)
                    {
                        double normX = x / width;
                        int sIdx = Math.Clamp((int)((normX * 255.0) / zoomFactor), 0, 255);
                        double y = center2 - waveTelemetry2[sIdx] * amplitude2;
                        ctx2.LineTo(new Point(x, y), true, false);
                    }
                }
            }
            else
            {
                double k = (2.0 * Math.PI / 260.0) / zoomFactor;
                double phaseShift = (delayMs / 18.0) * Math.PI;

                double carrierAmp1 = 14.0 * baseZoomAmp * Math.Clamp(vol1Factor, 0.1, 1.0);
                double carrierAmp2 = 14.0 * baseZoomAmp * Math.Clamp(vol2Factor, 0.1, 1.0);

                using (var ctx1 = g1.Open())
                {
                    double y0_1 = center1 + carrierAmp1 * (0.85 * Math.Sin(animationPhase) + 0.15 * Math.Sin(2.0 * animationPhase));
                    ctx1.BeginFigure(new Point(0, y0_1), false, false);
                    for (double x = step; x <= width; x += step)
                    {
                        double theta = k * x + animationPhase;
                        double y = center1 + carrierAmp1 * (0.85 * Math.Sin(theta) + 0.15 * Math.Sin(2.0 * theta));
                        ctx1.LineTo(new Point(x, y), true, false);
                    }
                }

                using (var ctx2 = g2.Open())
                {
                    double y0_2 = center2 + carrierAmp2 * (0.85 * Math.Sin(animationPhase - phaseShift) + 0.15 * Math.Sin(2.0 * (animationPhase - phaseShift)));
                    ctx2.BeginFigure(new Point(0, y0_2), false, false);
                    for (double x = step; x <= width; x += step)
                    {
                        double theta = k * x + animationPhase - phaseShift;
                        double y = center2 + carrierAmp2 * (0.85 * Math.Sin(theta) + 0.15 * Math.Sin(2.0 * theta));
                        ctx2.LineTo(new Point(x, y), true, false);
                    }
                }
            }

            g1.Freeze();
            Wave1Path.Data = g1;

            g2.Freeze();
            Wave2Path.Data = g2;
        }

        private void AssignNode1Btn_Click(object sender, RoutedEventArgs e)
        {
            if (Device1ComboBox.Items.Count > 0)
            {
                Device1ComboBox.SelectedIndex = 0;
                selectedDevice1Id = (Device1ComboBox.SelectedItem as MMDevice)?.ID;
                UpdateNodesDisplay();
                UpdateHostLaptopProfile();
                UpdateConsolidatedAuditTable();
                AppendLog("Slot 1 hardware set as primary Earbuds 1.", "CONNECTION");
                MessageBox.Show($"Earbuds 1 assigned: {(Device1ComboBox.SelectedItem as MMDevice)?.FriendlyName}", "Device Assignment", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void AssignNode2Btn_Click(object sender, RoutedEventArgs e)
        {
            if (Device2ComboBox.Items.Count > 1)
            {
                int altIdx = Device2ComboBox.Items.OfType<MMDevice>().ToList().FindIndex(d => d.ID != selectedDevice1Id);
                Device2ComboBox.SelectedIndex = altIdx >= 0 ? altIdx : 1;
                selectedDevice2Id = (Device2ComboBox.SelectedItem as MMDevice)?.ID;
                UpdateNodesDisplay();
                UpdateHostLaptopProfile();
                UpdateConsolidatedAuditTable();
                AppendLog("Slot 2 hardware set as secondary Earbuds 2.", "CONNECTION");
                MessageBox.Show($"Earbuds 2 assigned: {(Device2ComboBox.SelectedItem as MMDevice)?.FriendlyName}", "Device Assignment", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Only 1 audio output device is currently detected.\n\nPlease connect a second audio device (e.g. Bluetooth earbuds) to assign Slot 2.", "DualSync", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        public void AppendLog(string message, string category = "GENERAL")
        {
            string entry = $"[{DateTime.Now:HH:mm:ss.fff}] [{category}] {message}";
            fullSessionLogs.Add(entry);
            if (fullSessionLogs.Count > 500)
            {
                fullSessionLogs.RemoveRange(0, 100);
            }
            FilterAndRenderLogs();
        }

        private void EventFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            FilterAndRenderLogs();
        }

        private void FilterAndRenderLogs()
        {
            if (SessionLogBox == null || EventFilterComboBox == null) return;

            string filter = (EventFilterComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Events";

            var filtered = fullSessionLogs.Where(log =>
            {
                if (filter == "All Events") return true;
                if (filter == "Sync Events" && log.Contains("[SYNC]")) return true;
                if (filter == "Volume" && log.Contains("[VOLUME]")) return true;
                if (filter == "Connections" && log.Contains("[CONNECTION]")) return true;
                return false;
            });

            SessionLogBox.Text = string.Join(Environment.NewLine, filtered);
            SessionLogBox.ScrollToEnd();
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            fullSessionLogs.Clear();
            if (SessionLogBox != null) SessionLogBox.Clear();
            AppendLog("Session activity logs cleared.", "GENERAL");
        }

        private void UpdateConsolidatedAuditTable()
        {
            consolidatedAuditRows.Clear();
            string now = DateTime.Now.ToString("HH:mm:ss.fff");

            var d1 = Device1ComboBox.SelectedItem as MMDevice;
            var d2 = Device2ComboBox.SelectedItem as MMDevice;

            if (SummaryEngineStateText != null)
            {
                string state = audioEngine != null ? "Dual Output" : "Stopped";
                SummaryEngineStateText.Text = state;
                SummaryEngineStateText.Foreground = audioEngine != null ? GreenText : Brushes.Gray;
            }
            if (SummaryEngineDot != null)
            {
                SummaryEngineDot.Fill = audioEngine != null ? GreenDot : Brushes.Gray;
            }
            if (EngineConditionTitle != null && EngineConditionDetail != null && EngineConditionBox != null)
            {
                if (audioEngine != null)
                {
                    EngineConditionTitle.Text = "Audio Engine Stable";
                    EngineConditionTitle.Foreground = GreenText;
                    EngineConditionDetail.Text = "Capturing and streaming normally";
                    EngineConditionDetail.Foreground = GreenText;
                    EngineConditionBox.Background = GreenBg;
                    EngineConditionBox.BorderBrush = GreenBorder;
                    if (EngineConditionIcon != null) EngineConditionIcon.Stroke = GreenDot;
                }
                else
                {
                    EngineConditionTitle.Text = "Audio Engine Standby";
                    EngineConditionTitle.Foreground = SlateGray;
                    EngineConditionDetail.Text = "Ready to start dual audio stream";
                    EngineConditionDetail.Foreground = SlateGray;
                    EngineConditionBox.Background = CardBg;
                    EngineConditionBox.BorderBrush = CardBorder;
                    if (EngineConditionIcon != null) EngineConditionIcon.Stroke = SlateGray;
                }
            }

            if (SummaryDeviceCountText != null)
            {
                int count = discoveredDeviceItems.Count > 0 ? discoveredDeviceItems.Count : (Device1ComboBox?.Items.Count ?? 0);
                SummaryDeviceCountText.Text = $"{count} Devices";
            }
            if (ReportEndpointsItemsControl != null)
            {
                ReportEndpointsItemsControl.ItemsSource = null;
                ReportEndpointsItemsControl.ItemsSource = discoveredDeviceItems;
            }

            if (SummaryDelayText != null)
            {
                SummaryDelayText.Text = FormatDelayValue(configuredDelayMs);
            }
            if (SummaryDurationText != null)
            {
                SummaryDurationText.Text = StreamDurationText?.Text ?? "00:00:00";
            }
            if (SummarySyncBadge != null && SummarySyncBadgeText != null)
            {
                bool active = audioEngine != null;
                SummarySyncBadgeText.Text = active ? "Active" : "Standby";
                SummarySyncBadgeText.Foreground = active ? GreenText : SlateGray;
                SummarySyncBadge.Background = active ? GreenBg : CardBg;
                SummarySyncBadge.BorderBrush = active ? GreenBorder : CardBorder;
            }

            consolidatedAuditRows.Add(new TelemetryRow(now, "Host Environment", "Machine Name & OS", $"{Environment.MachineName} ({Environment.OSVersion.VersionString})", "Active", "Running 64-bit .NET 8 WPF Host"));

            string engineState = audioEngine != null ? "Active Broadcasting (DualStream)" : "Standby (Idle)";
            consolidatedAuditRows.Add(new TelemetryRow(now, "DualStream Core", "Engine Run State", engineState, audioEngine != null ? "Broadcasting" : "Standby", "WASAPI Loopback system capture"));

            int devCount = discoveredDeviceItems.Count > 0 ? discoveredDeviceItems.Count : (Device1ComboBox?.Items.Count ?? 0);
            consolidatedAuditRows.Add(new TelemetryRow(now, "Audio Subsystem", "Detected Audio Endpoints", $"{devCount} Output Endpoints", "Enumerated", "Active Windows CoreAudio render endpoints"));

            string d1Name = d1?.FriendlyName ?? "Unassigned";
            string dev1Format = "Unavailable (Not initialized)";
            if (d1 != null)
            {
                try { var fmt = d1.AudioClient.MixFormat; dev1Format = $"{fmt.SampleRate} Hz / {fmt.BitsPerSample}-bit ({fmt.Channels} ch)"; } catch { }
            }
            string d1State = d1 != null ? (audioEngine != null ? (Device1StatusText?.Text ?? "Connected") : "Assigned") : "Unassigned";
            consolidatedAuditRows.Add(new TelemetryRow(now, "Output Pipeline 1", "Earbuds 1 (Primary)", d1Name, d1State, $"Gain: {(int)Device1VolumeSlider.Value}% | Muted: {device1Muted} | Mix: {dev1Format}"));

            string d2Name = d2?.FriendlyName ?? "Unassigned";
            string dev2Format = "Unavailable (Not initialized)";
            if (d2 != null)
            {
                try { var fmt = d2.AudioClient.MixFormat; dev2Format = $"{fmt.SampleRate} Hz / {fmt.BitsPerSample}-bit ({fmt.Channels} ch)"; } catch { }
            }
            string d2State = d2 != null ? (audioEngine != null ? (Device2StatusText?.Text ?? "Connected") : "Assigned") : "Unassigned";
            consolidatedAuditRows.Add(new TelemetryRow(now, "Output Pipeline 2", "Earbuds 2 (Secondary)", d2Name, d2State, $"Gain: {(int)Device2VolumeSlider.Value}% | Muted: {device2Muted} | Mix: {dev2Format}"));

            consolidatedAuditRows.Add(new TelemetryRow(now, "Synchronization", "Configured Audio Phase Delay", FormatDelayValue(configuredDelayMs), "User Configured", "Circular FIFO buffer delay offset (-20 ms to +20 ms)"));

            int v1 = (int)Device1VolumeSlider.Value;
            int v2 = (int)Device2VolumeSlider.Value;
            int maxV = Math.Max(v1, v2);
            string safetyState = maxV > 80 ? "High Volume" : (maxV > 75 ? "Near Limit" : "Verified Safe");
            consolidatedAuditRows.Add(new TelemetryRow(now, "Hearing Safety", "Real-time Safety Monitor", $"Safe Volume ({v1}% & {v2}%)", safetyState, "Safety threshold: 80% volume / 120 min break reminder"));

            string duration = StreamDurationText?.Text ?? "00:00:00";
            consolidatedAuditRows.Add(new TelemetryRow(now, "Session Monitor", "Broadcast Stream Duration", duration, audioEngine != null ? "Streaming" : "Idle", "Active elapsed session time"));

            string lastLog = fullSessionLogs.LastOrDefault() ?? "Session started";
            consolidatedAuditRows.Add(new TelemetryRow(now, "Event Log", "Recent Diagnostic Trace", lastLog, "Recorded", "Last recorded application event"));

            if (ConsolidatedTelemetryGrid != null)
            {
                ConsolidatedTelemetryGrid.ItemsSource = null;
                ConsolidatedTelemetryGrid.ItemsSource = consolidatedAuditRows;
            }
        }

        private void BtnPrintReport_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                UpdateConsolidatedAuditTable();
                string reportContent = GenerateFullDiagnosticReport();

                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DualSync");
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, $"DualSync_Diagnostic_Summary_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                File.WriteAllText(path, reportContent);

                PrintDialog printDialog = new PrintDialog();
                if (printDialog.ShowDialog() == true)
                {
                    FlowDocument doc = CreatePrintableDocument(reportContent);
                    IDocumentPaginatorSource dps = doc;
                    printDialog.PrintDocument(dps.DocumentPaginator, "DualSync Diagnostic Summary");
                }

                MessageBox.Show($"Summary Report compiled and saved successfully at:\n{path}", "Report Exported", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to generate report: {ex.Message}", "Print/Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string GenerateFullDiagnosticReport()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("==========================================================================");
            sb.AppendLine("              DUALSYNC • SYSTEM TELEMETRY & DIAGNOSTIC REPORT            ");
            sb.AppendLine("==========================================================================");
            sb.AppendLine($"Generated On     : {DateTime.Now:yyyy-MM-dd HH:mm:ss} Local");
            sb.AppendLine($"Host Machine     : {Environment.MachineName}");
            sb.AppendLine($"Operating System : {Environment.OSVersion}");
            sb.AppendLine($"Session State    : {(audioEngine != null ? "Active Broadcasting (DualStream)" : "Standby Mode")}");
            sb.AppendLine($"Stream Duration  : {StreamDurationText?.Text ?? "00:00:00"}");
            sb.AppendLine($"Battery Status   : {GetHostBatteryStatus()}");
            sb.AppendLine("--------------------------------------------------------------------------");
            sb.AppendLine("AUDIO HARDWARE ENDPOINTS:");
            sb.AppendLine($"  • Primary Node (Earbuds 1)   : {(Device1ComboBox.SelectedItem as MMDevice)?.FriendlyName ?? "Unassigned"}");
            sb.AppendLine($"    Volume Gain                : {(int)Device1VolumeSlider.Value}% | Muted: {device1Muted}");
            sb.AppendLine($"  • Secondary Node (Earbuds 2) : {(Device2ComboBox.SelectedItem as MMDevice)?.FriendlyName ?? "Unassigned"}");
            sb.AppendLine($"    Volume Gain                : {(int)Device2VolumeSlider.Value}% | Muted: {device2Muted}");
            sb.AppendLine($"  • Audio Phase Delay Offset   : {FormatDelayValue(configuredDelayMs)} (Configured circular buffer delay)");
            sb.AppendLine("--------------------------------------------------------------------------");
            sb.AppendLine("HEARING SAFETY AUDIT:");
            int maxV = Math.Max((int)Device1VolumeSlider.Value, (int)Device2VolumeSlider.Value);
            string safeText = maxV > 80 ? "HIGH VOLUME ALERT (Limit 80% Exceeded)" : (maxV > 75 ? "NEAR LIMIT (75%-80%)" : "SAFE VOLUME (<= 80%)");
            sb.AppendLine($"  • Earbuds 1 Safety State     : {(int)Device1VolumeSlider.Value}% / 80% Threshold");
            sb.AppendLine($"  • Earbuds 2 Safety State     : {(int)Device2VolumeSlider.Value}% / 80% Threshold");
            sb.AppendLine($"  • Overall Hearing Condition  : {safeText}");
            sb.AppendLine($"  • Listening Duration         : {StreamDurationText?.Text ?? "00:00:00"} (Recommended break every 120 min)");
            sb.AppendLine("--------------------------------------------------------------------------");
            sb.AppendLine("TELEMETRY MATRIX (REAL PARAMETERS):");
            foreach (var r in consolidatedAuditRows)
            {
                sb.AppendLine($"[{r.Timestamp}] [{r.Source}] {r.Metric}: {r.Value} ({r.State}) -> {r.Notes}");
            }
            sb.AppendLine("--------------------------------------------------------------------------");
            sb.AppendLine("LIVE SESSION AUDIT TRACES:");
            if (fullSessionLogs.Count == 0)
            {
                sb.AppendLine("No runtime logs recorded during this session.");
            }
            else
            {
                foreach (var log in fullSessionLogs)
                {
                    sb.AppendLine(log);
                }
            }
            sb.AppendLine("==========================================================================");
            sb.AppendLine("        END OF DIAGNOSTIC REPORT • ZERO ACOUSTIC AUDIO RETENTION          ");
            sb.AppendLine("==========================================================================");
            return sb.ToString();
        }

        private FlowDocument CreatePrintableDocument(string content)
        {
            FlowDocument doc = new FlowDocument
            {
                PagePadding = new Thickness(40),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 10,
                ColumnWidth = double.PositiveInfinity
            };

            Paragraph p = new Paragraph(new Run(content));
            doc.Blocks.Add(p);
            return doc;
        }

        private void ConnectionStatusTimer_Tick(object? sender, EventArgs e)
        {
            UpdateConnectionStatusAsync();
        }

        private async void UpdateConnectionStatusAsync()
        {
            if (audioEngine == null || isCheckingConnection) return;
            isCheckingConnection = true;

            try
            {
                bool d1 = await Task.Run(() => audioEngine?.IsDevice1Connected() ?? false);
                bool d2 = await Task.Run(() => audioEngine?.IsDevice2Connected() ?? false);

                SetDeviceConnectionStatus(Device1StatusDot, Device1StatusText, d1 ? "Connected" : "Disconnected", d1 ? GreenDot : RedDot);
                SetDeviceConnectionStatus(Device2StatusDot, Device2StatusText, d2 ? "Connected" : "Disconnected", d2 ? GreenDot : RedDot);

                int activeCount = (d1 ? 1 : 0) + (d2 ? 1 : 0);
                if (ActiveEndpointsSummaryText != null)
                {
                    ActiveEndpointsSummaryText.Text = $"{activeCount} / 2 Active";
                }

                if (d1 && d2)
                {
                    SetStatus("Running", GreenDot);
                    UpdateSyncModeDisplay("Dual Output", "DualStream active on 2 endpoints", GreenText);
                }
                else if (d1 || d2)
                {
                    SetStatus(d1 ? "Earbuds 2 disconnected" : "Earbuds 1 disconnected", AmberDot);
                    UpdateSyncModeDisplay("Single Output", d1 ? "Earbuds 1 active (Earbuds 2 offline)" : "Earbuds 2 active (Earbuds 1 offline)", AmberText);
                }
                else
                {
                    SetStatus("Both devices disconnected", RedDot);
                    UpdateSyncModeDisplay("Reconnecting", "Awaiting endpoint recovery", RedText);
                }
            }
            catch
            {
            }
            finally
            {
                isCheckingConnection = false;
            }
        }

        private void SetDeviceConnectionStatus(System.Windows.Shapes.Ellipse dot, TextBlock text, string status, Brush color)
        {
            if (dot != null) dot.Fill = color;
            if (text != null) text.Text = status;
        }

        private void SetStatus(string text, Brush color)
        {
            if (StatusText != null) StatusText.Text = text;
            if (StatusDot != null) StatusDot.Fill = color;
            if (HeaderStatusText != null) HeaderStatusText.Text = $"DualStream • {text}";
            if (HeaderStatusDot != null) HeaderStatusDot.Fill = color;
            if (FooterStatusText != null) FooterStatusText.Text = $"● {text} • v1.0.0";
            if (StatusDetailText != null)
            {
                if (text == "Running")
                {
                    StatusDetailText.Text = "Dual audio streaming active • Both earbuds connected";
                }
                else if (text == "Ready")
                {
                    StatusDetailText.Text = "Dual audio streaming idle • Standby";
                }
                else if (text == "Refreshed")
                {
                    StatusDetailText.Text = "Hardware audio endpoints re-enumerated";
                }
                else
                {
                    StatusDetailText.Text = text;
                }
            }
        }

        private void LoadConfiguration()
        {
            try
            {
                string configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DualSync", "config.json");
                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
                    var cfg = JsonSerializer.Deserialize<AppConfig>(json);
                    if (cfg != null)
                    {
                        if (cfg.Vol1 > 0) Device1VolumeSlider.Value = cfg.Vol1;
                        if (cfg.Vol2 > 0) Device2VolumeSlider.Value = cfg.Vol2;
                        configuredDelayMs = Math.Clamp(cfg.DelayMs, -20.0, 20.0);
                        LoadAudioDevices(cfg.Dev1Id, cfg.Dev2Id);
                        return;
                    }
                }
            }
            catch { }
            LoadAudioDevices();
        }

        private void SaveConfiguration()
        {
            try
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DualSync");
                Directory.CreateDirectory(folder);
                string configPath = Path.Combine(folder, "config.json");

                var cfg = new AppConfig
                {
                    Dev1Id = selectedDevice1Id,
                    Dev2Id = selectedDevice2Id,
                    Vol1 = Device1VolumeSlider.Value,
                    Vol2 = Device2VolumeSlider.Value,
                    DelayMs = configuredDelayMs
                };
                File.WriteAllText(configPath, JsonSerializer.Serialize(cfg));
            }
            catch { }
        }

        protected override void OnClosed(EventArgs e)
        {
            waveformTimer?.Stop();
            refreshFeedbackTimer?.Stop();
            SaveConfiguration();
            StopButton_Click(this, new RoutedEventArgs());
            base.OnClosed(e);
        }

        public class DiscoveredAudioDeviceItem
        {
            public string Id { get; set; } = string.Empty;
            public string FriendlyName { get; set; } = string.Empty;
            public string DeviceType { get; set; } = "Audio Output";
            public string DeviceIcon { get; set; } = "🎧";
            public string Status { get; set; } = "Active Endpoint";
            public string SlotAssignment { get; set; } = "Unassigned";
            public Brush SlotBadgeForeground { get; set; } = SlateGray;
            public Brush SlotBadgeBackground { get; set; } = CardBg;
            public Brush SlotBadgeBorder { get; set; } = CardBorder;
            public Brush StatusDotBrush { get; set; } = GreenDot;
            public MMDevice Device { get; set; } = null!;
        }

        public class TelemetryRow
        {
            public string Timestamp { get; set; }
            public string Source { get; set; }
            public string Metric { get; set; }
            public string Value { get; set; }
            public string State { get; set; }
            public string Notes { get; set; }
            public Brush StateBrush { get; set; }

            public TelemetryRow(string t, string s, string m, string v, string st, string n, Brush? brush = null)
            {
                Timestamp = t;
                Source = s;
                Metric = m;
                Value = v;
                State = st;
                Notes = n;
                StateBrush = brush ?? GetDefaultStateBrush(st);
            }

            private static Brush GetDefaultStateBrush(string st)
            {
                if (st.Contains("Active", StringComparison.OrdinalIgnoreCase) ||
                    st.Contains("Broadcasting", StringComparison.OrdinalIgnoreCase) ||
                    st.Contains("Connected", StringComparison.OrdinalIgnoreCase) ||
                    st.Contains("Verified Safe", StringComparison.OrdinalIgnoreCase) ||
                    st.Contains("Streaming", StringComparison.OrdinalIgnoreCase))
                {
                    return GreenDot;
                }
                if (st.Contains("Enumerated", StringComparison.OrdinalIgnoreCase) ||
                    st.Contains("User Configured", StringComparison.OrdinalIgnoreCase) ||
                    st.Contains("Recorded", StringComparison.OrdinalIgnoreCase))
                {
                    return BrandBlue;
                }
                if (st.Contains("Near", StringComparison.OrdinalIgnoreCase) ||
                    st.Contains("Warning", StringComparison.OrdinalIgnoreCase) ||
                    st.Contains("Break", StringComparison.OrdinalIgnoreCase))
                {
                    return AmberDot;
                }
                if (st.Contains("Disconnected", StringComparison.OrdinalIgnoreCase) ||
                    st.Contains("High", StringComparison.OrdinalIgnoreCase) ||
                    st.Contains("Error", StringComparison.OrdinalIgnoreCase))
                {
                    return RedDot;
                }
                return SlateGray;
            }
        }

        private class AppConfig
        {
            public string? Dev1Id { get; set; }
            public string? Dev2Id { get; set; }
            public double Vol1 { get; set; }
            public double Vol2 { get; set; }
            public double DelayMs { get; set; }
        }
    }
}
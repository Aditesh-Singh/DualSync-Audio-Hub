using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using DualSync.Audio;

namespace DualSync
{
    public partial class MainWindow : Window
    {
        private DualAudioEngine? audioEngine;

        private MMDevice? laptopSpeaker;
        private bool speakerWasMuted;

        private bool device1Muted;
        private bool device2Muted;

        private double device1PreviousVolume = 90;
        private double device2PreviousVolume = 85;

        private string? selectedDevice1Id;
        private string? selectedDevice2Id;

        private readonly DispatcherTimer connectionStatusTimer;
        private readonly DispatcherTimer streamDurationTimer;
        private readonly DispatcherTimer waveformTimer;
        private DispatcherTimer? refreshFeedbackTimer;
        private double animationPhase;
        private DateTime streamStartTime;
        private bool isCheckingConnection;

        private readonly List<string> fullSessionLogs = new();
        private readonly List<TelemetryRow> consolidatedAuditRows = new();

        public MainWindow()
        {
            InitializeComponent();

            UpdateHostLaptopProfile();

            Device1ComboBox.SelectionChanged += DeviceComboBox_SelectionChanged;
            Device2ComboBox.SelectionChanged += DeviceComboBox_SelectionChanged;

            LoadAudioDevices();
            LoadConfiguration();
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
                    StreamDurationText.Text = elapsed.ToString(@"hh\:mm\:ss");
                }
            };

            waveformTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            waveformTimer.Tick += (s, e) =>
            {
                animationPhase += 0.08;
                RenderWaveforms();
            };
            waveformTimer.Start();

            RenderWaveforms();

            AppendLog("DualSync v1.0 initialized. DualStream Core online.", "SYNC");
        }

        // =========================================================
        // NAVIGATION SWITCHING
        // =========================================================

        private void NavConsoleRadio_Checked(object sender, RoutedEventArgs e)
        {
            HideAllPages();
            if (Page1_Console != null) Page1_Console.Visibility = Visibility.Visible;
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
            if (Page3_Reports != null) Page3_Reports.Visibility = Visibility.Visible;
        }

        private void NavExportRadio_Checked(object sender, RoutedEventArgs e)
        {
            HideAllPages();
            if (Page4_ExportSummary != null)
            {
                Page4_ExportSummary.Visibility = Visibility.Visible;
                UpdateConsolidatedAuditTable();
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

        // =========================================================
        // DYNAMIC HOST LAPTOP PROFILE
        // =========================================================

        private void UpdateHostLaptopProfile()
        {
            try
            {
                string machine = Environment.MachineName;

                if (TopLaptopNameText != null) TopLaptopNameText.Text = machine;
                if (TopHoverLaptopName != null) TopHoverLaptopName.Text = machine;
                if (ProfileLaptopNameText != null) ProfileLaptopNameText.Text = machine;

                var power = System.Windows.Forms.SystemInformation.PowerStatus;
                int percent = (int)(power.BatteryLifePercent * 100);
                string status = power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online ? "Plugged In" : "Discharging";
                string batteryFormatted = percent >= 0 ? $"{percent}% ({status})" : "AC Power";

                if (ProfileBatteryText != null) ProfileBatteryText.Text = batteryFormatted;
                if (TopBatteryText != null) TopBatteryText.Text = batteryFormatted;

                int activeCount = (selectedDevice1Id != null ? 1 : 0) + (selectedDevice2Id != null ? 1 : 0);
                string nodeInfo = $"{activeCount} Active Sync";

                if (ProfileDevicesCountText != null) ProfileDevicesCountText.Text = nodeInfo;
                if (TopDevicesCountText != null) TopDevicesCountText.Text = nodeInfo;

                if (ProfileSignalStrengthText != null) ProfileSignalStrengthText.Text = "Unavailable (WASAPI)";
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

        // =========================================================
        // DEVICE ENUMERATION & SAFE SELECTION
        // =========================================================

        private void DeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender == Device1ComboBox && Device1ComboBox.SelectedItem is MMDevice d1)
            {
                selectedDevice1Id = d1.ID;
                if (selectedDevice1Id == selectedDevice2Id)
                {
                    // Prevent selecting the same device twice
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
                    // Prevent selecting the same device twice
                    var alternate = Device1ComboBox.Items.OfType<MMDevice>().FirstOrDefault(d => d.ID != d2.ID);
                    Device1ComboBox.SelectedItem = alternate;
                    selectedDevice1Id = alternate?.ID;
                    AppendLog("Both slots cannot use the same device. Earbuds 1 selection updated.", "CONNECTION");
                }
                AppendLog($"Slot 2 mapped: {d2.FriendlyName}", "CONNECTION");
            }

            UpdateNodesDisplay();
            UpdateHostLaptopProfile();
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
                    try
                    {
                        var fmt = d1.AudioClient.MixFormat;
                        Slot1DetailsText.Text = $"Format: {fmt.SampleRate}Hz {fmt.BitsPerSample}-bit ({fmt.Channels}ch) • Codec/RSSI: Unavailable via WASAPI";
                    }
                    catch
                    {
                        Slot1DetailsText.Text = "Endpoint active • Codec/RSSI: Unavailable via WASAPI";
                    }
                }
                else
                {
                    Slot1DetailsText.Text = "Hardware format query: Standby";
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
                    try
                    {
                        var fmt = d2.AudioClient.MixFormat;
                        Slot2DetailsText.Text = $"Format: {fmt.SampleRate}Hz {fmt.BitsPerSample}-bit ({fmt.Channels}ch) • Codec/RSSI: Unavailable via WASAPI";
                    }
                    catch
                    {
                        Slot2DetailsText.Text = "Endpoint active • Codec/RSSI: Unavailable via WASAPI";
                    }
                }
                else
                {
                    Slot2DetailsText.Text = "Hardware format query: Standby";
                }
            }
        }

        private void LoadAudioDevices(string? prev1 = null, string? prev2 = null)
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

                if (!string.IsNullOrWhiteSpace(prev1))
                {
                    int idx = devices.FindIndex(d => d.ID == prev1);
                    if (idx >= 0) Device1ComboBox.SelectedIndex = idx;
                }

                if (!string.IsNullOrWhiteSpace(prev2))
                {
                    int idx = devices.FindIndex(d => d.ID == prev2);
                    if (idx >= 0) Device2ComboBox.SelectedIndex = idx;
                }

                // If no previous preferences, default to first two distinct devices
                if (string.IsNullOrWhiteSpace(prev1) && string.IsNullOrWhiteSpace(prev2))
                {
                    if (devices.Count >= 2)
                    {
                        Device1ComboBox.SelectedIndex = 0;
                        Device2ComboBox.SelectedIndex = 1;
                    }
                    else if (devices.Count == 1)
                    {
                        Device1ComboBox.SelectedIndex = 0;
                    }
                }

                UpdateNodesDisplay();
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

            // Feedback: Show "Refreshed" where "Ready / Running" is displayed
            SetStatus("Refreshed", (SolidColorBrush)new BrushConverter().ConvertFrom("#10B981")!);

            refreshFeedbackTimer?.Stop();
            refreshFeedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            refreshFeedbackTimer.Tick += (s, args) =>
            {
                refreshFeedbackTimer.Stop();
                string status = audioEngine != null ? "Running" : "Ready";
                Brush brush = audioEngine != null ? (SolidColorBrush)new BrushConverter().ConvertFrom("#10B981")! : Brushes.Gray;
                SetStatus(status, brush);
            };
            refreshFeedbackTimer.Start();
        }

        // =========================================================
        // START / STOP AUDIO ENGINE
        // =========================================================

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(selectedDevice1Id) || string.IsNullOrWhiteSpace(selectedDevice2Id))
                {
                    MessageBox.Show("Please select two distinct audio devices before starting DualSync.", "DualSync", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (selectedDevice1Id == selectedDevice2Id)
                {
                    MessageBox.Show("DualSync requires two different audio output devices. Both slots cannot use the same device.", "DualSync", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                using var enumerator = new MMDeviceEnumerator();
                var dev1 = enumerator.GetDevice(selectedDevice1Id);
                var dev2 = enumerator.GetDevice(selectedDevice2Id);

                // Check default speaker muting only if it is NOT one of the selected playback devices
                laptopSpeaker = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                    .FirstOrDefault(d => d.FriendlyName.Contains("Realtek", StringComparison.OrdinalIgnoreCase));

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

                // Apply current volume and phase delay settings
                audioEngine.SetVolume1((float)(Device1VolumeSlider.Value / 100.0));
                audioEngine.SetVolume2((float)(Device2VolumeSlider.Value / 100.0));
                audioEngine.SetPhaseDelay(DelayRangeSlider.Value);
                if (EchoTestCheckBox.IsChecked == true)
                {
                    audioEngine.SetEchoTest(true);
                }

                StartButton.IsEnabled = false;
                StopButton.IsEnabled = true;
                if (RefreshButton != null) RefreshButton.IsEnabled = false;
                if (ScanBtn != null) ScanBtn.IsEnabled = false;

                streamStartTime = DateTime.Now;
                streamDurationTimer.Start();
                connectionStatusTimer.Start();

                SetStatus("Running", Brushes.Green);
                AppendLog("DualStream synchronization broadcast started.", "SYNC");
                UpdateConnectionStatusAsync();
                UpdateHostLaptopProfile();
                UpdateConsolidatedAuditTable();
            }
            catch (Exception ex)
            {
                audioEngine?.Dispose();
                audioEngine = null;
                RestoreLaptopSpeaker();
                SetStatus("Error", Brushes.Red);
                AppendLog($"Start failed: {ex.Message}", "SYNC");
                MessageBox.Show($"Start failed: {ex.Message}", "DualSync Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                connectionStatusTimer.Stop();
                streamDurationTimer.Stop();

                audioEngine?.Dispose();
                audioEngine = null;

                RestoreLaptopSpeaker();

                StartButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                if (RefreshButton != null) RefreshButton.IsEnabled = true;
                if (ScanBtn != null) ScanBtn.IsEnabled = true;

                SetDeviceConnectionStatus(Device1StatusDot, Device1StatusText, "Not connected", Brushes.Gray);
                SetDeviceConnectionStatus(Device2StatusDot, Device2StatusText, "Not connected", Brushes.Gray);

                SetStatus("Ready", Brushes.Gray);
                AppendLog("Audio broadcast stopped. Returned to standby.", "SYNC");
                UpdateConsolidatedAuditTable();
            }
            catch (Exception ex)
            {
                SetStatus("Stop Error", Brushes.Red);
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

        // =========================================================
        // VOLUME & MUTE CONTROLS
        // =========================================================

        private void Device1VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (audioEngine != null && !device1Muted)
            {
                audioEngine.SetVolume1((float)(e.NewValue / 100.0));
            }
            device1PreviousVolume = e.NewValue;
            AppendLog($"Node 1 Gain adjusted: {(int)e.NewValue}%", "VOLUME");
            RenderWaveforms();
        }

        private void Device2VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (audioEngine != null && !device2Muted)
            {
                audioEngine.SetVolume2((float)(e.NewValue / 100.0));
            }
            device2PreviousVolume = e.NewValue;
            AppendLog($"Node 2 Gain adjusted: {(int)e.NewValue}%", "VOLUME");
            RenderWaveforms();
        }

        private void Device1MuteButton_Click(object sender, RoutedEventArgs e)
        {
            if (audioEngine == null) return;
            device1Muted = !device1Muted;

            if (device1Muted)
            {
                audioEngine.SetMute1(true);
                Device1MuteButton.Content = "Unmute";
                AppendLog("Node 1 Muted.", "VOLUME");
            }
            else
            {
                audioEngine.SetMute1(false);
                audioEngine.SetVolume1((float)(device1PreviousVolume / 100.0));
                Device1MuteButton.Content = "Mute";
                AppendLog("Node 1 Unmuted.", "VOLUME");
            }
            RenderWaveforms();
        }

        private void Device2MuteButton_Click(object sender, RoutedEventArgs e)
        {
            if (audioEngine == null) return;
            device2Muted = !device2Muted;

            if (device2Muted)
            {
                audioEngine.SetMute2(true);
                Device2MuteButton.Content = "Unmute";
                AppendLog("Node 2 Muted.", "VOLUME");
            }
            else
            {
                audioEngine.SetMute2(false);
                audioEngine.SetVolume2((float)(device2PreviousVolume / 100.0));
                Device2MuteButton.Content = "Mute";
                AppendLog("Node 2 Unmuted.", "VOLUME");
            }
            RenderWaveforms();
        }

        // =========================================================
        // TEST CHIMES (FIXED STREAM & RATE MATCHING)
        // =========================================================

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
                    using var device = enumerator.GetDevice(deviceId);
                    using var waveOut = new WasapiOut(device, AudioClientShareMode.Shared, false, 50);

                    // Match device mix format sample rate to prevent UnsupportedFormat exceptions
                    int sampleRate = device.AudioClient.MixFormat.SampleRate;
                    if (sampleRate <= 0) sampleRate = 48000;

                    var format = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);
                    int sampleCount = sampleRate / 2; // 0.5s chime
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

        // =========================================================
        // REAL LATENCY CALIBRATION & ECHO TEST
        // =========================================================

        private void DelayRangeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            double val = e.NewValue;

            // Apply real physical audio timing delay
            audioEngine?.SetPhaseDelay(val);

            if (DelayLabel != null)
            {
                DelayLabel.Text = $"{(val >= 0 ? "+" : "")}{val:0.0} ms";
            }
            if (MetricDelayDisplay != null)
            {
                MetricDelayDisplay.Text = $"{(val >= 0 ? "+" : "")}{val:0.0} ms";
            }

            if (DelayStatusBadge != null)
            {
                if (Math.Abs(val) < 0.01)
                {
                    DelayStatusBadge.Text = "Neutral (0.0 ms)";
                }
                else if (val > 0)
                {
                    DelayStatusBadge.Text = $"Beta delayed +{val:0.1}ms";
                }
                else
                {
                    DelayStatusBadge.Text = $"Alpha delayed +{-val:0.1}ms";
                }
            }

            if (PhaseBadgeText != null && EchoTestCheckBox?.IsChecked != true)
            {
                PhaseBadgeText.Text = Math.Abs(val) < 0.01
                    ? "Manual Timing: 0.00ms Offset"
                    : $"Offset: {(val >= 0 ? "+" : "")}{val:0.1}ms";
            }

            RenderWaveforms();
        }

        private void QuickCalibrateButton_Click(object sender, RoutedEventArgs e)
        {
            DelayRangeSlider.Value = 0.0;
            audioEngine?.SetPhaseDelay(0.0);
            RenderWaveforms();
            AppendLog("Delay offset reset to neutral baseline (0.00 ms).", "SYNC");
            MessageBox.Show(
                "Delay offset reset to neutral baseline (0.00 ms).\n\n" +
                "Note: True over-the-air Bluetooth acoustic latency calibration requires external acoustic microphone feedback. " +
                "Use the Test Chimes and delay slider to fine-tune sync by ear.",
                "Quick Calibrate",
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
                PhaseBadgeText.Foreground = Brushes.Orange;
                JitterText.Text = "Hardware Jitter: Unavailable | +50ms Test Offset";
                AppendLog("[ALERT] Audible Echo Test enabled: +50ms delay injected to Output 2.", "SYNC");
            }
            else
            {
                PhaseBadgeText.Text = Math.Abs(DelayRangeSlider.Value) < 0.01
                    ? "Manual Timing: 0.00ms Offset"
                    : $"Offset: {(DelayRangeSlider.Value >= 0 ? "+" : "")}{DelayRangeSlider.Value:0.1}ms";
                PhaseBadgeText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#34D399")!;
                JitterText.Text = "Hardware Jitter: Unavailable (WASAPI Shared)";
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
            if (Wave1Path == null || Wave2Path == null || WaveCanvas == null) return;

            double zoomFactor = WaveZoomSlider != null ? WaveZoomSlider.Value / 100.0 : 1.0;
            if (zoomFactor < 0.3) zoomFactor = 0.3;

            double delayMs = DelayRangeSlider?.Value ?? 0.0;
            if (EchoTestCheckBox?.IsChecked == true)
            {
                delayMs += 50.0;
            }

            // Horizontal spatial frequency: zooming in expands the wave period
            double k = (2.0 * Math.PI / 260.0) / zoomFactor;
            // Phase shift representing time delay offset between Wave 1 and Wave 2
            double phaseShift = (delayMs / 18.0) * Math.PI;

            // Volume scaling for each channel
            double vol1Factor = (Device1VolumeSlider != null ? Device1VolumeSlider.Value / 100.0 : 0.90);
            double vol2Factor = (Device2VolumeSlider != null ? Device2VolumeSlider.Value / 100.0 : 0.85);

            if (device1Muted) vol1Factor *= 0.1;
            if (device2Muted) vol2Factor *= 0.1;

            double baseZoomAmp = Math.Clamp(zoomFactor, 0.75, 1.25);
            double amplitude1 = 23.0 * baseZoomAmp * Math.Clamp(vol1Factor, 0.2, 1.0);
            double amplitude2 = 16.0 * baseZoomAmp * Math.Clamp(vol2Factor, 0.2, 1.0);

            // Distinct dual-trace baselines so both Node 1 (Cyan) and Node 2 (Purple)
            // are always clearly visible, even when delay is exactly 0.00ms
            const double center1 = 37.0;
            const double center2 = 43.0;
            const double width = 1000.0;
            const double step = 4.0;

            // Wave 1 (Alpha - Cyan)
            var g1 = new StreamGeometry();
            using (var ctx = g1.Open())
            {
                double y0 = center1 + amplitude1 * (0.85 * Math.Sin(animationPhase) + 0.15 * Math.Sin(2.0 * animationPhase));
                ctx.BeginFigure(new Point(0, y0), false, false);
                for (double x = step; x <= width; x += step)
                {
                    double theta = k * x + animationPhase;
                    double y = center1 + amplitude1 * (0.85 * Math.Sin(theta) + 0.15 * Math.Sin(2.0 * theta));
                    ctx.LineTo(new Point(x, y), true, false);
                }
            }
            g1.Freeze();
            Wave1Path.Data = g1;

            // Wave 2 (Beta - Purple, shifted by phase delay)
            var g2 = new StreamGeometry();
            using (var ctx = g2.Open())
            {
                double y0 = center2 + amplitude2 * (0.85 * Math.Sin(animationPhase - phaseShift) + 0.15 * Math.Sin(2.0 * (animationPhase - phaseShift)));
                ctx.BeginFigure(new Point(0, y0), false, false);
                for (double x = step; x <= width; x += step)
                {
                    double theta = k * x + animationPhase - phaseShift;
                    double y = center2 + amplitude2 * (0.85 * Math.Sin(theta) + 0.15 * Math.Sin(2.0 * theta));
                    ctx.LineTo(new Point(x, y), true, false);
                }
            }
            g2.Freeze();
            Wave2Path.Data = g2;
        }

        private void AssignNode1Btn_Click(object sender, RoutedEventArgs e)
        {
            if (Device1ComboBox.Items.Count > 0)
            {
                Device1ComboBox.SelectedIndex = 0;
                AppendLog("Slot 1 hardware set as primary Earbuds 1.", "CONNECTION");
                MessageBox.Show($"Earbuds 1 assigned: {(Device1ComboBox.SelectedItem as MMDevice)?.FriendlyName}", "Device Assignment", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void AssignNode2Btn_Click(object sender, RoutedEventArgs e)
        {
            if (Device2ComboBox.Items.Count > 1)
            {
                Device2ComboBox.SelectedIndex = 1;
                AppendLog("Slot 2 hardware set as secondary Earbuds 2.", "CONNECTION");
                MessageBox.Show($"Earbuds 2 assigned: {(Device2ComboBox.SelectedItem as MMDevice)?.FriendlyName}", "Device Assignment", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Only 1 audio output device is currently detected.\n\nPlease connect a second audio device (e.g. Bluetooth earbuds) to assign Slot 2.", "DualSync", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // =========================================================
        // LOGGING
        // =========================================================

        public void AppendLog(string message, string category = "GENERAL")
        {
            string entry = $"[{DateTime.Now:HH:mm:ss.fff}] [{category}] {message}";
            fullSessionLogs.Add(entry);
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

        // =========================================================
        // REAL CONSOLIDATED AUDIT TABLE & REPORTS
        // =========================================================

        private void UpdateConsolidatedAuditTable()
        {
            consolidatedAuditRows.Clear();
            string now = DateTime.Now.ToString("HH:mm:ss.fff");

            var d1 = Device1ComboBox.SelectedItem as MMDevice;
            var d2 = Device2ComboBox.SelectedItem as MMDevice;

            // Real host info
            consolidatedAuditRows.Add(new TelemetryRow(now, "Host Environment", "Machine Name & OS", $"{Environment.MachineName} ({Environment.OSVersion.VersionString})", "Active", "Running 64-bit .NET 8 WPF Host"));

            // Real audio engine state
            string engineState = audioEngine != null ? "Active Broadcasting (DualStream)" : "Standby (Idle)";
            consolidatedAuditRows.Add(new TelemetryRow(now, "DualStream Core", "Engine Run State", engineState, audioEngine != null ? "Broadcasting" : "Standby", "WASAPI Loopback system capture"));

            // Real device 1
            string dev1Name = d1?.FriendlyName ?? "Unassigned";
            string dev1Format = "Unavailable (Not initialized)";
            if (d1 != null)
            {
                try { var fmt = d1.AudioClient.MixFormat; dev1Format = $"{fmt.SampleRate} Hz / {fmt.BitsPerSample}-bit ({fmt.Channels} ch)"; } catch { }
            }
            consolidatedAuditRows.Add(new TelemetryRow(now, "Output Pipeline 1", "Primary Device (Alpha)", dev1Name, d1 != null ? "Assigned" : "Unassigned", $"Gain: {(int)Device1VolumeSlider.Value}% | Muted: {device1Muted} | Mix: {dev1Format}"));

            // Real device 2
            string dev2Name = d2?.FriendlyName ?? "Unassigned";
            string dev2Format = "Unavailable (Not initialized)";
            if (d2 != null)
            {
                try { var fmt = d2.AudioClient.MixFormat; dev2Format = $"{fmt.SampleRate} Hz / {fmt.BitsPerSample}-bit ({fmt.Channels} ch)"; } catch { }
            }
            consolidatedAuditRows.Add(new TelemetryRow(now, "Output Pipeline 2", "Secondary Device (Beta)", dev2Name, d2 != null ? "Assigned" : "Unassigned", $"Gain: {(int)Device2VolumeSlider.Value}% | Muted: {device2Muted} | Mix: {dev2Format}"));

            // Real phase delay
            double delayVal = DelayRangeSlider.Value;
            consolidatedAuditRows.Add(new TelemetryRow(now, "Synchronization", "Audio Phase Delay Offset", $"{(delayVal >= 0 ? "+" : "")}{delayVal:0.00} ms", "User Configured", "Circular FIFO delay applied to audio frames"));

            // Honest Bluetooth telemetry
            consolidatedAuditRows.Add(new TelemetryRow(now, "Bluetooth Stack", "RF RSSI & Packet Loss", "Unavailable", "Not Exposed", "Windows CoreAudio does not expose RF signal or A2DP packet loss to audio clients"));
            consolidatedAuditRows.Add(new TelemetryRow(now, "Bluetooth Codec", "Codec Negotiation & Bitrate", "Unavailable (OS Managed)", "OS Managed", "Windows handles Bluetooth codec selection in driver stack"));

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
            sb.AppendLine($"Stream Duration  : {StreamDurationText.Text}");

            try
            {
                var power = System.Windows.Forms.SystemInformation.PowerStatus;
                int percent = (int)(power.BatteryLifePercent * 100);
                string status = power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online ? "Plugged In" : "Discharging";
                sb.AppendLine($"Battery Status   : {percent}% ({status})");
            }
            catch
            {
                sb.AppendLine("Battery Status   : Standard AC Power");
            }

            sb.AppendLine("--------------------------------------------------------------------------");
            sb.AppendLine("AUDIO HARDWARE ENDPOINTS:");
            sb.AppendLine($"  • Primary Node (Alpha)  : {(Device1ComboBox.SelectedItem as MMDevice)?.FriendlyName ?? "Unassigned"}");
            sb.AppendLine($"    Volume Gain           : {(int)Device1VolumeSlider.Value}% | Muted: {device1Muted}");
            sb.AppendLine($"  • Secondary Node (Beta) : {(Device2ComboBox.SelectedItem as MMDevice)?.FriendlyName ?? "Unassigned"}");
            sb.AppendLine($"    Volume Gain           : {(int)Device2VolumeSlider.Value}% | Muted: {device2Muted}");
            sb.AppendLine($"  • Audio Phase Offset    : {DelayLabel.Text} (User-configured circular buffer delay)");
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

        // =========================================================
        // STATUS & PERSISTENCE (NON-BLOCKING POLLING)
        // =========================================================

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
                // Run COM enumeration in background to prevent UI freeze
                bool d1 = await Task.Run(() => audioEngine?.IsDevice1Connected() ?? false);
                bool d2 = await Task.Run(() => audioEngine?.IsDevice2Connected() ?? false);

                SetDeviceConnectionStatus(Device1StatusDot, Device1StatusText, d1 ? "Connected" : "Disconnected", d1 ? Brushes.Green : Brushes.Red);
                SetDeviceConnectionStatus(Device2StatusDot, Device2StatusText, d2 ? "Connected" : "Disconnected", d2 ? Brushes.Green : Brushes.Red);

                if (!d1 && !d2) SetStatus("Both devices disconnected", Brushes.Red);
                else if (!d1) SetStatus("Earbuds 1 disconnected", Brushes.Orange);
                else if (!d2) SetStatus("Earbuds 2 disconnected", Brushes.Orange);
                else SetStatus("Running", Brushes.Green);
            }
            catch
            {
                // Ignore transient status polling error
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
                        if (!string.IsNullOrWhiteSpace(cfg.Dev1Id)) selectedDevice1Id = cfg.Dev1Id;
                        if (!string.IsNullOrWhiteSpace(cfg.Dev2Id)) selectedDevice2Id = cfg.Dev2Id;
                        if (cfg.DelayMs >= -100 && cfg.DelayMs <= 100) DelayRangeSlider.Value = cfg.DelayMs;
                    }
                }
            }
            catch { }
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
                    DelayMs = DelayRangeSlider.Value
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

        public class TelemetryRow
        {
            public string Timestamp { get; set; }
            public string Source { get; set; }
            public string Metric { get; set; }
            public string Value { get; set; }
            public string State { get; set; }
            public string Notes { get; set; }

            public TelemetryRow(string t, string s, string m, string v, string st, string n)
            {
                Timestamp = t; Source = s; Metric = m; Value = v; State = st; Notes = n;
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
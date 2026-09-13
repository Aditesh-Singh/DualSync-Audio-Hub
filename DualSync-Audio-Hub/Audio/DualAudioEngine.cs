using System;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DualSync.Audio
{
    public class DualAudioEngine : IDisposable
    {
        private readonly AudioCapture capture;

        private MMDevice? device1;
        private MMDevice? device2;

        private AudioOutput? output1;
        private AudioOutput? output2;

        private AudioDelayBuffer? delayBuffer1;
        private AudioDelayBuffer? delayBuffer2;

        private double currentPhaseDelayMs;
        private bool echoTestEnabled;

        // Stable device IDs for reconnection
        private readonly string device1Id;
        private readonly string device2Id;

        // Synchronization lock for audio output pipeline
        private readonly object outputLock = new();

        private bool captureStarted;
        private bool disposed;

        private bool device1WasConnected;
        private bool device2WasConnected;

        public DualAudioEngine(MMDevice device1, MMDevice device2)
        {
            this.device1 = device1 ?? throw new ArgumentNullException(nameof(device1));
            this.device2 = device2 ?? throw new ArgumentNullException(nameof(device2));

            device1Id = device1.ID;
            device2Id = device2.ID;

            capture = new AudioCapture();
            capture.DataAvailable += OnAudioDataAvailable;
        }

        // =========================================================
        // REAL AUDIO PHASE DELAY & ECHO TEST
        // =========================================================

        /// <summary>
        /// Sets real audio phase delay between Device 1 and Device 2.
        /// Positive values delay Device 2 relative to Device 1.
        /// Negative values delay Device 1 relative to Device 2.
        /// </summary>
        public void SetPhaseDelay(double delayMs)
        {
            lock (outputLock)
            {
                currentPhaseDelayMs = delayMs;
                ApplyDelayConfiguration();
            }
        }

        /// <summary>
        /// Enables or disables the audible Echo Stress Test.
        /// When enabled, injects a genuine +50ms delay to Device 2 so echo is audibly verifiable.
        /// </summary>
        public void SetEchoTest(bool enabled)
        {
            lock (outputLock)
            {
                echoTestEnabled = enabled;
                ApplyDelayConfiguration();
            }
        }

        private void ApplyDelayConfiguration()
        {
            if (delayBuffer1 == null || delayBuffer2 == null)
            {
                return;
            }

            double effectiveDelay = currentPhaseDelayMs;
            if (echoTestEnabled)
            {
                effectiveDelay += 50.0; // Audible echo offset
            }

            if (effectiveDelay >= 0.0)
            {
                delayBuffer1.SetDelayMs(0.0);
                delayBuffer2.SetDelayMs(effectiveDelay);
            }
            else
            {
                delayBuffer1.SetDelayMs(-effectiveDelay);
                delayBuffer2.SetDelayMs(0.0);
            }
        }

        // =========================================================
        // CONNECTION STATUS + AUTOMATIC AUDIO RECOVERY
        // =========================================================

        public bool IsDevice1Connected()
        {
            return CheckDeviceConnection(device1Id, 1);
        }

        public bool IsDevice2Connected()
        {
            return CheckDeviceConnection(device2Id, 2);
        }

        private bool CheckDeviceConnection(string deviceId, int deviceNumber)
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

                MMDevice? freshDevice = null;

                foreach (var device in devices)
                {
                    if (freshDevice == null && device.ID == deviceId)
                    {
                        freshDevice = device;
                    }
                    else
                    {
                        // Dispose unused COM device handles immediately to avoid leaks
                        device.Dispose();
                    }
                }

                if (freshDevice == null)
                {
                    if (deviceNumber == 1) device1WasConnected = false;
                    else device2WasConnected = false;
                    return false;
                }

                bool wasConnected = deviceNumber == 1 ? device1WasConnected : device2WasConnected;

                if (!wasConnected)
                {
                    RecoverOutput(freshDevice, deviceNumber);
                }
                else
                {
                    freshDevice.Dispose();
                }

                if (deviceNumber == 1) device1WasConnected = true;
                else device2WasConnected = true;

                return true;
            }
            catch
            {
                if (deviceNumber == 1) device1WasConnected = false;
                else device2WasConnected = false;
                return false;
            }
        }

        private void RecoverOutput(MMDevice freshDevice, int deviceNumber)
        {
            lock (outputLock)
            {
                if (disposed || !captureStarted)
                {
                    freshDevice.Dispose();
                    return;
                }

                if (deviceNumber == 1)
                {
                    float previousVolume = output1?.GetVolume() ?? 1.0f;
                    bool previousMuted = output1?.IsMuted() ?? false;

                    try { output1?.Stop(); } catch { }
                    try { output1?.Dispose(); } catch { }
                    try { device1?.Dispose(); } catch { }

                    device1 = freshDevice;

                    try
                    {
                        output1 = new AudioOutput(device1, capture.Format);
                        output1.SetVolume(previousVolume);
                        output1.SetMute(previousMuted);
                        output1.Start();
                    }
                    catch
                    {
                        output1 = null;
                    }
                }
                else
                {
                    float previousVolume = output2?.GetVolume() ?? 1.0f;
                    bool previousMuted = output2?.IsMuted() ?? false;

                    try { output2?.Stop(); } catch { }
                    try { output2?.Dispose(); } catch { }
                    try { device2?.Dispose(); } catch { }

                    device2 = freshDevice;

                    try
                    {
                        output2 = new AudioOutput(device2, capture.Format);
                        output2.SetVolume(previousVolume);
                        output2.SetMute(previousMuted);
                        output2.Start();
                    }
                    catch
                    {
                        output2 = null;
                    }
                }
            }
        }

        // =========================================================
        // START / STOP
        // =========================================================

        public void Start()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(DualAudioEngine));
            }

            if (captureStarted)
            {
                return;
            }

            capture.Start();
            captureStarted = true;

            var format = capture.Format;

            lock (outputLock)
            {
                delayBuffer1 = new AudioDelayBuffer(format);
                delayBuffer2 = new AudioDelayBuffer(format);
                ApplyDelayConfiguration();

                output1 = new AudioOutput(device1!, format);
                output2 = new AudioOutput(device2!, format);

                output1.Start();
                output2.Start();
            }

            device1WasConnected = true;
            device2WasConnected = true;
        }

        public void Stop()
        {
            lock (outputLock)
            {
                if (!captureStarted && output1 == null && output2 == null)
                {
                    return;
                }

                try { capture.Stop(); } catch { }
                captureStarted = false;

                try { output1?.Stop(); } catch { }
                try { output2?.Stop(); } catch { }

                delayBuffer1?.Reset();
                delayBuffer2?.Reset();

                device1WasConnected = false;
                device2WasConnected = false;
            }
        }

        // =========================================================
        // VOLUME & MUTE
        // =========================================================

        public void SetVolume1(float volume)
        {
            lock (outputLock)
            {
                output1?.SetVolume(volume);
            }
        }

        public void SetVolume2(float volume)
        {
            lock (outputLock)
            {
                output2?.SetVolume(volume);
            }
        }

        public void SetMute1(bool mute)
        {
            lock (outputLock)
            {
                output1?.SetMute(mute);
            }
        }

        public void SetMute2(bool mute)
        {
            lock (outputLock)
            {
                output2?.SetMute(mute);
            }
        }

        // =========================================================
        // AUDIO DATA DISPATCH (WITH REAL TIMING DELAY)
        // =========================================================

        private void OnAudioDataAvailable(object? sender, WaveInEventArgs e)
        {
            lock (outputLock)
            {
                if (disposed || !captureStarted)
                {
                    return;
                }

                if (delayBuffer1 != null && output1 != null)
                {
                    byte[] data1 = delayBuffer1.Process(e.Buffer, 0, e.BytesRecorded);
                    output1.Write(data1, 0, data1.Length);
                }

                if (delayBuffer2 != null && output2 != null)
                {
                    byte[] data2 = delayBuffer2.Process(e.Buffer, 0, e.BytesRecorded);
                    output2.Write(data2, 0, data2.Length);
                }
            }
        }

        // =========================================================
        // DISPOSE
        // =========================================================

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            lock (outputLock)
            {
                try { capture.DataAvailable -= OnAudioDataAvailable; } catch { }
                try { capture.Stop(); } catch { }
                captureStarted = false;

                try { output1?.Stop(); } catch { }
                try { output2?.Stop(); } catch { }

                try { output1?.Dispose(); } catch { }
                try { output2?.Dispose(); } catch { }

                output1 = null;
                output2 = null;

                try { device1?.Dispose(); } catch { }
                try { device2?.Dispose(); } catch { }

                device1 = null;
                device2 = null;

                delayBuffer1?.Reset();
                delayBuffer2?.Reset();
                delayBuffer1 = null;
                delayBuffer2 = null;

                device1WasConnected = false;
                device2WasConnected = false;
            }
        }
    }
}

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

        private readonly string device1Id;
        private readonly string device2Id;

        private readonly object outputLock = new();

        private bool captureStarted;
        private bool disposed;

        private bool device1WasConnected;
        private bool device2WasConnected;

        private readonly float[] telemetrySamples1 = new float[256];
        private readonly float[] telemetrySamples2 = new float[256];
        private readonly object telemetrySync = new();

        public DualAudioEngine(MMDevice device1, MMDevice device2)
        {
            this.device1 = device1 ?? throw new ArgumentNullException(nameof(device1));
            this.device2 = device2 ?? throw new ArgumentNullException(nameof(device2));

            device1Id = device1.ID;
            device2Id = device2.ID;

            capture = new AudioCapture();
            capture.DataAvailable += OnAudioDataAvailable;
        }

        public void GetWaveformTelemetry(float[] dest1, float[] dest2)
        {
            lock (telemetrySync)
            {
                Array.Copy(telemetrySamples1, dest1, Math.Min(telemetrySamples1.Length, dest1.Length));
                Array.Copy(telemetrySamples2, dest2, Math.Min(telemetrySamples2.Length, dest2.Length));
            }
        }

        public void SetPhaseDelay(double delayMs)
        {
            lock (outputLock)
            {
                currentPhaseDelayMs = delayMs;
                ApplyDelayConfiguration();
            }
        }

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
                effectiveDelay += 50.0;
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

                lock (telemetrySync)
                {
                    Array.Clear(telemetrySamples1, 0, telemetrySamples1.Length);
                    Array.Clear(telemetrySamples2, 0, telemetrySamples2.Length);
                }
            }
        }

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

        private void OnAudioDataAvailable(object? sender, WaveInEventArgs e)
        {
            lock (outputLock)
            {
                if (disposed || !captureStarted)
                {
                    return;
                }

                byte[]? d1 = null;
                byte[]? d2 = null;

                if (delayBuffer1 != null && output1 != null)
                {
                    d1 = delayBuffer1.Process(e.Buffer, 0, e.BytesRecorded);
                    output1.Write(d1, 0, d1.Length);
                }

                if (delayBuffer2 != null && output2 != null)
                {
                    d2 = delayBuffer2.Process(e.Buffer, 0, e.BytesRecorded);
                    output2.Write(d2, 0, d2.Length);
                }

                if (d1 != null && d2 != null)
                {
                    RecordTelemetry(d1, d2);
                }
            }
        }

        private void RecordTelemetry(byte[] d1, byte[] d2)
        {
            try
            {
                var fmt = capture.Format;
                int bPerSample = Math.Max(1, fmt.BitsPerSample / 8);
                int frameSize = Math.Max(1, fmt.Channels * bPerSample);
                int frames1 = d1.Length / frameSize;
                int frames2 = d2.Length / frameSize;
                if (frames1 <= 0) return;

                int count = 256;
                int step1 = Math.Max(1, frames1 / count);
                int step2 = Math.Max(1, frames2 / count);

                lock (telemetrySync)
                {
                    for (int i = 0; i < count; i++)
                    {
                        int o1 = Math.Min(i * step1 * frameSize, d1.Length - bPerSample);
                        telemetrySamples1[i] = ReadSampleFloat(d1, o1, fmt);
                        if (frames2 > 0)
                        {
                            int o2 = Math.Min(i * step2 * frameSize, d2.Length - bPerSample);
                            telemetrySamples2[i] = ReadSampleFloat(d2, o2, fmt);
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private static float ReadSampleFloat(byte[] data, int offset, WaveFormat fmt)
        {
            if (offset < 0 || offset >= data.Length) return 0f;
            if (fmt.Encoding == WaveFormatEncoding.IeeeFloat && fmt.BitsPerSample == 32)
            {
                if (offset + 4 <= data.Length) return BitConverter.ToSingle(data, offset);
            }
            else if (fmt.BitsPerSample == 16)
            {
                if (offset + 2 <= data.Length)
                {
                    short s = (short)(data[offset] | (data[offset + 1] << 8));
                    return s / 32768.0f;
                }
            }
            return 0f;
        }

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

                lock (telemetrySync)
                {
                    Array.Clear(telemetrySamples1, 0, telemetrySamples1.Length);
                    Array.Clear(telemetrySamples2, 0, telemetrySamples2.Length);
                }
            }
        }
    }
}

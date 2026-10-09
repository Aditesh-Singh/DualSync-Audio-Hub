using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DualSync.Audio
{
    public sealed record AudioLatencyCalibrationResult(
        bool Success,
        double Device1LatencyMs,
        double Device2LatencyMs,
        double DifferenceMs,
        double CorrectionMs,
        string Message);

    public static class AudioLatencyCalibrator
    {
        private const int Trials = 3;
        private const int LeadInMs = 250;
        private const int CaptureMs = 1250;

        public static async Task<AudioLatencyCalibrationResult> MeasureAsync(
            string device1Id,
            string device2Id,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(device1Id) ||
                string.IsNullOrWhiteSpace(device2Id))
            {
                return new(false, 0, 0, 0, 0, "Two audio devices are required.");
            }

            var d1 = new List<double>();
            var d2 = new List<double>();

            for (int i = 0; i < Trials; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var t1 = await MeasureDeviceAsync(
                    device1Id,
                    880.0,
                    cancellationToken);

                if (!t1.HasValue)
                    return new(false, 0, 0, 0, 0,
                        "Could not detect the first earbud chime.");

                await Task.Delay(150, cancellationToken);

                var t2 = await MeasureDeviceAsync(
                    device2Id,
                    1320.0,
                    cancellationToken);

                if (!t2.HasValue)
                    return new(false, 0, 0, 0, 0,
                        "Could not detect the second earbud chime.");

                d1.Add(t1.Value);
                d2.Add(t2.Value);

                await Task.Delay(150, cancellationToken);
            }

            double latency1 = Median(d1);
            double latency2 = Median(d2);
            double difference = latency2 - latency1;
            double correction = Math.Clamp(-difference, -20.0, 20.0);

            return new(
                true,
                latency1,
                latency2,
                difference,
                correction,
                Math.Abs(difference) > 20.0
                    ? "Measured difference exceeds ±20 ms."
                    : "Acoustic calibration completed.");
        }

        private static async Task<double?> MeasureDeviceAsync(
            string deviceId,
            double frequency,
            CancellationToken cancellationToken)
        {
            var captured = new List<byte>();

            using var capture = new WasapiCapture();

            capture.DataAvailable += (_, e) =>
            {
                lock (captured)
                {
                    for (int i = 0; i < e.BytesRecorded; i++)
                        captured.Add(e.Buffer[i]);
                }
            };

            capture.StartRecording();

            try
            {
                await Task.Delay(
                    LeadInMs,
                    cancellationToken);

                using var enumerator =
                    new MMDeviceEnumerator();

                using var device =
                    enumerator.GetDevice(deviceId);

                var format =
                    device.AudioClient.MixFormat;

                byte[] tone =
                    CreateTone(
                        format,
                        frequency,
                        120,
                        0.75);

                using var output =
                    new WasapiOut(
                        device,
                        AudioClientShareMode.Shared,
                        true,
                        50);

                var buffer =
                    new BufferedWaveProvider(format)
                    {
                        DiscardOnBufferOverflow = true
                    };

                output.Init(buffer);
                output.Play();

                buffer.AddSamples(
                    tone,
                    0,
                    tone.Length);

                await Task.Delay(
                    CaptureMs,
                    cancellationToken);

                output.Stop();
            }
            finally
            {
                try
                {
                    capture.StopRecording();
                }
                catch
                {
                }
            }

            byte[] data;

            lock (captured)
                data = captured.ToArray();

            return DetectTone(
                data,
                capture.WaveFormat,
                frequency);
        }

        private static byte[] CreateTone(
            WaveFormat format,
            double frequency,
            int durationMs,
            double amplitude)
        {
            int sampleRate = format.SampleRate;
            int channels = Math.Max(1, format.Channels);
            int samples = sampleRate * durationMs / 1000;
            int bytesPerSample =
                Math.Max(1, format.BitsPerSample / 8);

            byte[] data =
                new byte[
                    samples *
                    channels *
                    bytesPerSample];

            int p = 0;

            for (int i = 0; i < samples; i++)
            {
                double t =
                    i / (double)sampleRate;

                double fadeIn =
                    Math.Min(
                        1.0,
                        i / (sampleRate * 0.008));

                double fadeOut =
                    Math.Min(
                        1.0,
                        (samples - i) /
                        (sampleRate * 0.035));

                double value =
                    Math.Sin(
                        2.0 *
                        Math.PI *
                        frequency *
                        t) *
                    amplitude *
                    fadeIn *
                    fadeOut;

                for (int ch = 0; ch < channels; ch++)
                {
                    if (
                        format.Encoding ==
                        WaveFormatEncoding.IeeeFloat &&
                        format.BitsPerSample == 32)
                    {
                        BitConverter
                            .GetBytes((float)value)
                            .CopyTo(data, p);

                        p += 4;
                    }
                    else if (
                        format.Encoding ==
                        WaveFormatEncoding.Pcm &&
                        format.BitsPerSample == 16)
                    {
                        short sample =
                            (short)Math.Clamp(
                                value * short.MaxValue,
                                short.MinValue,
                                short.MaxValue);

                        data[p++] =
                            (byte)(sample & 0xFF);

                        data[p++] =
                            (byte)((sample >> 8) & 0xFF);
                    }
                    else if (
                        format.Encoding ==
                        WaveFormatEncoding.Pcm &&
                        format.BitsPerSample == 24)
                    {
                        int sample =
                            (int)Math.Clamp(
                                value * 8388607.0,
                                -8388608.0,
                                8388607.0);

                        data[p++] =
                            (byte)(sample & 0xFF);

                        data[p++] =
                            (byte)((sample >> 8) & 0xFF);

                        data[p++] =
                            (byte)((sample >> 16) & 0xFF);
                    }
                    else if (
                        format.Encoding ==
                        WaveFormatEncoding.Pcm &&
                        format.BitsPerSample == 32)
                    {
                        int sample =
                            (int)Math.Clamp(
                                value * int.MaxValue,
                                int.MinValue,
                                int.MaxValue);

                        data[p++] =
                            (byte)(sample & 0xFF);

                        data[p++] =
                            (byte)((sample >> 8) & 0xFF);

                        data[p++] =
                            (byte)((sample >> 16) & 0xFF);

                        data[p++] =
                            (byte)((sample >> 24) & 0xFF);
                    }
                }
            }

            return data;
        }

        private static double? DetectTone(
            byte[] data,
            WaveFormat format,
            double frequency)
        {
            int channels =
                Math.Max(1, format.Channels);

            int bytesPerSample =
                Math.Max(1, format.BitsPerSample / 8);

            int frameSize =
                channels * bytesPerSample;

            if (frameSize <= 0)
                return null;

            int totalFrames =
                data.Length / frameSize;

            int window =
                Math.Max(256, format.SampleRate / 50);

            int hop =
                Math.Max(128, window / 4);

            int startFrame =
                format.SampleRate * 180 / 1000;

            int endFrame =
                Math.Min(
                    totalFrames - window,
                    format.SampleRate * 1100 / 1000);

            if (startFrame >= endFrame)
                return null;

            var powers =
                new List<(int Frame, double Power)>();

            double omega =
                2.0 *
                Math.PI *
                frequency /
                format.SampleRate;

            double coeff =
                2.0 * Math.Cos(omega);

            for (
                int start = startFrame;
                start <= endFrame;
                start += hop)
            {
                double q1 = 0;
                double q2 = 0;

                for (int i = 0; i < window; i++)
                {
                    double sample = 0;

                    int frame =
                        start + i;

                    int baseIndex =
                        frame * frameSize;

                    for (int ch = 0; ch < channels; ch++)
                    {
                        sample += ReadSample(
                            data,
                            baseIndex +
                            ch * bytesPerSample,
                            format);
                    }

                    sample /= channels;

                    double q0 =
                        sample +
                        coeff * q1 -
                        q2;

                    q2 = q1;
                    q1 = q0;
                }

                double power =
                    q1 * q1 +
                    q2 * q2 -
                    coeff * q1 * q2;

                powers.Add(
                    (start, power));
            }

            if (powers.Count == 0)
                return null;

            double max =
                powers.Max(x => x.Power);

            if (max <= 0)
                return null;

            double threshold =
                max * 0.35;

            var hit =
                powers.FirstOrDefault(
                    x => x.Power >= threshold);

            if (hit.Power <= 0)
                return null;

            return
                hit.Frame *
                1000.0 /
                format.SampleRate -
                LeadInMs;
        }

        private static double ReadSample(
            byte[] data,
            int index,
            WaveFormat format)
        {
            if (
                format.Encoding ==
                WaveFormatEncoding.IeeeFloat &&
                format.BitsPerSample == 32)
            {
                return BitConverter
                    .ToSingle(
                        data,
                        index);
            }

            if (format.BitsPerSample == 16)
            {
                short value =
                    (short)(
                        data[index] |
                        data[index + 1] << 8);

                return value / 32768.0;
            }

            if (format.BitsPerSample == 24)
            {
                int value =
                    data[index] |
                    data[index + 1] << 8 |
                    data[index + 2] << 16;

                if ((value & 0x800000) != 0)
                    value |=
                        unchecked((int)0xFF000000);

                return value / 8388608.0;
            }

            if (format.BitsPerSample == 32)
            {
                int value =
                    data[index] |
                    data[index + 1] << 8 |
                    data[index + 2] << 16 |
                    data[index + 3] << 24;

                return value / 2147483648.0;
            }

            return 0;
        }

        private static double Median(
            List<double> values)
        {
            values.Sort();

            int middle =
                values.Count / 2;

            return values.Count % 2 == 0
                ? (values[middle - 1] +
                   values[middle]) / 2.0
                : values[middle];
        }
    }
}
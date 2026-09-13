using System;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DualSync.Audio
{
    public class AudioOutput : IDisposable
    {
        private readonly WasapiOut output;
        private readonly BufferedWaveProvider buffer;
        private readonly MediaFoundationResampler? resampler;

        private float currentVolume = 1.0f;
        private bool isMuted;
        private bool disposed;

        public AudioOutput(MMDevice device, WaveFormat inputFormat)
        {
            ArgumentNullException.ThrowIfNull(device);
            ArgumentNullException.ThrowIfNull(inputFormat);

            buffer = new BufferedWaveProvider(inputFormat)
            {
                DiscardOnBufferOverflow = true,
                BufferDuration = TimeSpan.FromSeconds(2)
            };

            output = new WasapiOut(
                device,
                AudioClientShareMode.Shared,
                true,
                50);

            var deviceMixFormat = device.AudioClient.MixFormat;

            // Check if input format matches device format
            if (IsFormatCompatible(inputFormat, deviceMixFormat))
            {
                output.Init(buffer);
            }
            else
            {
                // Resample to match device's expected mix format
                resampler = new MediaFoundationResampler(buffer, deviceMixFormat)
                {
                    ResamplerQuality = 60
                };
                output.Init(resampler);
            }

            output.Volume = currentVolume;
        }

        private static bool IsFormatCompatible(WaveFormat a, WaveFormat b)
        {
            if (a.SampleRate != b.SampleRate) return false;
            if (a.Channels != b.Channels) return false;
            if (a.BitsPerSample != b.BitsPerSample) return false;
            if (a.Encoding != b.Encoding) return false;
            return true;
        }

        public void Write(byte[] data, int offset, int count)
        {
            if (disposed || count <= 0) return;

            try
            {
                buffer.AddSamples(data, offset, count);
            }
            catch
            {
                // Discard on buffer fault or overflow
            }
        }

        public void SetVolume(float volume)
        {
            currentVolume = Math.Clamp(volume, 0.0f, 1.0f);
            if (!isMuted && !disposed)
            {
                try
                {
                    output.Volume = currentVolume;
                }
                catch
                {
                    // Ignore transient COM volume error
                }
            }
        }

        public float GetVolume()
        {
            return currentVolume;
        }

        public void SetMute(bool mute)
        {
            isMuted = mute;
            if (!disposed)
            {
                try
                {
                    output.Volume = mute ? 0.0f : currentVolume;
                }
                catch
                {
                    // Ignore transient COM volume error
                }
            }
        }

        public bool IsMuted()
        {
            return isMuted;
        }

        public void Start()
        {
            if (disposed) return;
            output.Play();
        }

        public void Stop()
        {
            if (disposed) return;
            try
            {
                output.Stop();
            }
            catch
            {
                // Ignore stop error
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            try { output.Stop(); } catch { }
            try { output.Dispose(); } catch { }
            try { resampler?.Dispose(); } catch { }
            try { buffer.ClearBuffer(); } catch { }
        }
    }
}
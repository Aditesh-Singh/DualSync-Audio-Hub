using System;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DualSync.Audio
{
    public class AudioOutput : IDisposable
    {
        private readonly WasapiOut output;
        private readonly BufferedWaveProvider buffer;

        private float currentVolume = 1.0f;
        private bool isMuted;
        private bool disposed;

        public AudioOutput(MMDevice device, WaveFormat format)
        {
            ArgumentNullException.ThrowIfNull(device);
            ArgumentNullException.ThrowIfNull(format);

            buffer = new BufferedWaveProvider(format)
            {
                DiscardOnBufferOverflow = true
            };

            output = new WasapiOut(
                device,
                AudioClientShareMode.Shared,
                true,
                50);

            output.Init(buffer);
            output.Volume = currentVolume;
        }

        public void Write(byte[] data, int offset, int count)
        {
            if (disposed || count <= 0)
                return;

            buffer.AddSamples(data, offset, count);
        }

        public void SetVolume(float volume)
        {
            currentVolume = Math.Clamp(volume, 0.0f, 1.0f);

            if (!disposed)
                output.Volume = isMuted ? 0.0f : currentVolume;
        }

        public float GetVolume()
        {
            return currentVolume;
        }

        public void SetMute(bool mute)
        {
            isMuted = mute;

            if (!disposed)
                output.Volume = mute ? 0.0f : currentVolume;
        }

        public bool IsMuted()
        {
            return isMuted;
        }

        public void Start()
        {
            if (!disposed)
                output.Play();
        }

        public void Stop()
        {
            if (!disposed)
            {
                try
                {
                    output.Stop();
                }
                catch
                {
                }
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;

            try
            {
                output.Stop();
            }
            catch
            {
            }

            output.Dispose();
            buffer.ClearBuffer();
        }
    }
}
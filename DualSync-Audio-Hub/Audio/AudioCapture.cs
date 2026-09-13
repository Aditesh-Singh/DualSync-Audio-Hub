using System;
using NAudio.Wave;

namespace DualSync.Audio
{
    public class AudioCapture : IDisposable
    {
        private WasapiLoopbackCapture? capture;

        public WaveFormat Format =>
            capture?.WaveFormat
            ?? throw new InvalidOperationException(
                "Audio capture has not started.");

        public event EventHandler<WaveInEventArgs>? DataAvailable;

        public void Start()
        {
            capture = new WasapiLoopbackCapture();

            capture.DataAvailable += (sender, e) =>
            {
                DataAvailable?.Invoke(this, e);
            };

            capture.StartRecording();
        }

        public void Stop()
        {
            capture?.StopRecording();
            capture?.Dispose();
            capture = null;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
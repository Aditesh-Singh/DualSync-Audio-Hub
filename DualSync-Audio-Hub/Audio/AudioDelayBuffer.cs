using System;
using NAudio.Wave;

namespace DualSync.Audio
{
    /// <summary>
    /// Implements a thread-safe, frame-aligned circular buffer that delays
    /// incoming audio data by a specified duration in milliseconds.
    /// This introduces real, physical audio timing delay rather than a UI mockup.
    /// </summary>
    public class AudioDelayBuffer
    {
        private readonly WaveFormat format;
        private readonly object bufferLock = new();
        private readonly byte[] circularBuffer;
        private readonly int bytesPerFrame;
        private readonly int bytesPerSecond;

        private int writeIndex;
        private int targetDelayBytes;
        private long totalBytesWritten;

        public AudioDelayBuffer(WaveFormat format, double maxDelayMs = 2000.0)
        {
            this.format = format ?? throw new ArgumentNullException(nameof(format));

            int bitsPerSample = format.BitsPerSample > 0 ? format.BitsPerSample : 16;
            bytesPerFrame = format.Channels * (bitsPerSample / 8);
            if (bytesPerFrame <= 0)
            {
                bytesPerFrame = 4;
            }

            bytesPerSecond = format.AverageBytesPerSecond > 0
                ? format.AverageBytesPerSecond
                : format.SampleRate * bytesPerFrame;

            int maxCapacity = (int)Math.Ceiling(bytesPerSecond * (maxDelayMs / 1000.0)) + (bytesPerFrame * 1024);
            maxCapacity = (maxCapacity / bytesPerFrame) * bytesPerFrame;
            circularBuffer = new byte[maxCapacity];
        }

        public void SetDelayMs(double delayMs)
        {
            lock (bufferLock)
            {
                if (delayMs < 0.0)
                {
                    delayMs = 0.0;
                }

                int desiredBytes = (int)Math.Round((delayMs / 1000.0) * bytesPerSecond);
                desiredBytes = (desiredBytes / bytesPerFrame) * bytesPerFrame;

                int maxDelay = circularBuffer.Length - bytesPerFrame;
                targetDelayBytes = Math.Clamp(desiredBytes, 0, maxDelay);
            }
        }

        public byte[] Process(byte[] input, int offset, int count)
        {
            lock (bufferLock)
            {
                if (count <= 0)
                {
                    return Array.Empty<byte>();
                }

                if (targetDelayBytes <= 0)
                {
                    // Continuously populate circular buffer history so transitions are seamless
                    for (int i = 0; i < count; i += bytesPerFrame)
                    {
                        int frameBytes = Math.Min(bytesPerFrame, count - i);
                        for (int b = 0; b < frameBytes; b++)
                        {
                            circularBuffer[(writeIndex + b) % circularBuffer.Length] = input[offset + i + b];
                        }
                        writeIndex = (writeIndex + bytesPerFrame) % circularBuffer.Length;
                        totalBytesWritten += bytesPerFrame;
                    }

                    byte[] direct = new byte[count];
                    Buffer.BlockCopy(input, offset, direct, 0, count);
                    return direct;
                }

                byte[] output = new byte[count];
                int outputOffset = 0;

                for (int i = 0; i < count; i += bytesPerFrame)
                {
                    int frameBytes = Math.Min(bytesPerFrame, count - i);

                    for (int b = 0; b < frameBytes; b++)
                    {
                        circularBuffer[(writeIndex + b) % circularBuffer.Length] = input[offset + i + b];
                    }

                    writeIndex = (writeIndex + bytesPerFrame) % circularBuffer.Length;
                    totalBytesWritten += bytesPerFrame;

                    if (totalBytesWritten < targetDelayBytes)
                    {
                        // Initial buffering silence until target delay window is populated
                        for (int b = 0; b < frameBytes; b++)
                        {
                            output[outputOffset + b] = 0;
                        }
                    }
                    else
                    {
                        // Calculate read index: targetDelayBytes frames prior to the frame just written
                        int readIndex = (writeIndex - targetDelayBytes - bytesPerFrame) % circularBuffer.Length;
                        if (readIndex < 0)
                        {
                            readIndex += circularBuffer.Length;
                        }

                        for (int b = 0; b < frameBytes; b++)
                        {
                            output[outputOffset + b] = circularBuffer[(readIndex + b) % circularBuffer.Length];
                        }
                    }

                    outputOffset += frameBytes;
                }

                return output;
            }
        }

        public void Reset()
        {
            lock (bufferLock)
            {
                Array.Clear(circularBuffer, 0, circularBuffer.Length);
                writeIndex = 0;
                totalBytesWritten = 0;
            }
        }
    }
}

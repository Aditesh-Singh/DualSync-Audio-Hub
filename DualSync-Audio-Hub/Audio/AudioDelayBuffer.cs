using System;
using System.Collections.Generic;
using NAudio.Wave;

namespace DualSync.Audio
{
    public sealed class AudioDelayBuffer
    {
        private readonly object sync = new();
        private readonly Queue<byte> queue = new();
        private readonly int bytesPerFrame;
        private readonly int bytesPerSecond;
        private readonly int maxDelayBytes;

        private int delayBytes;

        public AudioDelayBuffer(WaveFormat format, double maxDelayMs = 2000)
        {
            ArgumentNullException.ThrowIfNull(format);

            int bytesPerSample =
                Math.Max(1, format.BitsPerSample / 8);

            bytesPerFrame =
                Math.Max(
                    1,
                    format.Channels * bytesPerSample);

            bytesPerSecond =
                Math.Max(
                    1,
                    format.AverageBytesPerSecond);

            maxDelayBytes =
                Math.Max(
                    bytesPerFrame,
                    (int)(
                        bytesPerSecond *
                        Math.Max(0, maxDelayMs) /
                        1000.0));

            maxDelayBytes -=
                maxDelayBytes % bytesPerFrame;
        }

        public void SetDelayMs(double delayMs)
        {
            lock (sync)
            {
                delayMs = Math.Clamp(
                    delayMs,
                    0,
                    2000);

                int bytes =
                    (int)Math.Round(
                        delayMs *
                        bytesPerSecond /
                        1000.0);

                bytes -=
                    bytes % bytesPerFrame;

                delayBytes =
                    Math.Clamp(
                        bytes,
                        0,
                        maxDelayBytes);

                while (
                    queue.Count >
                    delayBytes + bytesPerFrame)
                {
                    for (
                        int i = 0;
                        i < bytesPerFrame &&
                        queue.Count > delayBytes;
                        i++)
                    {
                        queue.Dequeue();
                    }
                }
            }
        }

        public byte[] Process(
            byte[] input,
            int offset,
            int count)
        {
            if (count <= 0)
                return Array.Empty<byte>();

            lock (sync)
            {
                if (delayBytes == 0)
                {
                    byte[] direct =
                        new byte[count];

                    Buffer.BlockCopy(
                        input,
                        offset,
                        direct,
                        0,
                        count);

                    return direct;
                }

                int aligned =
                    count -
                    (count % bytesPerFrame);

                byte[] output =
                    new byte[count];

                for (int i = 0; i < aligned; i++)
                {
                    queue.Enqueue(
                        input[offset + i]);
                }

                int available =
                    queue.Count -
                    delayBytes;

                if (available <= 0)
                    return output;

                int outputBytes =
                    Math.Min(
                        aligned,
                        available);

                for (
                    int i = 0;
                    i < outputBytes;
                    i++)
                {
                    output[i] =
                        queue.Dequeue();
                }

                return output;
            }
        }

        public void Reset()
        {
            lock (sync)
            {
                queue.Clear();
            }
        }
    }
}
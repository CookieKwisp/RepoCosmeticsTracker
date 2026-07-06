using System;
using System.IO;
using System.Media;
using System.Text;

namespace RepoCosmeticTracker.Services
{
    /// <summary>
    /// Tiny procedural UI sounds, synthesized once into in-memory WAV streams —
    /// nothing to ship, nothing to load from disk. SoundPlayer's native
    /// PlaySound backend is fire-and-forget, which is exactly right for
    /// click feedback (a new sound simply replaces one still playing).
    /// </summary>
    public static class SoundService
    {
        public static bool Muted { get; set; }

        private const int SampleRate = 44100;

        private static readonly Lazy<SoundPlayer> CheckPlayer = new(() => Build(CheckSamples()));
        private static readonly Lazy<SoundPlayer> UncheckPlayer = new(() => Build(UncheckSamples()));
        private static readonly Lazy<SoundPlayer> ChimePlayer = new(() => Build(ChimeSamples()));

        public static void PlayCheck() => Play(CheckPlayer);
        public static void PlayUncheck() => Play(UncheckPlayer);
        public static void PlayChime() => Play(ChimePlayer);

        private static void Play(Lazy<SoundPlayer> player)
        {
            if (Muted)
                return;
            try
            {
                player.Value.Play();
            }
            catch
            {
                // No audio device / audio service hiccup — feedback sound is
                // never worth surfacing an error over.
            }
        }

        /// <summary>Soft rising "pop" — marking a cosmetic as owned.</summary>
        private static float[] CheckSamples()
        {
            var buf = new float[(int)(SampleRate * 0.15)];
            AddChirp(buf, startAt: 0, seconds: 0.15, f0: 520, f1: 790, amp: 0.26, decay: 26);
            return buf;
        }

        /// <summary>Falling version of the pop — un-owning.</summary>
        private static float[] UncheckSamples()
        {
            var buf = new float[(int)(SampleRate * 0.13)];
            AddChirp(buf, startAt: 0, seconds: 0.13, f0: 470, f1: 300, amp: 0.2, decay: 26);
            return buf;
        }

        /// <summary>Two-note chime — new unlocks synced in from the game's save.</summary>
        private static float[] ChimeSamples()
        {
            var buf = new float[(int)(SampleRate * 0.4)];
            AddChirp(buf, startAt: 0.0, seconds: 0.22, f0: 659.25, f1: 659.25, amp: 0.15, decay: 13); // E5
            AddChirp(buf, startAt: 0.09, seconds: 0.28, f0: 987.77, f1: 987.77, amp: 0.15, decay: 11); // B5
            return buf;
        }

        /// <summary>
        /// Adds a sine sweep with a fast attack, exponential decay, and a
        /// touch of second harmonic (keeps pure sines from sounding like a
        /// hearing test). Frequency is integrated per-sample so the glide is
        /// phase-continuous.
        /// </summary>
        private static void AddChirp(float[] buffer, double startAt, double seconds, double f0, double f1, double amp, double decay)
        {
            int start = (int)(startAt * SampleRate);
            int count = Math.Min((int)(seconds * SampleRate), buffer.Length - start);
            double phase = 0;

            for (int i = 0; i < count; i++)
            {
                double t = i / (double)SampleRate;
                double freq = f0 + (f1 - f0) * (i / (double)count);
                phase += 2 * Math.PI * freq / SampleRate;

                double envelope = Math.Min(1.0, t / 0.005) * Math.Exp(-t * decay);
                buffer[start + i] += (float)((Math.Sin(phase) + 0.25 * Math.Sin(phase * 2)) * amp * envelope);
            }
        }

        private static SoundPlayer Build(float[] samples)
        {
            var ms = new MemoryStream();
            int dataLength = samples.Length * 2;

            using (var w = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true))
            {
                w.Write("RIFF"u8.ToArray());
                w.Write(36 + dataLength);
                w.Write("WAVE"u8.ToArray());
                w.Write("fmt "u8.ToArray());
                w.Write(16);                     // fmt chunk size
                w.Write((short)1);               // PCM
                w.Write((short)1);               // mono
                w.Write(SampleRate);
                w.Write(SampleRate * 2);         // byte rate
                w.Write((short)2);               // block align
                w.Write((short)16);              // bits per sample
                w.Write("data"u8.ToArray());
                w.Write(dataLength);

                foreach (float s in samples)
                    w.Write((short)(Math.Clamp(s, -1f, 1f) * short.MaxValue));
            }

            ms.Position = 0;
            var player = new SoundPlayer(ms);
            player.Load();
            return player;
        }
    }
}

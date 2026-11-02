using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace DeadEye.Core;

/// <summary>
/// Sound effects go through winmm's waveOut API instead of Unity audio: in this IL2CPP
/// build AudioClip.Create + SetData yields an empty clip and the DownloadHandlerAudioClip
/// constructor is stripped (both proven dead ends in TLD_LanternBelt). Each sound opens its
/// OWN waveOut handle, so overlapping sounds never cut each other off — winmm!PlaySound is
/// single-channel per process and the mark chime used to kill the activation sound. Volume
/// is applied by scaling samples in memory, so no cached _v.wav copies are written.
/// </summary>
internal static class ModAudio
{
    private const string EnterFileName = "dead-eye-rdr2.wav";
    private const string MarkFileName = "dead-eye-mark.wav";

    private const int WaveMapper = -1;
    private const uint WhdrDone = 0x00000001;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct WaveFormatEx
    {
        internal ushort FormatTag;
        internal ushort Channels;
        internal uint SamplesPerSec;
        internal uint AvgBytesPerSec;
        internal ushort BlockAlign;
        internal ushort BitsPerSample;
        internal ushort Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHdr
    {
        internal IntPtr Data;
        internal uint BufferLength;
        internal uint BytesRecorded;
        internal IntPtr User;
        internal uint Flags;
        internal uint Loops;
        internal IntPtr Next;
        internal IntPtr Reserved;
    }

    [DllImport("winmm.dll")]
    private static extern int waveOutOpen(out IntPtr handle, int deviceId, ref WaveFormatEx format,
        IntPtr callback, IntPtr instance, uint flags);

    [DllImport("winmm.dll")]
    private static extern int waveOutPrepareHeader(IntPtr handle, IntPtr header, int size);

    [DllImport("winmm.dll")]
    private static extern int waveOutWrite(IntPtr handle, IntPtr header, int size);

    [DllImport("winmm.dll")]
    private static extern int waveOutReset(IntPtr handle);

    [DllImport("winmm.dll")]
    private static extern int waveOutUnprepareHeader(IntPtr handle, IntPtr header, int size);

    [DllImport("winmm.dll")]
    private static extern int waveOutClose(IntPtr handle);

    private static string? _enterPath;
    private static string? _markPath;
    private static bool _ready;
    private static bool _failed;

    private static CachedClip? _enterClip;
    private static CachedClip? _markClip;
    private static Playback? _background; // the long activation sound, the only one we stop

    internal static void PlayEnter() => Play(enter: true);
    internal static void PlayMark() => Play(enter: false);

    /// <summary>
    /// Warm up ahead of the first real use, called every Tick until done: validates the
    /// files and plays a short silent buffer once. The very first sound a process issues
    /// can be swallowed while the device is brought up — without this the first activation
    /// after game start stays silent (measured in-game).
    /// </summary>
    internal static void Tick()
    {
        if (_ready || _failed)
            return;
        if (!Prepare())
            return;

        try
        {
            var silence = new CachedClip(new byte[800 * 2], 8000, 1); // 0.1 s of silence
            Playback.Start(silence.Pcm, silence.Format);
            ModLog.Info("sound effects warmed up, first activation will be audible");
        }
        catch (Exception ex)
        {
            ModLog.Error($"audio warm-up failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Stop the activation sound only (it outlives a quick exit). Other sounds — ours and
    /// other mods' — keep playing: each owns its own waveOut handle.
    /// </summary>
    internal static void Stop() => _background?.Stop();

    private static void Play(bool enter)
    {
        if (!_ready && !Prepare())
            return;

        try
        {
            float volume = UnityEngine.Mathf.Clamp(DeadEyeMod.Settings?.SoundVolume ?? 1f, 0f, 1f);
            if (volume <= 0.001f)
                return; // silent setting

            CachedClip? clip = Load(enter);
            if (clip == null)
                return;

            byte[] pcm = volume < 0.999f ? Scale(clip.Pcm, volume) : clip.Pcm;
            var playback = Playback.Start(pcm, clip.Format);

            if (enter)
            {
                _background?.Stop(); // a re-entry replaces the previous activation sound
                _background = playback;
            }
        }
        catch (Exception ex)
        {
            ModLog.Error($"sound playback failed: {ex.Message}");
        }
    }

    private static CachedClip? Load(bool enter)
    {
        if (enter)
            return _enterClip ??= Decode(_enterPath!);
        return _markClip ??= Decode(_markPath!);
    }

    private static CachedClip? Decode(string path)
    {
        try
        {
            byte[] wav = File.ReadAllBytes(path);
            if (!TryParse(wav, out WavInfo info))
            {
                ModLog.Error($"not a 16-bit PCM WAV: {path}");
                return null;
            }

            var pcm = new byte[info.DataSize];
            Buffer.BlockCopy(wav, info.DataAt, pcm, 0, info.DataSize);
            return new CachedClip(pcm, info.SampleRate, info.Channels);
        }
        catch (Exception ex)
        {
            ModLog.Error($"reading {path} failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Volume applied to the samples directly — waveOut has no per-buffer gain.</summary>
    private static byte[] Scale(byte[] pcm, float volume)
    {
        var copy = new byte[pcm.Length];
        Buffer.BlockCopy(pcm, 0, copy, 0, pcm.Length);
        for (int i = 0; i + 1 < copy.Length; i += 2)
        {
            short sample = (short)(copy[i] | (copy[i + 1] << 8));
            int scaled = Math.Clamp((int)(sample * volume), short.MinValue, short.MaxValue);
            copy[i] = (byte)(scaled & 0xFF);
            copy[i + 1] = (byte)((scaled >> 8) & 0xFF);
        }
        return copy;
    }

    /// <summary>Locate and validate both WAV files; reports one failure, then gives up.</summary>
    private static bool Prepare()
    {
        if (_ready || _failed)
            return _ready;

        try
        {
            string? dir = ResolveAssetDir();
            if (dir == null)
                return Fail("cannot locate the mod directory");

            _enterPath = Validate(Path.Combine(dir, EnterFileName));
            _markPath = Validate(Path.Combine(dir, MarkFileName));
            if (_enterPath == null || _markPath == null)
                return Fail($"missing or invalid WAV in {dir} (expected {EnterFileName} / {MarkFileName})");

            _ready = true;
            ModLog.Info("sound effects ready (waveOut, Mods/DeadEye/Asset)");
            return true;
        }
        catch (Exception ex)
        {
            return Fail($"preparing sound effects failed: {ex.Message}");
        }
    }

    private static string? Validate(string path)
    {
        if (!File.Exists(path))
            return null;
        byte[] wav = File.ReadAllBytes(path);
        return TryParse(wav, out _) ? path : null;
    }

    private sealed class CachedClip
    {
        internal CachedClip(byte[] pcm, int sampleRate, int channels)
        {
            Pcm = pcm;
            Format = new WaveFormatEx
            {
                FormatTag = 1, // PCM
                Channels = (ushort)channels,
                SamplesPerSec = (uint)sampleRate,
                AvgBytesPerSec = (uint)(sampleRate * channels * 2),
                BlockAlign = (ushort)(channels * 2),
                BitsPerSample = 16,
                Size = 0,
            };
        }

        internal byte[] Pcm { get; }
        internal WaveFormatEx Format { get; }
    }

    /// <summary>
    /// One playing sound: its own waveOut handle, its own buffer, its own watcher thread.
    /// Handles are independent, so several sounds play at once without cutting each other.
    /// </summary>
    private sealed class Playback
    {
        private IntPtr _handle;
        private IntPtr _header;
        private IntPtr _data;
        private volatile bool _stopped;

        internal static Playback Start(byte[] pcm, WaveFormatEx format)
        {
            var playback = new Playback();
            var thread = new Thread(() => playback.Run(pcm, format))
            {
                IsBackground = true,
                Name = "DeadEyeAudio",
            };
            thread.Start();
            return playback;
        }

        internal void Stop()
        {
            _stopped = true;
            try
            {
                IntPtr handle = _handle;
                if (handle != IntPtr.Zero)
                    waveOutReset(handle); // marks every buffer done; the thread cleans up
            }
            catch
            {
                // device already gone
            }
        }

        private void Run(byte[] pcm, WaveFormatEx format)
        {
            try
            {
                if (waveOutOpen(out _handle, WaveMapper, ref format, IntPtr.Zero, IntPtr.Zero, 0) != 0)
                {
                    _handle = IntPtr.Zero;
                    return;
                }

                _data = Marshal.AllocHGlobal(pcm.Length);
                Marshal.Copy(pcm, 0, _data, pcm.Length);

                int headerSize = Marshal.SizeOf<WaveHdr>();
                _header = Marshal.AllocHGlobal(headerSize);
                var header = new WaveHdr { Data = _data, BufferLength = (uint)pcm.Length };
                Marshal.StructureToPtr(header, _header, false);

                waveOutPrepareHeader(_handle, _header, headerSize);
                waveOutWrite(_handle, _header, headerSize);

                while (!_stopped)
                {
                    var current = Marshal.PtrToStructure<WaveHdr>(_header);
                    if ((current.Flags & WhdrDone) != 0)
                        break;
                    Thread.Sleep(20);
                }
            }
            catch (Exception ex)
            {
                ModLog.Error($"waveOut playback failed: {ex.Message}");
            }
            finally
            {
                Cleanup();
            }
        }

        private void Cleanup()
        {
            try
            {
                int headerSize = Marshal.SizeOf<WaveHdr>();
                if (_handle != IntPtr.Zero)
                {
                    waveOutReset(_handle);
                    if (_header != IntPtr.Zero)
                        waveOutUnprepareHeader(_handle, _header, headerSize);
                    waveOutClose(_handle);
                    _handle = IntPtr.Zero;
                }
                if (_header != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_header);
                    _header = IntPtr.Zero;
                }
                if (_data != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_data);
                    _data = IntPtr.Zero;
                }
            }
            catch
            {
                // nothing sensible to do while tearing down
            }
        }
    }

    private readonly struct WavInfo
    {
        internal WavInfo(int dataAt, int dataSize, int sampleRate, int channels)
        {
            DataAt = dataAt;
            DataSize = dataSize;
            SampleRate = sampleRate;
            Channels = channels;
        }

        internal int DataAt { get; }
        internal int DataSize { get; }
        internal int SampleRate { get; }
        internal int Channels { get; }
    }

    /// <summary>RIFF/WAVE parser: 16-bit PCM only.</summary>
    private static bool TryParse(byte[] wav, out WavInfo info)
    {
        info = default;
        if (wav.Length < 44 || wav[0] != 'R' || wav[1] != 'I' || wav[2] != 'F' || wav[3] != 'F')
            return false;

        int channels = 0, sampleRate = 0, bits = 0;
        int pos = 12;
        while (pos + 8 <= wav.Length)
        {
            int size = BitConverter.ToInt32(wav, pos + 4);
            if (wav[pos] == 'f' && wav[pos + 1] == 'm' && wav[pos + 2] == 't' && wav[pos + 3] == ' ')
            {
                channels = BitConverter.ToInt16(wav, pos + 10);
                sampleRate = BitConverter.ToInt32(wav, pos + 12);
                bits = BitConverter.ToInt16(wav, pos + 22);
            }
            else if (wav[pos] == 'd' && wav[pos + 1] == 'a' && wav[pos + 2] == 't' && wav[pos + 3] == 'a')
            {
                int at = pos + 8;
                int len = size > 0 && at + size <= wav.Length ? size : wav.Length - at;
                if (channels <= 0 || sampleRate <= 0 || bits != 16 || len <= 0)
                    return false;
                info = new WavInfo(at, len, sampleRate, channels);
                return true;
            }
            pos += 8 + size + (size & 1); // chunks are word-aligned
        }
        return false;
    }

    /// <summary>Asset folder next to the dll: Mods\DeadEye\Asset.</summary>
    private static string? ResolveAssetDir()
    {
        try
        {
            string? dir = Path.GetDirectoryName(typeof(ModAudio).Assembly.Location);
            if (string.IsNullOrEmpty(dir))
            {
                ModLog.Error("cannot locate the mod directory for sound files");
                return null;
            }
            return Path.Combine(dir, "DeadEye", "Asset");
        }
        catch (Exception ex)
        {
            ModLog.Error($"locating the Asset folder failed: {ex.Message}");
            return null;
        }
    }

    private static bool Fail(string reason)
    {
        if (_failed)
            return false;
        _failed = true;
        ModLog.Error($"sound effects unavailable: {reason}");
        return false;
    }
}

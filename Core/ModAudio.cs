using System;
using System.IO;
using System.Runtime.InteropServices;

namespace DeadEye.Core;

/// <summary>
/// Sound effects are played through winmm!PlaySound instead of Unity audio: in this
/// IL2CPP build AudioClip.Create + SetData yields an empty clip (length/frequency/channels
/// all zero) and the DownloadHandlerAudioClip constructor is stripped — both proven dead
/// ends in TLD_LanternBelt. Windows decodes the WAV itself, independent of the game's
/// Wwise mix. PlaySound has no volume argument, so volume is applied by pre-scaling the
/// samples into a cached copy. Files live in Mods\DeadEye\Asset next to the dll.
/// </summary>
internal static class ModAudio
{
    private const string EnterFileName = "dead-eye-rdr2.wav";
    private const string MarkFileName = "dead-eye-mark.wav";

    private const uint SndAsync = 0x0001;
    private const uint SndNoDefault = 0x0002;
    private const uint SndFilename = 0x00020000;
    private const uint SndPurge = 0x0040;

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern bool PlaySound(string? pszSound, IntPtr hmod, uint fdwSound);

    private static string? _enterPath;
    private static string? _markPath;
    private static string? _scaledSource;      // source of the cached volume-scaled copy
    private static string? _scaledPath;        // the cached volume-scaled copy itself
    private static float _scaledVolume = -1f;
    private static bool _ready;
    private static bool _failed;

    internal static void PlayEnter() => Play(_enterPath);
    internal static void PlayMark() => Play(_markPath);

    private static void Play(string? path)
    {
        if (!_ready && !Prepare())
            return;
        if (path == null)
            return;

        try
        {
            float volume = UnityEngine.Mathf.Clamp(DeadEyeMod.Settings?.SoundVolume ?? 1f, 0f, 1f);
            if (volume <= 0.001f)
                return; // silent setting

            // PlaySound has no volume argument: play a pre-scaled copy instead (cached
            // per source file, regenerated only when the volume changes).
            if (volume < 0.999f)
            {
                string scaled = ScaleTo(path, volume);
                if (scaled.Length == 0)
                    return;
                path = scaled;
            }

            if (!PlaySound(path, IntPtr.Zero, SndFilename | SndAsync | SndNoDefault))
                ModLog.Error($"system sound playback refused: {path}");
        }
        catch (Exception ex)
        {
            ModLog.Error($"sound playback failed: {ex.Message}");
        }
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
            ModLog.Info("sound effects ready (system playback, Mods/DeadEye/Asset)");
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

    /// <summary>Scale the data chunk by <paramref name="volume"/> into a cached sibling file.</summary>
    private static string ScaleTo(string source, float volume)
    {
        if (_scaledSource == source && Math.Abs(_scaledVolume - volume) < 0.01f &&
            _scaledPath != null && File.Exists(_scaledPath))
            return _scaledPath;

        try
        {
            byte[] wav = File.ReadAllBytes(source);
            if (!TryParse(wav, out WavInfo info))
                return "";

            var copy = (byte[])wav.Clone();
            for (int i = info.DataAt; i + 1 < info.DataAt + info.DataSize; i += 2)
            {
                short sample = (short)(copy[i] | (copy[i + 1] << 8));
                int scaled = Math.Clamp((int)(sample * volume), short.MinValue, short.MaxValue);
                copy[i] = (byte)(scaled & 0xFF);
                copy[i + 1] = (byte)((scaled >> 8) & 0xFF);
            }

            string target = Path.Combine(
                Path.GetDirectoryName(source) ?? "",
                Path.GetFileNameWithoutExtension(source) + "_v.wav");
            File.WriteAllBytes(target, copy);

            _scaledSource = source;
            _scaledPath = target;
            _scaledVolume = volume;
            return target;
        }
        catch (Exception ex)
        {
            ModLog.Error($"creating the volume-scaled copy failed: {ex.Message}");
            return "";
        }
    }

    private readonly struct WavInfo
    {
        internal WavInfo(int dataAt, int dataSize)
        {
            DataAt = dataAt;
            DataSize = dataSize;
        }

        internal int DataAt { get; }
        internal int DataSize { get; }
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
                info = new WavInfo(at, len);
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

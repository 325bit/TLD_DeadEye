using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace DeadEye.Core;

/// <summary>
/// Diagnostics for the draw-animation work, in two halves.
/// (1) Once per session it dumps every clip name in the first-person Animator controller — the
/// controller is a binary asset, so this is the only way to learn which draw clips exist.
/// (2) Every animator write that matters (all triggers, plus the weapon bools and ints) is named
/// through the game's own hash fields, and each equip/unstow starts a short timeline of the
/// state and clips actually playing — Verbose only, nothing here changes behaviour.
/// </summary>
internal static class DrawProbe
{
    /// <summary>How long a draw is followed, in unscaled seconds — a little longer than the
    /// equip clip itself.</summary>
    private const float SessionSeconds = 2.4f;

    private const float SampleInterval = 0.08f;
    private const float WriteInterval = 0.20f;
    private const int ClipChunk = 6;

    /// <summary>Reload plus the raise that may follow it, so the timeline covers both.</summary>
    private const float ReloadSessionSeconds = 4.5f;

    private static readonly Dictionary<int, string> Names = new();
    private static readonly HashSet<int> Interesting = new();
    private static readonly Dictionary<int, float> LastWrite = new();

    private static bool _namesBuilt;
    private static bool _clipsDumped;

    private sealed class Session
    {
        internal PlayerAnimation Anim = null!;
        internal string Tag = string.Empty;
        internal float StartAt;
        internal float EndsAt;
        internal float NextSample;
    }

    private static Session? _session;
    private static string _lastSignature = string.Empty;

    private static bool Enabled => DeadEyeMod.Settings?.Verbose == true;

    /// <summary>Starts (or restarts) the timeline for one draw.</summary>
    internal static void Begin(PlayerAnimation? animation, string tag, float seconds = SessionSeconds)
    {
        if (animation == null || !Enabled)
            return;

        _session = new Session
        {
            Anim = animation,
            Tag = tag,
            StartAt = Time.unscaledTime,
            EndsAt = Time.unscaledTime + seconds,
            NextSample = 0f,
        };
    }

    /// <summary>
    /// Every animator write the game makes through PlayerAnimation. Triggers are always named;
    /// the bools/ints are logged only for the weapon-related parameters and at most every
    /// 200 ms, so the motion parameters do not drown the file.
    /// </summary>
    internal static void OnWrite(PlayerAnimation? animation, string kind, int id, string value)
    {
        if (animation == null || !Enabled || id == 0)
            return;

        try
        {
            BuildNames(animation);
            string name = Names.TryGetValue(id, out string? known) ? known : $"hash 0x{id:X8}";

            if (kind != "trigger")
            {
                if (!Interesting.Contains(id))
                    return;

                float now = Time.unscaledTime;
                if (LastWrite.TryGetValue(id, out float last) && now - last < WriteInterval)
                    return;
                LastWrite[id] = now;
            }

            ModLog.Info($"draw probe: {kind} {name} = {value}");

            // Every trigger starts its own timeline, so an animation that no hook covers (the
            // reload flourish, a brandish, an animated interaction) is still captured. A session
            // that started just now keeps its more descriptive tag.
            if (kind == "trigger" && (_session == null || Time.unscaledTime - _session.StartAt > 0.2f))
            {
                float seconds = name.Contains("Reload") ? ReloadSessionSeconds : SessionSeconds;
                Begin(animation, name, seconds);
            }
        }
        catch (System.Exception ex)
        {
            ModLog.Debug($"draw probe write log failed: {ex.Message}");
        }
    }

    internal static void Tick()
    {
        if (!Enabled)
        {
            _session = null;
            return;
        }

        DumpClipsOnce();

        Session? session = _session;
        float now = Time.unscaledTime;

        if (session != null && now > session.EndsAt)
        {
            Sample(session, force: true);
            _session = null;
            session = null;
        }

        if (session != null)
        {
            if (now < session.NextSample)
                return;
            session.NextSample = now + SampleInterval;
        }

        Sample(session, force: false);
    }

    /// <summary>
    /// One line per state/clip change. Sampling runs even outside a draw session: the animation
    /// being hunted fires no equip trigger, so a tape that only records what it expects would
    /// miss it again.
    /// </summary>
    private static void Sample(Session? session, bool force)
    {
        try
        {
            PlayerAnimation? animation = session != null ? session.Anim : GameManager.GetPlayerAnimationComponent();
            Animator? animator = animation != null ? animation.m_Animator : null;
            if (animation == null || animator == null)
                return;

            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            string clips = Clips(animator);
            string signature = $"{state.fullPathHash}|{clips}";
            if (!force && signature == _lastSignature)
                return;
            _lastSignature = signature;

            string prefix = session != null
                ? $"[{session.Tag}] +{(Time.unscaledTime - session.StartAt) * 1000f:F0}ms"
                : "state";

            ModLog.Info($"draw probe {prefix}: state {state.fullPathHash} " +
                        $"({state.normalizedTime:F2} of {state.length:F2}s), " +
                        $"clips {clips}, {Parameters(animator, animation)}");
        }
        catch (System.Exception ex)
        {
            ModLog.Debug($"draw probe sample failed: {ex.Message}");
        }
    }

    private static void DumpClipsOnce()
    {
        if (_clipsDumped)
            return;

        try
        {
            PlayerAnimation? animation = GameManager.GetPlayerAnimationComponent();
            Animator? animator = animation != null ? animation.m_Animator : null;
            RuntimeAnimatorController? controller = animator != null ? animator.runtimeAnimatorController : null;
            var clips = controller != null ? controller.animationClips : null;
            if (clips == null || clips.Length == 0)
                return;

            _clipsDumped = true;

            var names = new List<string>(clips.Length);
            for (int i = 0; i < clips.Length; i++)
            {
                AnimationClip? clip = clips[i];
                if (clip != null && clip.name != null)
                    names.Add(clip.name);
            }
            names.Sort(System.StringComparer.OrdinalIgnoreCase);

            ModLog.Info($"draw probe: first-person controller holds {names.Count} clips");
            for (int i = 0; i < names.Count; i += ClipChunk)
            {
                int take = Mathf.Min(ClipChunk, names.Count - i);
                ModLog.Info("draw probe: clip " + string.Join(", ", names.GetRange(i, take)));
            }
        }
        catch (System.Exception ex)
        {
            ModLog.Error($"draw probe: controller dump failed: {ex}");
        }
    }

    /// <summary>hash → the game's own field name, so a log line reads `Trigger_Generic_Unstow`
    /// instead of an int. Built once from the instance; every parameter field is an int.</summary>
    private static void BuildNames(PlayerAnimation animation)
    {
        if (_namesBuilt)
            return;

        _namesBuilt = true;
        foreach (PropertyInfo property in typeof(PlayerAnimation).GetProperties(
                     BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (property.PropertyType != typeof(int))
                continue;

            string name = property.Name;
            if (!name.StartsWith("m_AnimParameter_") && !name.StartsWith("m_AnimState_"))
                continue;

            try
            {
                object? raw = property.GetValue(animation);
                if (raw is not int hash || hash == 0)
                    continue;

                string shortName = name.Replace("m_AnimParameter_", string.Empty)
                                       .Replace("m_AnimState_", "state:");
                Names[hash] = shortName;
                if (IsWeaponParameter(shortName))
                    Interesting.Add(hash);
            }
            catch (System.Exception)
            {
                // a property the interop stub cannot read is simply not named
            }
        }
    }

    private static bool IsWeaponParameter(string name)
        => name.Contains("IsLoaded") || name.Contains("RoundsLoaded") || name.Contains("ReloadCount")
        || name.Contains("IsEquipped") || name.Contains("IsWeapon") || name.Contains("WeaponId")
        || name.Contains("WeaponFatigue") || name.Contains("HasUnloadedAmmo") || name.Contains("IsAiming");

    private static string Clips(Animator animator)
    {
        try
        {
            var clips = animator.GetCurrentAnimatorClipInfo(0);
            if (clips == null || clips.Length == 0)
                return "none";

            var parts = new List<string>(clips.Length);
            for (int i = 0; i < clips.Length; i++)
            {
                AnimationClip? clip = clips[i].clip;
                parts.Add(clip != null ? $"{clip.name}({clips[i].weight:F2})" : "null");
            }
            return string.Join(" ", parts);
        }
        catch (System.Exception ex)
        {
            return $"unreadable({ex.Message})";
        }
    }

    private static string Parameters(Animator animator, PlayerAnimation animation)
    {
        try
        {
            return $"IsLoaded {animator.GetBool(animation.m_AnimParameter_IsLoaded)}, " +
                   $"RoundsLoaded {animator.GetInteger(animation.m_AnimParameter_RoundsLoaded)}, " +
                   $"IsEquipped {animator.GetBool(animation.m_AnimParameter_IsEquipped)}";
        }
        catch (System.Exception ex)
        {
            return $"params unreadable({ex.Message})";
        }
    }
}

using UnityEngine;

namespace DeadEye.Core;

/// <summary>
/// Lerp helper for the bullet-time time scale.
/// Mirrors TT-PauseOnRadial: both Time.timeScale and GameManager.m_GlobalTimeScale must be
/// driven together, otherwise parts of the game keep running at full speed.
/// Runs on MelonCoroutines (unscaled, unaffected by the slowdown itself).
/// </summary>
internal static class TimeScaler
{
    private static object? _routine;

    /// <summary>True while this mod (not the game or another mod) owns a non-normal time scale.</summary>
    internal static bool Owned { get; private set; }

    internal static void LerpTo(float value, float speed)
    {
        StopRoutine();
        _routine = MelonLoader.MelonCoroutines.Start(LerpRoutine(value, Mathf.Max(0.05f, speed)));
        Owned = !Mathf.Approximately(value, 1f);
    }

    private static System.Collections.IEnumerator LerpRoutine(float value, float speed)
    {
        float start = Time.timeScale;
        float t = 0f;
        while (!Mathf.Approximately(value, Time.timeScale))
        {
            t += Time.unscaledDeltaTime * speed;
            Time.timeScale = GameManager.m_GlobalTimeScale = Mathf.Lerp(start, value, t);
            yield return new WaitForEndOfFrame();
        }
    }

    /// <summary>
    /// Hard reset. Only call when we know we own the time scale (state Inactive, scene change,
    /// exhausted / dead player) so we never stomp another system's slowdown.
    /// </summary>
    internal static void ForceRestore()
    {
        StopRoutine();
        Time.timeScale = GameManager.m_GlobalTimeScale = 1f;
        Owned = false;
    }

    private static void StopRoutine()
    {
        if (_routine != null)
        {
            MelonLoader.MelonCoroutines.Stop(_routine);
            _routine = null;
        }
    }
}

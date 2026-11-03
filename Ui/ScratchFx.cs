using UnityEngine;

namespace DeadEye.Core;

/// <summary>
/// RDR2-style white scratch wipe on Dead Eye activation: a bright, feathery white streak
/// sweeps horizontally across the whole screen in ~0.5 s (unscaled time), starting from a
/// random side — a hot head band with a torn, streaky tail fading behind it, like the
/// reference clip (10-11 s). Drawn in OnGUI from one procedurally generated texture
/// (white with a shaped alpha channel), mirrored at draw time for the right-to-left
/// direction. Purely cosmetic: no game objects, shaders, cameras or post-processing are
/// touched, and the strip self-terminates — it finishes sweeping even if the mode is
/// cancelled mid-reveal.
/// </summary>
internal static class ScratchFx
{
    private const float DurationSeconds = 0.5f;
    private const float SweepWidthFraction = 0.62f; // strip width as a fraction of screen width
    private const int TexW = 256;
    private const int TexH = 128;

    private static Texture2D? _strip;
    private static float _start = -1f;
    private static bool _fromLeft;

    /// <summary>Arm one sweep. Called when the mode actually enters (not on refused tries).</summary>
    internal static void Trigger()
    {
        _start = Time.unscaledTime;
        _fromLeft = UnityEngine.Random.value < 0.5f;
        BuildStrip(); // regenerate per activation: every scratch is a new tear
    }

    /// <summary>Called from OnGUI every event; draws nothing unless a sweep is running.</summary>
    internal static void Draw()
    {
        if (_start < 0f || _strip == null)
            return;

        float p = (Time.unscaledTime - _start) / DurationSeconds;
        if (p >= 1f)
        {
            _start = -1f;
            return;
        }

        float w = Screen.width * SweepWidthFraction;
        float h = Screen.height;

        // Ease-out: the head whips in fast and decelerates off the far edge, so the tail
        // clears the screen by the end — no abrupt disappearance at full alpha.
        float eased = 1f - (1f - p) * (1f - p);
        float head = Mathf.Lerp(0f, Screen.width + w, eased); // head position in travel direction
        float x = _fromLeft ? head - w : Screen.width - head;
        var rect = new Rect(x, 0f, w, h);

        Matrix4x4 saved = GUI.matrix;
        if (!_fromLeft)
        {
            // The texture is authored left-tail -> right-head; mirror the draw for R2L.
            var pivot = new Vector2(rect.x + rect.width / 2f, rect.y + rect.height / 2f);
            GUIUtility.ScaleAroundPivot(new Vector2(-1f, 1f), pivot);
        }
        GUI.DrawTexture(rect, _strip, ScaleMode.StretchToFill, true);
        GUI.matrix = saved;
    }

    /// <summary>
    /// The strip texture: x = tail (0) to head (1). Alpha = a power envelope (faint tail,
    /// hot head band) scaled by column streaks with occasional torn gaps, per-pixel grain,
    /// and a slightly wavy head line — that combination is what reads as "scratch" instead
    /// of a clean gradient.
    /// </summary>
    private static void BuildStrip()
    {
        if (_strip == null)
        {
            _strip = new Texture2D(TexW, TexH, TextureFormat.RGBA32, false, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
        }

        // Per-column streak strength with occasional near-gaps (the "tears").
        var streak = new float[TexW];
        for (int x = 0; x < TexW; x++)
        {
            float s = 0.55f + 0.45f * UnityEngine.Random.value;
            if (UnityEngine.Random.value < 0.10f)
                s *= 0.35f;
            streak[x] = s;
        }

        // Per-row head offset: the tear line is wavy, not a straight vertical edge.
        var shift = new float[TexH];
        float phase = UnityEngine.Random.value * Mathf.PI * 2f;
        for (int y = 0; y < TexH; y++)
            shift[y] = (UnityEngine.Random.value - 0.5f) * TexW * 0.05f
                       + Mathf.Sin(y / (float)TexH * Mathf.PI * 2f + phase) * TexW * 0.03f;

        var px = new Color32[TexW * TexH];
        for (int y = 0; y < TexH; y++)
        {
            int row = y * TexW;
            float rowShift = shift[y];
            for (int x = 0; x < TexW; x++)
            {
                float t = Mathf.Clamp01((x + rowShift) / (TexW - 1f)); // 0 tail, 1 head
                float envelope = Mathf.Pow(t, 2.4f);
                if (t > 0.88f)
                    envelope = Mathf.Lerp(envelope, 1.05f, (t - 0.88f) / 0.12f); // hot head
                float grain = 0.72f + 0.28f * UnityEngine.Random.value;
                float a = Mathf.Clamp01(envelope * streak[x] * grain);
                px[row + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }

        _strip.SetPixels32(px);
        _strip.Apply(false);
    }
}

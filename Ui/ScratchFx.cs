using UnityEngine;

namespace DeadEye.Core;

/// <summary>
/// RDR2-style white scratch on Dead Eye activation — veil variant. Two layers:
/// 1. A full-screen white VEIL that appears the moment the mode starts (same frame
///    as the yellow tint) and fades out globally over the second half of the sweep. Its
///    alpha has NO spatial gradient, so there is no moving visibility boundary — the
///    "onset line" class of artifact (0.7.27-0.7.32) cannot exist by construction.
/// 2. The bright head GLOW: a soft wide band (forward and backward Gaussian, plus
///    the vertical lower-middle mask) that sweeps across the veil in ~0.6 s unscaled from a
///    random side, with a small random lean. This is the only moving element; every edge
///    of it is a Gaussian. Width and sweep duration are user settings.
/// Both layers are procedural RGBA textures drawn in OnGUI (one DrawTexture each).
/// Purely cosmetic: no game objects, shaders, cameras or post-processing are touched, and
/// the sweep self-terminates — it finishes even if the mode is cancelled mid-reveal.
/// </summary>
internal static class ScratchFx
{
    private const float DurationSeconds = 0.6f;
    private const float VeilAlpha = 0.13f;        // peak opacity of the static fibrous veil
    private const float VeilFadeStart = 0.45f;    // sweep progress where the veil starts fading
    private const float BlobWidthFraction = 0.30f; // blob rect width as a fraction of screen width
    private const int TexW = 256;
    private const int TexH = 128;

    private static Texture2D? _veil; // full-screen fibrous fog, no spatial alpha ramp
    private static Texture2D? _blob; // the sweeping head glow: tail (0) -> centre (0.5) -> glow (1)
    private static float _start = -1f;
    private static bool _fromLeft;
    private static float _tilt; // small random lean of the sweeping blob, degrees

    /// <summary>Arm one sweep. Called when the mode actually enters (not on refused tries).</summary>
    internal static void Trigger()
    {
        _start = Time.unscaledTime;
        _fromLeft = UnityEngine.Random.value < 0.5f;
        _tilt = UnityEngine.Random.Range(3.5f, 8f) * (UnityEngine.Random.value < 0.5f ? -1f : 1f);
        BuildVeil();
        BuildBlob();
    }

    /// <summary>Called from OnGUI every event; draws nothing unless a sweep is running.</summary>
    internal static void Draw()
    {
        if (_start < 0f || _veil == null || _blob == null)
            return;

        float duration = Mathf.Max(0.1f, DeadEyeMod.Settings?.ScratchDuration ?? DurationSeconds);
        float p = (Time.unscaledTime - _start) / duration;
        if (p >= 1f)
        {
            _start = -1f;
            return;
        }

        DrawVeil(p);
        DrawBlob(p);
    }

    /// <summary>
    /// Static fibrous veil: full-screen from the first frame, fading out GLOBALLY over the
    /// last part of the sweep. A whole-screen alpha fade reads as the fog lifting — never
    /// as a boundary, because every pixel dims by the same amount on the same frame.
    /// </summary>
    private static void DrawVeil(float p)
    {
        float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((p - VeilFadeStart) / (1f - VeilFadeStart)));
        GUI.color = new Color(1f, 1f, 1f, VeilAlpha * fade);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _veil, ScaleMode.StretchToFill, true);
        GUI.color = Color.white;
    }

    /// <summary>The sweeping head glow: texture-authored glow edge->centre->glow edge, mirrored for R2L.</summary>
    private static void DrawBlob(float p)
    {
        float w = Screen.width * Mathf.Clamp01(DeadEyeMod.Settings?.ScratchBlobWidth ?? BlobWidthFraction);

        // Near-linear motion with a soft landing (the reference reads as constant speed).
        float eased = 1f - Mathf.Pow(1f - p, 1.2f);
        float centre = Mathf.Lerp(-w / 2f, Screen.width + w / 2f, eased); // blob centre, screen space

        // Tilted draw: rotate by the small random lean around the blob centre. The rect is
        // oversized vertically (1.35x) so its tips still reach past the screen's top/bottom
        // edges after the rotation.
        float h = Screen.height * 1.35f;
        var rect = new Rect(centre - w / 2f, (Screen.height - h) / 2f, w, h);

        Matrix4x4 saved = GUI.matrix;
        GUI.color = new Color(1f, 1f, 1f, 1f - 0.4f * p);
        var pivot = new Vector2(centre, Screen.height / 2f);
        GUIUtility.RotateAroundPivot(_tilt, pivot);
        if (!_fromLeft)
        {
            // The texture is authored left-tail -> right-head; mirror the draw for R2L.
            GUIUtility.ScaleAroundPivot(new Vector2(-1f, 1f), pivot);
        }
        GUI.DrawTexture(rect, _blob, ScaleMode.StretchToFill, true);
        GUI.matrix = saved;
        GUI.color = Color.white;
    }

    private static Texture2D NewTex()
    {
        return new Texture2D(TexW, TexH, TextureFormat.RGBA32, false, true)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
    }

    /// <summary>
    /// The veil: uniform along x AND y apart from the gentle lower-middle emphasis and a
    /// whisper of per-pixel grain. The old per-row wisp field rendered as full-width
    /// horizontal stripes (each of the 128 texture rows stretches to ~8 px of screen and
    /// adjacent rows differed by up to 5x in strength) — that is where the white
    /// scanlines across the whole frame came from.
    /// </summary>
    private static void BuildVeil()
    {
        if (_veil == null)
            _veil = NewTex();

        var px = new Color32[TexW * TexH];
        for (int y = 0; y < TexH; y++)
        {
            float v = y / (TexH - 1f);
            float dv = (v - 0.62f) / 0.32f;
            float vmask = 0.55f + 0.45f * Mathf.Exp(-dv * dv);

            int row = y * TexW;
            for (int x = 0; x < TexW; x++)
            {
                float grain = 0.92f + 0.08f * UnityEngine.Random.value;
                float a = vmask * grain;
                px[row + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
            }
        }

        _veil.SetPixels32(px);
        _veil.Apply(false);
    }

    /// <summary>
    /// The sweeping glow: a round-edged blob centred in the strip (Gaussian along x, so it
    /// fades out BOTH ways from the centre — no leading or trailing cut), weighted by the
    /// lower-middle vertical mask. Smooth on purpose: the row-fibre modulation of earlier
    /// builds is what drew horizontal lines through the band.
    /// </summary>
    private static void BuildBlob()
    {
        if (_blob == null)
            _blob = NewTex();

        var px = new Color32[TexW * TexH];
        for (int y = 0; y < TexH; y++)
        {
            float v = y / (TexH - 1f);
            float dv = (v - 0.62f) / 0.30f;
            float vmask = 0.15f + 0.85f * Mathf.Exp(-dv * dv);

            int row = y * TexW;
            for (int x = 0; x < TexW; x++)
            {
                float t = x / (TexW - 1f);
                float dx = (t - 0.5f) / 0.22f;
                float blob = Mathf.Exp(-dx * dx); // fades BOTH ways from the centre
                float grain = 0.94f + 0.06f * UnityEngine.Random.value;
                float a = vmask * blob * grain;
                px[row + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
            }
        }

        _blob.SetPixels32(px);
        _blob.Apply(false);
    }
}

using UnityEngine;

namespace DeadEye.Core;

/// <summary>
/// RDR2-style white scratch wipe on Dead Eye activation, modelled on the reference clip
/// (10.65-10.92 s): a cloud-like bright head with a long feathery tail hugging the
/// lower-middle of the screen, sweeping across in ~0.6 s (unscaled, near-linear motion)
/// from a random side, with a small random lean.
/// The yellow tint is NOT split at the head any more (0.7.27-0.7.30 tried that and every
/// hard-line artifact we chased — unclipped rect edges, the rect-end clip line, a 1 px
/// float seam between the solid and gradient rects — lived in exactly that code path).
/// The canvas overlay goes full yellow the moment the mode starts (vanilla behaviour) and
/// the white scratch sweeps OVER it: one texture, one DrawTexture call, no seams by
/// construction.
/// Drawn in OnGUI from one procedurally generated RGBA strip (regenerated per activation:
/// row streaks, torn gaps, grain, wavy head line) mirrored at draw time for R2L.
/// Purely cosmetic: no game objects, shaders, cameras or post-processing are touched, and
/// the sweep self-terminates — it finishes even if the mode is cancelled mid-reveal.
/// </summary>
internal static class ScratchFx
{
    private const float DurationSeconds = 0.6f;
    private const float SweepWidthFraction = 0.55f;   // strip width as a fraction of screen width
    private const float HeadLead = 0.25f;             // strip fraction reserved for the forward glow past the head
    private const int TexW = 256;
    private const int TexH = 128;

    private static Texture2D? _strip; // white streak: tail (x=0) -> head (x=1)
    private static float _start = -1f;
    private static bool _fromLeft;
    private static float _tilt; // small random lean of the whole streak, degrees

    /// <summary>Arm one sweep. Called when the mode actually enters (not on refused tries).</summary>
    internal static void Trigger()
    {
        _start = Time.unscaledTime;
        _fromLeft = UnityEngine.Random.value < 0.5f;
        _tilt = UnityEngine.Random.Range(3.5f, 8f) * (UnityEngine.Random.value < 0.5f ? -1f : 1f);
        BuildStrip();
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

        // Near-linear head motion with a soft landing (the reference reads as constant
        // speed): strong easing made the bright head cross in ~0.12 s, half the reference.
        float eased = 1f - Mathf.Pow(1f - p, 1.2f);
        float head = Mathf.Lerp(0f, Screen.width + w, eased); // head position in travel direction
        float headX = _fromLeft ? head : Screen.width - head; // head position in SCREEN space

        DrawStreak(headX, w, p);
    }

    /// <summary>The white comet: texture-authored tail->head, mirrored for R2L.</summary>
    private static void DrawStreak(float headX, float w, float progress)
    {
        // Pin the BLOB CENTRE (texture x = 1-HeadLead, the brightest point) to headX, not
        // the texture edge: the strip carries the forward glow past the head line, so the
        // bright core never ends in a hard vertical cut.
        //   L2R: blob at rect.x + 0.75w  -> rect starts at headX - 0.75w.
        //   R2L: mirrored in place, blob at rect.x + 0.25w -> rect starts at headX - 0.25w.
        float x = headX - (_fromLeft ? 1f - HeadLead : HeadLead) * w;

        // Tilted draw: rotate the whole strip by the small random lean around the head
        // point. The strip is oversized vertically (1.35x) so its tips still reach past
        // the screen's top/bottom edges after the rotation.
        float h = Screen.height * 1.35f;
        var rect = new Rect(x, (Screen.height - h) / 2f, w, h);

        // The tail thins out while the head is still travelling: in the reference the
        // wisps behind the head dissipate instead of hanging as a full-screen haze.
        Matrix4x4 saved = GUI.matrix;
        GUI.color = new Color(1f, 1f, 1f, 1f - 0.4f * progress);
        var pivot = new Vector2(headX, Screen.height / 2f);
        GUIUtility.RotateAroundPivot(_tilt, pivot);
        if (!_fromLeft)
        {
            // The texture is authored left-tail -> right-head; mirror the draw for R2L.
            GUIUtility.ScaleAroundPivot(new Vector2(-1f, 1f), pivot);
        }
        GUI.DrawTexture(rect, _strip, ScaleMode.StretchToFill, true);
        GUI.matrix = saved;
        GUI.color = Color.white;
    }

    /// <summary>
    /// The strip texture: x = tail (0) to head centre (1-HeadLead) to forward-glow edge (1).
    /// Alpha = a vertical Gaussian hugging the lower-middle of the frame times [a hot round
    /// head cloud + a feathery tail] — the tail is row-streaked with torn gaps and grain,
    /// the head line wavy per row, which is what reads as "scratch" instead of a clean
    /// gradient. The head cloud's Gaussian straddles its centre, so the streak keeps
    /// glowing a little way AHEAD of the head line and fades out there — no hard cut.
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

        // Per-ROW streak strength with occasional near-gaps (the "tears"): the strip is
        // stretched to full screen height, so row-wise variation renders as the long
        // HORIZONTAL wisps the reference tail is made of — column-wise variation turned
        // into vertical blinds instead (0.7.27 test capture).
        var streak = new float[TexH];
        for (int y = 0; y < TexH; y++)
        {
            float s = 0.55f + 0.45f * UnityEngine.Random.value;
            if (UnityEngine.Random.value < 0.10f)
                s *= 0.35f;
            streak[y] = s;
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
            // Vertical mask: bright in the lower-middle band (like the reference), faint
            // elsewhere. The low floor keeps it from reading as a vertical light pillar.
            float v = y / (TexH - 1f);
            float dv = (v - 0.62f) / 0.30f;
            float vmask = 0.15f + 0.85f * Mathf.Exp(-dv * dv);

            int row = y * TexW;
            float rowShift = shift[y];
            for (int x = 0; x < TexW; x++)
            {
                float t = Mathf.Clamp01((x + rowShift) / (TexW - 1f)); // 0 tail -> 1 glow edge
                float tt = Mathf.Clamp01(t / (1f - HeadLead));         // 0 tail -> 1 at the blob centre
                float tail = Mathf.Pow(tt, 1.8f) * 0.45f;
                float dx = (t - (1f - HeadLead)) / 0.12f;
                float blob = Mathf.Exp(-dx * dx); // hot round head cloud, glowing forward too
                float grain = 0.72f + 0.28f * UnityEngine.Random.value;
                float a = vmask * Mathf.Clamp01(blob + tail * streak[y] * grain);
                px[row + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }

        _strip.SetPixels32(px);
        _strip.Apply(false);
    }
}

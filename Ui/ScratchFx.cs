using UnityEngine;

namespace DeadEye.Core;

/// <summary>
/// RDR2-style white scratch wipe on Dead Eye activation, modelled frame-by-frame on the
/// reference clip (10.65-10.92 s). Three defining traits, all reproduced here:
/// 1. The tint boundary RIDES with the scratch head — the world behind the head is already
///    tinted, the world ahead of it is still normal. The yellow canvas overlay therefore
///    stays suppressed while the sweep runs; the tint is drawn in OnGUI split at the head,
///    and the canvas fades in when the sweep completes.
/// 2. The white element is a cloud-like bright head with a long feathery tail hugging the
///    lower-middle of the screen (a vertical Gaussian), not a uniform full-height band.
/// 3. ~0.6 s unscaled, near-linear motion, starting from a random side.
/// Drawn in OnGUI from one procedurally generated RGBA strip (regenerated per activation:
/// column streaks, torn gaps, grain, wavy head line) mirrored at draw time for R2L.
/// Purely cosmetic: no game objects, shaders, cameras or post-processing are touched, and
/// the sweep self-terminates — it finishes even if the mode is cancelled mid-reveal.
/// </summary>
internal static class ScratchFx
{
    private const float DurationSeconds = 0.6f;
    private const float SweepWidthFraction = 0.55f;   // strip width as a fraction of screen width
    private const float TintSoftEdgeFraction = 0.18f; // soft tint boundary around the head
    private const float HeadLead = 0.25f;             // strip fraction reserved for the forward glow past the head
    private const int TexW = 256;
    private const int TexH = 128;

    private static Texture2D? _strip; // white streak: tail (x=0) -> head (x=1)
    private static Texture2D? _grad;  // horizontal white -> transparent, for the soft tint edge
    private static float _start = -1f;
    private static bool _fromLeft;

    /// <summary>Arm one sweep. Called when the mode actually enters (not on refused tries).</summary>
    internal static void Trigger()
    {
        _start = Time.unscaledTime;
        _fromLeft = UnityEngine.Random.value < 0.5f;
        BuildStrip();
        // The canvas tint must not pop on globally: the sweep draws the split tint itself.
        Overlay.SetTintSuppressed(true);
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
            Overlay.SetTintSuppressed(false); // canvas tint takes over from the split tint
            return;
        }

        float w = Screen.width * SweepWidthFraction;
        float softW = Screen.width * TintSoftEdgeFraction;

        // Near-linear head motion with a soft landing (the reference reads as constant
        // speed): strong easing made the bright head cross in ~0.12 s, half the reference.
        float eased = 1f - Mathf.Pow(1f - p, 1.2f);
        float head = Mathf.Lerp(0f, Screen.width + w, eased); // head position in travel direction
        float headX = _fromLeft ? head : Screen.width - head; // head position in SCREEN space

        DrawTintBehindHead(headX, softW);
        DrawStreak(headX, w, p);
    }

    /// <summary>Tint only the world the head has already crossed; normal colours ahead.</summary>
    private static void DrawTintBehindHead(float headX, float softW)
    {
        var settings = DeadEyeMod.Settings;
        if (settings == null || !settings.YellowOverlay)
            return;
        float opacity = Mathf.Clamp01(settings.OverlayOpacity);
        if (opacity <= 0.001f)
            return;
        var tint = new Color(1f, 0.82f, 0.15f, opacity);

        // Solid part: from the screen edge BEHIND the head up to the head line, CLAMPED to
        // the screen. headX goes negative (R2L) / past W (L2R) during the last third of the
        // sweep; an unclamped [headX, headX+W] rect then ends INSIDE the screen and the
        // world beyond it visibly reverts to untinted behind a hard straight edge — the
        // gradient rides with the head, off-screen, so nothing softens it. Measured in the
        // 0.7.29 capture: B channel stepped 88 -> 114 within one sample at that edge.
        float solidL = _fromLeft ? 0f : headX;
        float solidR = _fromLeft ? headX : Screen.width;
        float drawL = Mathf.Max(0f, solidL);
        float drawR = Mathf.Min(Screen.width, solidR);
        if (drawR - drawL > 0f)
        {
            GUI.color = tint;
            GUI.DrawTexture(new Rect(drawL, 0f, drawR - drawL, Screen.height), Overlay.WhiteTex);
        }

        // Soft part: a short gradient straddling the head line so the boundary is a wash,
        // not a hard edge. The gradient texture fades white->transparent along +x with a
        // sqrt curve (a linear ramp's low-alpha half is imperceptible over bright snow, so
        // the fade READS as a line even though it is there — measured in the same capture),
        // the L2R case is [headX, headX+softW] (opaque at the head, fading AHEAD of it) and
        // the R2L case is [headX-softW, headX] mirrored in place (opaque at the head again).
        if (_grad != null && headX > -softW && headX < Screen.width + softW)
        {
            float gx = _fromLeft ? headX : headX - softW;
            var rect = new Rect(gx, 0f, softW, Screen.height);
            Matrix4x4 saved = GUI.matrix;
            if (!_fromLeft)
                GUIUtility.ScaleAroundPivot(new Vector2(-1f, 1f),
                    new Vector2(rect.x + rect.width / 2f, rect.y + rect.height / 2f));
            GUI.color = tint;
            GUI.DrawTexture(rect, _grad, ScaleMode.StretchToFill, true);
            GUI.matrix = saved;
        }

        GUI.color = Color.white;
    }

    /// <summary>The white comet: texture-authored tail->head, mirrored for R2L.</summary>
    private static void DrawStreak(float headX, float w, float progress)
    {
        // L2R: the head is the texture's right edge -> rect ends at headX.
        // R2L: mirrored in place, the head becomes the rect's LEFT edge -> rect starts at headX.
        float x = _fromLeft ? headX - w : headX;
        var rect = new Rect(x, 0f, w, Screen.height);

        // The tail thins out while the head is still travelling: in the reference the
        // wisps behind the head dissipate instead of hanging as a full-screen haze.
        Matrix4x4 saved = GUI.matrix;
        GUI.color = new Color(1f, 1f, 1f, 1f - 0.4f * progress);
        if (!_fromLeft)
        {
            // The texture is authored left-tail -> right-head; mirror the draw for R2L.
            var pivot = new Vector2(rect.x + rect.width / 2f, rect.y + rect.height / 2f);
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
        if (_grad == null)
        {
            _grad = new Texture2D(64, 4, TextureFormat.RGBA32, false, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var gp = new Color32[64 * 4];
            for (int x = 0; x < 64; x++)
            {
                // sqrt falloff: rises fast near the head, so the perceptible part of the
                // fade spans most of the zone (a linear ramp reads as a hard line — the
                // low-alpha half is invisible over bright snow).
                float t = 1f - x / 63f;
                byte a = (byte)(255 * Mathf.Sqrt(t));
                for (int y = 0; y < 4; y++)
                    gp[y * 64 + x] = new Color32(255, 255, 255, a);
            }
            _grad.SetPixels32(gp);
            _grad.Apply(false);
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
            float dv = (v - 0.62f) / 0.26f;
            float vmask = 0.15f + 0.85f * Mathf.Exp(-dv * dv);

            int row = y * TexW;
            float rowShift = shift[y];
            for (int x = 0; x < TexW; x++)
            {
                float t = Mathf.Clamp01((x + rowShift) / (TexW - 1f)); // 0 tail -> 1 glow edge
                float tt = Mathf.Clamp01(t / (1f - HeadLead));         // 0 tail -> 1 at the blob centre
                float tail = Mathf.Pow(tt, 1.8f) * 0.45f;
                float dx = (t - (1f - HeadLead)) / 0.07f;
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

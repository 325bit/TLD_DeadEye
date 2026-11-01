using UnityEngine;
using UnityEngine.UI;

namespace DeadEye.Core;

/// <summary>
/// In-mode visuals: fullscreen yellow filter (Canvas overlay) + OnGUI markers and messages.
/// The canvas is created lazily and toggled with the mode; markers are drawn by
/// DeadEyeMod.OnGUI while marking or firing.
/// </summary>
internal static class Overlay
{
    private static GameObject? _canvas;
    private static Image? _image;

    private static string _message = string.Empty;
    private static float _messageUntil;

    private static Texture2D? _whiteTex;

    internal static void SetOverlay(bool enabled)
    {
        if (!enabled)
        {
            if (_canvas != null)
                _canvas.SetActive(false);
            return;
        }

        EnsureCanvas();
        if (_canvas != null && _image != null)
        {
            float opacity = Mathf.Clamp01(DeadEyeMod.Settings?.OverlayOpacity ?? 0.22f);
            _image.color = new Color(1f, 0.82f, 0.15f, opacity);
            _canvas.SetActive(true);
        }
    }

    internal static void SetMessage(string message, float seconds)
    {
        _message = message;
        _messageUntil = Time.unscaledTime + seconds;
    }

    // ---------------------------------------------------------------- Canvas

    private static void EnsureCanvas()
    {
        if (_canvas != null)
            return;

        var go = new GameObject("DeadEyeYellowOverlay");
        UnityEngine.Object.DontDestroyOnLoad(go);

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30000;

        _image = go.AddComponent<Image>();
        _image.raycastTarget = false;
        _image.color = new Color(1f, 0.82f, 0.15f, 0.22f);

        // Stretch over the whole screen.
        var rect = _image.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        _canvas = go;
        _canvas.SetActive(false);
    }

    // ---------------------------------------------------------------- OnGUI drawing

    /// <summary>Red X per mark (green for the one being fired at) + message line + counter.</summary>
    internal static void Draw()
    {
        if (DeadEyeManager.State == DeadEyeState.Inactive)
            return;

        if (_whiteTex == null)
        {
            _whiteTex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var fill = new Color32(255, 255, 255, 255);
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    _whiteTex.SetPixel(x, y, fill);
            _whiteTex.Apply();
        }

        var camera = GameManager.GetMainCamera();
        if (camera != null)
        {
            var marks = DeadEyeManager.Marks;
            for (int i = 0; i < marks.Count; i++)
            {
                if (!marks[i].TryGetWorldPoint(out Vector3 worldPoint))
                    continue;

                Vector3 screen = camera.WorldToScreenPoint(worldPoint);
                if (screen.z <= 0f)
                    continue; // behind the camera

                float gx = screen.x;
                float gy = Screen.height - screen.y; // GUI coords are top-left
                Color color = i == DeadEyeManager.CurrentFireIndex
                    ? new Color(1f, 0.85f, 0.1f, 0.95f)
                    : new Color(0.95f, 0.1f, 0.1f, 0.9f);
                DrawX(gx, gy, 14f, 3f, color);
            }
        }

        // Message + counter, bottom center.
        if (Time.unscaledTime < _messageUntil && !string.IsNullOrEmpty(_message))
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(18, Screen.height / 40),
                alignment = TextAnchor.MiddleCenter,
            };
            style.normal.textColor = new Color(1f, 0.95f, 0.7f);
            var rect = new Rect(0, Screen.height - 90, Screen.width, 40);
            var shadow = new GUIStyle(style) { normal = { textColor = new Color(0, 0, 0, 0.8f) } };
            GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), _message, shadow);
            GUI.Label(rect, _message, style);
        }
    }

    private static void DrawX(float cx, float cy, float size, float thickness, Color color)
    {
        GUI.color = color;
        float half = size / 2f;
        Matrix4x4 saved = GUI.matrix;

        for (int i = 0; i < 2; i++)
        {
            float angle = i == 0 ? 45f : -45f;
            GUIUtility.RotateAroundPivot(angle, new Vector2(cx, cy));
            GUI.DrawTexture(new Rect(cx - half, cy - thickness / 2f, size, thickness), _whiteTex);
            GUI.matrix = saved;
        }

        GUI.color = Color.white;
    }
}

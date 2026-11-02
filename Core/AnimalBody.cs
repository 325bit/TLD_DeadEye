using UnityEngine;
using Il2CppTLD.AI;
using Il2CppInterop.Runtime;

namespace DeadEye.Core;

/// <summary>
/// Aim model per species. Small prey (grouse, rabbit) is aimed INSIDE the body: part of the
/// way from the animal's root toward the marked surface (user-tunable ballistics setting). Both alternatives failed on a
/// body this thin: the marked point sits on the surface (half of any error falls outside
/// it) and sweeps sideways when the animal turns, and the collider union centre sits off
/// the animal entirely (its bounds reach well beyond the body), which a 20 cm target
/// cannot absorb.
/// </summary>
internal static class AnimalBody
{
    /// <summary>Lower height bound for small prey: low enough that a mark on the feet still hits.</summary>
    internal const float PreyMinAimHeight = 0.06f;

    /// <summary>Used when the settings panel is not built yet (first frame / menu).</summary>
    private const float DefaultPreyAimFraction = 0.6f;

    /// <summary>Height clamps for large animals, taken from the tested deer behaviour.</summary>
    internal const float LargeMinAimHeight = 0.40f;
    internal const float LargeMaxAimHeight = 1.20f;

    /// <summary>Body height at or below which an animal is treated as small prey.</summary>
    private const float SmallPreyMaxHeight = 0.45f;

    /// <summary>
    /// How far from the root toward the marked surface a prey shot is aimed (the ballistics
    /// setting), read LIVE on every call so moving it applies to the next frame — including
    /// a shot already in the air — without re-marking. 0 = the root, 1 = the marked surface,
    /// in between = inside the body.
    /// </summary>
    internal static float PreyAimFraction
        => Mathf.Clamp(DeadEyeMod.Settings != null
            ? DeadEyeMod.Settings.PreyAimFraction
            : DefaultPreyAimFraction, 0f, 1f);

    /// <summary>
    /// Aim point for small prey: part of the way from the animal's root toward the marked
    /// surface, which is inside the body whatever part was marked. Only the VERTICAL part is
    /// shaped, and only from below (a mark on the feet must not aim at the ground): the marked
    /// point is itself on the body, so 0..1 of the way up can never overshoot it, while an upper
    /// clamp would push the point UNDER an animal whose root pivot sits low. The offset is
    /// WORLD space, so a turning animal does not sweep the point sideways.
    /// </summary>
    internal static Vector3 PreyAimPoint(Vector3 rootPosition, Vector3 bodyOffset)
    {
        float fraction = PreyAimFraction;
        float height = Mathf.Max(bodyOffset.y * fraction, PreyMinAimHeight);
        return rootPosition + new Vector3(bodyOffset.x, 0f, bodyOffset.z) * fraction + Vector3.up * height;
    }

    /// <summary>
    /// Height above the root used by in-flight steering on large animals, where the marked
    /// point is deliberately NOT followed (it sweeps sideways when the animal turns).
    /// </summary>
    internal static float LargeAimHeight(Vector3 bodyOffset)
        => Mathf.Clamp(bodyOffset.magnitude, LargeMinAimHeight, LargeMaxAimHeight);

    /// <summary>
    /// Species test. The concrete AI classes are asked first — through <c>TryCast</c>, which
    /// consults the RUNTIME class, because `GetComponent&lt;BaseAi&gt;()` returns a wrapper of
    /// exactly the requested type and `is AiRabbit` is then false for every animal. Then the
    /// prefab name, and finally the measured body height: an unrecognised species whose body
    /// is lower than the large-animal height offset is too thin to aim over.
    /// </summary>
    internal static bool IsSmallPrey(BaseAi? ai)
    {
        if (ai == null)
            return false;

        try
        {
            if (ai.TryCast<AiRabbit>() != null || ai.TryCast<AiPtarmigan>() != null)
                return true;

            var go = ai.gameObject;
            if (go == null)
                return false;

            string name = go.name;
            if (name != null && (name.Contains("Rabbit") || name.Contains("Ptarmigan")))
                return true;

            Collider[]? cache = null;
            return TryGetBodyBounds(go, ref cache, out Bounds bounds) &&
                   bounds.size.y > 0.01f && bounds.size.y <= SmallPreyMaxHeight;
        }
        catch (System.Exception ex)
        {
            ModLog.Debug($"small prey test failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// The animal's RUNTIME class name. <c>GetType()</c> only ever reports `BaseAi` (the
    /// interop wrapper is created for the type it was requested as), so it cannot be used to
    /// tell a rabbit from a deer in a log. Diagnostics only.
    /// </summary>
    internal static string SpeciesName(BaseAi? ai)
    {
        try
        {
            if (ai == null)
                return "null";

            string? name = IL2CPP.il2cpp_class_get_name_(ai.ObjectClass);
            return string.IsNullOrEmpty(name) ? "unknown" : name;
        }
        catch (System.Exception ex)
        {
            return $"unknown({ex.Message})";
        }
    }

    /// <summary>
    /// One-line collider summary for the mark log. Diagnostics only — no aim point is
    /// derived from it, see the class summary for why.
    /// </summary>
    internal static string DescribeBody(BaseAi? ai)
    {
        try
        {
            var go = ai != null ? ai.gameObject : null;
            if (go == null)
                return "body=unknown";

            Collider[]? cache = null;
            if (!TryGetBodyBounds(go, ref cache, out Bounds bounds))
                return "body=no-usable-collider";

            Vector3 delta = bounds.center - go.transform.position;
            return $"bodyBounds={bounds.size.x:F2}x{bounds.size.y:F2}x{bounds.size.z:F2} " +
                   $"bodyCentreFromRoot=({delta.x:F2},{delta.y:F2},{delta.z:F2})";
        }
        catch (System.Exception ex)
        {
            return $"body=read-failed({ex.Message})";
        }
    }

    /// <summary>
    /// Union of the enabled, non-trigger colliders under <paramref name="root"/>.
    /// <paramref name="cache"/> is filled once and reused by callers that run per frame;
    /// a cache that yields nothing is dropped so the next call re-collects.
    /// </summary>
    private static bool TryGetBodyBounds(GameObject? root, ref Collider[]? cache, out Bounds bounds)
    {
        bounds = default;
        if (root == null)
            return false;

        cache ??= Collect(root);
        if (cache == null || cache.Length == 0)
            return false;

        bool any = false;
        foreach (var collider in cache)
        {
            if (collider == null || collider.isTrigger || !collider.enabled)
                continue;
            var go = collider.gameObject;
            if (go == null || !go.activeInHierarchy)
                continue;

            if (!any)
            {
                bounds = collider.bounds;
                any = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        if (!any)
        {
            cache = null; // stale snapshot: re-collect on the next call
            return false;
        }

        return true;
    }

    private static Collider[]? Collect(GameObject root)
    {
        try
        {
            return root.GetComponentsInChildren<Collider>(true);
        }
        catch (System.Exception ex)
        {
            ModLog.Debug($"collider collect failed: {ex.Message}");
            return null;
        }
    }
}

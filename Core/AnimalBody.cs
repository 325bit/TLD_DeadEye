using UnityEngine;
using Il2CppTLD.AI;

namespace DeadEye.Core;

/// <summary>
/// Aim model per species. Small prey (grouse, rabbit) is aimed INSIDE the body: 60 % of
/// the way from the animal's root toward the marked surface. Both alternatives failed on a
/// body this thin: the marked point sits on the surface (half of any error falls outside
/// it) and sweeps sideways when the animal turns, and the collider union centre sits off
/// the animal entirely (its bounds reach well beyond the body), which a 20 cm target
/// cannot absorb.
/// </summary>
internal static class AnimalBody
{
    /// <summary>Height clamps for small prey: low enough that a mark on the feet still hits.</summary>
    internal const float PreyMinAimHeight = 0.06f;
    internal const float PreyMaxAimHeight = 0.28f;

    /// <summary>
    /// How far from the root toward the marked surface a prey shot is aimed. The crosshair
    /// lands on the visible SURFACE, and aiming at a surface means half of any error falls
    /// outside the body, so the point is pulled inward to sit inside it.
    /// </summary>
    private const float PreyInwardFraction = 0.6f;

    /// <summary>Height clamps for large animals, taken from the tested deer behaviour.</summary>
    internal const float LargeMinAimHeight = 0.40f;
    internal const float LargeMaxAimHeight = 1.20f;

    /// <summary>Body height at or below which an animal is treated as small prey.</summary>
    private const float SmallPreyMaxHeight = 0.45f;

    /// <summary>
    /// Aim point for small prey: 60 % of the way from the animal's root toward the marked
    /// surface, which is inside the body whatever part was marked. Only the VERTICAL part
    /// is clamped — a pivoted root that sits below the body must not push the point out of
    /// the top of a 20 cm animal.
    /// </summary>
    internal static Vector3 PreyAimPoint(Transform root, Vector3 localOffset)
    {
        Vector3 origin = root.position;
        Vector3 delta = root.TransformPoint(localOffset) - origin;
        float height = Mathf.Clamp(delta.y * PreyInwardFraction, PreyMinAimHeight, PreyMaxAimHeight);
        return origin + new Vector3(delta.x, 0f, delta.z) * PreyInwardFraction
                      + Vector3.up * height;
    }

    /// <summary>
    /// Height above the root used by in-flight steering on large animals, where the marked
    /// point is deliberately NOT followed (it sweeps sideways when the animal turns).
    /// </summary>
    internal static float LargeAimHeight(Vector3 localOffset)
        => Mathf.Clamp(localOffset.magnitude, LargeMinAimHeight, LargeMaxAimHeight);

    /// <summary>
    /// Species test. The concrete AI classes are checked first, then the prefab name, and
    /// finally the measured body height: an unrecognised species whose body is lower than
    /// the large-animal height offset is too thin to aim over, so it behaves as prey.
    /// </summary>
    internal static bool IsSmallPrey(BaseAi? ai)
    {
        if (ai == null)
            return false;

        try
        {
            if (ai is AiRabbit || ai is AiPtarmigan)
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

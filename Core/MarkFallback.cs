// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Hemalurgic

using Il2CppTLD.AI;
using UnityEngine;

namespace DeadEye.Core;

/// <summary>
/// Second chance for the FIRST marking gate. A visible animal can be wider than its colliders —
/// a howling wolf raises its head above the body capsule — so the crosshair ray touches nothing
/// and the click is dropped. When no raw hit resolves to a live <see cref="BaseAi"/>, this picks
/// the live animal whose BODY BOUNDS lie nearest to the ray, within the fallback radius.
/// It only CHOOSES the animal: the chosen one goes through the normal resolution, so the mark
/// point still comes from a damageable collider (or the existing degradation), never from here.
/// </summary>
internal static class MarkFallback
{
    /// <summary>Live radius in metres, read per click so a tuning change applies immediately.</summary>
    internal static float Radius
        => Mathf.Max(0f, DeadEyeMod.Settings != null
            ? DeadEyeMod.Settings.MarkFallbackRadius
            : Settings.DefaultMarkFallbackRadius);

    /// <summary>
    /// Smallest accepted body box. The same prefab can be present as a far "ghost" twin with no
    /// colliders at all (reported bounds 0.00x0.00x0.00); marking it would plant the mark on an
    /// animal 200 m away from the one the player sees.
    /// </summary>
    private const float MinBodyExtent = 0.02f;

    /// <summary>Alternating projection passes for the ray-to-box distance; see <see cref="RayToBoundsDistance"/>.</summary>
    private const int ProjectionPasses = 3;

    /// <summary>
    /// Nearest live animal within <see cref="Radius"/> of <paramref name="ray"/>. Ties go to the
    /// animal nearer the ray origin, so of two equally close to the line the one the player faces
    /// wins. <paramref name="bodyPoint"/> is the box point nearest that line, used only when the
    /// normal resolution still fails (this path has no raw hit point to degrade to).
    /// <paramref name="blockers"/> are the sorted raw hits: a hit in front of a candidate that is
    /// not one of its own colliders is cover, and the animal behind it must not be marked.
    /// </summary>
    internal static bool TryFind(Ray ray, RaycastHit[] blockers,
                                 out BaseAi? ai, out Collider? proxy, out Vector3 bodyPoint,
                                 out float linearMiss, out float span)
    {
        ai = null;
        proxy = null;
        bodyPoint = default;
        linearMiss = float.MaxValue;
        span = float.MaxValue;

        float radius = Radius;
        if (radius <= 0f)
            return false;

        BaseAi[]? animals;
        try
        {
            animals = UnityEngine.Object.FindObjectsOfType<BaseAi>();
        }
        catch (System.Exception ex)
        {
            ModLog.Warn($"mark fallback: animal scan failed ({ex.Message})");
            return false;
        }

        if (animals == null)
            return false;

        foreach (var candidate in animals)
        {
            try
            {
                if (candidate == null)
                    continue;
                var go = candidate.gameObject;
                if (go == null || !go.activeInHierarchy)
                    continue;
                if (candidate.GetAiMode() == AiMode.Dead)
                    continue;

                Collider[]? set = AnimalBody.CollectFor(candidate, null);
                if (set == null || set.Length == 0)
                    continue;

                Collider[]? cache = set;
                if (!AnimalBody.TryGetBodyBounds(go, ref cache, out Bounds bounds))
                    continue;
                if (bounds.size.x < MinBodyExtent || bounds.size.y < MinBodyExtent || bounds.size.z < MinBodyExtent)
                    continue;

                // Behind the camera: the distance to the line is meaningless there (an animal
                // standing at the player's back is 0.5 m from the ray ORIGIN), so it can only be
                // marked when it is in front.
                if (Vector3.Dot(bounds.center - ray.origin, ray.direction) <= 0f)
                    continue;

                float miss = RayToBoundsDistance(ray, bounds, out float along);
                if (miss > radius)
                    continue;
                if (Blocked(blockers, along, set, out Collider? cover, out float coverDistance))
                {
                    ModLog.Debug($"mark fallback: {go.name} at {Vector3.Distance(bounds.center, ray.origin):F1} m " +
                                 $"({miss:F2} m off the ray) is behind '{cover!.gameObject.name}' at {coverDistance:F1} m " +
                                 "— cover, not the target");
                    continue;
                }

                float candidateSpan = Vector3.Distance(bounds.center, ray.origin);
                if (miss < linearMiss || (Mathf.Approximately(miss, linearMiss) && candidateSpan < span))
                {
                    ai = candidate;
                    proxy = Proxy(set);
                    bodyPoint = bounds.ClosestPoint(ray.origin + ray.direction * along);
                    linearMiss = miss;
                    span = candidateSpan;
                }
            }
            catch (System.Exception ex)
            {
                // a dying or half-torn animal must not break the scan; skip it
                ModLog.Debug($"mark fallback: candidate skipped ({ex.Message})");
            }
        }

        return ai != null && proxy != null;
    }

    /// <summary>
    /// Distance from the ray to an axis-aligned box, by alternating projection: project the ray's
    /// nearest point onto the box, project that box point back onto the ray, repeat. Both sets are
    /// convex, so a couple of passes converge to the true shortest segment; a ray that starts
    /// inside the box yields 0. <paramref name="along"/> is the ray parameter of that closest
    /// point, used to decide whether the animal is behind cover.
    /// </summary>
    private static float RayToBoundsDistance(Ray ray, Bounds bounds, out float along)
    {
        const float Limit = AnimalBody.MarkRange;
        float t = Mathf.Clamp(Vector3.Dot(bounds.center - ray.origin, ray.direction), 0f, Limit);
        float best = float.MaxValue;

        for (int pass = 0; pass < ProjectionPasses; pass++)
        {
            Vector3 onRay = ray.origin + ray.direction * t;
            Vector3 onBox = bounds.ClosestPoint(onRay);
            best = Mathf.Min(best, Vector3.Distance(onRay, onBox));
            t = Mathf.Clamp(Vector3.Dot(onBox - ray.origin, ray.direction), 0f, Limit);
        }

        along = t;
        return best;
    }

    /// <summary>
    /// True when a raw hit that is not one of the candidate's OWN colliders sits in front of it:
    /// the ray is looking at terrain, scenery or another body, so this animal is not what the
    /// player is aiming at. Ownership is compared by collider identity rather than by distance
    /// slack, because the animal's oversized root capsule and its aux trigger volumes ARE crossed
    /// while the damage capsules are missed — those must not read as cover.
    /// TRIGGERS ARE NEVER COVER: they neither render nor stop anything, and the world is full of
    /// invisible trigger volumes (an untested distance-only comparison let a fishing-hole
    /// placement trigger refuse two perfectly aimed clicks as "nothing alive at the crosshair").
    /// </summary>
    private static bool Blocked(RaycastHit[] blockers, float along, Collider[] own,
                                out Collider? cover, out float coverDistance)
    {
        cover = null;
        coverDistance = float.MaxValue;
        if (blockers == null || blockers.Length == 0)
            return false;

        foreach (var blocker in blockers)
        {
            var collider = blocker.collider;
            if (collider == null || collider.isTrigger)
                continue;
            if (blocker.distance <= 0f || blocker.distance >= along)
                continue;
            if (Owns(own, collider))
                continue;

            cover = collider;
            coverDistance = blocker.distance;
            return true;
        }

        return false;
    }

    private static bool Owns(Collider[] own, Collider collider)
    {
        int id = collider.GetInstanceID();
        foreach (var mine in own)
        {
            if (mine != null && mine.GetInstanceID() == id)
                return true;
        }

        return false;
    }

    /// <summary>
    /// The collider handed to the normal resolution. It is only used to locate the collection
    /// root, so the largest solid collider of the animal is the sensible choice.
    /// </summary>
    private static Collider? Proxy(Collider[] set)
    {
        Collider? best = null;
        float bestVolume = -1f;
        foreach (var collider in set)
        {
            if (collider == null)
                continue;
            best ??= collider;
            if (collider.isTrigger || !collider.enabled)
                continue;

            Vector3 size = collider.bounds.size;
            float volume = size.x * size.y * size.z;
            if (volume > bestVolume)
            {
                bestVolume = volume;
                best = collider;
            }
        }

        return best;
    }
}

// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Hemalurgic

using UnityEngine;
using Il2CppTLD.AI;

namespace DeadEye.Core;

/// <summary>
/// Head marks on large animals. A shot is resolved on whichever damageable capsule the round
/// passes closest to — in practice the chest — so a mark the player put on the head would
/// otherwise roll the chest row of the damage table. A mark that qualifies here makes every
/// round that HITS the animal read the head row instead (damage scale, bleed-out minutes and
/// the critical-hit roll), and play its hit animation at the head.
/// This class only IDENTIFIES the head and its damage table. It never moves the mark, never
/// picks the aim point and never decides whether a round hit — the shot's own geometry still
/// has to land on a damageable capsule.
/// </summary>
internal static class HeadPart
{
    /// <summary>
    /// How far the crosshair ray may pass from the head and still count as aimed at it. The
    /// head capsule measures about 0.50 x 0.46 x 0.34 m (half-extents ~0.25 / 0.23 / 0.17) and
    /// the chest centre sits more than half a metre lower, so 0.35 m covers "crosshair on the
    /// head, edge included" while leaving a deliberate chest mark a chest mark.
    /// </summary>
    private const float RayRadius = 0.35f;

    /// <summary>A body box smaller than this is a placeholder instance, not a shape to trust.</summary>
    private const float MinExtent = 0.02f;

    /// <summary>
    /// The animal's head damage table when the click counts as a head mark, else false.
    /// A mark qualifies when the crosshair ray passes within <see cref="RayRadius"/> of the head
    /// box, which is the same test whether the ray grazed the head capsule's surface or missed
    /// every collider on the animal — the howling wolf, whose raised head sits ABOVE all of them.
    /// <paramref name="centre"/> is the head centre in world space, used for the hit animation.
    /// </summary>
    internal static bool TryFind(BaseAi? ai, Ray ray, out LocalizedDamage? head, out Vector3 centre)
    {
        head = null;
        centre = default;

        if (ai == null)
            return false;

        // Small prey dies to any hit, so its own aim model already covers the head; the head
        // row would only replace a table that is not what decides the outcome.
        if (AnimalBody.IsSmallPrey(ai))
            return false;

        Collider[]? set = AnimalBody.CollectFor(ai, null);
        if (set == null || set.Length == 0)
            return false;

        Collider? bestCollider = null;
        LocalizedDamage? bestHead = null;
        float bestMiss = float.MaxValue;
        float bestVolume = -1f;
        Bounds bestBounds = default;

        foreach (var collider in set)
        {
            if (collider == null || collider.isTrigger)
                continue;

            LocalizedDamage? part = ReadHeadPart(collider);
            if (part == null)
                continue;

            Bounds bounds;
            try
            {
                bounds = collider.bounds;
            }
            catch (System.Exception)
            {
                continue;
            }

            float miss = AnimalBody.RayToBoundsDistance(ray, bounds, out float _);

            // Nearest to the crosshair wins; the bigger capsule breaks a tie, because a
            // duplicated head volume (an LOD or a mirrored instance) is the less useful one.
            float volume = bounds.size.x * bounds.size.y * bounds.size.z;
            bool better = miss < bestMiss - 0.001f ||
                          (miss <= bestMiss + 0.001f && volume > bestVolume);
            if (!better)
                continue;

            bestCollider = collider;
            bestHead = part;
            bestMiss = miss;
            bestVolume = volume;
            bestBounds = bounds;
        }

        if (bestCollider == null || bestHead == null)
            return false;

        if (bestMiss > RayRadius)
        {
            ModLog.Debug($"head mark not applied: '{bestCollider.gameObject.name}' is {bestMiss:F2} m off the ray " +
                         $"(radius {RayRadius:F2} m)");
            return false;
        }

        centre = CentreOf(bestCollider, bestBounds);
        head = bestHead;
        return true;
    }

    /// <summary>The collider's damage table, but only when it is the HEAD part.</summary>
    private static LocalizedDamage? ReadHeadPart(Collider collider)
    {
        try
        {
            LocalizedDamage? part = collider.gameObject.GetComponent<LocalizedDamage>();
            if (part == null)
                return null;

            return part.m_BodyPart == BodyPart.head ? part : null;
        }
        catch (System.Exception)
        {
            // A torn-down prefab or a wrapper of the wrong type must not stop the scan.
            return null;
        }
    }

    /// <summary>
    /// World-space centre of the head shape. The collider's own bounds are preferred; while the
    /// damage capsules are DISABLED (the idle/howl state gate) the bounds can collapse, so the
    /// shape's centre field is read from the transform instead — that value does not depend on
    /// the collider being enabled.
    /// </summary>
    private static Vector3 CentreOf(Collider collider, Bounds bounds)
    {
        Vector3 size = bounds.size;
        if (size.x >= MinExtent && size.y >= MinExtent && size.z >= MinExtent)
            return bounds.center;

        try
        {
            CapsuleCollider? capsule = collider.TryCast<CapsuleCollider>();
            if (capsule != null)
                return capsule.transform.TransformPoint(capsule.center);

            BoxCollider? box = collider.TryCast<BoxCollider>();
            if (box != null)
                return box.transform.TransformPoint(box.center);

            SphereCollider? sphere = collider.TryCast<SphereCollider>();
            if (sphere != null)
                return sphere.transform.TransformPoint(sphere.center);
        }
        catch (System.Exception ex)
        {
            ModLog.Debug($"head centre: shape read failed ({ex.Message})");
        }

        try
        {
            return collider.transform.position;
        }
        catch (System.Exception)
        {
            return bounds.center;
        }
    }
}

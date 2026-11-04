// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Hemalurgic

using System.Collections.Generic;
using Il2CppTLD.AI;
using UnityEngine;

namespace DeadEye.Core;

/// <summary>
/// Log-only forensics for a marking attempt that failed at the FIRST gate (the ray hit
/// nothing that resolves to a BaseAi). It changes no behaviour and creates no mark — it only
/// records the evidence needed to tell apart the three known failure shapes: the ray truly
/// touched nothing (colliders thinner than the visible mesh, e.g. a howling wolf's raised
/// head), a hit WAS skipped by the layer-16/27 resolution (an aux volume such as the
/// Audio_Wildlife trigger), or the animal is genuinely out of the ray. Logged at Warning,
/// never behind Verbose — a diagnostic nobody can see is what made the 0.7.8 marking
/// regression invisible for a whole build.
/// </summary>
internal static class MarkForensics
{
    /// <summary>How many nearest live animals the cone scan reports.</summary>
    private const int CandidateCount = 3;

    /// <summary>Raw hits listed per miss before the rest is summarised. A ray across a whole
    /// valley can touch dozens of colliders and the first few already answer the question.</summary>
    private const int RawHitCap = 25;

    /// <summary>
    /// Animals whose FULL collider inventory was already dumped during the current marking
    /// session. A prefab root that hosts a dozen animals (the `Wildlife` holder) defeats the
    /// root rule on every single click, so the same dump repeated itself ~90 lines per click;
    /// the first copy carries all the evidence and the rest is noise. Cleared on entering the
    /// mode, so a new session always gets a fresh dump.
    /// </summary>
    private static readonly HashSet<int> DumpedThisSession = new HashSet<int>();

    /// <summary>Called on entering the marking mode: the next failure dumps in full again.</summary>
    internal static void BeginSession() => DumpedThisSession.Clear();

    /// <summary>Call exactly where <see cref="DeadEyeManager.TryMark"/> gives up with "the ray touched no animal".</summary>
    internal static void LogMiss(Ray ray, RaycastHit[] hits)
    {
        try
        {
            ModLog.Warn($"mark miss forensics: ray origin {ray.origin:F1}, direction {ray.direction:F3}, {hits.Length} raw hit(s) within {AnimalBody.MarkRange:F0} m");
            DumpRawHits(hits);
            ScanCone(ray);
        }
        catch (System.Exception ex)
        {
            ModLog.Warn($"mark miss forensics itself failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Second failure shape (0.7.38): the ray DID hit the animal, but the resolution found no
    /// damageable collider and degraded to the raw hit point. The open question this answers:
    /// are the LocalizedDamage capsules ABSENT from the collected set (hierarchy mismatch, e.g.
    /// a shared holder defeating the root rule) or merely DISABLED at that moment (a game-side
    /// state gate, which would also make the animal unhittable for vanilla bullets)? Dumps the
    /// FULL collider inventory — including disabled and trigger colliders — of both candidate
    /// sets (transform root and the BaseAi object), so the two hypotheses separate in one line each.
    /// </summary>
    internal static void LogNoDamageable(BaseAi ai, Collider hitCollider, RaycastHit hit)
    {
        try
        {
            var go = ai.gameObject;
            ModLog.Warn(
                $"no-damageable forensics: '{go.name}' id {go.GetInstanceID()} active={go.activeInHierarchy}" +
                $" hit '{hit.collider.gameObject.name}' @{hit.distance:F1} m, {Vector3.Distance(hit.point, ai.transform.position):F2} m from root");

            Transform? root = hitCollider != null ? hitCollider.transform.root : null;
            BaseAi[]? inRoot = root != null ? TryGet(() => root.GetComponentsInChildren<BaseAi>(true)) : null;
            int rootAiCount = inRoot != null ? inRoot.Length : -1;
            bool rootUsed = inRoot != null && rootAiCount == 1;
            ModLog.Warn($"no-damageable forensics: collect path root='{(root != null ? root.name : "null")}'" +
                        $" baseAiInRoot={rootAiCount} -> {(rootUsed ? "ROOT set" : "BaseAi-object set (root rule defeated)")}");

            // The header and the collect-path line above always print: they are two lines and
            // they are what separates the two failure shapes. Only the inventory repeats.
            if (!DumpedThisSession.Add(go.GetInstanceID()))
            {
                ModLog.Warn($"no-damageable forensics: inventory for this animal was dumped earlier " +
                            $"in this mark session — repeat skipped ({Summarize(go)})");
                return;
            }

            if (root != null)
                DumpInventory($"transform.root '{root.name}'", root.gameObject);
            if (root == null || !ReferenceEquals(root.gameObject, go))
                DumpInventory($"BaseAi object '{go.name}'", go);
        }
        catch (System.Exception ex)
        {
            ModLog.Warn($"no-damageable forensics itself failed: {ex.Message}");
        }
    }

    /// <summary>
    /// One-line stand-in for a repeated inventory dump: how many colliders the animal has, how
    /// many of them are damageable AND enabled (the only ones the resolution accepts), and how
    /// many are triggers. Those three numbers are what the full dump was being read for.
    /// </summary>
    private static string Summarize(GameObject go)
    {
        Collider[]? all = TryGet(() => go.GetComponentsInChildren<Collider>(true));
        if (all == null)
            return "collider collection FAILED";

        int damageable = 0, enabledDamageable = 0, triggers = 0;
        foreach (var collider in all)
        {
            if (collider == null)
                continue;
            if (collider.isTrigger)
                triggers++;
            if (!TryGet(() => collider.gameObject.GetComponent<LocalizedDamage>() != null, false))
                continue;
            damageable++;
            if (collider.enabled)
                enabledDamageable++;
        }

        return $"{all.Length} collider(s), {damageable} damageable of which {enabledDamageable} enabled, {triggers} trigger(s)";
    }

    /// <summary>Every collider under <paramref name="root"/>, enabled or not. One line each,
    /// capped so a pathological prefab cannot flood the log.</summary>
    private static void DumpInventory(string setLabel, GameObject root)
    {
        Collider[]? all = TryGet(() => root.GetComponentsInChildren<Collider>(true));
        if (all == null)
        {
            ModLog.Warn($"no-damageable forensics [{setLabel}]: collider collection FAILED");
            return;
        }

        ModLog.Warn($"no-damageable forensics [{setLabel}]: {all.Length} collider(s), inactive included");
        int logged = 0;
        foreach (var collider in all)
        {
            if (collider == null)
                continue;
            if (logged >= 40)
            {
                ModLog.Warn($"no-damageable forensics [{setLabel}]: ... {all.Length - logged} more omitted");
                break;
            }

            var go = collider.gameObject;
            bool damage = TryGet(() => go.GetComponent<LocalizedDamage>() != null, false);
            ModLog.Warn(
                $"no-damageable forensics [{setLabel}]: '{go.name}' layer {go.layer} {collider.GetType().Name}" +
                $" goActive={go.activeInHierarchy} enabled={collider.enabled} trigger={collider.isTrigger} damage={damage}");
            logged++;
        }
    }

    /// <summary>Every raw hit with the resolution facts the mark loop acts on. The aiDirect /
    /// aiParent columns mirror the two branches of the layer test; a hit that shows
    /// aiParent=yes but aiDirect=no on a layer other than 16/27 is a candidate the current
    /// gate silently skips.</summary>
    private static void DumpRawHits(RaycastHit[] hits)
    {
        if (hits.Length == 0)
        {
            ModLog.Warn("forensics: RaycastAll returned ZERO hits — the ray touched nothing at all on its whole 150 m path");
            return;
        }

        int listed = 0;
        foreach (var hit in hits)
        {
            var collider = hit.collider;
            if (collider == null)
            {
                ModLog.Warn($"forensics hit @{hit.distance:F1} m: NULL collider");
                continue;
            }

            if (listed >= RawHitCap)
            {
                ModLog.Warn($"forensics: ... {hits.Length - listed} more hit(s) omitted");
                break;
            }

            var go = collider.gameObject;
            BaseAi? direct = TryGet(() => go.GetComponent<BaseAi>());
            BaseAi? inParent = TryGet(() => go.GetComponentInParent<BaseAi>());
            bool damage = TryGet(() => go.GetComponent<LocalizedDamage>() != null, false);

            ModLog.Warn(
                $"forensics hit @{hit.distance:F1} m: '{go.name}' layer {go.layer} {collider.GetType().Name}" +
                $" trigger={collider.isTrigger} enabled={collider.enabled} damage={damage}" +
                $" aiDirect={(direct != null ? "yes" : "no")} aiParent={(inParent != null ? "yes" : "no")}");
            listed++;
        }
    }

    /// <summary>
    /// What an ANGULAR fallback would have chosen: the live animals nearest to the ray by
    /// ANGLE (not distance), with their body bounds so the log shows whether the ray sat
    /// inside the visible animal. If the smallest angle is a few degrees at tens of metres,
    /// the crosshair was on the animal and the ray still found no collider — the evidence
    /// for a cone/sentinel-volume fallback instead of tolerance tuning.
    /// </summary>
    private static void ScanCone(Ray ray)
    {
        BaseAi[]? ais = TryGet(() => UnityEngine.Object.FindObjectsOfType<BaseAi>());
        if (ais == null || ais.Length == 0)
        {
            ModLog.Warn("forensics cone scan: no BaseAi in the scene");
            return;
        }

        var candidates = new List<(float angle, float distance, BaseAi ai, Vector3 size)>();
        foreach (var ai in ais)
        {
            if (ai == null || ai.gameObject == null)
                continue;

            try
            {
                if (ai.GetAiMode() == AiMode.Dead)
                    continue;

                Collider[]? cache = null;
                Vector3 center = ai.transform.position;
                Vector3 size = Vector3.zero;
                if (AnimalBody.TryGetBodyBounds(ai.gameObject, ref cache, out Bounds bounds))
                {
                    center = bounds.center;
                    size = bounds.size;
                }

                Vector3 to = center - ray.origin;
                float distance = to.magnitude;
                if (distance < 0.01f)
                    continue;

                candidates.Add((Vector3.Angle(ray.direction, to), distance, ai, size));
            }
            catch (System.Exception)
            {
                // a dying or half-torn animal must not break the scan; skip it
            }
        }

        if (candidates.Count == 0)
        {
            ModLog.Warn("forensics cone scan: no LIVE animal in the scene");
            return;
        }

        candidates.Sort((a, b) => a.angle.CompareTo(b.angle));
        for (int i = 0; i < candidates.Count && i < CandidateCount; i++)
        {
            var (angle, distance, ai, size) = candidates[i];
            var go = ai.gameObject;
            ModLog.Warn(
                $"forensics cone #{i + 1}: {go.name} id {go.GetInstanceID()} active={go.activeInHierarchy}" +
                $" [{AnimalBody.SpeciesName(ai)}] angle {angle:F2} deg, distance {distance:F1} m," +
                $" body {size.x:F2}x{size.y:F2}x{size.z:F2} m");
        }
    }

    private static T? TryGet<T>(Func<T> read) where T : class
    {
        try { return read(); }
        catch { return null; }
    }

    private static bool TryGet(Func<bool> read, bool fallback)
    {
        try { return read(); }
        catch { return fallback; }
    }
}

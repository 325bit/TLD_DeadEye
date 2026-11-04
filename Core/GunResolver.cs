// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Hemalurgic

using UnityEngine;

namespace DeadEye.Core;

/// <summary>
/// Locates the bound revolver (in hands or backpack) and its GunItem / ammo count.
/// Gear names cross-checked against AdaptiveArsenal's muzzle-velocity table.
/// </summary>
internal static class GunResolver
{
    internal static readonly string[] GearNames =
    {
        "GEAR_Revolver",
        "GEAR_RevolverFancy",
        "GEAR_RevolverGreen",
        "GEAR_RevolverStubNosed",
    };

    internal static string BoundGearName
    {
        get
        {
            int idx = DeadEyeMod.Settings?.BoundRevolver ?? 0;
            return GearNames[Mathf.Clamp(idx, 0, GearNames.Length - 1)];
        }
    }

    /// <summary>The bound revolver if the player carries it (in hands first, then backpack), else null.</summary>
    internal static GearItem? FindCarried()
    {
        string gearName = BoundGearName;

        var inHands = GameManager.GetPlayerManagerComponent()?.m_ItemInHands;
        if (inHands != null && inHands.name != null && inHands.name.Contains(gearName))
            return inHands;

        // Inventory.m_Items is List<GearItemObject> (TLD.Gear struct with m_GearItem/m_GearItemName);
        // iterating it avoids il2cpp generic-delegate marshalling of GetGearItems(Predicate, List).
        var inventory = GameManager.GetInventoryComponent();
        if (inventory?.m_Items == null)
            return null;

        foreach (var itemObject in inventory.m_Items)
        {
            var gearItem = itemObject.m_GearItem;
            if (gearItem != null && gearItem.name != null && gearItem.name.Contains(gearName))
                return gearItem;
        }

        return null;
    }

    internal static GunItem? GetGunItem(GearItem? gearItem)
        => gearItem == null ? null : gearItem.GetComponent<GunItem>();

    /// <summary>Loaded rounds of the bound revolver (the mark cap). 0 when not carried.</summary>
    internal static int RoundsAvailable(GearItem? gearItem)
        => GetGunItem(gearItem)?.NumRoundsInClip() ?? 0;

    internal static bool IsInHands(GearItem gearItem)
        => GameManager.GetPlayerManagerComponent()?.m_ItemInHands == gearItem;
}

using DeadEye.Core;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(DeadEye.DeadEyeMod), DeadEye.BuildInfo.GUIName, DeadEye.BuildInfo.Version, DeadEye.BuildInfo.Author)]
[assembly: MelonGame("Hinterland", "TheLongDark")]

namespace DeadEye;

/// <summary>
/// Dead Eye: an RDR2-style bullet-time marking mode for The Long Dark.
/// Toggle (default mouse side button) while carrying the bound revolver -> time slows, the
/// screen tints yellow, fatigue drains 3x, and every middle-click raycasts a mark onto an
/// animal (cap = revolver clip size). Toggle again -> time restores and the mod auto-draws
/// the revolver, locks the camera onto each mark in order and chain-fires via the vanilla
/// firing pipeline (vanilla damage, bleed, skill XP and audio all preserved).
/// </summary>
public class DeadEyeMod : MelonMod
{
    internal static Settings? Settings;

    public override void OnInitializeMelon()
    {
        ModLog.Bind(LoggerInstance);

        try
        {
            Settings = new Settings();
            Settings.AddToModSettings(BuildInfo.GUIName);
        }
        catch (System.Exception ex)
        {
            LoggerInstance.Error("settings init failed, using built-in defaults: " + ex);
        }

        LoggerInstance.Msg($"{BuildInfo.GUIName} {BuildInfo.Version} loaded");

        try
        {
            HarmonyInstance.PatchAll();

            int patched = 0;
            foreach (var _ in HarmonyInstance.GetPatchedMethods())
                patched++;

            LoggerInstance.Msg($"Harmony patches applied: {patched} method(s)");
        }
        catch (Exception ex)
        {
            LoggerInstance.Error("applying Harmony patches failed, Dead Eye will not work: " + ex);
        }

        TryHookAdaptiveArsenal();
    }

    /// <summary>
    /// AdaptiveArsenal replaces the vanilla projectile pipeline with its own physics
    /// projectile, which makes the vp_Bullet redirect dead code. When its ProjectileItem
    /// type is present, bind an aim prefix onto its private Fire() method so Dead Eye
    /// shots launch exactly onto the marked point. Done via reflection so Dead Eye has no
    /// hard dependency on AdaptiveArsenal.
    /// </summary>
    private void TryHookAdaptiveArsenal()
    {
        try
        {
            var type = HarmonyLib.AccessTools.TypeByName("AdaptiveArsenal.Components.ProjectileItem");
            if (type == null)
            {
                LoggerInstance.Msg("AdaptiveArsenal not detected: vanilla vp_Bullet redirect active");
                return;
            }

            var fire = HarmonyLib.AccessTools.Method(type, "Fire");
            if (fire == null)
            {
                LoggerInstance.Warning("AdaptiveArsenal detected but its ProjectileItem.Fire method was not found; bullet redirect disabled");
                return;
            }

            HarmonyInstance.Patch(fire, prefix: new HarmonyLib.HarmonyMethod(
                typeof(Patches.AdaptiveProjectileRedirectPatch), nameof(Patches.AdaptiveProjectileRedirectPatch.Prefix)));
            Core.Ballistics.SetAdaptiveBallistics(true);
            LoggerInstance.Msg("AdaptiveArsenal detected: projectile aim bound to its Fire method, ballistics halved to its effective speeds");
        }
        catch (Exception ex)
        {
            LoggerInstance.Error("binding the AdaptiveArsenal redirect failed: " + ex);
        }
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        try
        {
            DeadEyeManager.HardReset();
        }
        catch (Exception ex)
        {
            LoggerInstance.Error("scene-change reset failed: " + ex);
        }
    }

    public override void OnUpdate()
    {
        try
        {
            DeadEyeManager.Tick();
        }
        catch (Exception ex)
        {
            LoggerInstance.Error("state tick failed: " + ex);
            DeadEyeManager.ForceExit("tick exception");
        }
    }

    public override void OnGUI()
    {
        try
        {
            if (Settings == null)
                return;
            Overlay.Draw();
        }
        catch (Exception ex)
        {
            LoggerInstance.Error("overlay draw failed: " + ex);
        }
    }
}

// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Hemalurgic

using MelonLoader;

namespace DeadEye;

/// <summary>
/// Thin static wrapper around the MelonLogger instance.
/// A separate class avoids the `Log.` name-collision pitfall (CS0117) inside static helpers.
/// </summary>
internal static class ModLog
{
    private static MelonLogger.Instance? _logger;

    internal static void Bind(MelonLogger.Instance logger) => _logger = logger;

    internal static void Info(string message) => _logger?.Msg(message);
    internal static void Warn(string message) => _logger?.Warning(message);
    internal static void Error(string message) => _logger?.Error(message);

    internal static void Debug(string message)
    {
        if (DeadEyeMod.Settings?.Verbose == true)
            _logger?.Msg("[DEBUG] " + message);
    }
}

namespace DeadEye;

/// <summary>
/// Build metadata referenced by MelonInfo and log lines.
/// </summary>
internal static class BuildInfo
{
    internal const string Name = "TLD_DeadEye";
    internal const string GUIName = "Dead Eye (死神之眼)";
    internal const string Version = "0.7.35";
    internal const string Author = "Hemalurgic";

    /// <summary>
    /// Which configuration this assembly was built in. The DEBUG build is the only one that
    /// exposes the tuning knobs in ModSettings (see Settings); printing it here is how a
    /// deployed dll can be told apart in <c>MelonLoader/Latest.log</c>.
    /// </summary>
    internal const string BuildKind =
#if DEBUG
        "debug";
#else
        "release";
#endif
}

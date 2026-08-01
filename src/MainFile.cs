using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace MultiplayerColors;

/// <summary>
/// Mod entry point. Everything this mod does is a Harmony postfix, so initialization is just PatchAll.
/// </summary>
/// <remarks>
/// ModManager.CallModInitializer only requires the attributed type to expose a static method of the named
/// name — it does not have to be a Godot Node — so this stays a plain static class and the mod needs no
/// Godot project, no .pck, and no BaseLib.
/// </remarks>
[ModInitializer(nameof(Initialize))]
public static class MainFile
{
    public const string ModId = "MultiplayerColors";

    public static Logger Logger { get; } = new(ModId, LogType.Generic);

    public static void Initialize()
    {
        Harmony harmony = new(ModId);
        harmony.PatchAll();
        Logger.Info($"{ModId} initialized.");
    }
}

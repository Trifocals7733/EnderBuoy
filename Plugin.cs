using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using ModSettingsMenu.Api;
using UnityEngine;

namespace EnderBuoy;

[BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
[BepInDependency(ModSettingsMenu.PluginInfo.PLUGIN_GUID, ">=1.1.0")]
public class Plugin : BasePlugin
{
    public const string PLUGIN_GUID = "walker.enderbuoy";
    public const string PLUGIN_NAME = "EnderBuoy";
    public const string PLUGIN_VERSION = "1.0.0";

    internal static new ManualLogSource Log;

    public override void Load()
    {
        Log = base.Log;

        var enabled = Config.Bind("General", "Enabled", true,
            new ConfigDescription("Master switch for EnderBuoy. Turn this off to fully disable the mod.",
                null, ModSettingsTags.Section("General", order: 10), ModSettingsTags.Entry(order: 10)));
        var blink = Config.Bind("Blink", "BlinkEnabled", true,
            new ConfigDescription("When on, throwing a buoy teleports you to where it first lands. Turn off to throw buoys normally.",
                null, ModSettingsTags.Section("Blink", order: 20), ModSettingsTags.Entry(order: 10)));
        var requireLit = Config.Bind("Blink", "RequireLit", true,
            new ConfigDescription("Only lit buoys teleport you. Turn off so even unlit (toggled-off) buoys teleport.",
                null, ModSettingsTags.Entry(order: 20)));
        var maxDist = Config.Bind("Blink", "MaxDistance", 60f,
            new ConfigDescription("Longest allowed teleport in meters, measured from throw point to landing. Longer throws do nothing.",
                new AcceptableValueRange<float>(10f, 200f),
                ModSettingsTags.Entry(order: 30, sliderStep: 5d)));
        var hiddenConfig = new ConfigFile(Config.ConfigFilePath, true);
        var timeout = hiddenConfig.Bind("Blink", "Timeout", 10f,
            "Safety fuse: seconds a tracked throw may stay airborne before giving up. Covers lost props; normal throws land long before this. Giving up means no teleport.");
        var diagnostics = Config.Bind("Blink", "Diagnostics", false,
            new ConfigDescription("Writes a status line to the game log once per second while tracking a thrown buoy.",
                null, ModSettingsTags.Entry(order: 40)));
        var toggleKey = Config.Bind("Input", "ToggleKey", KeyCode.F7,
            new ConfigDescription("Keyboard shortcut that turns Blink on and off. (It does not change the master Enabled switch.)",
                null, ModSettingsTags.Section("Input", order: 30), ModSettingsTags.Entry(order: 10)));

        var playSound = Config.Bind("Effects", "PlaySound", true,
            new ConfigDescription("Play a random throw whoosh sound upon teleporting.",
                null, ModSettingsTags.Section("Effects", order: 25), ModSettingsTags.Entry(order: 10)));
        var spawnSmoke = Config.Bind("Effects", "SpawnSmoke", true,
            new ConfigDescription("Spawn flare smoke clouds at departure and arrival locations.",
                null, ModSettingsTags.Entry(order: 20)));

        ModSettingsRegistry.Register(PLUGIN_GUID, new ModSettingsModOptions
        {
            Name = "EnderBuoy",
            Description = "Throw a buoy, teleport to where it lands.",
            Version = PLUGIN_VERSION
        });

        Blink.Bind(enabled, blink, requireLit, maxDist, timeout, diagnostics, toggleKey, playSound, spawnSmoke);
        new HarmonyLib.Harmony(PLUGIN_GUID).PatchAll();
        AddComponent<Blink>(); // BasePlugin.AddComponent also injects the type

        Log.LogInfo($"EnderBuoy v{PLUGIN_VERSION} loaded.");
    }
}

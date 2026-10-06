// Shared, dependency-free settings-menu ordering tags.
//
// Both in-game menus (ModSettingsMenu 1.1.x and its cfgMenu clone) read
// ConfigDescription.Tags by reflection, not by type identity: they look for
// properties named "Section"/"Order" and "Order"/"SliderStep". So plain classes
// with those property names order our entries in either menu — with no assembly
// reference, and therefore no hard [BepInDependency] that can stop our mod from
// loading when the menu mod is missing or broken by a game update.
//
// Contract (verified by decompiling both mods):
//   section tag: string Section + int? Order        (Order must be set to count)
//   entry tag:   int? Order + double? SliderStep    (must NOT expose Section)
public sealed class SettingsSectionTag
{
    public string Section { get; set; }
    public int? Order { get; set; }
}

public sealed class SettingsEntryTag
{
    public int? Order { get; set; }
    public double? SliderStep { get; set; }
}

public static class SettingsTags
{
    public static SettingsSectionTag Section(string section, int? order = null)
        => new SettingsSectionTag { Section = section, Order = order };

    public static SettingsEntryTag Entry(int? order = null, double? sliderStep = null)
        => new SettingsEntryTag { Order = order, SliderStep = sliderStep };
}

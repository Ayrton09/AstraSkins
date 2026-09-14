namespace AstraSkins.Models;

// Chinese names come from the same upstream data as the English ones; the
// menu shows them to players whose game language is Chinese and falls back
// to English wherever a translation is missing.
public static class DisplayNameExtensions
{
    public static string Localized(this CosmeticEntry entry, bool zh) => Pick(zh, entry.DisplayNameZh, entry.DisplayName);
    public static string Localized(this WeaponDefinition entry, bool zh) => Pick(zh, entry.DisplayNameZh, entry.DisplayName);
    public static string Localized(this KnifeDefinition entry, bool zh) => Pick(zh, entry.DisplayNameZh, entry.DisplayName);
    public static string Localized(this GloveDefinition entry, bool zh) => Pick(zh, entry.DisplayNameZh, entry.DisplayName);
    public static string Localized(this AgentDefinition entry, bool zh) => Pick(zh, entry.DisplayNameZh, entry.DisplayName);
    public static string Localized(this CategoryDefinition entry, bool zh) => Pick(zh, entry.DisplayNameZh, entry.DisplayName);
    public static string Localized(this MusicKitDefinition entry, bool zh) => Pick(zh, entry.DisplayNameZh, entry.DisplayName);
    public static string Localized(this StickerDefinition entry, bool zh) => Pick(zh, entry.DisplayNameZh, entry.DisplayName);
    public static string Localized(this KeychainDefinition entry, bool zh) => Pick(zh, entry.DisplayNameZh, entry.DisplayName);

    private static string Pick(bool zh, string? chinese, string english)
    {
        return zh && !string.IsNullOrWhiteSpace(chinese) ? chinese! : english;
    }
}

using AstraSkins.Models;

namespace AstraSkins;

public interface ISkinStorage : IDisposable
{
    void Initialize();
    PlayerSkinProfile LoadProfile(ulong steamId64);
    void SaveWeaponSkin(ulong steamId64, string weaponEntity, string cosmeticId);
    void SaveKnifeType(ulong steamId64, string knifeId);
    void SaveKnifeSkin(ulong steamId64, string cosmeticId);
    void SaveGloveSkin(ulong steamId64, string cosmeticId);
    void SaveAgent(ulong steamId64, string team, string agentId);
    void ResetProfile(ulong steamId64);
    void ResetCategory(ulong steamId64, string category);
}

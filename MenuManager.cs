using System.Net;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using AstraSkins.Models;

namespace AstraSkins;

public sealed class MenuManager
{
    private readonly SkinManager _skinManager;
    private readonly PluginConfig _config;
    private readonly IStringLocalizer _localizer;
    private readonly ILogger _logger;
    private readonly Dictionary<int, PlayerMenuState> _states = new();
    private readonly Dictionary<int, float> _savedVelocity = new();

    private const int InitialInputDelayMilliseconds = 200;
    private const int MaxTitleLength = 46;
    private const int MaxItemLabelLength = 34;
    private const int MaxSearchResults = 64;
    private const string MusicKitColor = "#f08ac8";
    private const string StickerColor = "#f5c542";
    private const string KeychainColor = "#7fd8c9";

    public MenuManager(SkinManager skinManager, PluginConfig config, IStringLocalizer localizer, ILogger logger)
    {
        _skinManager = skinManager;
        _config = config;
        _localizer = localizer;
        _logger = logger;
    }

    public void OpenMain(CCSPlayerController player)
    {
        var state = GetState(player);
        state.BackStack.Clear();
        state.CategoryId = null;
        state.AgentTeam = null;
        state.Weapon = null;
        state.Knife = null;
        state.Glove = null;
        ResetAttachmentState(state, MenuPurpose.Skins);
        ResetInputState(player, state);
        ChangeView(player, state, MenuView.Main);
    }

    // Straight to the held gun's sticker slots; with nothing usable in hand,
    // to the weapon picker.
    public void OpenStickers(CCSPlayerController player, string? weaponEntity)
    {
        var state = GetState(player);
        state.BackStack.Clear();
        state.CategoryId = null;
        ResetAttachmentState(state, MenuPurpose.Stickers);
        ResetInputState(player, state);
        state.Weapon = FindWeapon(weaponEntity);
        ChangeView(player, state, state.Weapon is null ? MenuView.AttachmentWeapons : MenuView.StickerSlots);
    }

    public void OpenKeychains(CCSPlayerController player, string? weaponEntity)
    {
        var state = GetState(player);
        state.BackStack.Clear();
        state.CategoryId = null;
        ResetAttachmentState(state, MenuPurpose.Keychain);
        ResetInputState(player, state);
        state.Weapon = FindWeapon(weaponEntity);
        ChangeView(player, state, state.Weapon is null ? MenuView.AttachmentWeapons : MenuView.KeychainGroups);
    }

    public void OpenStickerSearch(CCSPlayerController player, string weaponEntity, string query)
    {
        var state = GetState(player);
        state.BackStack.Clear();
        ResetAttachmentState(state, MenuPurpose.Stickers);
        ResetInputState(player, state);
        state.Weapon = FindWeapon(weaponEntity);
        state.SearchQuery = query;
        ChangeView(player, state, MenuView.StickerSearch);
    }

    public void OpenKeychainSearch(CCSPlayerController player, string weaponEntity, string query)
    {
        var state = GetState(player);
        state.BackStack.Clear();
        ResetAttachmentState(state, MenuPurpose.Keychain);
        ResetInputState(player, state);
        state.Weapon = FindWeapon(weaponEntity);
        state.SearchQuery = query;
        ChangeView(player, state, MenuView.KeychainSearch);
    }

    private WeaponDefinition? FindWeapon(string? weaponEntity)
    {
        return weaponEntity is not null && _skinManager.Catalog.WeaponsByEntity.TryGetValue(weaponEntity, out var weapon) ? weapon : null;
    }

    private static void ResetAttachmentState(PlayerMenuState state, MenuPurpose purpose)
    {
        state.Purpose = purpose;
        state.StickerSlot = 0;
        state.StickerGroup = null;
        state.StickerCapsule = null;
        state.KeychainGroup = null;
        state.PendingStickerId = null;
        state.PendingStickerName = null;
    }

    public void OpenKnives(CCSPlayerController player)
    {
        var state = GetState(player);
        state.BackStack.Clear();
        ResetInputState(player, state);
        ChangeView(player, state, MenuView.KnifeTypes);
    }

    public void OpenGloves(CCSPlayerController player)
    {
        var state = GetState(player);
        state.BackStack.Clear();
        ResetInputState(player, state);
        ChangeView(player, state, MenuView.GloveTypes);
    }

    public void OpenAgents(CCSPlayerController player)
    {
        var state = GetState(player);
        state.BackStack.Clear();
        state.AgentTeam = null;
        ResetInputState(player, state);
        ChangeView(player, state, MenuView.AgentTeams);
    }

    public void OpenSearch(CCSPlayerController player, string query)
    {
        var state = GetState(player);
        state.BackStack.Clear();
        state.SearchQuery = query;
        ResetInputState(player, state);
        ChangeView(player, state, MenuView.Search);
    }

    public bool HasSearchResults(CCSPlayerController player)
    {
        return _states.TryGetValue(player.Slot, out var state) &&
               state.View is MenuView.Search or MenuView.StickerSearch or MenuView.KeychainSearch &&
               GetOptions(state).Count > 0;
    }

    public void Close(CCSPlayerController player, bool clearScreen = true)
    {
        if (!_states.Remove(player.Slot))
        {
            return;
        }

        Unfreeze(player);
        if (clearScreen && player.IsValid)
        {
            SafePrint(player, " ");
        }
    }

    public void CloseAll()
    {
        foreach (var player in Utilities.GetPlayers())
        {
            if (player is { IsValid: true })
            {
                Close(player);
            }
        }
    }

    public void CloseSlot(int slot)
    {
        _states.Remove(slot);
        _savedVelocity.Remove(slot);
    }

    public void OnTick()
    {
        if (_states.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var player in Utilities.GetPlayers().Where(p => p is { IsValid: true }))
        {
            if (!_states.TryGetValue(player.Slot, out var state) || !state.IsOpen)
            {
                continue;
            }

            if ((now - state.LastInteractionUtc).TotalSeconds >= _config.Menu.TimeoutSeconds)
            {
                Close(player);
                continue;
            }

            Freeze(player);

            // The button-change listener reads the player's own pawn, which
            // receives no input while dead; poll the observer path instead.
            if (!player.PawnIsAlive)
            {
                PollDeadPlayerButtons(player, state);
                if (!_states.ContainsKey(player.Slot) || !state.IsOpen)
                {
                    continue;
                }
            }
            else
            {
                state.DeadPollingActive = false;
            }

            Render(player, state);
        }
    }

    private void PollDeadPlayerButtons(CCSPlayerController player, PlayerMenuState state)
    {
        PlayerButtons current;
        try
        {
            current = player.Buttons;
        }
        catch
        {
            return;
        }

        if (!state.DeadPollingActive)
        {
            state.DeadPollingActive = true;
            state.PreviousButtons = current;
            return;
        }

        var pressed = current & ~state.PreviousButtons;
        state.PreviousButtons = current;
        if (pressed != 0)
        {
            OnButtonsChanged(player, pressed);
        }
    }

    private PlayerMenuState GetState(CCSPlayerController player)
    {
        if (!_states.TryGetValue(player.Slot, out var state))
        {
            state = new PlayerMenuState { Slot = player.Slot, PreferZh = PrefersChinese(player) };
            _states[player.Slot] = state;
        }

        return state;
    }

    private static void ResetInputState(CCSPlayerController player, PlayerMenuState state)
    {
        var now = DateTime.UtcNow;
        state.OpenedAtUtc = now;
        state.LastInputUtc = now;
        state.LastSelectionUtc = DateTime.MinValue;
        state.LastSelectionKey = null;
        state.LastInteractionUtc = now;
        state.DeadPollingActive = false;
        try
        {
            state.PreviousButtons = player.Buttons;
        }
        catch
        {
            state.PreviousButtons = 0;
        }
    }

    // purpose: set after the snapshot is taken, so going back restores the
    // view the player left with the purpose it had then.
    private void ChangeView(CCSPlayerController player, PlayerMenuState state, MenuView view, bool push = false, MenuPurpose? purpose = null)
    {
        if (push)
        {
            state.BackStack.Push(new MenuSnapshot(
                state.View, state.Cursor, state.CategoryId, state.AgentTeam, state.Weapon, state.Knife, state.Glove,
                state.Purpose, state.StickerSlot, state.StickerGroup, state.StickerCapsule, state.KeychainGroup));
        }

        if (purpose is not null)
        {
            ResetAttachmentState(state, purpose.Value);
        }

        state.View = view;
        state.Cursor = 0;
        // A new view is a new context: the repeat throttle must not carry
        // over to a row that happens to share a label.
        state.LastSelectionKey = null;
        state.LastInteractionUtc = DateTime.UtcNow;
        InvalidateOptions(state);
        Freeze(player);
        Render(player, state);
    }

    private void MoveCursor(PlayerMenuState state, int delta)
    {
        var count = GetOptions(state).Count;
        if (count == 0)
        {
            state.Cursor = 0;
            return;
        }

        state.Cursor = (state.Cursor + delta + count) % count;
    }

    private void GoBack(CCSPlayerController player, PlayerMenuState state)
    {
        if (state.BackStack.TryPop(out var snapshot))
        {
            state.View = snapshot.View;
            state.Cursor = snapshot.Cursor;
            state.CategoryId = snapshot.CategoryId;
            state.AgentTeam = snapshot.AgentTeam;
            state.Weapon = snapshot.Weapon;
            state.Knife = snapshot.Knife;
            state.Glove = snapshot.Glove;
            state.Purpose = snapshot.Purpose;
            state.StickerSlot = snapshot.StickerSlot;
            state.StickerGroup = snapshot.StickerGroup;
            state.StickerCapsule = snapshot.StickerCapsule;
            state.KeychainGroup = snapshot.KeychainGroup;
            // Backing out of the slot choice drops the sticker picked from
            // the search results.
            if (snapshot.View != MenuView.StickerSlots)
            {
                state.PendingStickerId = null;
                state.PendingStickerName = null;
            }

            state.LastSelectionKey = null;
            InvalidateOptions(state);
            return;
        }

        Close(player);
    }

    private void Select(CCSPlayerController player, PlayerMenuState state)
    {
        var options = GetOptions(state);
        if (options.Count == 0)
        {
            return;
        }

        var optionIndex = Math.Clamp(state.Cursor, 0, options.Count - 1);
        var option = options[optionIndex];
        if (option.ThrottleSelection)
        {
            // Throttle repeats of the same option; picking a different option
            // is allowed immediately.
            var selectionKey = $"{state.View}:{option.SelectionKey ?? option.Label}";
            var now = DateTime.UtcNow;
            if (selectionKey.Equals(state.LastSelectionKey, StringComparison.Ordinal) &&
                (now - state.LastSelectionUtc).TotalMilliseconds < _config.Menu.SelectionCooldownMilliseconds)
            {
                return;
            }

            state.LastSelectionKey = selectionKey;
            state.LastSelectionUtc = now;
        }

        option.Action();
        InvalidateOptions(state);
    }

    // Options are cached per state and rebuilt only when the view or the
    // selection changes; the views that list the weapons the player currently
    // owns also refresh on a short TTL.
    private IReadOnlyList<MenuOption> GetOptions(PlayerMenuState state)
    {
        var now = DateTime.UtcNow;
        if (state.CachedOptions is not null &&
            (state.View is not (MenuView.Main or MenuView.AttachmentWeapons) || (now - state.CachedOptionsAtUtc).TotalSeconds < 1))
        {
            return state.CachedOptions;
        }

        state.CachedOptions = BuildOptions(state);
        state.CachedOptionsAtUtc = now;
        return state.CachedOptions;
    }

    private static void InvalidateOptions(PlayerMenuState state)
    {
        state.CachedOptions = null;
    }

    public void InvalidateAll()
    {
        foreach (var state in _states.Values)
        {
            InvalidateOptions(state);
        }
    }

    private IReadOnlyList<MenuOption> BuildOptions(PlayerMenuState state)
    {
        return state.View switch
        {
            MenuView.Main => BuildMainOptions(state),
            MenuView.Categories => BuildCategoryOptions(state),
            MenuView.Weapons => BuildWeaponOptions(state),
            MenuView.WeaponSkins => BuildWeaponSkinOptions(state),
            MenuView.KnifeTypes => BuildKnifeOptions(state),
            MenuView.KnifeSkins => BuildKnifeSkinOptions(state),
            MenuView.GloveTypes => BuildGloveOptions(state),
            MenuView.GloveSkins => BuildGloveSkinOptions(state),
            MenuView.AgentTeams => BuildAgentTeamOptions(state),
            MenuView.Agents => BuildAgentOptions(state),
            MenuView.MusicKits => BuildMusicKitOptions(state),
            MenuView.Search => BuildSearchOptions(state),
            MenuView.AttachmentWeapons => BuildAttachmentWeaponOptions(state),
            MenuView.StickerSlots => BuildStickerSlotOptions(state),
            MenuView.StickerGroups => BuildStickerGroupOptions(state),
            MenuView.StickerCapsules => BuildStickerCapsuleOptions(state),
            MenuView.Stickers => BuildStickerOptions(state),
            MenuView.StickerSearch => BuildStickerSearchOptions(state),
            MenuView.KeychainGroups => BuildKeychainGroupOptions(state),
            MenuView.Keychains => BuildKeychainOptions(state),
            MenuView.KeychainSearch => BuildKeychainSearchOptions(state),
            _ => Array.Empty<MenuOption>()
        };
    }

    private IReadOnlyList<MenuOption> BuildMainOptions(PlayerMenuState state)
    {
        var player = Utilities.GetPlayerFromSlot(state.Slot);
        if (player is null || !player.IsValid)
        {
            return Array.Empty<MenuOption>();
        }

        var options = new List<MenuOption>();
        var visualIndex = 1;
        var profile = _skinManager.GetProfile(player);
        // Each module shows up only when it is on and the player holds its
        // flag; a server that keeps just the charms gets a menu with charms.
        if (_skinManager.CanUseWeapons(player))
        {
            options.Add(new MenuOption($"{visualIndex++}. {_localizer.ForPlayer(player, "menu.configure_all")}", () =>
            {
                var current = Utilities.GetPlayerFromSlot(state.Slot);
                if (current is null) return;
                ChangeView(current, state, MenuView.Categories, push: true);
            }, LabelColor: "#f0b65a"));

            foreach (var weapon in _skinManager.GetOwnedWeaponDefinitions(player))
            {
                // Tint each owned weapon with the rarity of its equipped skin so
                // the main view reads like the real inventory.
                string? equippedRarity = null;
                if (profile.WeaponSkins.TryGetValue(weapon.EntityName, out var equippedId) &&
                    _skinManager.Catalog.WeaponSkinsById.TryGetValue(equippedId, out var equippedSkin))
                {
                    equippedRarity = equippedSkin.Rarity;
                }

                var label = $"{visualIndex++}. {weapon.Localized(state.PreferZh)}";
                options.Add(new MenuOption(label, () =>
                {
                    var current = Utilities.GetPlayerFromSlot(state.Slot);
                    if (current is null) return;
                    state.Weapon = weapon;
                    ChangeView(current, state, MenuView.WeaponSkins, push: true);
                }, LabelColor: RarityColor(equippedRarity)));
            }
        }

        if (_skinManager.CanUseKnives(player))
        {
            var knife = _skinManager.GetCurrentKnifeDefinition(player);
            var knifeLabel = knife is null ? _localizer.ForPlayer(player, "menu.knife") : $"* {knife.Localized(state.PreferZh)}";
            string? knifeRarity = null;
            if (profile.KnifeSkinId is not null &&
                _skinManager.Catalog.KnifeSkinsById.TryGetValue(profile.KnifeSkinId, out var equippedKnifeSkin))
            {
                knifeRarity = equippedKnifeSkin.Rarity;
            }
            options.Add(new MenuOption($"{visualIndex++}. {knifeLabel}", () =>
            {
                var current = Utilities.GetPlayerFromSlot(state.Slot);
                if (current is null) return;
                if (knife is null)
                {
                    OpenKnives(current);
                    return;
                }

                state.Knife = knife;
                ChangeView(current, state, MenuView.KnifeSkins, push: true);
            }, LabelColor: RarityColor(knifeRarity) ?? "#8bdcff"));
        }

        if (_skinManager.CanUseGloves(player))
        {
            options.Add(new MenuOption($"{visualIndex++}. {_localizer.ForPlayer(player, "menu.gloves")}", () =>
            {
                var current = Utilities.GetPlayerFromSlot(state.Slot);
                if (current is not null) ChangeView(current, state, MenuView.GloveTypes, push: true);
            }, LabelColor: "#8bdcff"));
        }

        if (_skinManager.CanUseAgents(player))
        {
            options.Add(new MenuOption($"{visualIndex++}. {_localizer.ForPlayer(player, "menu.agents")}", () =>
            {
                var current = Utilities.GetPlayerFromSlot(state.Slot);
                if (current is not null) ChangeView(current, state, MenuView.AgentTeams, push: true);
            }, LabelColor: "#b58fff"));
        }

        if (_skinManager.CanUseMusicKits(player))
        {
            options.Add(new MenuOption($"{visualIndex++}. {_localizer.ForPlayer(player, "menu.music")}", () =>
            {
                var current = Utilities.GetPlayerFromSlot(state.Slot);
                if (current is not null) ChangeView(current, state, MenuView.MusicKits, push: true);
            }, LabelColor: MusicKitColor));
        }

        if (_skinManager.CanUseStickers(player))
        {
            options.Add(new MenuOption($"{visualIndex++}. {_localizer.ForPlayer(player, "menu.stickers")}", () =>
            {
                var current = Utilities.GetPlayerFromSlot(state.Slot);
                if (current is null) return;
                ChangeView(current, state, MenuView.AttachmentWeapons, push: true, purpose: MenuPurpose.Stickers);
            }, LabelColor: StickerColor));
        }

        if (_skinManager.CanUseKeychains(player))
        {
            options.Add(new MenuOption($"{visualIndex++}. {_localizer.ForPlayer(player, "menu.charms")}", () =>
            {
                var current = Utilities.GetPlayerFromSlot(state.Slot);
                if (current is null) return;
                ChangeView(current, state, MenuView.AttachmentWeapons, push: true, purpose: MenuPurpose.Keychain);
            }, LabelColor: KeychainColor));
        }

        return options;
    }

    // Driven by the OnPlayerButtonsChanged listener: `pressed` only contains
    // buttons that went down this frame, so no previous-state tracking needed.
    public void OnButtonsChanged(CCSPlayerController player, PlayerButtons pressed)
    {
        if (!_states.TryGetValue(player.Slot, out var state) || !state.IsOpen)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if ((now - state.OpenedAtUtc).TotalMilliseconds < InitialInputDelayMilliseconds ||
            (now - state.LastInputUtc).TotalMilliseconds < _config.Menu.CooldownMilliseconds)
        {
            return;
        }

        if ((pressed & PlayerButtons.Reload) != 0)
        {
            Close(player);
            return;
        }

        if ((pressed & PlayerButtons.Forward) != 0)
        {
            MoveCursor(state, -1);
        }
        else if ((pressed & PlayerButtons.Back) != 0)
        {
            MoveCursor(state, 1);
        }
        else if ((pressed & PlayerButtons.Use) != 0)
        {
            Select(player, state);
        }
        else if ((pressed & PlayerButtons.Speed) != 0)
        {
            GoBack(player, state);
        }
        else
        {
            return;
        }

        state.LastInputUtc = now;
        state.LastInteractionUtc = now;
    }

    private IReadOnlyList<MenuOption> BuildCategoryOptions(PlayerMenuState state)
    {
        var menuPlayer = Utilities.GetPlayerFromSlot(state.Slot);
        var options = new List<MenuOption>();
        var categories = _skinManager.Catalog.Categories.Count > 0
            ? _skinManager.Catalog.Categories
            : _skinManager.Catalog.Weapons.Select(w => new CategoryDefinition { Id = w.Category, DisplayName = w.Category }).DistinctBy(c => c.Id).ToList();

        // For skins, the weapon categories show only with the weapons module
        // on. Picking a gun for stickers or a charm goes through here too and
        // does not depend on that module.
        var weaponsOn = state.Purpose != MenuPurpose.Skins || (menuPlayer is not null && _skinManager.CanUseWeapons(menuPlayer));
        foreach (var category in weaponsOn ? categories : Array.Empty<CategoryDefinition>())
        {
            if (!_skinManager.Catalog.Weapons.Any(w => w.Category.Equals(category.Id, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            options.Add(new MenuOption(category.Localized(state.PreferZh), () =>
            {
                var player = Utilities.GetPlayerFromSlot(state.Slot);
                if (player is null) return;
                state.CategoryId = category.Id;
                ChangeView(player, state, MenuView.Weapons, push: true);
            }));
        }

        // Picking a weapon for stickers or a charm: only the gun categories.
        if (state.Purpose != MenuPurpose.Skins)
        {
            return options;
        }

        if (menuPlayer is not null && _skinManager.CanUseKnives(menuPlayer))
        {
            options.Add(new MenuOption(_localizer.ForPlayer(menuPlayer, "menu.knives"), () =>
            {
                var player = Utilities.GetPlayerFromSlot(state.Slot);
                if (player is not null) OpenKnives(player);
            }));
        }

        if (menuPlayer is not null && _skinManager.CanUseGloves(menuPlayer))
        {
            options.Add(new MenuOption(_localizer.ForPlayer(menuPlayer, "menu.gloves"), () =>
            {
                var player = Utilities.GetPlayerFromSlot(state.Slot);
                if (player is not null) ChangeView(player, state, MenuView.GloveTypes, push: true);
            }));
        }

        if (menuPlayer is not null && _skinManager.CanUseAgents(menuPlayer))
        {
            options.Add(new MenuOption(_localizer.ForPlayer(menuPlayer, "menu.agents"), () =>
            {
                var player = Utilities.GetPlayerFromSlot(state.Slot);
                if (player is not null) ChangeView(player, state, MenuView.AgentTeams, push: true);
            }));
        }

        if (menuPlayer is not null && _skinManager.CanUseMusicKits(menuPlayer))
        {
            options.Add(new MenuOption(_localizer.ForPlayer(menuPlayer, "menu.music"), () =>
            {
                var player = Utilities.GetPlayerFromSlot(state.Slot);
                if (player is not null) ChangeView(player, state, MenuView.MusicKits, push: true);
            }));
        }

        return options;
    }

    private IReadOnlyList<MenuOption> BuildMusicKitOptions(PlayerMenuState state)
    {
        var player = Utilities.GetPlayerFromSlot(state.Slot);
        if (player is null)
        {
            return Array.Empty<MenuOption>();
        }

        var profile = _skinManager.GetProfile(player);
        var preferZh = state.PreferZh;
        string Name(MusicKitDefinition kit) =>
            preferZh && !string.IsNullOrWhiteSpace(kit.DisplayNameZh) ? kit.DisplayNameZh! : kit.DisplayName;

        var options = new List<MenuOption>
        {
            new(
                _localizer.ForPlayer(player, "menu.music.default"),
                () =>
                {
                    var current = Utilities.GetPlayerFromSlot(state.Slot);
                    if (current is null) return;
                    _skinManager.ClearMusicKit(current);
                    current.PrintToChat($"{AstraSkinsPlugin.FormatPrefix()} {_localizer.ForPlayer(current, "menu.equipped", _localizer.ForPlayer(current, "menu.music.default"))}");
                    InvalidateOptions(state);
                },
                string.IsNullOrWhiteSpace(profile.MusicKitId),
                ThrottleSelection: true)
        };

        options.AddRange(_skinManager.Catalog.MusicKits
            .Where(k => _skinManager.CanUse(player, k))
            .Select(k => new MenuOption(
                Name(k),
                () =>
                {
                    var current = Utilities.GetPlayerFromSlot(state.Slot);
                    if (current is null) return;
                    if (k.Id.Equals(_skinManager.GetProfile(current).MusicKitId, StringComparison.OrdinalIgnoreCase))
                    {
                        state.LastInteractionUtc = DateTime.UtcNow;
                        Render(current, state);
                        return;
                    }

                    var saved = _skinManager.SetMusicKit(current, k.Id);
                    current.PrintToChat(saved
                        ? $"{AstraSkinsPlugin.FormatPrefix()} {_localizer.ForPlayer(current, "menu.equipped", Name(k))}"
                        : $"{AstraSkinsPlugin.FormatPrefix()} {_localizer.ForPlayer(current, "menu.save_failed")}");
                    state.LastInteractionUtc = DateTime.UtcNow;
                    Render(current, state);
                },
                k.Id.Equals(profile.MusicKitId, StringComparison.OrdinalIgnoreCase),
                ThrottleSelection: true)));

        return options;
    }

    private IReadOnlyList<MenuOption> BuildWeaponOptions(PlayerMenuState state)
    {
        return _skinManager.Catalog.Weapons
            .Where(w => state.CategoryId is null || w.Category.Equals(state.CategoryId, StringComparison.OrdinalIgnoreCase))
            .Select(w => new MenuOption(w.Localized(state.PreferZh), () =>
            {
                var player = Utilities.GetPlayerFromSlot(state.Slot);
                if (player is null) return;
                state.Weapon = w;
                ChangeView(player, state, WeaponTargetView(state.Purpose), push: true);
            }))
            .ToList();
    }

    private static MenuView WeaponTargetView(MenuPurpose purpose)
    {
        return purpose switch
        {
            MenuPurpose.Stickers => MenuView.StickerSlots,
            MenuPurpose.Keychain => MenuView.KeychainGroups,
            _ => MenuView.WeaponSkins
        };
    }

    // Weapon picker for stickers and charms: the guns in hand first, marked
    // when they already carry something, then the full catalog by category.
    private IReadOnlyList<MenuOption> BuildAttachmentWeaponOptions(PlayerMenuState state)
    {
        var player = Utilities.GetPlayerFromSlot(state.Slot);
        if (player is null || !player.IsValid)
        {
            return Array.Empty<MenuOption>();
        }

        var profile = _skinManager.GetProfile(player);
        var options = new List<MenuOption>();
        foreach (var weapon in _skinManager.GetOwnedWeaponDefinitions(player))
        {
            var hasAttachment = state.Purpose == MenuPurpose.Stickers
                ? profile.Stickers.TryGetValue(weapon.EntityName, out var slots) && slots.Count > 0
                : profile.Keychains.ContainsKey(weapon.EntityName);
            options.Add(new MenuOption(weapon.Localized(state.PreferZh), () =>
            {
                var current = Utilities.GetPlayerFromSlot(state.Slot);
                if (current is null) return;
                state.Weapon = weapon;
                ChangeView(current, state, WeaponTargetView(state.Purpose), push: true);
            }, hasAttachment));
        }

        options.Add(new MenuOption(_localizer.ForPlayer(player, "menu.configure_all"), () =>
        {
            var current = Utilities.GetPlayerFromSlot(state.Slot);
            if (current is null) return;
            state.CategoryId = null;
            ChangeView(current, state, MenuView.Categories, push: true);
        }, LabelColor: "#f0b65a"));

        return options;
    }

    // One row per slot showing what is on it. A sticker picked from the
    // search lands on the chosen slot; otherwise the slot opens the catalog.
    private IReadOnlyList<MenuOption> BuildStickerSlotOptions(PlayerMenuState state)
    {
        var player = Utilities.GetPlayerFromSlot(state.Slot);
        if (player is null || state.Weapon is null)
        {
            return Array.Empty<MenuOption>();
        }

        var weapon = state.Weapon;
        var profile = _skinManager.GetProfile(player);
        profile.Stickers.TryGetValue(weapon.EntityName, out var slots);
        var options = new List<MenuOption>();
        for (var slot = 0; slot < SkinManager.StickerSlots; slot++)
        {
            StickerDefinition? equipped = null;
            if (slots is not null && slots.TryGetValue(slot, out var equippedId))
            {
                _skinManager.Catalog.StickersById.TryGetValue(equippedId, out equipped);
            }

            var name = equipped?.Localized(state.PreferZh) ?? _localizer.ForPlayer(player, "menu.sticker.empty");
            var label = _localizer.ForPlayer(player, "menu.sticker.slot", slot + 1, name);
            var slotIndex = slot;
            options.Add(new MenuOption(label, () =>
            {
                var current = Utilities.GetPlayerFromSlot(state.Slot);
                if (current is null) return;
                state.StickerSlot = slotIndex;
                if (state.PendingStickerId is null)
                {
                    state.StickerGroup = null;
                    state.StickerCapsule = null;
                    ChangeView(current, state, MenuView.StickerGroups, push: true);
                    return;
                }

                var pendingId = state.PendingStickerId;
                var pendingName = state.PendingStickerName ?? pendingId;
                state.PendingStickerId = null;
                state.PendingStickerName = null;
                var saved = _skinManager.SetSticker(current, weapon.EntityName, slotIndex, pendingId);
                current.PrintToChat(saved
                    ? $"{AstraSkinsPlugin.FormatPrefix()} {_localizer.ForPlayer(current, "menu.sticker.applied", pendingName, slotIndex + 1)}"
                    : $"{AstraSkinsPlugin.FormatPrefix()} {_localizer.ForPlayer(current, "menu.save_failed")}");
                state.LastInteractionUtc = DateTime.UtcNow;
                InvalidateOptions(state);
                Render(current, state);
            }, equipped is not null, ThrottleSelection: state.PendingStickerId is not null, LabelColor: RarityColor(equipped?.Rarity)));
        }

        return options;
    }

    private IReadOnlyList<MenuOption> BuildStickerGroupOptions(PlayerMenuState state)
    {
        var player = Utilities.GetPlayerFromSlot(state.Slot);
        if (player is null || state.Weapon is null)
        {
            return Array.Empty<MenuOption>();
        }

        var weapon = state.Weapon;
        var slot = state.StickerSlot;
        var options = new List<MenuOption>();
        var profile = _skinManager.GetProfile(player);
        if (profile.Stickers.TryGetValue(weapon.EntityName, out var slots) && slots.ContainsKey(slot))
        {
            options.Add(new MenuOption(_localizer.ForPlayer(player, "menu.sticker.remove"), () =>
            {
                var current = Utilities.GetPlayerFromSlot(state.Slot);
                if (current is null) return;
                if (_skinManager.ClearSticker(current, weapon.EntityName, slot))
                {
                    current.PrintToChat($"{AstraSkinsPlugin.FormatPrefix()} {_localizer.ForPlayer(current, "menu.sticker.removed", slot + 1)}");
                }

                state.LastInteractionUtc = DateTime.UtcNow;
                InvalidateOptions(state);
                Render(current, state);
            }, ThrottleSelection: true, LabelColor: "#ffb3b3"));
        }

        foreach (var group in _skinManager.Catalog.StickerGroups)
        {
            options.Add(new MenuOption(GroupLabel(player, state.PreferZh, group.Name, group.NameZh), () =>
            {
                var current = Utilities.GetPlayerFromSlot(state.Slot);
                if (current is null) return;
                state.StickerGroup = group;
                state.StickerCapsule = null;
                ChangeView(current, state, group.Capsules.Count > 0 ? MenuView.StickerCapsules : MenuView.Stickers, push: true);
            }));
        }

        return options;
    }

    private IReadOnlyList<MenuOption> BuildStickerCapsuleOptions(PlayerMenuState state)
    {
        var player = Utilities.GetPlayerFromSlot(state.Slot);
        var group = state.StickerGroup;
        if (player is null || group is null)
        {
            return Array.Empty<MenuOption>();
        }

        var labels = DisambiguateLabels(group.Capsules.Select(capsule => CapsuleLabel(group, capsule, state.PreferZh)).ToList());
        var options = new List<MenuOption>();
        for (var index = 0; index < group.Capsules.Count; index++)
        {
            var capsule = group.Capsules[index];
            options.Add(new MenuOption(labels[index], () =>
            {
                var current = Utilities.GetPlayerFromSlot(state.Slot);
                if (current is null) return;
                state.StickerCapsule = capsule;
                ChangeView(current, state, MenuView.Stickers, push: true);
            }, SelectionKey: capsule.Name));
        }

        if (group.Stickers.Count > 0)
        {
            options.Add(new MenuOption(_localizer.ForPlayer(player, "menu.other"), () =>
            {
                var current = Utilities.GetPlayerFromSlot(state.Slot);
                if (current is null) return;
                state.StickerCapsule = null;
                ChangeView(current, state, MenuView.Stickers, push: true);
            }));
        }

        return options;
    }

    private IReadOnlyList<MenuOption> BuildStickerOptions(PlayerMenuState state)
    {
        var player = Utilities.GetPlayerFromSlot(state.Slot);
        var weapon = state.Weapon;
        var stickers = state.StickerCapsule?.Stickers ?? state.StickerGroup?.Stickers;
        if (player is null || weapon is null || stickers is null)
        {
            return Array.Empty<MenuOption>();
        }

        var slot = state.StickerSlot;
        var profile = _skinManager.GetProfile(player);
        string? equippedId = null;
        if (profile.Stickers.TryGetValue(weapon.EntityName, out var slots))
        {
            slots.TryGetValue(slot, out equippedId);
        }

        return stickers
            .Where(sticker => _skinManager.CanUse(player, sticker))
            .Select(sticker => new MenuOption(sticker.Localized(state.PreferZh), () =>
            {
                var current = Utilities.GetPlayerFromSlot(state.Slot);
                if (current is null) return;
                ApplySticker(current, state, weapon, slot, sticker);
            }, sticker.Id.Equals(equippedId, StringComparison.OrdinalIgnoreCase), ThrottleSelection: true, LabelColor: RarityColor(sticker.Rarity), SelectionKey: sticker.Id))
            .ToList();
    }

    private void ApplySticker(CCSPlayerController player, PlayerMenuState state, WeaponDefinition weapon, int slot, StickerDefinition sticker)
    {
        var profile = _skinManager.GetProfile(player);
        if (profile.Stickers.TryGetValue(weapon.EntityName, out var slots) &&
            slots.TryGetValue(slot, out var equippedId) &&
            equippedId.Equals(sticker.Id, StringComparison.OrdinalIgnoreCase))
        {
            state.LastInteractionUtc = DateTime.UtcNow;
            Render(player, state);
            return;
        }

        var saved = _skinManager.SetSticker(player, weapon.EntityName, slot, sticker.Id);
        player.PrintToChat(saved
            ? $"{AstraSkinsPlugin.FormatPrefix()} {_localizer.ForPlayer(player, "menu.sticker.applied", sticker.Localized(state.PreferZh), slot + 1)}"
            : $"{AstraSkinsPlugin.FormatPrefix()} {_localizer.ForPlayer(player, "menu.save_failed")}");
        state.LastInteractionUtc = DateTime.UtcNow;
        InvalidateOptions(state);
        Render(player, state);
    }

    // Flat search over every sticker; picking one asks for the slot next.
    private IReadOnlyList<MenuOption> BuildStickerSearchOptions(PlayerMenuState state)
    {
        var player = Utilities.GetPlayerFromSlot(state.Slot);
        if (player is null || !player.IsValid || state.Weapon is null || string.IsNullOrWhiteSpace(state.SearchQuery))
        {
            return Array.Empty<MenuOption>();
        }

        var terms = state.SearchQuery.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0)
        {
            return Array.Empty<MenuOption>();
        }

        var zh = state.PreferZh;
        var options = new List<MenuOption>();
        foreach (var sticker in _skinManager.Catalog.Stickers)
        {
            if (options.Count >= MaxSearchResults)
            {
                break;
            }

            var label = StickerSearchLabel(player, zh, sticker);
            var english = StickerSearchLabel(player, false, sticker);
            if (!MatchesEither(label, english, terms) || !_skinManager.CanUse(player, sticker))
            {
                continue;
            }

            options.Add(new MenuOption(label, () =>
            {
                var current = Utilities.GetPlayerFromSlot(state.Slot);
                if (current is null) return;
                state.PendingStickerId = sticker.Id;
                state.PendingStickerName = sticker.Localized(zh);
                ChangeView(current, state, MenuView.StickerSlots, push: true);
            }, LabelColor: RarityColor(sticker.Rarity)));
        }

        return options;
    }

    // Name first: rows are trimmed to MaxItemLabelLength, and the variant
    // ("Holo", "Gold, Ranked") is what tells the results apart.
    private string StickerSearchLabel(CCSPlayerController player, bool zh, StickerDefinition sticker)
    {
        var group = GroupLabel(player, zh, sticker.Group, sticker.GroupZh);
        return $"{sticker.Localized(zh)} | {group}";
    }

    private IReadOnlyList<MenuOption> BuildKeychainGroupOptions(PlayerMenuState state)
    {
        var player = Utilities.GetPlayerFromSlot(state.Slot);
        if (player is null || state.Weapon is null)
        {
            return Array.Empty<MenuOption>();
        }

        var weapon = state.Weapon;
        var options = new List<MenuOption>();
        if (_skinManager.GetProfile(player).Keychains.ContainsKey(weapon.EntityName))
        {
            options.Add(new MenuOption(_localizer.ForPlayer(player, "menu.charm.remove"), () =>
            {
                var current = Utilities.GetPlayerFromSlot(state.Slot);
                if (current is null) return;
                if (_skinManager.ClearKeychain(current, weapon.EntityName))
                {
                    current.PrintToChat($"{AstraSkinsPlugin.FormatPrefix()} {_localizer.ForPlayer(current, "menu.charm.removed")}");
                }

                state.LastInteractionUtc = DateTime.UtcNow;
                InvalidateOptions(state);
                Render(current, state);
            }, ThrottleSelection: true, LabelColor: "#ffb3b3"));
        }

        foreach (var group in _skinManager.Catalog.KeychainGroups)
        {
            options.Add(new MenuOption(GroupLabel(player, state.PreferZh, group.Name, group.NameZh), () =>
            {
                var current = Utilities.GetPlayerFromSlot(state.Slot);
                if (current is null) return;
                state.KeychainGroup = group;
                ChangeView(current, state, MenuView.Keychains, push: true);
            }));
        }

        return options;
    }

    private IReadOnlyList<MenuOption> BuildKeychainOptions(PlayerMenuState state)
    {
        var player = Utilities.GetPlayerFromSlot(state.Slot);
        var weapon = state.Weapon;
        var group = state.KeychainGroup;
        if (player is null || weapon is null || group is null)
        {
            return Array.Empty<MenuOption>();
        }

        _skinManager.GetProfile(player).Keychains.TryGetValue(weapon.EntityName, out var equippedId);
        return group.Keychains
            .Where(keychain => _skinManager.CanUse(player, keychain))
            .Select(keychain => new MenuOption(keychain.Localized(state.PreferZh), () =>
            {
                var current = Utilities.GetPlayerFromSlot(state.Slot);
                if (current is null) return;
                ApplyKeychain(current, state, weapon, keychain);
            }, keychain.Id.Equals(equippedId, StringComparison.OrdinalIgnoreCase), ThrottleSelection: true, LabelColor: RarityColor(keychain.Rarity), SelectionKey: keychain.Id))
            .ToList();
    }

    private void ApplyKeychain(CCSPlayerController player, PlayerMenuState state, WeaponDefinition weapon, KeychainDefinition keychain)
    {
        if (_skinManager.GetProfile(player).Keychains.TryGetValue(weapon.EntityName, out var equippedId) &&
            equippedId.Equals(keychain.Id, StringComparison.OrdinalIgnoreCase))
        {
            state.LastInteractionUtc = DateTime.UtcNow;
            Render(player, state);
            return;
        }

        var saved = _skinManager.SetKeychain(player, weapon.EntityName, keychain.Id);
        player.PrintToChat(saved
            ? $"{AstraSkinsPlugin.FormatPrefix()} {_localizer.ForPlayer(player, "menu.equipped", keychain.Localized(state.PreferZh))}"
            : $"{AstraSkinsPlugin.FormatPrefix()} {_localizer.ForPlayer(player, "menu.save_failed")}");
        state.LastInteractionUtc = DateTime.UtcNow;
        InvalidateOptions(state);
        Render(player, state);
    }

    private IReadOnlyList<MenuOption> BuildKeychainSearchOptions(PlayerMenuState state)
    {
        var player = Utilities.GetPlayerFromSlot(state.Slot);
        var weapon = state.Weapon;
        if (player is null || !player.IsValid || weapon is null || string.IsNullOrWhiteSpace(state.SearchQuery))
        {
            return Array.Empty<MenuOption>();
        }

        var terms = state.SearchQuery.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0)
        {
            return Array.Empty<MenuOption>();
        }

        var zh = state.PreferZh;
        _skinManager.GetProfile(player).Keychains.TryGetValue(weapon.EntityName, out var equippedId);
        var options = new List<MenuOption>();
        foreach (var keychain in _skinManager.Catalog.Keychains)
        {
            if (options.Count >= MaxSearchResults)
            {
                break;
            }

            var label = $"{keychain.Localized(zh)} | {GroupLabel(player, zh, keychain.Group, keychain.GroupZh)}";
            var english = $"{keychain.DisplayName} | {GroupLabel(player, false, keychain.Group, keychain.GroupZh)}";
            if (!MatchesEither(label, english, terms) || !_skinManager.CanUse(player, keychain))
            {
                continue;
            }

            options.Add(new MenuOption(label, () =>
            {
                var current = Utilities.GetPlayerFromSlot(state.Slot);
                if (current is null) return;
                ApplyKeychain(current, state, weapon, keychain);
            }, keychain.Id.Equals(equippedId, StringComparison.OrdinalIgnoreCase), ThrottleSelection: true, LabelColor: RarityColor(keychain.Rarity), SelectionKey: keychain.Id));
        }

        return options;
    }

    // Capsule names repeat the event ("Cologne 2026 Team Sticker Capsule"
    // under "IEM Cologne 2026"); the title already says it, so the leading
    // words the group name contains are dropped. Chinese names have no word
    // boundaries and are left alone.
    private static string CapsuleLabel(StickerGroup group, StickerCapsule capsule, bool zh)
    {
        var name = zh && !string.IsNullOrWhiteSpace(capsule.NameZh) ? capsule.NameZh! : capsule.Name;
        if (zh || string.IsNullOrWhiteSpace(group.Name))
        {
            return name;
        }

        var groupWords = new HashSet<string>(group.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var skip = 0;
        while (skip < words.Length - 1 && groupWords.Contains(words[skip]))
        {
            skip++;
        }

        return skip == 0 ? name : string.Join(' ', words.Skip(skip));
    }

    // Rows that would render the same once trimmed drop as few of their
    // shared leading words as the trim needs, keeping the rest for context:
    // "...with Flash Gaming (Holo/Foil)" next to "...with Flash Gaming
    // Autograph Capsule".
    private static List<string> DisambiguateLabels(List<string> labels)
    {
        var result = new List<string>(labels);
        var clashes = labels
            .Select((label, index) => (Label: label, Index: index))
            .GroupBy(entry => TrimForOverlay(entry.Label, MaxItemLabelLength), StringComparer.Ordinal)
            .Where(g => g.Count() > 1 && g.Key.EndsWith("...", StringComparison.Ordinal));
        foreach (var clash in clashes)
        {
            var members = clash.ToList();
            var prefix = CommonPrefixLength(members.Select(m => m.Label).ToList());
            var first = members[0].Label;
            for (var cut = first.IndexOf(' ', 0); cut > 0 && cut < prefix; cut = first.IndexOf(' ', cut + 1))
            {
                var start = cut + 1;
                var candidates = members.Select(m => "..." + m.Label[start..]).ToList();
                if (candidates.Select(c => TrimForOverlay(c, MaxItemLabelLength)).Distinct(StringComparer.Ordinal).Count() != members.Count)
                {
                    continue;
                }

                for (var i = 0; i < members.Count; i++)
                {
                    result[members[i].Index] = candidates[i];
                }

                break;
            }
        }

        return result;
    }

    private static int CommonPrefixLength(List<string> labels)
    {
        var first = labels[0];
        var length = first.Length;
        foreach (var other in labels.Skip(1))
        {
            var i = 0;
            while (i < length && i < other.Length && first[i] == other[i])
            {
                i++;
            }

            length = i;
        }

        return length;
    }

    private string GroupLabel(CCSPlayerController player, bool zh, string? name, string? nameZh)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return _localizer.ForPlayer(player, "menu.other");
        }

        return zh && !string.IsNullOrWhiteSpace(nameZh) ? nameZh! : name;
    }

    private IReadOnlyList<MenuOption> BuildWeaponSkinOptions(PlayerMenuState state)
    {
        if (state.Weapon is null)
        {
            return Array.Empty<MenuOption>();
        }

        var player = Utilities.GetPlayerFromSlot(state.Slot);
        var profile = player is not null ? _skinManager.GetProfile(player) : null;
        string? selectedId = null;
        profile?.WeaponSkins.TryGetValue(state.Weapon.EntityName, out selectedId);

        var weaponEntity = state.Weapon.EntityName;
        var options = new List<MenuOption>
        {
            DefaultOption(state, "menu.skin.default", selectedId is null, current => _skinManager.ClearWeaponSkin(current, weaponEntity))
        };
        options.AddRange(state.Weapon.Skins
            .Where(s => player is null || _skinManager.CanUse(player, s))
            .Select(s => new MenuOption(s.Localized(state.PreferZh), () =>
            {
                var current = Utilities.GetPlayerFromSlot(state.Slot);
                if (current is null || state.Weapon is null) return;
                var currentSelectedId = _skinManager.GetProfile(current).WeaponSkins.TryGetValue(state.Weapon.EntityName, out var weaponSkinId)
                    ? weaponSkinId
                    : null;
                if (s.Id.Equals(currentSelectedId, StringComparison.OrdinalIgnoreCase))
                {
                    state.LastInteractionUtc = DateTime.UtcNow;
                    Render(current, state);
                    return;
                }

                var saved = _skinManager.SetWeaponSkin(current, state.Weapon.EntityName, s.Id);
                current.PrintToChat(saved
                    ? $"{AstraSkinsPlugin.FormatPrefix()} {_localizer.ForPlayer(current, "menu.equipped", s.Localized(state.PreferZh))}"
                    : $"{AstraSkinsPlugin.FormatPrefix()} {_localizer.ForPlayer(current, "menu.save_failed")}");
                state.LastInteractionUtc = DateTime.UtcNow;
                Render(current, state);
            }, s.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase), ThrottleSelection: true, LabelColor: RarityColor(s.Rarity))));
        return options;
    }

    // The "Default" row at the top of a list: drops the selection and marks
    // itself when nothing is selected. A clear that had nothing to drop just
    // re-renders, like picking the row that is already selected.
    private MenuOption DefaultOption(PlayerMenuState state, string labelKey, bool selected, Func<CCSPlayerController, bool> clear, string messageKey = "menu.equipped")
    {
        var player = Utilities.GetPlayerFromSlot(state.Slot);
        return new MenuOption(_localizer.ForPlayer(player, labelKey), () =>
        {
            var current = Utilities.GetPlayerFromSlot(state.Slot);
            if (current is null) return;
            if (clear(current))
            {
                current.PrintToChat($"{AstraSkinsPlugin.FormatPrefix()} {_localizer.ForPlayer(current, messageKey, _localizer.ForPlayer(current, labelKey))}");
            }

            state.LastInteractionUtc = DateTime.UtcNow;
            Render(current, state);
        }, selected, ThrottleSelection: true);
    }

    private IReadOnlyList<MenuOption> BuildKnifeOptions(PlayerMenuState state)
    {
        var player = Utilities.GetPlayerFromSlot(state.Slot);
        var profile = player is not null ? _skinManager.GetProfile(player) : null;
        var selectedKnifeId = player is not null
            ? profile!.KnifeId ?? _skinManager.GetCurrentKnifeDefinition(player)?.Id
            : null;
        var options = new List<MenuOption>
        {
            // Marked only when nothing is saved and the knife in hand is not a
            // catalog one either (the row below would be marked then).
            DefaultOption(state, "menu.knife.default", profile is { KnifeSkinId: null } && selectedKnifeId is null, current => _skinManager.ClearKnife(current))
        };
        options.AddRange(_skinManager.Catalog.Knives
            .Where(k => player is null || _skinManager.CanUse(player, k))
            .Select(k => new MenuOption(k.Localized(state.PreferZh), () =>
            {
                var current = Utilities.GetPlayerFromSlot(state.Slot);
                if (current is null) return;
                if (k.Id.Equals(_skinManager.GetProfile(current).KnifeId, StringComparison.OrdinalIgnoreCase))
                {
                    state.LastInteractionUtc = DateTime.UtcNow;
                    Render(current, state);
                    return;
                }

                state.Knife = k;
                var saved = _skinManager.SetKnifeType(current, k.Id);
                current.PrintToChat(saved
                    ? $"{AstraSkinsPlugin.FormatPrefix()} {_localizer.ForPlayer(current, "menu.equipped", k.Localized(state.PreferZh))}"
                    : $"{AstraSkinsPlugin.FormatPrefix()} {_localizer.ForPlayer(current, "menu.save_failed")}");
                state.LastInteractionUtc = DateTime.UtcNow;
                Render(current, state);
            }, k.Id.Equals(selectedKnifeId, StringComparison.OrdinalIgnoreCase), ThrottleSelection: true)));
        return options;
    }

    private IReadOnlyList<MenuOption> BuildKnifeSkinOptions(PlayerMenuState state)
    {
        if (state.Knife is null)
        {
            return Array.Empty<MenuOption>();
        }

        var player = Utilities.GetPlayerFromSlot(state.Slot);
        var selectedId = player is not null ? _skinManager.GetProfile(player).KnifeSkinId : null;
        var options = new List<MenuOption>
        {
            DefaultOption(state, "menu.skin.default", selectedId is null, current => _skinManager.ClearKnifeSkin(current))
        };
        options.AddRange(state.Knife.Skins
            .Where(s => player is null || _skinManager.CanUse(player, s))
            .Select(s => new MenuOption(s.Localized(state.PreferZh), () =>
            {
                var current = Utilities.GetPlayerFromSlot(state.Slot);
                if (current is null) return;
                if (s.Id.Equals(_skinManager.GetProfile(current).KnifeSkinId, StringComparison.OrdinalIgnoreCase))
                {
                    state.LastInteractionUtc = DateTime.UtcNow;
                    Render(current, state);
                    return;
                }

                var saved = _skinManager.SetKnifeSkin(current, s.Id);
                current.PrintToChat(saved
                    ? $"{AstraSkinsPlugin.FormatPrefix()} {_localizer.ForPlayer(current, "menu.equipped", s.Localized(state.PreferZh))}"
                    : $"{AstraSkinsPlugin.FormatPrefix()} {_localizer.ForPlayer(current, "menu.save_failed")}");
                state.LastInteractionUtc = DateTime.UtcNow;
                Render(current, state);
            }, s.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase), ThrottleSelection: true, LabelColor: RarityColor(s.Rarity))));
        return options;
    }

    private IReadOnlyList<MenuOption> BuildGloveOptions(PlayerMenuState state)
    {
        var player = Utilities.GetPlayerFromSlot(state.Slot);
        var options = new List<MenuOption>
        {
            DefaultOption(state, "menu.gloves.default", player is not null && _skinManager.GetProfile(player).GloveSkinId is null, current => _skinManager.ClearGloveSkin(current))
        };
        options.AddRange(_skinManager.Catalog.Gloves
            .Where(g => player is null || _skinManager.CanUse(player, g))
            .Select(g => new MenuOption(g.Localized(state.PreferZh), () =>
            {
                var current = Utilities.GetPlayerFromSlot(state.Slot);
                if (current is null) return;
                state.Glove = g;
                ChangeView(current, state, MenuView.GloveSkins, push: true);
            })));
        return options;
    }

    private IReadOnlyList<MenuOption> BuildGloveSkinOptions(PlayerMenuState state)
    {
        if (state.Glove is null)
        {
            return Array.Empty<MenuOption>();
        }

        var player = Utilities.GetPlayerFromSlot(state.Slot);
        var selectedId = player is not null ? _skinManager.GetProfile(player).GloveSkinId : null;
        var options = new List<MenuOption>
        {
            DefaultOption(state, "menu.gloves.default", selectedId is null, current => _skinManager.ClearGloveSkin(current))
        };
        options.AddRange(state.Glove.Skins
            .Where(s => player is null || _skinManager.CanUse(player, s))
            .Select(s => new MenuOption(s.Localized(state.PreferZh), () =>
            {
                var current = Utilities.GetPlayerFromSlot(state.Slot);
                if (current is null) return;
                if (s.Id.Equals(_skinManager.GetProfile(current).GloveSkinId, StringComparison.OrdinalIgnoreCase))
                {
                    state.LastInteractionUtc = DateTime.UtcNow;
                    Render(current, state);
                    return;
                }

                var saved = _skinManager.SetGloveSkin(current, s.Id);
                current.PrintToChat(saved
                    ? $"{AstraSkinsPlugin.FormatPrefix()} {_localizer.ForPlayer(current, "menu.equipped", s.Localized(state.PreferZh))}"
                    : $"{AstraSkinsPlugin.FormatPrefix()} {_localizer.ForPlayer(current, "menu.save_failed")}");
                state.LastInteractionUtc = DateTime.UtcNow;
                Render(current, state);
            }, s.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase), ThrottleSelection: true, LabelColor: RarityColor(s.Rarity))));
        return options;
    }

    private IReadOnlyList<MenuOption> BuildAgentTeamOptions(PlayerMenuState state)
    {
        var menuPlayer = Utilities.GetPlayerFromSlot(state.Slot);
        var options = new List<MenuOption>();
        if (_skinManager.Catalog.Agents.Any(a => a.Team.Equals("t", StringComparison.OrdinalIgnoreCase)))
        {
            options.Add(new MenuOption(_localizer.ForPlayer(menuPlayer, "menu.t_agents"), () =>
            {
                var player = Utilities.GetPlayerFromSlot(state.Slot);
                if (player is null) return;
                state.AgentTeam = "t";
                ChangeView(player, state, MenuView.Agents, push: true);
            }));
        }

        if (_skinManager.Catalog.Agents.Any(a => a.Team.Equals("ct", StringComparison.OrdinalIgnoreCase)))
        {
            options.Add(new MenuOption(_localizer.ForPlayer(menuPlayer, "menu.ct_agents"), () =>
            {
                var player = Utilities.GetPlayerFromSlot(state.Slot);
                if (player is null) return;
                state.AgentTeam = "ct";
                ChangeView(player, state, MenuView.Agents, push: true);
            }));
        }

        return options;
    }

    private IReadOnlyList<MenuOption> BuildAgentOptions(PlayerMenuState state)
    {
        if (state.AgentTeam is not "t" and not "ct")
        {
            return Array.Empty<MenuOption>();
        }

        var player = Utilities.GetPlayerFromSlot(state.Slot);
        var selectedId = player is not null && _skinManager.GetProfile(player).AgentIdsByTeam.TryGetValue(state.AgentTeam, out var agentId)
            ? agentId
            : null;

        var team = state.AgentTeam;
        var options = new List<MenuOption>
        {
            DefaultOption(state, "menu.agent.default", player is not null && selectedId is null, current => _skinManager.ClearAgent(current, team), "menu.agent.cleared")
        };
        options.AddRange(_skinManager.Catalog.Agents
            .Where(a => a.Team.Equals(state.AgentTeam, StringComparison.OrdinalIgnoreCase))
            .Where(a => player is null || _skinManager.CanUse(player, a))
            .Select(a => new MenuOption(a.Localized(state.PreferZh), () =>
            {
                var current = Utilities.GetPlayerFromSlot(state.Slot);
                if (current is null || state.AgentTeam is null) return;
                if (a.Id.Equals(_skinManager.GetProfile(current).AgentIdsByTeam.GetValueOrDefault(state.AgentTeam), StringComparison.OrdinalIgnoreCase))
                {
                    state.LastInteractionUtc = DateTime.UtcNow;
                    Render(current, state);
                    return;
                }

                var saved = _skinManager.SetAgent(current, state.AgentTeam, a.Id);
                current.PrintToChat(saved
                    ? $"{AstraSkinsPlugin.FormatPrefix()} {_localizer.ForPlayer(current, "menu.equipped", a.Localized(state.PreferZh))}"
                    : $"{AstraSkinsPlugin.FormatPrefix()} {_localizer.ForPlayer(current, "menu.save_failed")}");
                state.LastInteractionUtc = DateTime.UtcNow;
                Render(current, state);
            }, a.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase), ThrottleSelection: true, LabelColor: RarityColor(a.Rarity))));
        return options;
    }

    // Flat search across every cosmetic the player may equip. Every whitespace
    // separated term must appear in the entry label, so "ak redline" works.
    private IReadOnlyList<MenuOption> BuildSearchOptions(PlayerMenuState state)
    {
        var player = Utilities.GetPlayerFromSlot(state.Slot);
        if (player is null || !player.IsValid || string.IsNullOrWhiteSpace(state.SearchQuery))
        {
            return Array.Empty<MenuOption>();
        }

        // Any whitespace, so an ideographic space from a Chinese IME splits too.
        var terms = state.SearchQuery.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var zh = state.PreferZh;
        if (terms.Length == 0)
        {
            return Array.Empty<MenuOption>();
        }

        var profile = _skinManager.GetProfile(player);
        var catalog = _skinManager.Catalog;
        var options = new List<MenuOption>();

        void Add(string label, bool selected, Func<CCSPlayerController, bool> apply, string? rarity = null, string? color = null)
        {
            options.Add(new MenuOption(label, () =>
            {
                var current = Utilities.GetPlayerFromSlot(state.Slot);
                if (current is null || !current.IsValid)
                {
                    return;
                }

                var saved = apply(current);
                current.PrintToChat(saved
                    ? $"{AstraSkinsPlugin.FormatPrefix()} {_localizer.ForPlayer(current, "menu.equipped", label)}"
                    : $"{AstraSkinsPlugin.FormatPrefix()} {_localizer.ForPlayer(current, "menu.save_failed")}");
                state.LastInteractionUtc = DateTime.UtcNow;
                Render(current, state);
            }, selected, ThrottleSelection: true, LabelColor: color ?? RarityColor(rarity)));
        }

        // Every section answers to its module switch and flag.
        foreach (var weapon in _skinManager.CanUseWeapons(player) ? catalog.Weapons : Array.Empty<WeaponDefinition>())
        {
            foreach (var skin in weapon.Skins)
            {
                if (options.Count >= MaxSearchResults)
                {
                    return options;
                }

                var label = $"{weapon.Localized(zh)} | {skin.Localized(zh)}";
                var english = $"{weapon.DisplayName} | {skin.DisplayName}";
                if (!MatchesEither(label, english, terms) || !_skinManager.CanUse(player, skin))
                {
                    continue;
                }

                var entity = weapon.EntityName;
                var skinId = skin.Id;
                var selected = profile.WeaponSkins.TryGetValue(entity, out var equipped) &&
                               equipped.Equals(skinId, StringComparison.OrdinalIgnoreCase);
                Add(label, selected, current => _skinManager.SetWeaponSkin(current, entity, skinId), skin.Rarity);
            }
        }

        foreach (var knife in _skinManager.CanUseKnives(player) ? catalog.Knives : Array.Empty<KnifeDefinition>())
        {
            if (!_skinManager.CanUse(player, knife))
            {
                continue;
            }

            foreach (var skin in knife.Skins)
            {
                if (options.Count >= MaxSearchResults)
                {
                    return options;
                }

                var label = $"{knife.Localized(zh)} | {skin.Localized(zh)}";
                var english = $"{knife.DisplayName} | {skin.DisplayName}";
                if (!MatchesEither(label, english, terms) || !_skinManager.CanUse(player, skin))
                {
                    continue;
                }

                var skinId = skin.Id;
                var selected = skinId.Equals(profile.KnifeSkinId, StringComparison.OrdinalIgnoreCase);
                Add(label, selected, current => _skinManager.SetKnifeSkin(current, skinId), skin.Rarity);
            }
        }

        foreach (var glove in _skinManager.CanUseGloves(player) ? catalog.Gloves : Array.Empty<GloveDefinition>())
        {
            if (!_skinManager.CanUse(player, glove))
            {
                continue;
            }

            foreach (var skin in glove.Skins)
            {
                if (options.Count >= MaxSearchResults)
                {
                    return options;
                }

                var label = $"{glove.Localized(zh)} | {skin.Localized(zh)}";
                var english = $"{glove.DisplayName} | {skin.DisplayName}";
                if (!MatchesEither(label, english, terms) || !_skinManager.CanUse(player, skin))
                {
                    continue;
                }

                var skinId = skin.Id;
                var selected = skinId.Equals(profile.GloveSkinId, StringComparison.OrdinalIgnoreCase);
                Add(label, selected, current => _skinManager.SetGloveSkin(current, skinId), skin.Rarity);
            }
        }

        foreach (var agent in _skinManager.CanUseAgents(player) ? catalog.Agents : Array.Empty<AgentDefinition>())
        {
            if (options.Count >= MaxSearchResults)
            {
                return options;
            }

            var label = $"{agent.Team.ToUpperInvariant()} | {agent.Localized(zh)}";
            var english = $"{agent.Team.ToUpperInvariant()} | {agent.DisplayName}";
            if (!MatchesEither(label, english, terms) || !_skinManager.CanUse(player, agent))
            {
                continue;
            }

            var agentId = agent.Id;
            var team = agent.Team;
            var selected = profile.AgentIdsByTeam.TryGetValue(team, out var equippedAgent) &&
                           equippedAgent.Equals(agentId, StringComparison.OrdinalIgnoreCase);
            Add(label, selected, current => _skinManager.SetAgent(current, team, agentId), agent.Rarity);
        }

        var preferZh = state.PreferZh;
        var musicLabel = _localizer.ForPlayer(player, "menu.music");
        foreach (var kit in _skinManager.CanUseMusicKits(player) ? catalog.MusicKits : Array.Empty<MusicKitDefinition>())
        {
            if (options.Count >= MaxSearchResults)
            {
                return options;
            }

            var name = preferZh && !string.IsNullOrWhiteSpace(kit.DisplayNameZh) ? kit.DisplayNameZh! : kit.DisplayName;
            var label = $"{musicLabel} | {name}";
            // Match the English name too so a zh player can search either way.
            if ((!MatchesAllTerms(label, terms) && !MatchesAllTerms(kit.DisplayName, terms)) || !_skinManager.CanUse(player, kit))
            {
                continue;
            }

            var kitId = kit.Id;
            var selected = kitId.Equals(profile.MusicKitId, StringComparison.OrdinalIgnoreCase);
            Add(label, selected, current => _skinManager.SetMusicKit(current, kitId), color: MusicKitColor);
        }

        return options;
    }

    private static bool PrefersChinese(CCSPlayerController player)
    {
        return player.GetLanguage().Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
    }

    // Search matches the label the player sees and the English one, so a
    // Chinese player can type either.
    private static bool MatchesEither(string label, string english, string[] terms)
    {
        return MatchesAllTerms(label, terms) || MatchesAllTerms(english, terms);
    }

    private static bool MatchesAllTerms(string label, string[] terms)
    {
        foreach (var term in terms)
        {
            if (label.IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }
        }

        return true;
    }

    private void Render(CCSPlayerController player, PlayerMenuState state)
    {
        if (!player.IsValid || !state.IsOpen)
        {
            return;
        }

        var options = GetOptions(state);
        state.Cursor = Math.Clamp(state.Cursor, 0, Math.Max(0, options.Count - 1));
        var visibleItems = Math.Clamp(_config.Menu.ItemsPerPage, 3, 6);
        var start = Math.Max(0, state.Cursor - visibleItems / 2);
        if (start + visibleItems > options.Count)
        {
            start = Math.Max(0, options.Count - visibleItems);
        }

        var end = Math.Min(options.Count, start + visibleItems);

        var title = GetTitle(player, state);
        var encodedTitle = WebUtility.HtmlEncode(TrimForOverlay(title, MaxTitleLength));
        var lines = new List<string>
        {
            state.View == MenuView.Main
                ? $"<font class='fontSize-m' color='#eb4b4b'><b>{encodedTitle}</b></font>"
                : $"<font class='fontSize-m' color='#8bdcff'><b>{encodedTitle}</b></font> <font color='#8a8f98'>{state.Cursor + 1}/{Math.Max(1, options.Count)}</font>",
        };

        if (options.Count == 0)
        {
            lines.Add($"<font color='#ffb3b3'>{WebUtility.HtmlEncode(_localizer.ForPlayer(player, "menu.no_entries"))}</font>");
        }
        else
        {
            for (var index = start; index < end; index++)
            {
                var option = options[index];
                var isCursor = index == state.Cursor;
                var label = WebUtility.HtmlEncode(TrimForOverlay(option.Label, MaxItemLabelLength));
                var labelColor = option.LabelColor ?? (isCursor ? "#f7d774" : "#e8e8e8");
                var prefix = isCursor ? "<font color='#f0b65a'>► </font>" : "<font color='#f0b65a'>   </font>";
                var body = isCursor
                    ? $"<font color='{labelColor}'><b>{label}</b></font>"
                    : $"<font color='{labelColor}'>{label}</font>";
                var selected = option.IsSelected ? " <font color='#7dff8a'>✔</font>" : string.Empty;
                lines.Add($"{prefix}{body}{selected}");
            }
        }

        lines.Add(state.View == MenuView.Main
            ? "<small><small><font color='#8a8f98'>W/S · E · R</font></small></small>"
            : "<small><small><font color='#8a8f98'>W/S · E · Shift · R</font></small></small>");
        SafePrint(player, string.Join("<br>", lines));
    }

    private string GetTitle(CCSPlayerController player, PlayerMenuState state)
    {
        return state.View switch
        {
            MenuView.Main => "Astra Skins",
            MenuView.Categories => "Astra Skins",
            MenuView.Weapons => _localizer.ForPlayer(player, "menu.title.weapons"),
            MenuView.WeaponSkins => state.Weapon?.Localized(state.PreferZh) ?? _localizer.ForPlayer(player, "menu.title.weapon_skins"),
            MenuView.KnifeTypes => _localizer.ForPlayer(player, "menu.title.knives"),
            MenuView.KnifeSkins => state.Knife?.Localized(state.PreferZh) ?? _localizer.ForPlayer(player, "menu.title.knife_skins"),
            MenuView.GloveTypes => _localizer.ForPlayer(player, "menu.title.gloves"),
            MenuView.GloveSkins => state.Glove?.Localized(state.PreferZh) ?? _localizer.ForPlayer(player, "menu.title.glove_skins"),
            MenuView.AgentTeams => _localizer.ForPlayer(player, "menu.title.agent_teams"),
            MenuView.MusicKits => _localizer.ForPlayer(player, "menu.music"),
            MenuView.Search => _localizer.ForPlayer(player, "menu.title.search", state.SearchQuery ?? string.Empty),
            MenuView.Agents => state.AgentTeam == "ct"
                ? _localizer.ForPlayer(player, "menu.title.agents_ct")
                : _localizer.ForPlayer(player, "menu.title.agents_t"),
            MenuView.AttachmentWeapons => _localizer.ForPlayer(player, "menu.title.weapons"),
            MenuView.StickerSlots => state.PendingStickerName is not null
                ? _localizer.ForPlayer(player, "menu.title.sticker_pick_slot", state.PendingStickerName)
                : _localizer.ForPlayer(player, "menu.title.stickers", state.Weapon?.Localized(state.PreferZh) ?? string.Empty),
            MenuView.StickerGroups => _localizer.ForPlayer(player, "menu.title.sticker_groups"),
            MenuView.StickerCapsules => GroupLabel(player, state.PreferZh, state.StickerGroup?.Name, state.StickerGroup?.NameZh),
            MenuView.Stickers => state.StickerCapsule is not null
                ? (state.PreferZh && !string.IsNullOrWhiteSpace(state.StickerCapsule.NameZh) ? state.StickerCapsule.NameZh! : state.StickerCapsule.Name)
                : GroupLabel(player, state.PreferZh, state.StickerGroup?.Name, state.StickerGroup?.NameZh),
            MenuView.StickerSearch => _localizer.ForPlayer(player, "menu.title.search", state.SearchQuery ?? string.Empty),
            MenuView.KeychainGroups => _localizer.ForPlayer(player, "menu.title.charms", state.Weapon?.Localized(state.PreferZh) ?? string.Empty),
            MenuView.Keychains => GroupLabel(player, state.PreferZh, state.KeychainGroup?.Name, state.KeychainGroup?.NameZh),
            MenuView.KeychainSearch => _localizer.ForPlayer(player, "menu.title.search", state.SearchQuery ?? string.Empty),
            _ => "Astra Skins"
        };
    }

    private void SafePrint(CCSPlayerController player, string message)
    {
        try
        {
            player.PrintToCenterHtml(message);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to render menu for slot {Slot}.", player.Slot);
        }
    }

    private void Freeze(CCSPlayerController player)
    {
        var pawn = player.PlayerPawn?.Value;
        if (pawn == null)
        {
            return;
        }

        if (!_savedVelocity.ContainsKey(player.Slot))
        {
            _savedVelocity[player.Slot] = pawn.VelocityModifier;
        }

        if (pawn.VelocityModifier != 0f)
        {
            pawn.VelocityModifier = 0f;
            MarkVelocityModifierChanged(pawn);
        }
    }

    private void Unfreeze(CCSPlayerController player)
    {
        var pawn = player.PlayerPawn?.Value;
        if (!_savedVelocity.TryGetValue(player.Slot, out var velocity) || pawn == null)
        {
            _savedVelocity.Remove(player.Slot);
            return;
        }

        // Only hand the value back if it is still the one we forced; if another
        // plugin changed it while the menu was open, theirs wins.
        if (pawn.VelocityModifier == 0f)
        {
            pawn.VelocityModifier = velocity;
            MarkVelocityModifierChanged(pawn);
        }

        _savedVelocity.Remove(player.Slot);
    }

    // Without marking the field dirty the client keeps animating with the old
    // modifier (frozen legs after closing the menu) until something else
    // forces a resync.
    private void MarkVelocityModifierChanged(CCSPlayerPawn pawn)
    {
        try
        {
            Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_flVelocityModifier");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to mark m_flVelocityModifier as changed.");
        }
    }

    // Official rarity tint per grade, so skins read like the real inventory.
    private static string? RarityColor(string? rarity)
    {
        if (string.IsNullOrWhiteSpace(rarity))
        {
            return null;
        }

        var value = rarity.ToLowerInvariant();
        if (value.Contains("contraband") || value.Contains("immortal")) return "#e4ae39";
        if (value.Contains("ancient")) return "#eb4b4b";
        if (value.Contains("legendary")) return "#d32ce6";
        if (value.Contains("mythical")) return "#8847ff";
        if (value.Contains("uncommon")) return "#5e98d9";
        if (value.Contains("rare")) return "#4b69ff";
        if (value.Contains("common")) return "#b0c3d9";
        return null;
    }

    private static string TrimForOverlay(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length <= maxLength)
        {
            return text;
        }

        return $"{text[..Math.Max(0, maxLength - 3)]}...";
    }
}

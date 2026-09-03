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
        ResetInputState(player, state);
        ChangeView(player, state, MenuView.Main);
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
               state.View == MenuView.Search &&
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
            state = new PlayerMenuState { Slot = player.Slot };
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

    private void ChangeView(CCSPlayerController player, PlayerMenuState state, MenuView view, bool push = false)
    {
        if (push)
        {
            state.BackStack.Push(new MenuSnapshot(state.View, state.Cursor, state.CategoryId, state.AgentTeam, state.Weapon, state.Knife, state.Glove));
        }

        state.View = view;
        state.Cursor = 0;
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
            var selectionKey = $"{state.View}:{option.Label}";
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
    // selection changes; the main view also refreshes on a short TTL because
    // it lists the weapons the player currently owns.
    private IReadOnlyList<MenuOption> GetOptions(PlayerMenuState state)
    {
        var now = DateTime.UtcNow;
        if (state.CachedOptions is not null &&
            (state.View != MenuView.Main || (now - state.CachedOptionsAtUtc).TotalSeconds < 1))
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

            var label = $"{visualIndex++}. {weapon.DisplayName}";
            options.Add(new MenuOption(label, () =>
            {
                var current = Utilities.GetPlayerFromSlot(state.Slot);
                if (current is null) return;
                state.Weapon = weapon;
                ChangeView(current, state, MenuView.WeaponSkins, push: true);
            }, LabelColor: RarityColor(equippedRarity)));
        }

        var knife = _skinManager.GetCurrentKnifeDefinition(player);
        var knifeLabel = knife is null ? _localizer.ForPlayer(player, "menu.knife") : $"* {knife.DisplayName}";
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

        options.Add(new MenuOption($"{visualIndex++}. {_localizer.ForPlayer(player, "menu.gloves")}", () =>
        {
            var current = Utilities.GetPlayerFromSlot(state.Slot);
            if (current is not null) ChangeView(current, state, MenuView.GloveTypes, push: true);
        }, LabelColor: "#8bdcff"));

        options.Add(new MenuOption($"{visualIndex++}. {_localizer.ForPlayer(player, "menu.agents")}", () =>
        {
            var current = Utilities.GetPlayerFromSlot(state.Slot);
            if (current is not null) ChangeView(current, state, MenuView.AgentTeams, push: true);
        }, LabelColor: "#b58fff"));

        options.Add(new MenuOption($"{visualIndex++}. {_localizer.ForPlayer(player, "menu.music")}", () =>
        {
            var current = Utilities.GetPlayerFromSlot(state.Slot);
            if (current is not null) ChangeView(current, state, MenuView.MusicKits, push: true);
        }, LabelColor: MusicKitColor));

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

        foreach (var category in categories)
        {
            if (!_skinManager.Catalog.Weapons.Any(w => w.Category.Equals(category.Id, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            options.Add(new MenuOption(category.DisplayName, () =>
            {
                var player = Utilities.GetPlayerFromSlot(state.Slot);
                if (player is null) return;
                state.CategoryId = category.Id;
                ChangeView(player, state, MenuView.Weapons, push: true);
            }));
        }

        options.Add(new MenuOption(_localizer.ForPlayer(menuPlayer, "menu.knives"), () =>
        {
            var player = Utilities.GetPlayerFromSlot(state.Slot);
            if (player is not null) OpenKnives(player);
        }));
        options.Add(new MenuOption(_localizer.ForPlayer(menuPlayer, "menu.gloves"), () =>
        {
            var player = Utilities.GetPlayerFromSlot(state.Slot);
            if (player is not null) ChangeView(player, state, MenuView.GloveTypes, push: true);
        }));
        options.Add(new MenuOption(_localizer.ForPlayer(menuPlayer, "menu.agents"), () =>
        {
            var player = Utilities.GetPlayerFromSlot(state.Slot);
            if (player is not null) ChangeView(player, state, MenuView.AgentTeams, push: true);
        }));
        options.Add(new MenuOption(_localizer.ForPlayer(menuPlayer, "menu.music"), () =>
        {
            var player = Utilities.GetPlayerFromSlot(state.Slot);
            if (player is not null) ChangeView(player, state, MenuView.MusicKits, push: true);
        }));
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
        var preferZh = player.GetLanguage().Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
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
            .Select(w => new MenuOption(w.DisplayName, () =>
            {
                var player = Utilities.GetPlayerFromSlot(state.Slot);
                if (player is null) return;
                state.Weapon = w;
                ChangeView(player, state, MenuView.WeaponSkins, push: true);
            }))
            .ToList();
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

        return state.Weapon.Skins
            .Where(s => player is null || _skinManager.CanUse(player, s))
            .Select(s => new MenuOption(s.DisplayName, () =>
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
                    ? $"{AstraSkinsPlugin.FormatPrefix()} {_localizer.ForPlayer(current, "menu.equipped", s.DisplayName)}"
                    : $"{AstraSkinsPlugin.FormatPrefix()} {_localizer.ForPlayer(current, "menu.save_failed")}");
                state.LastInteractionUtc = DateTime.UtcNow;
                Render(current, state);
            }, s.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase), ThrottleSelection: true, LabelColor: RarityColor(s.Rarity)))
            .ToList();
    }

    private IReadOnlyList<MenuOption> BuildKnifeOptions(PlayerMenuState state)
    {
        var player = Utilities.GetPlayerFromSlot(state.Slot);
        var selectedKnifeId = player is not null
            ? _skinManager.GetProfile(player).KnifeId ?? _skinManager.GetCurrentKnifeDefinition(player)?.Id
            : null;
        return _skinManager.Catalog.Knives
            .Where(k => player is null || _skinManager.CanUse(player, k))
            .Select(k => new MenuOption(k.DisplayName, () =>
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
                    ? $"{AstraSkinsPlugin.FormatPrefix()} {_localizer.ForPlayer(current, "menu.equipped", k.DisplayName)}"
                    : $"{AstraSkinsPlugin.FormatPrefix()} {_localizer.ForPlayer(current, "menu.save_failed")}");
                state.LastInteractionUtc = DateTime.UtcNow;
                Render(current, state);
            }, k.Id.Equals(selectedKnifeId, StringComparison.OrdinalIgnoreCase), ThrottleSelection: true))
            .ToList();
    }

    private IReadOnlyList<MenuOption> BuildKnifeSkinOptions(PlayerMenuState state)
    {
        if (state.Knife is null)
        {
            return Array.Empty<MenuOption>();
        }

        var player = Utilities.GetPlayerFromSlot(state.Slot);
        var selectedId = player is not null ? _skinManager.GetProfile(player).KnifeSkinId : null;
        return state.Knife.Skins
            .Where(s => player is null || _skinManager.CanUse(player, s))
            .Select(s => new MenuOption(s.DisplayName, () =>
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
                    ? $"{AstraSkinsPlugin.FormatPrefix()} {_localizer.ForPlayer(current, "menu.equipped", s.DisplayName)}"
                    : $"{AstraSkinsPlugin.FormatPrefix()} {_localizer.ForPlayer(current, "menu.save_failed")}");
                state.LastInteractionUtc = DateTime.UtcNow;
                Render(current, state);
            }, s.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase), ThrottleSelection: true, LabelColor: RarityColor(s.Rarity)))
            .ToList();
    }

    private IReadOnlyList<MenuOption> BuildGloveOptions(PlayerMenuState state)
    {
        var player = Utilities.GetPlayerFromSlot(state.Slot);
        return _skinManager.Catalog.Gloves
            .Where(g => player is null || _skinManager.CanUse(player, g))
            .Select(g => new MenuOption(g.DisplayName, () =>
            {
                var current = Utilities.GetPlayerFromSlot(state.Slot);
                if (current is null) return;
                state.Glove = g;
                ChangeView(current, state, MenuView.GloveSkins, push: true);
            }))
            .ToList();
    }

    private IReadOnlyList<MenuOption> BuildGloveSkinOptions(PlayerMenuState state)
    {
        if (state.Glove is null)
        {
            return Array.Empty<MenuOption>();
        }

        var player = Utilities.GetPlayerFromSlot(state.Slot);
        var selectedId = player is not null ? _skinManager.GetProfile(player).GloveSkinId : null;
        return state.Glove.Skins
            .Where(s => player is null || _skinManager.CanUse(player, s))
            .Select(s => new MenuOption(s.DisplayName, () =>
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
                    ? $"{AstraSkinsPlugin.FormatPrefix()} {_localizer.ForPlayer(current, "menu.equipped", s.DisplayName)}"
                    : $"{AstraSkinsPlugin.FormatPrefix()} {_localizer.ForPlayer(current, "menu.save_failed")}");
                state.LastInteractionUtc = DateTime.UtcNow;
                Render(current, state);
            }, s.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase), ThrottleSelection: true, LabelColor: RarityColor(s.Rarity)))
            .ToList();
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

        return _skinManager.Catalog.Agents
            .Where(a => a.Team.Equals(state.AgentTeam, StringComparison.OrdinalIgnoreCase))
            .Where(a => player is null || _skinManager.CanUse(player, a))
            .Select(a => new MenuOption(a.DisplayName, () =>
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
                    ? $"{AstraSkinsPlugin.FormatPrefix()} {_localizer.ForPlayer(current, "menu.equipped", a.DisplayName)}"
                    : $"{AstraSkinsPlugin.FormatPrefix()} {_localizer.ForPlayer(current, "menu.save_failed")}");
                state.LastInteractionUtc = DateTime.UtcNow;
                Render(current, state);
            }, a.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase), ThrottleSelection: true, LabelColor: RarityColor(a.Rarity)))
            .ToList();
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

        var terms = state.SearchQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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

        foreach (var weapon in catalog.Weapons)
        {
            foreach (var skin in weapon.Skins)
            {
                if (options.Count >= MaxSearchResults)
                {
                    return options;
                }

                var label = $"{weapon.DisplayName} | {skin.DisplayName}";
                if (!MatchesAllTerms(label, terms) || !_skinManager.CanUse(player, skin))
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

        foreach (var knife in catalog.Knives)
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

                var label = $"{knife.DisplayName} | {skin.DisplayName}";
                if (!MatchesAllTerms(label, terms) || !_skinManager.CanUse(player, skin))
                {
                    continue;
                }

                var skinId = skin.Id;
                var selected = skinId.Equals(profile.KnifeSkinId, StringComparison.OrdinalIgnoreCase);
                Add(label, selected, current => _skinManager.SetKnifeSkin(current, skinId), skin.Rarity);
            }
        }

        foreach (var glove in catalog.Gloves)
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

                var label = $"{glove.DisplayName} | {skin.DisplayName}";
                if (!MatchesAllTerms(label, terms) || !_skinManager.CanUse(player, skin))
                {
                    continue;
                }

                var skinId = skin.Id;
                var selected = skinId.Equals(profile.GloveSkinId, StringComparison.OrdinalIgnoreCase);
                Add(label, selected, current => _skinManager.SetGloveSkin(current, skinId), skin.Rarity);
            }
        }

        foreach (var agent in catalog.Agents)
        {
            if (options.Count >= MaxSearchResults)
            {
                return options;
            }

            var label = $"{agent.Team.ToUpperInvariant()} | {agent.DisplayName}";
            if (!MatchesAllTerms(label, terms) || !_skinManager.CanUse(player, agent))
            {
                continue;
            }

            var agentId = agent.Id;
            var team = agent.Team;
            var selected = profile.AgentIdsByTeam.TryGetValue(team, out var equippedAgent) &&
                           equippedAgent.Equals(agentId, StringComparison.OrdinalIgnoreCase);
            Add(label, selected, current => _skinManager.SetAgent(current, team, agentId), agent.Rarity);
        }

        var preferZh = player.GetLanguage().Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        var musicLabel = _localizer.ForPlayer(player, "menu.music");
        foreach (var kit in catalog.MusicKits)
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
            MenuView.WeaponSkins => state.Weapon?.DisplayName ?? _localizer.ForPlayer(player, "menu.title.weapon_skins"),
            MenuView.KnifeTypes => _localizer.ForPlayer(player, "menu.title.knives"),
            MenuView.KnifeSkins => state.Knife?.DisplayName ?? _localizer.ForPlayer(player, "menu.title.knife_skins"),
            MenuView.GloveTypes => _localizer.ForPlayer(player, "menu.title.gloves"),
            MenuView.GloveSkins => state.Glove?.DisplayName ?? _localizer.ForPlayer(player, "menu.title.glove_skins"),
            MenuView.AgentTeams => _localizer.ForPlayer(player, "menu.title.agent_teams"),
            MenuView.MusicKits => _localizer.ForPlayer(player, "menu.music"),
            MenuView.Search => _localizer.ForPlayer(player, "menu.title.search", state.SearchQuery ?? string.Empty),
            MenuView.Agents => state.AgentTeam == "ct"
                ? _localizer.ForPlayer(player, "menu.title.agents_ct")
                : _localizer.ForPlayer(player, "menu.title.agents_t"),
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

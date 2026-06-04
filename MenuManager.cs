using System.Net;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using AstraSkins.Models;

namespace AstraSkins;

public sealed class MenuManager
{
    private readonly SkinManager _skinManager;
    private readonly PluginConfig _config;
    private readonly ILogger _logger;
    private readonly Dictionary<int, PlayerMenuState> _states = new();
    private readonly Dictionary<int, float> _savedVelocity = new();

    private const int InitialInputDelayMilliseconds = 200;
    private const string UpKey = "W";
    private const string DownKey = "S";
    private const string SelectKey = "E";
    private const string BackKey = "Shift";
    private const string CloseKey = "R";
    private const int MaxTitleLength = 46;
    private const int MaxItemLabelLength = 34;

    private static readonly Dictionary<string, PlayerButtons> ButtonMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [UpKey] = PlayerButtons.Forward,
        [DownKey] = PlayerButtons.Back,
        [SelectKey] = PlayerButtons.Use,
        [BackKey] = PlayerButtons.Speed,
        [CloseKey] = PlayerButtons.Reload
    };

    public MenuManager(SkinManager skinManager, PluginConfig config, ILogger logger)
    {
        _skinManager = skinManager;
        _config = config;
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
            HandleButtonInput(player, state, now);
            if (!_states.ContainsKey(player.Slot) || !state.IsOpen)
            {
                continue;
            }

            Render(player, state);
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
        state.PreviousButtons = player.Buttons;
    }

    private void ChangeView(CCSPlayerController player, PlayerMenuState state, MenuView view, bool push = false)
    {
        if (push)
        {
            state.BackStack.Push(new MenuSnapshot(state.View, state.Cursor, state.Page, state.CategoryId, state.AgentTeam, state.Weapon, state.Knife, state.Glove));
        }

        state.View = view;
        state.Cursor = 0;
        state.Page = 0;
        state.LastSelectionKey = null;
        state.LastInteractionUtc = DateTime.UtcNow;
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
            state.Page = snapshot.Page;
            state.CategoryId = snapshot.CategoryId;
            state.AgentTeam = snapshot.AgentTeam;
            state.Weapon = snapshot.Weapon;
            state.Knife = snapshot.Knife;
            state.Glove = snapshot.Glove;
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
            var selectionKey = $"{state.View}:{option.Label}";
            var now = DateTime.UtcNow;
            if ((now - state.LastSelectionUtc).TotalMilliseconds < _config.Menu.SelectionCooldownMilliseconds)
            {
                return;
            }

            state.LastSelectionKey = selectionKey;
            state.LastSelectionUtc = now;
        }

        option.Action();
    }

    private IReadOnlyList<MenuOption> GetOptions(PlayerMenuState state)
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
        options.Add(new MenuOption($"{visualIndex++}. Configure all weapons", () =>
        {
            var current = Utilities.GetPlayerFromSlot(state.Slot);
            if (current is null) return;
            ChangeView(current, state, MenuView.Categories, push: true);
        }));

        foreach (var weapon in _skinManager.GetOwnedWeaponDefinitions(player))
        {
            var label = $"{visualIndex++}. {weapon.DisplayName}";
            options.Add(new MenuOption(label, () =>
            {
                var current = Utilities.GetPlayerFromSlot(state.Slot);
                if (current is null) return;
                state.Weapon = weapon;
                ChangeView(current, state, MenuView.WeaponSkins, push: true);
            }));
        }

        var knife = _skinManager.GetCurrentKnifeDefinition(player);
        var knifeLabel = knife is null ? "Knife" : $"* {knife.DisplayName}";
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
        }));

        options.Add(new MenuOption($"{visualIndex++}. Gloves", () =>
        {
            var current = Utilities.GetPlayerFromSlot(state.Slot);
            if (current is not null) ChangeView(current, state, MenuView.GloveTypes, push: true);
        }));

        options.Add(new MenuOption($"{visualIndex++}. Agents", () =>
        {
            var current = Utilities.GetPlayerFromSlot(state.Slot);
            if (current is not null) ChangeView(current, state, MenuView.AgentTeams, push: true);
        }));

        return options;
    }

    private void HandleButtonInput(CCSPlayerController player, PlayerMenuState state, DateTime now)
    {
        var current = player.Buttons;
        if ((now - state.OpenedAtUtc).TotalMilliseconds < InitialInputDelayMilliseconds ||
            (now - state.LastInputUtc).TotalMilliseconds < _config.Menu.CooldownMilliseconds)
        {
            state.PreviousButtons = current;
            return;
        }

        if (JustPressed(state.PreviousButtons, current, UpKey))
        {
            MoveCursor(state, -1);
            state.LastInputUtc = now;
            state.LastInteractionUtc = now;
        }
        else if (JustPressed(state.PreviousButtons, current, DownKey))
        {
            MoveCursor(state, 1);
            state.LastInputUtc = now;
            state.LastInteractionUtc = now;
        }
        else if (JustPressed(state.PreviousButtons, current, SelectKey))
        {
            Select(player, state);
            state.LastInputUtc = now;
            state.LastInteractionUtc = now;
        }
        else if (JustPressed(state.PreviousButtons, current, BackKey))
        {
            GoBack(player, state);
            state.LastInputUtc = now;
            state.LastInteractionUtc = now;
        }
        else if (JustPressed(state.PreviousButtons, current, CloseKey))
        {
            Close(player);
            state.LastInputUtc = now;
            state.LastInteractionUtc = now;
        }

        state.PreviousButtons = current;
    }

    private IReadOnlyList<MenuOption> BuildCategoryOptions(PlayerMenuState state)
    {
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

        options.Add(new MenuOption("Knives", () =>
        {
            var player = Utilities.GetPlayerFromSlot(state.Slot);
            if (player is not null) OpenKnives(player);
        }));
        options.Add(new MenuOption("Gloves", () =>
        {
            var player = Utilities.GetPlayerFromSlot(state.Slot);
            if (player is not null) ChangeView(player, state, MenuView.GloveTypes, push: true);
        }));
        options.Add(new MenuOption("Agents", () =>
        {
            var player = Utilities.GetPlayerFromSlot(state.Slot);
            if (player is not null) ChangeView(player, state, MenuView.AgentTeams, push: true);
        }));
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
                    ? $"{AstraSkinsPlugin.FormatPrefix()} Equipped {s.DisplayName}."
                    : $"{AstraSkinsPlugin.FormatPrefix()} Could not save selection.");
                state.LastInteractionUtc = DateTime.UtcNow;
                Render(current, state);
            }, s.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase), ThrottleSelection: true))
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
                    ? $"{AstraSkinsPlugin.FormatPrefix()} Equipped {k.DisplayName}."
                    : $"{AstraSkinsPlugin.FormatPrefix()} Could not save selection.");
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
                    ? $"{AstraSkinsPlugin.FormatPrefix()} Equipped {s.DisplayName}."
                    : $"{AstraSkinsPlugin.FormatPrefix()} Could not save selection.");
                state.LastInteractionUtc = DateTime.UtcNow;
                Render(current, state);
            }, s.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase), ThrottleSelection: true))
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
                    ? $"{AstraSkinsPlugin.FormatPrefix()} Equipped {s.DisplayName}."
                    : $"{AstraSkinsPlugin.FormatPrefix()} Could not save selection.");
                state.LastInteractionUtc = DateTime.UtcNow;
                Render(current, state);
            }, s.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase), ThrottleSelection: true))
            .ToList();
    }

    private IReadOnlyList<MenuOption> BuildAgentTeamOptions(PlayerMenuState state)
    {
        var options = new List<MenuOption>();
        if (_skinManager.Catalog.Agents.Any(a => a.Team.Equals("t", StringComparison.OrdinalIgnoreCase)))
        {
            options.Add(new MenuOption("T Agents", () =>
            {
                var player = Utilities.GetPlayerFromSlot(state.Slot);
                if (player is null) return;
                state.AgentTeam = "t";
                ChangeView(player, state, MenuView.Agents, push: true);
            }));
        }

        if (_skinManager.Catalog.Agents.Any(a => a.Team.Equals("ct", StringComparison.OrdinalIgnoreCase)))
        {
            options.Add(new MenuOption("CT Agents", () =>
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
                    ? $"{AstraSkinsPlugin.FormatPrefix()} Equipped {a.DisplayName}."
                    : $"{AstraSkinsPlugin.FormatPrefix()} Could not save selection.");
                state.LastInteractionUtc = DateTime.UtcNow;
                Render(current, state);
            }, a.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase), ThrottleSelection: true))
            .ToList();
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

        var title = GetTitle(state);
        var lines = new List<string>
        {
            state.View == MenuView.Main
                ? $"<b><font color='#f0b65a'>{WebUtility.HtmlEncode(TrimForOverlay(title, MaxTitleLength))}</font></b>"
                : $"<b><font color='#8bdcff'>{WebUtility.HtmlEncode(TrimForOverlay(title, MaxTitleLength))}</font></b> <font color='#d7f08a'>{state.Cursor + 1}</font>/<font color='#e2e2e2'>{Math.Max(1, options.Count)}</font>",
        };

        if (options.Count == 0)
        {
            lines.Add("<font color='#ffb3b3'>No entries available</font>");
        }
        else
        {
            for (var index = start; index < end; index++)
            {
                var option = options[index];
                var prefix = index == state.Cursor ? "> " : string.Empty;
                var selected = option.IsSelected ? " *" : string.Empty;
                var color = index == state.Cursor ? "#f7d774" : "#ffffff";
                lines.Add($"<font color='{color}'>{prefix}{WebUtility.HtmlEncode(TrimForOverlay(option.Label, MaxItemLabelLength))}{selected}</font>");
            }
        }

        lines.Add(state.View == MenuView.Main
            ? "<small><small><font color='#f0b65a'>W/S | E | R</font></small></small>"
            : "<small><small><font color='#f0b65a'>W/S | E | Shift | R</font></small></small>");
        SafePrint(player, string.Join("<br>", lines));
    }

    private static string GetTitle(PlayerMenuState state)
    {
        return state.View switch
        {
            MenuView.Main => "Astra Skins",
            MenuView.Categories => "Astra Skins",
            MenuView.Weapons => "Choose Weapon",
            MenuView.WeaponSkins => state.Weapon?.DisplayName ?? "Choose Skin",
            MenuView.KnifeTypes => "Choose Knife",
            MenuView.KnifeSkins => state.Knife?.DisplayName ?? "Choose Knife Skin",
            MenuView.GloveTypes => "Choose Gloves",
            MenuView.GloveSkins => state.Glove?.DisplayName ?? "Choose Glove Finish",
            MenuView.AgentTeams => "Choose Agents",
            MenuView.Agents => state.AgentTeam == "ct" ? "Choose CT Agent" : "Choose T Agent",
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
        if (player.PlayerPawn?.Value == null)
        {
            return;
        }

        if (!_savedVelocity.ContainsKey(player.Slot))
        {
            _savedVelocity[player.Slot] = player.PlayerPawn.Value.VelocityModifier;
        }

        player.PlayerPawn.Value.VelocityModifier = 0f;
    }

    private void Unfreeze(CCSPlayerController player)
    {
        if (!_savedVelocity.TryGetValue(player.Slot, out var velocity) || player.PlayerPawn?.Value == null)
        {
            _savedVelocity.Remove(player.Slot);
            return;
        }

        player.PlayerPawn.Value.VelocityModifier = velocity;
        _savedVelocity.Remove(player.Slot);
    }

    private static bool JustPressed(PlayerButtons oldButtons, PlayerButtons newButtons, string key)
    {
        if (!ButtonMap.TryGetValue(key, out var button))
        {
            return false;
        }

        return (newButtons & button) != 0 && (oldButtons & button) == 0;
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

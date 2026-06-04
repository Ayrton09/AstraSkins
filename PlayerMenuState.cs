using AstraSkins.Models;
using CounterStrikeSharp.API;

namespace AstraSkins;

public enum MenuView
{
    Closed,
    Main,
    Categories,
    Weapons,
    WeaponSkins,
    KnifeTypes,
    KnifeSkins,
    GloveTypes,
    GloveSkins,
    AgentTeams,
    Agents
}

public enum SelectionKind
{
    Weapon,
    Knife,
    Glove,
    Agent
}

public sealed class PlayerMenuState
{
    public int Slot { get; init; }
    public MenuView View { get; set; }
    public Stack<MenuSnapshot> BackStack { get; } = new();
    public int Cursor { get; set; }
    public int Page { get; set; }
    public DateTime LastInteractionUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastInputUtc { get; set; } = DateTime.MinValue;
    public DateTime LastSelectionUtc { get; set; } = DateTime.MinValue;
    public DateTime OpenedAtUtc { get; set; } = DateTime.UtcNow;
    public PlayerButtons PreviousButtons { get; set; }
    public string? LastSelectionKey { get; set; }
    public string? CategoryId { get; set; }
    public string? AgentTeam { get; set; }
    public WeaponDefinition? Weapon { get; set; }
    public KnifeDefinition? Knife { get; set; }
    public GloveDefinition? Glove { get; set; }
    public bool IsOpen => View != MenuView.Closed;
}

public sealed record MenuSnapshot(
    MenuView View,
    int Cursor,
    int Page,
    string? CategoryId,
    string? AgentTeam,
    WeaponDefinition? Weapon,
    KnifeDefinition? Knife,
    GloveDefinition? Glove);

public sealed record MenuOption(string Label, Action Action, bool IsSelected = false, bool ThrottleSelection = false);

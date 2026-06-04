namespace AstraSkins.Models;

public sealed class AgentDefinition
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Team { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public ushort? ItemDefinitionIndex { get; set; }
    public string? VoicePrefix { get; set; }
    public bool? HasFemaleVoice { get; set; }
    public bool Enabled { get; set; } = true;
    public string? Permission { get; set; }
    public string? Rarity { get; set; }
    public string? Group { get; set; }
}

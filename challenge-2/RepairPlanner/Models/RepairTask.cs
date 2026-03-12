using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace RepairPlanner.Models;

/// <summary>
/// A single repair step within a WorkOrder.
/// Ordered by sequence number.
/// </summary>
public sealed class RepairTask
{
    /// <summary>Execution order (1-based)</summary>
    [JsonPropertyName("sequence")]
    [JsonProperty("sequence")]
    public int Sequence { get; set; }

    [JsonPropertyName("title")]
    [JsonProperty("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Duration in minutes as an integer (e.g. 90).
    /// LLM responses are parsed with NumberHandling.AllowReadingFromString
    /// to handle cases where the model returns "90" as a string.
    /// </summary>
    [JsonPropertyName("estimatedDurationMinutes")]
    [JsonProperty("estimatedDurationMinutes")]
    public int EstimatedDurationMinutes { get; set; }

    /// <summary>Skill tags required to perform this task</summary>
    [JsonPropertyName("requiredSkills")]
    [JsonProperty("requiredSkills")]
    public List<string> RequiredSkills { get; set; } = [];

    [JsonPropertyName("safetyNotes")]
    [JsonProperty("safetyNotes")]
    public string SafetyNotes { get; set; } = string.Empty;

    /// <summary>Current status: "pending", "in_progress", "completed", "skipped"</summary>
    [JsonPropertyName("status")]
    [JsonProperty("status")]
    public string Status { get; set; } = "pending";
}

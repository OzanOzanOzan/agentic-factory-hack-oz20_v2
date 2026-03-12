using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace RepairPlanner.Models;

/// <summary>
/// The output of the Repair Planner Agent — saved to the Cosmos DB "WorkOrders" container.
/// Partition key: status
/// </summary>
public sealed class WorkOrder
{
    // Cosmos DB requires a string "id" field.
    [JsonPropertyName("id")]
    [JsonProperty("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("workOrderNumber")]
    [JsonProperty("workOrderNumber")]
    public string WorkOrderNumber { get; set; } = string.Empty;

    [JsonPropertyName("machineId")]
    [JsonProperty("machineId")]
    public string MachineId { get; set; } = string.Empty;

    [JsonPropertyName("machineName")]
    [JsonProperty("machineName")]
    public string MachineName { get; set; } = string.Empty;

    [JsonPropertyName("faultType")]
    [JsonProperty("faultType")]
    public string FaultType { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    [JsonProperty("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>"corrective" | "preventive" | "emergency"</summary>
    [JsonPropertyName("type")]
    [JsonProperty("type")]
    public string Type { get; set; } = "corrective";

    /// <summary>"critical" | "high" | "medium" | "low"</summary>
    [JsonPropertyName("priority")]
    [JsonProperty("priority")]
    public string Priority { get; set; } = "medium";

    /// <summary>"open" | "in_progress" | "completed" | "cancelled"</summary>
    [JsonPropertyName("status")]
    [JsonProperty("status")]
    public string Status { get; set; } = "open";

    /// <summary>Technician id assigned to this work order, or null if unassigned</summary>
    [JsonPropertyName("assignedTo")]
    [JsonProperty("assignedTo")]
    public string? AssignedTo { get; set; }

    [JsonPropertyName("assignedTechnicianName")]
    [JsonProperty("assignedTechnicianName")]
    public string? AssignedTechnicianName { get; set; }

    /// <summary>
    /// Total estimated duration in minutes as an integer (e.g. 120).
    /// LLM may return this as a string — NumberHandling.AllowReadingFromString handles that.
    /// </summary>
    [JsonPropertyName("estimatedDuration")]
    [JsonProperty("estimatedDuration")]
    public int EstimatedDuration { get; set; }

    [JsonPropertyName("createdAt")]
    [JsonProperty("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("scheduledFor")]
    [JsonProperty("scheduledFor")]
    public DateTime? ScheduledFor { get; set; }

    [JsonPropertyName("completedAt")]
    [JsonProperty("completedAt")]
    public DateTime? CompletedAt { get; set; }

    [JsonPropertyName("tasks")]
    [JsonProperty("tasks")]
    public List<RepairTask> Tasks { get; set; } = [];

    [JsonPropertyName("partsUsed")]
    [JsonProperty("partsUsed")]
    public List<WorkOrderPartUsage> PartsUsed { get; set; } = [];

    [JsonPropertyName("notes")]
    [JsonProperty("notes")]
    public string Notes { get; set; } = string.Empty;

    [JsonPropertyName("sourceFaultId")]
    [JsonProperty("sourceFaultId")]
    public string SourceFaultId { get; set; } = string.Empty;
}

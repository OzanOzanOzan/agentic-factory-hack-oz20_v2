using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace RepairPlanner.Models;

/// <summary>
/// Represents a diagnosed fault received from the Fault Diagnosis Agent (Challenge 1).
/// This is the input that triggers work order creation.
/// </summary>
public sealed class DiagnosedFault
{
    // Both [JsonPropertyName] (System.Text.Json) and [JsonProperty] (Newtonsoft.Json)
    // are needed because Cosmos DB SDK uses Newtonsoft, while the rest of the app uses STJ.

    [JsonPropertyName("id")]
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("machineId")]
    [JsonProperty("machineId")]
    public string MachineId { get; set; } = string.Empty;

    [JsonPropertyName("machineName")]
    [JsonProperty("machineName")]
    public string MachineName { get; set; } = string.Empty;

    [JsonPropertyName("faultType")]
    [JsonProperty("faultType")]
    public string FaultType { get; set; } = string.Empty;

    [JsonPropertyName("faultDescription")]
    [JsonProperty("faultDescription")]
    public string FaultDescription { get; set; } = string.Empty;

    /// <summary>Severity level: "critical", "high", "medium", or "low"</summary>
    [JsonPropertyName("severity")]
    [JsonProperty("severity")]
    public string Severity { get; set; } = string.Empty;

    [JsonPropertyName("detectedAt")]
    [JsonProperty("detectedAt")]
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("sensorReadings")]
    [JsonProperty("sensorReadings")]
    public Dictionary<string, double> SensorReadings { get; set; } = [];

    [JsonPropertyName("department")]
    [JsonProperty("department")]
    public string Department { get; set; } = string.Empty;
}

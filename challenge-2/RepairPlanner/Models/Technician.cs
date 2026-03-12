using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace RepairPlanner.Models;

/// <summary>
/// A maintenance technician stored in the Cosmos DB "Technicians" container.
/// Partition key: department
/// </summary>
public sealed class Technician
{
    [JsonPropertyName("id")]
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("employeeId")]
    [JsonProperty("employeeId")]
    public string EmployeeId { get; set; } = string.Empty;

    [JsonPropertyName("department")]
    [JsonProperty("department")]
    public string Department { get; set; } = string.Empty;

    /// <summary>Skill tags, e.g. ["tire_curing_press", "plc_troubleshooting"]</summary>
    [JsonPropertyName("skills")]
    [JsonProperty("skills")]
    public List<string> Skills { get; set; } = [];

    /// <summary>Certification codes the technician holds</summary>
    [JsonPropertyName("certifications")]
    [JsonProperty("certifications")]
    public List<string> Certifications { get; set; } = [];

    /// <summary>Current availability: "available", "on_shift", "off_shift", "on_leave"</summary>
    [JsonPropertyName("availability")]
    [JsonProperty("availability")]
    public string Availability { get; set; } = string.Empty;

    [JsonPropertyName("shiftStart")]
    [JsonProperty("shiftStart")]
    public string ShiftStart { get; set; } = string.Empty;

    [JsonPropertyName("shiftEnd")]
    [JsonProperty("shiftEnd")]
    public string ShiftEnd { get; set; } = string.Empty;

    [JsonPropertyName("contactEmail")]
    [JsonProperty("contactEmail")]
    public string ContactEmail { get; set; } = string.Empty;

    [JsonPropertyName("contactPhone")]
    [JsonProperty("contactPhone")]
    public string ContactPhone { get; set; } = string.Empty;
}

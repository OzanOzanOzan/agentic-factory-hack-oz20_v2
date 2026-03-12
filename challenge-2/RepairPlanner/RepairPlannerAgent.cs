using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using RepairPlanner.Models;
using RepairPlanner.Services;

namespace RepairPlanner;

/// <summary>
/// Orchestrates the Repair Planner workflow:
///   1. Look up required skills + parts for the diagnosed fault
///   2. Query Cosmos DB for matching technicians and available parts
///   3. Build a context-rich prompt and invoke the Foundry Agent
///   4. Parse the LLM response into a WorkOrder
///   5. Save the WorkOrder to Cosmos DB
/// </summary>
public sealed class RepairPlannerAgent(
    AIProjectClient projectClient,       // Connection to Azure AI Foundry project
    CosmosDbService cosmosDb,
    IFaultMappingService faultMapping,
    string modelDeploymentName,
    ILogger<RepairPlannerAgent> logger)
{
    private const string AgentName = "RepairPlannerAgent";

    // System instructions sent to the LLM every time the agent is invoked.
    private const string AgentInstructions = """
        You are a Repair Planner Agent for tire manufacturing equipment.
        Generate a repair plan with tasks, timeline, and resource allocation.
        Return the response as valid JSON matching the WorkOrder schema.

        Output JSON with these fields:
        - workOrderNumber, machineId, title, description
        - type: "corrective" | "preventive" | "emergency"
        - priority: "critical" | "high" | "medium" | "low"
        - status, assignedTo (technician id or null), notes
        - estimatedDuration: integer (minutes, e.g. 90 not "90 minutes")
        - partsUsed: [{ partId, partNumber, quantity }]
        - tasks: [{ sequence, title, description, estimatedDurationMinutes (integer), requiredSkills, safetyNotes }]

        IMPORTANT: All duration fields must be integers representing minutes (e.g. 90), not strings.

        Rules:
        - Assign the most qualified available technician
        - Include only relevant parts from the provided list; empty array if none needed
        - Tasks must be ordered sequentially and be actionable
        - Return ONLY the JSON object — no markdown fences, no explanation
        """;

    // JsonSerializerOptions shared across all parse calls.
    // AllowReadingFromString handles cases where the LLM returns 90 as "90".
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    // -------------------------------------------------------------------------
    // Agent registration
    // -------------------------------------------------------------------------

    /// <summary>
    /// Registers (or updates) the agent definition in Azure AI Foundry.
    /// Safe to call on every startup — CreateAgentVersionAsync is idempotent.
    /// </summary>
    public async Task EnsureAgentVersionAsync(CancellationToken ct = default)
    {
        logger.LogInformation("Registering Foundry Agent '{AgentName}' with model '{Model}'...",
            AgentName, modelDeploymentName);

        var definition = new PromptAgentDefinition(model: modelDeploymentName)
        {
            Instructions = AgentInstructions
        };

        await projectClient.Agents.CreateAgentVersionAsync(
            AgentName,
            new AgentVersionCreationOptions(definition),
            ct);

        logger.LogInformation("Agent '{AgentName}' registered successfully.", AgentName);
    }

    // -------------------------------------------------------------------------
    // Main workflow
    // -------------------------------------------------------------------------

    /// <summary>
    /// Full pipeline: fault → context gathering → LLM → WorkOrder saved to Cosmos DB.
    /// </summary>
    public async Task<WorkOrder> PlanAndCreateWorkOrderAsync(
        DiagnosedFault fault,
        CancellationToken ct = default)
    {
        logger.LogInformation(
            "Planning repair for fault '{FaultType}' on machine '{MachineId}'",
            fault.FaultType, fault.MachineId);

        // ------------------------------------------------------------------
        // Step 1: Determine required skills and part numbers from static map
        // ------------------------------------------------------------------
        var requiredSkills = faultMapping.GetRequiredSkills(fault.FaultType);
        var requiredPartNumbers = faultMapping.GetRequiredParts(fault.FaultType);

        logger.LogInformation("Required skills: {Skills}", string.Join(", ", requiredSkills));
        logger.LogInformation("Required parts:  {Parts}", string.Join(", ", requiredPartNumbers));

        // ------------------------------------------------------------------
        // Step 2: Query Cosmos DB for matching technicians and parts
        // ------------------------------------------------------------------
        var technicians = await cosmosDb.GetAvailableTechniciansBySkillsAsync(requiredSkills, ct);
        var parts = await cosmosDb.GetPartsByPartNumbersAsync(requiredPartNumbers, ct);

        logger.LogInformation(
            "Found {TechCount} matching technician(s) and {PartCount} available part(s)",
            technicians.Count, parts.Count);

        // ------------------------------------------------------------------
        // Step 3: Build a context-rich prompt
        // ------------------------------------------------------------------
        var prompt = BuildPrompt(fault, requiredSkills, technicians, parts);

        // ------------------------------------------------------------------
        // Step 4: Invoke the Foundry Agent
        // ------------------------------------------------------------------
        logger.LogInformation("Invoking Foundry Agent '{AgentName}'...", AgentName);

        var agent = projectClient.GetAIAgent(name: AgentName);
        var response = await agent.RunAsync(prompt, thread: null, options: null);
        var rawJson = response.Text ?? string.Empty;

        logger.LogInformation("Agent response received ({Length} chars)", rawJson.Length);
        logger.LogDebug("Raw agent response: {Raw}", rawJson);

        // ------------------------------------------------------------------
        // Step 5: Parse the JSON response into a WorkOrder
        // ------------------------------------------------------------------
        var workOrder = ParseWorkOrder(rawJson, fault);

        // Apply defaults for fields the LLM might leave blank
        workOrder.MachineId    = fault.MachineId;
        workOrder.MachineName  = fault.MachineName;
        workOrder.FaultType    = fault.FaultType;
        workOrder.SourceFaultId = fault.Id;
        workOrder.Status       ??= "open";
        workOrder.Priority     ??= MapSeverityToPriority(fault.Severity);
        workOrder.Type         ??= "corrective";
        workOrder.CreatedAt    = DateTime.UtcNow;

        // Enrich parts usage with names and costs from the Cosmos DB records
        EnrichPartsUsage(workOrder, parts);

        // ------------------------------------------------------------------
        // Step 6: Save to Cosmos DB
        // ------------------------------------------------------------------
        var saved = await cosmosDb.CreateWorkOrderAsync(workOrder, ct);

        logger.LogInformation(
            "Work order '{WorkOrderNumber}' created (id: {Id})",
            saved.WorkOrderNumber, saved.Id);

        return saved;
    }

    // -------------------------------------------------------------------------
    // Prompt builder
    // -------------------------------------------------------------------------

    private static string BuildPrompt(
        DiagnosedFault fault,
        IReadOnlyList<string> requiredSkills,
        List<Technician> technicians,
        List<Part> parts)
    {
        var techList = technicians.Count == 0
            ? "  (none available — leave assignedTo null)"
            : string.Join("\n", technicians.Select(t =>
                $"  - id={t.Id} name=\"{t.Name}\" skills=[{string.Join(", ", t.Skills)}] availability={t.Availability}"));

        var partList = parts.Count == 0
            ? "  (no matching parts in stock — use empty partsUsed array)"
            : string.Join("\n", parts.Select(p =>
                $"  - id={p.Id} partNumber={p.PartNumber} name=\"{p.Name}\" qty={p.QuantityInStock} cost={p.UnitCost:F2}"));

        return $"""
            ## Diagnosed Fault

            Machine ID:   {fault.MachineId}
            Machine Name: {fault.MachineName}
            Fault Type:   {fault.FaultType}
            Description:  {fault.FaultDescription}
            Severity:     {fault.Severity}
            Detected At:  {fault.DetectedAt:u}

            ## Required Skills
            {string.Join(", ", requiredSkills)}

            ## Available Technicians
            {techList}

            ## Available Parts
            {partList}

            ---
            Create a complete work order JSON for this repair. Follow the schema in your instructions exactly.
            """;
    }

    // -------------------------------------------------------------------------
    // Response parsing
    // -------------------------------------------------------------------------

    private WorkOrder ParseWorkOrder(string rawJson, DiagnosedFault fault)
    {
        // Strip markdown code fences if the LLM wraps the JSON in ```json … ```
        var json = rawJson.Trim();
        if (json.StartsWith("```"))
        {
            var firstNewline = json.IndexOf('\n');
            var lastFence    = json.LastIndexOf("```");
            if (firstNewline > 0 && lastFence > firstNewline)
                json = json[(firstNewline + 1)..lastFence].Trim();
        }

        try
        {
            var workOrder = JsonSerializer.Deserialize<WorkOrder>(json, JsonOptions);
            if (workOrder is null)
                throw new InvalidOperationException("Deserialized WorkOrder was null.");

            return workOrder;
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse agent response as WorkOrder JSON. Raw: {Raw}", rawJson);

            // Return a minimal safe fallback so the pipeline doesn't crash.
            // The raw LLM output is preserved in Notes for manual review.
            return new WorkOrder
            {
                WorkOrderNumber = $"WO-{DateTime.UtcNow:yyyyMMddHHmmss}",
                Title           = $"Repair: {fault.FaultType} on {fault.MachineId}",
                Description     = fault.FaultDescription,
                Notes           = $"[PARSE ERROR — manual review required]\n\n{rawJson}",
                Status          = "open",
                Priority        = MapSeverityToPriority(fault.Severity),
                Type            = "corrective",
            };
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fills in PartName and UnitCost on each WorkOrderPartUsage entry
    /// by matching against the records retrieved from Cosmos DB.
    /// </summary>
    private static void EnrichPartsUsage(WorkOrder workOrder, List<Part> cosmosDbParts)
    {
        var partIndex = cosmosDbParts.ToDictionary(p => p.PartNumber, StringComparer.OrdinalIgnoreCase);

        foreach (var usage in workOrder.PartsUsed)
        {
            if (partIndex.TryGetValue(usage.PartNumber, out var part))
            {
                // ?? means "if null, use this" — preserves any value the LLM already set
                usage.PartId   = string.IsNullOrEmpty(usage.PartId) ? part.Id : usage.PartId;
                usage.PartName = string.IsNullOrEmpty(usage.PartName) ? part.Name : usage.PartName;
                usage.UnitCost = usage.UnitCost == 0 ? part.UnitCost : usage.UnitCost;
            }
        }
    }

    /// <summary>Maps a fault severity string to a work order priority level.</summary>
    private static string MapSeverityToPriority(string severity) =>
        severity.ToLowerInvariant() switch
        {
            "critical" => "critical",
            "high"     => "high",
            "medium"   => "medium",
            _          => "low"
        };
}

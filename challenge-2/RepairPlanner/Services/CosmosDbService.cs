using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using RepairPlanner.Models;

namespace RepairPlanner.Services;

/// <summary>
/// Encapsulates all Cosmos DB access for the Repair Planner:
///   - Read Technicians (filter by skills + availability)
///   - Read PartsInventory (filter by part numbers)
///   - Write WorkOrders
/// </summary>
public sealed class CosmosDbService
{
    private readonly CosmosClient _client;
    private readonly CosmosDbOptions _options;
    private readonly ILogger<CosmosDbService> _logger;

    // Lazy-initialised container references; created once on first use.
    private Container? _techniciansContainer;
    private Container? _partsContainer;
    private Container? _workOrdersContainer;

    // Primary constructor — parameters become private fields automatically.
    public CosmosDbService(CosmosClient client, CosmosDbOptions options, ILogger<CosmosDbService> logger)
    {
        _client = client;
        _options = options;
        _logger = logger;
    }

    // -------------------------------------------------------------------------
    // Container accessors (initialised lazily via ??= "assign-if-null" operator)
    // -------------------------------------------------------------------------

    private Container Technicians =>
        _techniciansContainer ??= _client.GetContainer(_options.DatabaseName, _options.TechniciansContainer);

    private Container Parts =>
        _partsContainer ??= _client.GetContainer(_options.DatabaseName, _options.PartsContainer);

    private Container WorkOrders =>
        _workOrdersContainer ??= _client.GetContainer(_options.DatabaseName, _options.WorkOrdersContainer);

    // -------------------------------------------------------------------------
    // Technicians
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns all technicians whose skills list contains at least one of the
    /// <paramref name="requiredSkills"/> and who are currently available.
    /// Cosmos DB ARRAY_CONTAINS is used for the skill-matching predicate.
    /// </summary>
    public async Task<List<Technician>> GetAvailableTechniciansBySkillsAsync(
        IReadOnlyList<string> requiredSkills,
        CancellationToken ct = default)
    {
        if (requiredSkills.Count == 0)
        {
            _logger.LogWarning("GetAvailableTechniciansBySkillsAsync called with empty skill list; returning all available technicians.");
        }

        // Build an OR predicate: ARRAY_CONTAINS(t.skills, "skill1") OR ARRAY_CONTAINS(t.skills, "skill2") …
        // This is done in application code because Cosmos SQL does not support parameterised
        // arrays directly; each skill becomes a separate IN-check via ARRAY_CONTAINS.
        var skillPredicates = requiredSkills.Count > 0
            ? string.Join(" OR ", requiredSkills.Select((s, i) => $"ARRAY_CONTAINS(t.skills, @skill{i})"))
            : "1=1";

        var sql = $"""
            SELECT * FROM t
            WHERE t.availability = 'available'
            AND ({skillPredicates})
            """;

        var definition = new QueryDefinition(sql);
        for (int i = 0; i < requiredSkills.Count; i++)
            definition = definition.WithParameter($"@skill{i}", requiredSkills[i]);

        return await ExecuteQueryAsync<Technician>(Technicians, definition, "technicians", ct);
    }

    // -------------------------------------------------------------------------
    // Parts Inventory
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns parts whose partNumber matches any entry in <paramref name="partNumbers"/>.
    /// Only parts with quantity > 0 are returned.
    /// </summary>
    public async Task<List<Part>> GetPartsByPartNumbersAsync(
        IReadOnlyList<string> partNumbers,
        CancellationToken ct = default)
    {
        if (partNumbers.Count == 0)
        {
            _logger.LogInformation("No part numbers requested; skipping parts query.");
            return [];
        }

        // Build parameterised IN list: p.partNumber IN (@p0, @p1, …)
        var paramNames = partNumbers.Select((_, i) => $"@p{i}");
        var sql = $"""
            SELECT * FROM p
            WHERE p.partNumber IN ({string.Join(", ", paramNames)})
            AND p.quantityInStock > 0
            """;

        var definition = new QueryDefinition(sql);
        for (int i = 0; i < partNumbers.Count; i++)
            definition = definition.WithParameter($"@p{i}", partNumbers[i]);

        return await ExecuteQueryAsync<Part>(Parts, definition, "parts", ct);
    }

    // -------------------------------------------------------------------------
    // Work Orders
    // -------------------------------------------------------------------------

    /// <summary>
    /// Upserts a work order document into Cosmos DB.
    /// Uses UpsertItemAsync so re-runs are idempotent for the same WorkOrder.Id.
    /// The partition key is the work order's <c>status</c> field.
    /// </summary>
    public async Task<WorkOrder> CreateWorkOrderAsync(WorkOrder workOrder, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Saving work order {WorkOrderNumber} for machine {MachineId} (status: {Status})",
            workOrder.WorkOrderNumber, workOrder.MachineId, workOrder.Status);

        try
        {
            var response = await WorkOrders.UpsertItemAsync(
                workOrder,
                new PartitionKey(workOrder.Status),
                cancellationToken: ct);

            _logger.LogInformation(
                "Work order {WorkOrderNumber} saved. RU charge: {RU}",
                workOrder.WorkOrderNumber, response.RequestCharge);

            return response.Resource;
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex,
                "Cosmos DB error saving work order {WorkOrderNumber}: {StatusCode}",
                workOrder.WorkOrderNumber, ex.StatusCode);
            throw;
        }
    }

    // -------------------------------------------------------------------------
    // Shared query helper
    // -------------------------------------------------------------------------

    /// <summary>
    /// Iterates all pages of a Cosmos DB feed iterator and returns results as a flat list.
    /// Logs query execution details for observability.
    /// </summary>
    private async Task<List<T>> ExecuteQueryAsync<T>(
        Container container,
        QueryDefinition query,
        string entityLabel,
        CancellationToken ct)
    {
        var results = new List<T>();
        double totalRu = 0;

        using var iterator = container.GetItemQueryIterator<T>(query);

        try
        {
            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(ct);
                totalRu += page.RequestCharge;
                results.AddRange(page);
            }
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex,
                "Cosmos DB query failed for {EntityLabel}: {StatusCode}",
                entityLabel, ex.StatusCode);
            throw;
        }

        _logger.LogInformation(
            "Queried {Count} {EntityLabel}. Total RU charge: {RU}",
            results.Count, entityLabel, totalRu);

        return results;
    }
}

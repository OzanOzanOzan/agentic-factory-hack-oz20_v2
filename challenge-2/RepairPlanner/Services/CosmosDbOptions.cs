namespace RepairPlanner.Services;

/// <summary>
/// Holds Cosmos DB connection settings, populated from environment variables in Program.cs.
/// </summary>
public sealed class CosmosDbOptions
{
    public string Endpoint { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;

    // Container names match the seeded data from Challenge 0
    public string TechniciansContainer { get; set; } = "Technicians";
    public string PartsContainer { get; set; } = "PartsInventory";
    public string WorkOrdersContainer { get; set; } = "WorkOrders";
}

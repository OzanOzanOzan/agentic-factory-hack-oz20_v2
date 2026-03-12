using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RepairPlanner;
using RepairPlanner.Models;
using RepairPlanner.Services;

// ---------------------------------------------------------------------------
// Read required environment variables (set these before running)
// ---------------------------------------------------------------------------
static string RequiredEnv(string name) =>
    Environment.GetEnvironmentVariable(name)
    ?? throw new InvalidOperationException($"Missing required environment variable: {name}");

var projectEndpoint   = RequiredEnv("AZURE_AI_PROJECT_ENDPOINT");
var modelDeployment   = RequiredEnv("MODEL_DEPLOYMENT_NAME");
var cosmosEndpoint    = RequiredEnv("COSMOS_ENDPOINT");
var cosmosKey         = RequiredEnv("COSMOS_KEY");
var cosmosDatabaseName = RequiredEnv("COSMOS_DATABASE_NAME");

// ---------------------------------------------------------------------------
// Build the DI container
// ---------------------------------------------------------------------------
// await using is like Python's "async with" — disposes the provider when done
await using var provider = new ServiceCollection()
    .AddLogging(b => b
        .AddConsole()
        .SetMinimumLevel(LogLevel.Information))
    .BuildServiceProvider();

var loggerFactory = provider.GetRequiredService<ILoggerFactory>();

// ---------------------------------------------------------------------------
// Build services manually (keeps the entry point simple and readable)
// ---------------------------------------------------------------------------

// Cosmos DB client — uses account key auth for workshop simplicity
var cosmosClient = new CosmosClient(cosmosEndpoint, cosmosKey, new CosmosClientOptions
{
    SerializerOptions = new CosmosSerializationOptions
    {
        PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
    }
});

var cosmosOptions = new CosmosDbOptions
{
    Endpoint      = cosmosEndpoint,
    Key           = cosmosKey,
    DatabaseName  = cosmosDatabaseName,
};

var cosmosDbService = new CosmosDbService(
    cosmosClient,
    cosmosOptions,
    loggerFactory.CreateLogger<CosmosDbService>());

var faultMapping = new FaultMappingService();

// AIProjectClient uses DefaultAzureCredential (Managed Identity / env vars / CLI)
var projectClient = new Azure.AI.Projects.AIProjectClient(
    new Uri(projectEndpoint),
    new DefaultAzureCredential());

var agent = new RepairPlannerAgent(
    projectClient,
    cosmosDbService,
    faultMapping,
    modelDeployment,
    loggerFactory.CreateLogger<RepairPlannerAgent>());

// ---------------------------------------------------------------------------
// Register the Foundry Agent (idempotent — safe to call on every startup)
// ---------------------------------------------------------------------------
await agent.EnsureAgentVersionAsync();

// ---------------------------------------------------------------------------
// Sample DiagnosedFault — mirrors what the Fault Diagnosis Agent (Challenge 1)
// would produce. Uses real machine/technician IDs from the seeded Cosmos data.
// ---------------------------------------------------------------------------
var sampleFault = new DiagnosedFault
{
    Id               = $"fault-{Guid.NewGuid():N}",
    MachineId        = "machine-001",
    MachineName      = "Tire Curing Press A1",
    FaultType        = "curing_temperature_excessive",
    FaultDescription = "Curing press temperature exceeded safe threshold by 18°C. " +
                       "Heating element may be malfunctioning or temperature sensor drifting.",
    Severity         = "high",
    DetectedAt       = DateTime.UtcNow,
    Department       = "Curing",
    SensorReadings   = new Dictionary<string, double>
    {
        ["temperature_c"]   = 198.5,   // threshold is 180°C
        ["pressure_bar"]    = 12.1,
        ["cycle_time_sec"]  = 285.0,
    }
};

Console.WriteLine("==========================================================");
Console.WriteLine(" Repair Planner Agent — Challenge 2");
Console.WriteLine("==========================================================");
Console.WriteLine($" Machine  : {sampleFault.MachineName} ({sampleFault.MachineId})");
Console.WriteLine($" Fault    : {sampleFault.FaultType}");
Console.WriteLine($" Severity : {sampleFault.Severity}");
Console.WriteLine("----------------------------------------------------------");

// ---------------------------------------------------------------------------
// Run the full planning workflow
// ---------------------------------------------------------------------------
var workOrder = await agent.PlanAndCreateWorkOrderAsync(sampleFault);

// ---------------------------------------------------------------------------
// Print a summary of the generated work order
// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("==========================================================");
Console.WriteLine(" Work Order Created");
Console.WriteLine("==========================================================");
Console.WriteLine($" Number      : {workOrder.WorkOrderNumber}");
Console.WriteLine($" Title       : {workOrder.Title}");
Console.WriteLine($" Type        : {workOrder.Type}");
Console.WriteLine($" Priority    : {workOrder.Priority}");
Console.WriteLine($" Status      : {workOrder.Status}");
Console.WriteLine($" Assigned To : {workOrder.AssignedTechnicianName ?? workOrder.AssignedTo ?? "(unassigned)"}");
Console.WriteLine($" Est. Duration: {workOrder.EstimatedDuration} min");

if (workOrder.Tasks.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine(" Tasks:");
    foreach (var task in workOrder.Tasks.OrderBy(t => t.Sequence))
        Console.WriteLine($"   {task.Sequence}. [{task.EstimatedDurationMinutes} min] {task.Title}");
}

if (workOrder.PartsUsed.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine(" Parts:");
    foreach (var part in workOrder.PartsUsed)
        Console.WriteLine($"   - {part.PartNumber}  x{part.Quantity}  ({part.PartName})");
}

Console.WriteLine();
Console.WriteLine($" Cosmos DB id: {workOrder.Id}");
Console.WriteLine("==========================================================");


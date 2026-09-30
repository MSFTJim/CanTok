var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddSingleton<IOrderRepo, FakeOrderRepo>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/", (ILogger<Program> logger) =>
{
    logger.LogInformation("GetServerInfo endpoint called");
    var response = new
    {
        machineName = Environment.MachineName,
        timestamp = DateTime.UtcNow
    };
    logger.LogInformation("Returning server info: {MachineName}, {Timestamp}", response.machineName, response.timestamp);
    return Results.Ok(response);
});

// BAD EXAMPLE: Ignores client cancellation
app.MapGet("/orders/slow", async (string? q, IOrderRepo repo, ILogger<Program> logger) =>
{
    logger.LogInformation("HTTP GET /orders/slow received for query: '{Query}'", q);
    
    var results = await repo.SearchWithoutCancellationAsync(q ?? string.Empty);
    
    logger.LogInformation("[SLOW] Search finished processing for query: '{Query}'", q);
    return Results.Ok(results);
});

// GOOD EXAMPLE: Respects client cancellation
app.MapGet("/orders", async (string? q, IOrderRepo repo, ILogger<Program> logger, CancellationToken ct) =>
{
    logger.LogInformation("HTTP GET /orders received for query: '{Query}'", q);

    try
    {
        var results = await repo.SearchAsync(q ?? string.Empty, ct);
        logger.LogInformation("Search completed successfully for query: '{Query}'", q);
        return Results.Ok(results);
    }
    catch (OperationCanceledException)
    {
        logger.LogWarning("Request for query '{Query}' was canceled by the client.", q);
        throw;
    }
});

app.Run();

public record OrderDto(int OrderId, string CustomerName, decimal Total, string Status);

public interface IOrderRepo
{
    Task<IEnumerable<OrderDto>> SearchAsync(string query, CancellationToken ct);
    Task<IEnumerable<OrderDto>> SearchWithoutCancellationAsync(string query);
}

public class FakeOrderRepo : IOrderRepo
{
    private readonly ILogger<FakeOrderRepo> _logger;

    public FakeOrderRepo(ILogger<FakeOrderRepo> logger)
    {
        _logger = logger;
    }

    // Ignores cancellation - keeps running even if client disconnects
    public async Task<IEnumerable<OrderDto>> SearchWithoutCancellationAsync(string query)
    {
        _logger.LogInformation("[SLOW REPO] Starting uncancelled long-running search...");

        for (var i = 1; i <= 5; i++)
        {
            _logger.LogInformation("[SLOW REPO] Executing step {Step}/5 (Wasting server time)...", i);
            await Task.Delay(1000); // No cancellation token passed
        }

        return GenerateMockOrders(query);
    }

    // Respects cancellation - aborts immediately on client disconnect
    public async Task<IEnumerable<OrderDto>> SearchAsync(string query, CancellationToken ct)
    {
        _logger.LogInformation("[CANCELABLE REPO] Starting long-running order search...");

        for (var i = 1; i <= 5; i++)
        {
            _logger.LogInformation("[CANCELABLE REPO] Executing step {Step}/5...", i);
            await Task.Delay(1000, ct); // Token passed here
        }

        return GenerateMockOrders(query);
    }

    private static List<OrderDto> GenerateMockOrders(string query)
    {
        var searchTerm = string.IsNullOrWhiteSpace(query) ? "Standard Order" : query;

        return new List<OrderDto>
        {
            new(1001, $"Acme Corp ({searchTerm})", 1250.00m, "Shipped"),
            new(1002, $"Contoso Ltd ({searchTerm})", 450.50m, "Processing"),
            new(1003, $"Fabrikam Inc ({searchTerm})", 89.99m, "Delivered")
        };
    }
}
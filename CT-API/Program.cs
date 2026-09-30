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
        // Caught at the endpoint level to highlight that the pipeline was aborted
        logger.LogWarning("Request for query '{Query}' was canceled by the client.", q);
        throw; // Re-throw so Kestrel handles the socket cleanup
    }
});

app.Run();

public interface IOrderRepo
{
    Task<IEnumerable<string>> SearchAsync(string query, CancellationToken ct);
}

public class FakeOrderRepo : IOrderRepo
{
    private readonly ILogger<FakeOrderRepo> _logger;

    public FakeOrderRepo(ILogger<FakeOrderRepo> logger)
    {
        _logger = logger;
    }

    public async Task<IEnumerable<string>> SearchAsync(string query, CancellationToken ct)
    {
        _logger.LogInformation("Starting long-running order search...");

        for (var i = 1; i <= 5; i++)
        {
            // Optional explicit check if you want to log before attempting the delay
            if (ct.IsCancellationRequested)
            {
                _logger.LogWarning("Cancellation detected before starting step {Step}/5.", i);
            }

            _logger.LogInformation("Executing search step {Step}/5...", i);

            // Task.Delay will throw OperationCanceledException instantly if ct is canceled during the wait
            await Task.Delay(1000, ct);
        }

        return new[] { $"Result for {query}" };
    }
}
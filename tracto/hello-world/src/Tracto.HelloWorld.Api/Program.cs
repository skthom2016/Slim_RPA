var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "tracto-hello-world", timestamp = DateTimeOffset.UtcNow }))
   .WithName("Health");

app.MapGet("/api/hello", (string? name) =>
{
    var target = string.IsNullOrWhiteSpace(name) ? "World" : name;
    return Results.Ok(new
    {
        message = $"Hello, {target}!",
        service = "tracto-hello-world",
        timestamp = DateTimeOffset.UtcNow
    });
})
.WithName("Hello");

app.MapPost("/api/echo", async (HttpRequest request) =>
{
    using var reader = new StreamReader(request.Body);
    var body = await reader.ReadToEndAsync();
    return Results.Ok(new
    {
        echo = body,
        length = body.Length,
        timestamp = DateTimeOffset.UtcNow
    });
})
.WithName("Echo");

app.Run();

public partial class Program { }

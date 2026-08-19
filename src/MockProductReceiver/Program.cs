using System.Xml.Linq;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:7072");

var app = builder.Build();
var receiverState = new ReceiverState();
app.Lifetime.ApplicationStopping.Register(receiverState.Dispose);

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    receivedCount = receiverState.ReceivedCount
}));

app.MapPost("/products", async (HttpRequest request) =>
{
    string xml;
    using (var reader = new StreamReader(request.Body))
    {
        xml = await reader.ReadToEndAsync(request.HttpContext.RequestAborted);
    }

    XDocument document;
    try
    {
        document = XDocument.Parse(xml);
    }
    catch (Exception exception) when (exception is System.Xml.XmlException or InvalidOperationException)
    {
        return Results.BadRequest(new { error = "The request body must contain valid XML." });
    }

    var root = document.Root;
    var correlationId = root?.Element("CorrelationId")?.Value;
    var product = root?.Element("Product");

    if (root?.Name.LocalName != "ProductDelivery" ||
        string.IsNullOrWhiteSpace(correlationId) ||
        product is null)
    {
        return Results.BadRequest(new
        {
            error = "The XML must be a ProductDelivery containing CorrelationId and Product."
        });
    }

    var count = receiverState.Record(correlationId, document.ToString());

    return Results.Ok(new { received = true, count, correlationId });
});

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine("Mock receiving system listening on http://127.0.0.1:7072");
Console.WriteLine("Waiting for canonical XML products...");

await app.RunAsync();

internal sealed class ReceiverState : IDisposable
{
    private static readonly TimeSpan DisplayRefreshInterval = TimeSpan.FromMilliseconds(200);

    private readonly object _stateLock = new();
    private readonly object _displayLock = new();
    private readonly Timer _displayTimer;

    private DisplaySnapshot? _latest;
    private int _displayPending;
    private int _receivedCount;

    public ReceiverState()
    {
        _displayTimer = new Timer(
            _ => DisplayLatest(),
            state: null,
            DisplayRefreshInterval,
            DisplayRefreshInterval);
    }

    public int ReceivedCount => Volatile.Read(ref _receivedCount);

    public int Record(string correlationId, string canonicalXml)
    {
        var count = Interlocked.Increment(ref _receivedCount);

        lock (_stateLock)
        {
            _latest = new DisplaySnapshot(
                count,
                correlationId,
                canonicalXml,
                DateTimeOffset.Now);
        }

        Volatile.Write(ref _displayPending, 1);
        return count;
    }

    public void Dispose() => _displayTimer.Dispose();

    private void DisplayLatest()
    {
        if (Interlocked.Exchange(ref _displayPending, 0) == 0)
        {
            return;
        }

        DisplaySnapshot? snapshot;
        lock (_stateLock)
        {
            snapshot = _latest;
        }

        if (snapshot is null)
        {
            return;
        }

        lock (_displayLock)
        {
            TryClearConsole();

            WriteHeading($"PRODUCTS RECEIVED: {snapshot.Count}", ConsoleColor.Green);
            Console.WriteLine($"Correlation ID: {snapshot.CorrelationId}");
            Console.WriteLine($"Received at:    {snapshot.ReceivedAt:yyyy-MM-dd HH:mm:ss zzz}");
            Console.WriteLine();
            WriteHeading("CANONICAL XML PAYLOAD", ConsoleColor.Yellow);
            Console.WriteLine(snapshot.CanonicalXml);
            Console.WriteLine();
            WriteStatusLine(snapshot.Count);
        }
    }

    private static void WriteHeading(string text, ConsoleColor color)
    {
        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(new string('=', 72));
        Console.WriteLine(text);
        Console.WriteLine(new string('=', 72));
        Console.ForegroundColor = originalColor;
    }

    private static void WriteStatusLine(int count)
    {
        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write($"TOTAL RECEIVED: {count}");
        Console.ForegroundColor = originalColor;
        Console.WriteLine("  |  Waiting for the next product...");
    }

    private static void TryClearConsole()
    {
        try
        {
            Console.Clear();
        }
        catch (IOException)
        {
            // Output may be redirected during automated verification.
        }
    }

    private sealed record DisplaySnapshot(
        int Count,
        string CorrelationId,
        string CanonicalXml,
        DateTimeOffset ReceivedAt);
}

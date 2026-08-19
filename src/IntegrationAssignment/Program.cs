using IntegrationAssignment.Configuration;
using IntegrationAssignment.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection("RabbitMQ"));
builder.Services.Configure<QueueOptions>(builder.Configuration.GetSection("Queues"));
builder.Services.Configure<ExchangeOptions>(builder.Configuration.GetSection("Exchanges"));
builder.Services.Configure<RoutingKeyOptions>(builder.Configuration.GetSection("RoutingKeys"));
builder.Services.Configure<TargetApiOptions>(builder.Configuration.GetSection("TargetApi"));
builder.Services.AddSingleton<RabbitMqPublisher>();
builder.Services.AddSingleton<ProductTransformer>();
builder.Services.AddSingleton<ProductValidator>();
builder.Services.AddSingleton<ProductXmlSerializer>();
builder.Services.AddSingleton<ProductXmlValidator>();
builder.Services.AddHttpClient<ProductApiClient>((services, client) =>
{
    var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<TargetApiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
    client.Timeout = TimeSpan.FromSeconds(10);
});

var host = builder.Build();

await host.Services
    .GetRequiredService<RabbitMqPublisher>()
    .InitializeAsync(CancellationToken.None);

await host.RunAsync();

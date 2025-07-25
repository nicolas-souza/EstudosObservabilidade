
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenTelemetry().WithTracing((options) =>
{
    options.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(builder.Configuration["OTEL_SERVICE_NAME"] ?? "api"))
           .AddAspNetCoreInstrumentation()
           .AddHttpClientInstrumentation()
           .AddOtlpExporter(otel =>
           {
               otel.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
               otel.Endpoint = new Uri(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]!);
           });
});

builder.Logging.ClearProviders();

builder.Services.AddLogging(loggingBuilder =>
{
    loggingBuilder.ClearProviders();    
    
    loggingBuilder.AddOpenTelemetry(logging =>
    {
        
        logging.SetResourceBuilder(
            ResourceBuilder.CreateDefault()
                .AddService(
                    serviceName: builder.Configuration["OTEL_SERVICE_NAME"] ?? "api",
                    serviceNamespace: builder.Configuration["OTEL_SERVICE_NAMESPACE"] ?? "observability")
                .AddTelemetrySdk()
                .AddAttributes(
                [
                    new("deployment.environment", builder.Environment.EnvironmentName),
                    new("host.name", Environment.MachineName)
                ])
        );

        logging.IncludeFormattedMessage = true;
        logging.IncludeScopes = true;
        logging.ParseStateValues = true;

        logging.AddOtlpExporter(otlpOptions =>
        {
            otlpOptions.Endpoint = new Uri(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? "http://otel-collector:4317");
            otlpOptions.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
            otlpOptions.Headers = "X-Scope-OrgID=otel";
            otlpOptions.ExportProcessorType = ExportProcessorType.Simple;
        });

        if (builder.Environment.IsDevelopment())
        {
            logging.AddConsoleExporter();
        }
    });
});

builder.Logging.SetMinimumLevel(LogLevel.Warning);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var app = builder.Build();

var logger = app.Services.GetRequiredService<ILogger<Program>>();


// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    logger.LogInformation("Realizando GET /weatherforecast");

    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast")
.WithOpenApi();

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

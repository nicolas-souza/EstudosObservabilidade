
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics;

// Criar ActivitySource para traces customizados
var activitySource = new ActivitySource("WeatherAPI");

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenTelemetry().WithTracing((options) =>
{
    options.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(builder.Configuration["OTEL_SERVICE_NAME"] ?? "api"))
           .AddAspNetCoreInstrumentation()
           .AddHttpClientInstrumentation()
           .AddSource("WeatherAPI") // Adicionar nossa ActivitySource personalizada
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
            otlpOptions.Endpoint = new Uri(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? "http://alloy:4317");
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
    // Iniciar um trace customizado
    using var activity = activitySource.StartActivity("GetWeatherForecast");
    activity?.SetTag("weather.operation", "forecast_generation");
    activity?.SetTag("weather.forecast_days", 5);
    
    logger.LogInformation("🌤️  Iniciando geração da previsão do tempo");

    try
    {
        // Simular algum processamento
        using var processingActivity = activitySource.StartActivity("ProcessWeatherData");
        processingActivity?.SetTag("processing.type", "weather_calculation");
        
        var forecast = Enumerable.Range(1, 5).Select(index =>
        {
            var temperature = Random.Shared.Next(-20, 55);
            var summary = summaries[Random.Shared.Next(summaries.Length)];
            
            // Adicionar informações ao trace
            processingActivity?.AddEvent(new ActivityEvent($"Generated forecast for day {index}: {temperature}°C, {summary}"));
            
            return new WeatherForecast(
                DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                temperature,
                summary
            );
        }).ToArray();

        processingActivity?.SetTag("processing.records_generated", forecast.Length);
        processingActivity?.SetStatus(ActivityStatusCode.Ok);
        
        // Adicionar informações de sucesso ao trace principal
        activity?.SetTag("weather.forecast_count", forecast.Length);
        activity?.SetStatus(ActivityStatusCode.Ok, "Previsão gerada com sucesso");
        activity?.AddEvent(new ActivityEvent("Previsão do tempo gerada com sucesso"));
        
        logger.LogInformation("✅ Previsão do tempo gerada com sucesso. {ForecastCount} registros criados", forecast.Length);
        
        return forecast;
    }
    catch (Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.AddEvent(new ActivityEvent("Erro ao gerar previsão", DateTimeOffset.UtcNow, 
            new ActivityTagsCollection([new("error.message", ex.Message)])));
        
        logger.LogError(ex, "❌ Erro ao gerar previsão do tempo");
        throw;
    }
})
.WithName("GetWeatherForecast")
.WithOpenApi();

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

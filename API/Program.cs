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

builder.Logging.SetMinimumLevel(LogLevel.Debug);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

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

app.MapGet("/weatherforecast/error", () =>
{
    // Iniciar um trace customizado para teste de erro
    using var activity = activitySource.StartActivity("GetWeatherForecastError");
    activity?.SetTag("weather.operation", "error_test");
    activity?.SetTag("weather.test_type", "intentional_error");
    
    logger.LogWarning("⚠️  Iniciando teste de erro intencional");

    try
    {
        // Simular algum processamento antes do erro
        using var processingActivity = activitySource.StartActivity("ProcessErrorTest");
        processingActivity?.SetTag("processing.type", "error_simulation");
        processingActivity?.AddEvent(new ActivityEvent("Simulando processamento antes do erro"));
        
        // Simular um erro crítico
        var errorMessage = "Erro intencional para teste de observabilidade - Falha na conexão com serviço externo";
        
        // Adicionar informações de erro ao trace de processamento
        processingActivity?.SetStatus(ActivityStatusCode.Error, errorMessage);
        processingActivity?.AddEvent(new ActivityEvent("Erro simulado ocorreu", DateTimeOffset.UtcNow, 
            new ActivityTagsCollection([
                new("error.type", "SimulatedError"),
                new("error.severity", "High"),
                new("error.category", "ServiceUnavailable")
            ])));
        
        // Adicionar informações de erro ao trace principal
        activity?.SetStatus(ActivityStatusCode.Error, errorMessage);
        activity?.AddEvent(new ActivityEvent("Falha na operação de teste", DateTimeOffset.UtcNow, 
            new ActivityTagsCollection([
                new("error.message", errorMessage),
                new("error.intentional", "true")
            ])));
        
        // Log de erro crítico
        logger.LogError("🔥 ERRO INTENCIONAL: {ErrorMessage}. Testando fluxo de observabilidade", errorMessage);
        
        // Gerar exceção
        throw new InvalidOperationException(errorMessage);
    }
    catch (Exception ex)
    {
        // Garantir que o trace está marcado como erro
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.AddEvent(new ActivityEvent("Exceção capturada", DateTimeOffset.UtcNow, 
            new ActivityTagsCollection([
                new("exception.type", ex.GetType().Name),
                new("exception.message", ex.Message)
            ])));
        
        logger.LogCritical(ex, "💥 Erro crítico capturado durante teste de observabilidade");
        
        // Re-lançar a exceção para que ela seja tratada pelo middleware
        throw;
    }
})
.WithName("GetWeatherForecastError")
.WithOpenApi()
.WithSummary("Endpoint de teste que sempre retorna erro")
.WithDescription("Este endpoint é usado para testar o fluxo de observabilidade de erros");

app.MapGet("/weatherforecast/logs/{level}", (string level) =>
{
    using var activity = activitySource.StartActivity("TestLogLevels");
    activity?.SetTag("log.test_type", "severity_levels");
    activity?.SetTag("log.requested_level", level);
    
    var message = $"Teste de log level: {level.ToUpper()}";
    
    switch (level.ToLower())
    {
        case "debug":
            logger.LogDebug("🐛 {Message}", message);
            activity?.SetStatus(ActivityStatusCode.Ok, "Debug log generated");
            return new { Level = "Debug", Message = message, SeverityNumber = 5 };
            
        case "info":
            logger.LogInformation("ℹ️  {Message}", message);
            activity?.SetStatus(ActivityStatusCode.Ok, "Info log generated");
            return new { Level = "Information", Message = message, SeverityNumber = 9 };
            
        case "warning":
            logger.LogWarning("⚠️  {Message}", message);
            activity?.SetStatus(ActivityStatusCode.Ok, "Warning log generated");
            return new { Level = "Warning", Message = message, SeverityNumber = 13 };
            
        case "error":
            logger.LogError("❌ {Message}", message);
            activity?.SetStatus(ActivityStatusCode.Error, "Error log generated");
            activity?.AddEvent(new ActivityEvent("Error log intentionally generated"));
            return new { Level = "Error", Message = message, SeverityNumber = 17 };
            
        case "critical":
            logger.LogCritical("🔥 {Message}", message);
            activity?.SetStatus(ActivityStatusCode.Error, "Critical log generated");
            activity?.AddEvent(new ActivityEvent("Critical log intentionally generated"));
            return new { Level = "Critical", Message = message, SeverityNumber = 21 };
            
        default:
            logger.LogError("❌ Nível de log inválido: {Level}", level);
            activity?.SetStatus(ActivityStatusCode.Error, "Invalid log level");
            return new { Level = "Error", Message = $"Nível de log inválido: {level}", SeverityNumber = 17 };
    }
})
.WithName("TestLogLevels")
.WithOpenApi()
.WithSummary("Endpoint para testar diferentes níveis de log")
.WithDescription("Use: debug, info, warning, error, critical");

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

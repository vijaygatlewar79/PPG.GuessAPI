using PPG.GuessAPI;
using PPG.GuessData;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IExcelReaderService, ExcelReaderService>();
builder.Services.AddScoped<IPanelAnalysisService, PanelAnalysisService>();
builder.Services.AddScoped<IPanelGameService, PanelGameService>();
builder.Services.Configure<GeminiPatternPredictionOptions>(
    builder.Configuration.GetSection(GeminiPatternPredictionOptions.SectionName));
builder.Services.AddScoped<IPatternPredictionService, GeminiPatternPredictionService>();
builder.Services.AddSingleton<ChartSourceCatalog>();
builder.Services.AddHttpClient<IChartExcelService, ChartExcelService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
});

var allowedCorsOrigins = (builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .Select(origin => origin.Trim().TrimEnd('/'))
    .Where(origin => origin.Length > 0)
    .ToHashSet(StringComparer.OrdinalIgnoreCase);
var allowedStaticWebAppHostPrefixes = (
        builder.Configuration.GetSection("Cors:AllowedAzureStaticWebAppHostPrefixes").Get<string[]>() ?? [])
    .Select(prefix => prefix.Trim().TrimEnd('.'))
    .Where(prefix => prefix.Length > 0)
    .ToArray();

bool IsAllowedCorsOrigin(string origin)
{
    if (allowedCorsOrigins.Contains(origin.TrimEnd('/')))
    {
        return true;
    }

    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        || !uri.Host.EndsWith(".azurestaticapps.net", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    return allowedStaticWebAppHostPrefixes.Any(prefix =>
        uri.Host.Equals($"{prefix}.azurestaticapps.net", StringComparison.OrdinalIgnoreCase)
        || uri.Host.StartsWith($"{prefix}.", StringComparison.OrdinalIgnoreCase)
        || uri.Host.StartsWith($"{prefix}-", StringComparison.OrdinalIgnoreCase));
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("GuessUi", policy =>
    {
        policy.SetIsOriginAllowed(IsAllowedCorsOrigin)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment() || app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("GuessUi");

app.UseAuthorization();

app.MapControllers();

app.Run();

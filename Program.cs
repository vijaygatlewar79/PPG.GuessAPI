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
builder.Services.AddCors(options =>
{
    options.AddPolicy("GuessUi", policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("GuessUi");

app.UseAuthorization();

app.MapControllers();

app.Run();

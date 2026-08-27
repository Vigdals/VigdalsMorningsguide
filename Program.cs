using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Options;
using VigdalsMorningsguide.Options;
using VigdalsMorningsguide.Services;

var builder =
    WebApplication.CreateBuilder(
        args);

builder.Services.AddControllersWithViews();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ShellyCloudRequestGate>();
var nynorskCulture =
    new CultureInfo(
        "nn-NO");

builder.Services.Configure<RequestLocalizationOptions>(
    options =>
    {
        options.DefaultRequestCulture =
            new RequestCulture(
                nynorskCulture);

        options.SupportedCultures =
        [
            nynorskCulture
        ];

        options.SupportedUICultures =
        [
            nynorskCulture
        ];
    });

builder.Services
    .AddOptions<FrostOptions>()
    .Bind(
        builder.Configuration.GetSection(
            FrostOptions.SectionName))
    .Validate(
        options =>
            !string.IsNullOrWhiteSpace(
                options.ClientId),
        "Frost:ClientId manglar.")
    .Validate(
        options =>
            options.MinimumCoveragePercent
            is >= 0 and <= 100,
        "MinimumCoveragePercent må vere mellom 0 og 100.")
    .ValidateOnStart();

builder.Services
    .AddOptions<MetForecastOptions>()
    .Bind(
        builder.Configuration.GetSection(
            MetForecastOptions.SectionName))
    .Validate(
        options =>
            !string.IsNullOrWhiteSpace(
                options.BaseUrl),
        "MetForecast:BaseUrl manglar.")
    .Validate(
        options =>
            !string.IsNullOrWhiteSpace(
                options.UserAgent),
        "MetForecast:UserAgent manglar.")
    .ValidateOnStart();

builder.Services
    .AddOptions<ShellyOptions>()
    .Bind(
        builder.Configuration.GetSection(
            ShellyOptions.SectionName))
    .Validate(
        options =>
            Uri.TryCreate(
                options.BaseUrl,
                UriKind.Absolute,
                out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps,
        "Shelly:BaseUrl må vere ei gyldig HTTPS-adresse.")
    .Validate(
        options =>
            !string.IsNullOrWhiteSpace(
                options.DeviceId),
        "Shelly:DeviceId manglar.")
    .Validate(
        options =>
            !string.IsNullOrWhiteSpace(
                options.AuthKey),
        "Shelly:AuthKey manglar.")
    .Validate(
        options =>
            !string.IsNullOrWhiteSpace(
                options.DisplayName),
        "Shelly:DisplayName manglar.")
    .Validate(
        options =>
            options.CacheSeconds
            is >= 1 and <= 300,
        "Shelly:CacheSeconds må vere mellom 1 og 300.")
    .Validate(
        options =>
            options.StaleAfterMinutes
            is >= 1 and <= 1440,
        "Shelly:StaleAfterMinutes må vere mellom 1 og 1440.")
    .Validate(
        options =>
            options.MinimumCoveragePercent
            is >= 0 and <= 100,
        "Shelly:MinimumCoveragePercent må vere mellom 0 og 100.")
    .Validate(
        options =>
            options.MaximumAcceptedGapMinutes > 0,
        "Shelly:MaximumAcceptedGapMinutes må vere større enn null.")
    .Validate(
        options =>
            options.MaximumDaysBack > 0,
        "Shelly:MaximumDaysBack må vere større enn null.")
    .ValidateOnStart();

static void ConfigureFrostClient(
    IServiceProvider serviceProvider,
    HttpClient httpClient)
{
    var options =
        serviceProvider
            .GetRequiredService<
                IOptions<FrostOptions>>()
            .Value;

    httpClient.BaseAddress =
        new Uri(
            options.BaseUrl);

    httpClient.Timeout =
        TimeSpan.FromSeconds(
            30);

    httpClient.DefaultRequestHeaders
        .UserAgent
        .ParseAdd(
            "VigdalsMorningsguide/1.0");
}

static void ConfigureMetForecastClient(
    IServiceProvider serviceProvider,
    HttpClient httpClient)
{
    var options =
        serviceProvider
            .GetRequiredService<
                IOptions<MetForecastOptions>>()
            .Value;

    httpClient.BaseAddress =
        new Uri(
            options.BaseUrl);

    httpClient.Timeout =
        TimeSpan.FromSeconds(
            30);

    httpClient.DefaultRequestHeaders
        .UserAgent
        .ParseAdd(
            options.UserAgent);
}

static void ConfigureShellyClient(
    IServiceProvider serviceProvider,
    HttpClient httpClient)
{
    var options =
        serviceProvider
            .GetRequiredService<
                IOptions<ShellyOptions>>()
            .Value;

    var baseUrl =
        options.BaseUrl.TrimEnd('/') +
        "/";

    httpClient.BaseAddress =
        new Uri(
            baseUrl);

    httpClient.Timeout =
        TimeSpan.FromSeconds(
            10);

    httpClient.DefaultRequestHeaders
        .UserAgent
        .ParseAdd(
            "VigdalsMorningsguide/1.0");
}

static void ConfigureShellyHistoryClient(
    IServiceProvider serviceProvider,
    HttpClient httpClient)
{
    ConfigureShellyClient(
        serviceProvider,
        httpClient);

    httpClient.Timeout =
        TimeSpan.FromSeconds(
            30);
}

builder.Services.AddHttpClient<FrostService>(
    ConfigureFrostClient);
builder.Services.AddHttpClient<FrostStationService>(
    ConfigureFrostClient);
builder.Services.AddHttpClient<MetForecastService>(
    ConfigureMetForecastClient);
builder.Services.AddHttpClient<ShellyService>(
    ConfigureShellyClient);
builder.Services.AddHttpClient<ShellyHistoryService>(
    ConfigureShellyHistoryClient);

builder.Services.AddSingleton<MorningForecastService>();
builder.Services.AddSingleton<DegreeDayCalculationService>();

var app =
    builder.Build();

var localizationOptions =
    app.Services
        .GetRequiredService<
            IOptions<RequestLocalizationOptions>>()
        .Value;

app.UseRequestLocalization(
    localizationOptions);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(
        "/Home/Error");

    app.UseHsts();
}

// Aktiver når appen blir køyrd med HTTPS.
// app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern:
        "{controller=Morning}/{action=Index}/{id?}");

app.MapGet(
    "/healthz",
    () => Results.Ok(
        new
        {
            status = "healthy",
            service = "VigdalsMorningsguide"
        }));

app.Run();

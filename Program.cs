using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Options;
using VigdalsMorningsguide.Options;
using VigdalsMorningsguide.Services;

var builder =
    WebApplication.CreateBuilder(
        args);

builder.Services.AddControllersWithViews();

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

builder.Services.AddHttpClient<FrostService>(
    ConfigureFrostClient);

builder.Services.AddHttpClient<FrostStationService>(
    ConfigureFrostClient);

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

app.Run();
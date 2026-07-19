using Microsoft.Extensions.Options;
using VigdalsMorningsguide.Options;
using VigdalsMorningsguide.Services;
using System.Globalization;
using Microsoft.AspNetCore.Localization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

var nynorskCulture = new CultureInfo("nn-NO");

builder.Services.Configure<RequestLocalizationOptions>(
    options =>
    {
        options.DefaultRequestCulture =
            new RequestCulture(nynorskCulture);

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
            !string.IsNullOrWhiteSpace(
                options.SourceId),
        "Frost:SourceId manglar.")
    .Validate(
        options =>
            !string.IsNullOrWhiteSpace(
                options.ElementId),
        "Frost:ElementId manglar.")
    .Validate(
        options =>
            options.MinimumCoveragePercent
            is >= 0 and <= 100,
        "MinimumCoveragePercent må vere mellom 0 og 100.")
    .ValidateOnStart();

builder.Services.AddHttpClient<FrostService>(
    (serviceProvider, httpClient) =>
    {
        var options = serviceProvider
            .GetRequiredService<
                IOptions<FrostOptions>>()
            .Value;

        httpClient.BaseAddress =
            new Uri(options.BaseUrl);

        httpClient.Timeout =
            TimeSpan.FromSeconds(30);

        httpClient.DefaultRequestHeaders
            .UserAgent
            .ParseAdd(
                "VigdalsMorningsguide/1.0");
    });

var app = builder.Build();

var localizationOptions = app.Services
    .GetRequiredService<
        Microsoft.Extensions.Options.IOptions<
            RequestLocalizationOptions>>()
    .Value;

app.UseRequestLocalization(localizationOptions);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(
        "/Home/Error");

    app.UseHsts();
}

/*
 * Du køyrer førebels berre HTTP lokalt.
 */
// app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern:
        "{controller=Morning}/{action=Index}/{id?}");

app.Run();
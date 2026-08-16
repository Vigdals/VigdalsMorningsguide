namespace VigdalsMorningsguide.Options
{
    public sealed class MetForecastOptions
    {
        public const string SectionName = "MetForecast";
        public string BaseUrl { get; set; } = "https://api.met.no/weatherapi/locationforecast/2.0/";
        public string UserAgent { get; set; } = "VigdalsMorningsguide/1.0" + "(+https://github.com/Vigdals/VigdalsMorningsguide)";
    }
}

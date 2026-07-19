using System.Text.Json.Serialization;

namespace VigdalsMorningsguide.Models;

public sealed class FrostObservationResponse
{
    [JsonPropertyName("data")]
    public List<FrostDataPoint> Data { get; init; } = [];
}

public sealed class FrostDataPoint
{
    [JsonPropertyName("sourceId")]
    public string SourceId { get; init; } = string.Empty;

    [JsonPropertyName("referenceTime")]
    public DateTimeOffset ReferenceTime { get; init; }

    [JsonPropertyName("observations")]
    public List<FrostObservation> Observations { get; init; } = [];
}

public sealed class FrostObservation
{
    [JsonPropertyName("elementId")]
    public string ElementId { get; init; } = string.Empty;

    [JsonPropertyName("value")]
    public double Value { get; init; }

    [JsonPropertyName("unit")]
    public string Unit { get; init; } = string.Empty;

    [JsonPropertyName("qualityCode")]
    public int? QualityCode { get; init; }
}

public sealed class FrostErrorResponse
{
    [JsonPropertyName("error")]
    public FrostError? Error { get; init; }
}

public sealed class FrostError
{
    [JsonPropertyName("code")]
    public int Code { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;
}
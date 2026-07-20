using System.ComponentModel.DataAnnotations;

namespace VigdalsMorningsguide.Models;

public sealed class MorningInputModel
{
    [Display(Name = "Dato")]
    [Required(ErrorMessage = "Du må velje dato.")]
    public DateOnly HungDate { get; set; } =
        DateOnly.FromDateTime(DateTime.Now.AddDays(-2));

    [Display(Name = "Klokkeslett")]
    [Required(ErrorMessage = "Du må velje klokkeslett.")]
    public TimeOnly HungTime { get; set; } =
        TimeOnly.FromDateTime(DateTime.Now);

    [Display(Name = "Målestasjon")]
    [Required(ErrorMessage = "Du må velje målestasjon.")]
    public string SelectedSourceId { get; set; } =
        WeatherStationCatalog.DefaultSourceId;

    [Display(Name = "Mål for døgngrader")]
    [Range(
        1,
        120,
        ErrorMessage = "Målet må vere mellom 1 og 120 døgngrader.")]
    public double TargetDegreeDays { get; set; } = 80;

    public DateTime GetHungAt()
    {
        return DateTime.SpecifyKind(
            HungDate.ToDateTime(HungTime),
            DateTimeKind.Unspecified);
    }
}
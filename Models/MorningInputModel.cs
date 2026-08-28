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

    [Display(Name = "Temperaturkjelde")]
    [Required(ErrorMessage = "Du må velje temperaturkjelde.")]
    public string SelectedSourceId { get; set; } =
        TemperatureSourceCatalog.ShellySourceId;

    [Display(Name = "Mål for døgngrader")]
    [Range(
        1,
        120,
        ErrorMessage = "Målet må vere mellom 1 og 120 døgngrader.")]
    public double TargetDegreeDays { get; set; } = 80;

    [Display(
        Name = "Har du lagt kjøtet i kjøleskap i løpet av mørningsperioden?")]
    public bool HasBeenRefrigerated { get; set; }

    [Display(Name = "Dato lagt i kjøleskap")]
    public DateOnly? RefrigeratedFromDate { get; set; }

    [Display(Name = "Klokkeslett lagt i kjøleskap")]
    public TimeOnly? RefrigeratedFromTime { get; set; }

    public DateTime GetHungAt()
    {
        return DateTime.SpecifyKind(
            HungDate.ToDateTime(HungTime),
            DateTimeKind.Unspecified);
    }

    public DateTime? GetRefrigeratedAt()
    {
        if (!HasBeenRefrigerated ||
            !RefrigeratedFromDate.HasValue ||
            !RefrigeratedFromTime.HasValue)
        {
            return null;
        }

        return DateTime.SpecifyKind(
            RefrigeratedFromDate.Value.ToDateTime(
                RefrigeratedFromTime.Value),
            DateTimeKind.Unspecified);
    }
}

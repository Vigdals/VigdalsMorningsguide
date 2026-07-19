using System.ComponentModel.DataAnnotations;

namespace VigdalsMorningsguide.Models;

public sealed class LocationInputModel
{
    [Display(Name = "Breiddegrad")]
    [Range(
        -90,
        90,
        ErrorMessage = "Breiddegraden må vere mellom -90 og 90.")]
    public double Latitude { get; set; } = 61.22908;

    [Display(Name = "Lengdegrad")]
    [Range(
        -180,
        180,
        ErrorMessage = "Lengdegraden må vere mellom -180 og 180.")]
    public double Longitude { get; set; } = 7.09674;
}
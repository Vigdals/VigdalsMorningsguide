using Microsoft.AspNetCore.Mvc.Rendering;

namespace VigdalsMorningsguide.Models;

public sealed class MorningPageViewModel
{
    public MorningInputModel Input { get; set; } =
        new();

    public MorningResultModel? Result { get; set; }

    public IReadOnlyList<SelectListItem> StationOptions { get; set; } =
        [];
}
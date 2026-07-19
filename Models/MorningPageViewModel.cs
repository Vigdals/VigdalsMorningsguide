namespace VigdalsMorningsguide.Models;

public sealed class MorningPageViewModel
{
    public MorningInputModel Input { get; set; } = new();

    public MorningResultModel? Result { get; set; }
}
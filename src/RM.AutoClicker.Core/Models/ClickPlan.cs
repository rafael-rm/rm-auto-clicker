namespace RM.AutoClicker.Core.Models;

public sealed record ClickPlan(
    double ClicksPerSecond = 10,
    double VariationPercent = 0,
    ClickButton Button = ClickButton.Left,
    TimeSpan InitialDelay = default,
    long? MaximumClicks = null,
    TimeSpan? MaximumDuration = null)
{
    public TimeSpan BaseInterval => TimeSpan.FromSeconds(1.0 / ClicksPerSecond);

    public void Validate()
    {
        if (!double.IsFinite(ClicksPerSecond) || ClicksPerSecond is < 1 or > 25)
            throw new ArgumentOutOfRangeException(nameof(ClicksPerSecond), "Speed must be between 1 and 25 clicks/s.");

        if (!double.IsFinite(VariationPercent) || VariationPercent is < 0 or > 50)
            throw new ArgumentOutOfRangeException(nameof(VariationPercent), "Variation must be between 0 and 50%.");

        if (!Enum.IsDefined(Button))
            throw new ArgumentOutOfRangeException(nameof(Button), "Select a valid mouse button.");

        if (InitialDelay < TimeSpan.Zero || InitialDelay > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(InitialDelay), "Initial delay must be between 0 and 300 seconds.");

        if (MaximumClicks is <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaximumClicks), "The click limit must be positive.");

        if (MaximumDuration is { } duration && duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(MaximumDuration), "Duration must be positive.");
    }
}

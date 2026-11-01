using RM.AutoClicker.Core.Abstractions;
using RM.AutoClicker.Core.Execution;
using RM.AutoClicker.Core.Models;

namespace RM.AutoClicker.Core.Tests;

public sealed class IntervalPolicyTests
{
    [Theory]
    [InlineData(0, 80)]
    [InlineData(0.5, 100)]
    [InlineData(1, 120)]
    public void VariationIsSymmetricAroundTheBaseInterval(double sample, double expectedMilliseconds)
    {
        var policy = new IntervalPolicy(new FixedRandom(sample));
        Assert.Equal(TimeSpan.FromMilliseconds(expectedMilliseconds), policy.GetNextInterval(new(VariationPercent: 20)));
    }

    [Fact]
    public void DoesNotTruncateTheRateToIntegerMilliseconds()
    {
        var plan = new ClickPlan(ClicksPerSecond: 24);
        Assert.InRange(plan.BaseInterval.TotalMilliseconds, 41.66, 41.67);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(51)]
    [InlineData(double.NaN)]
    public void RejectsInvalidVariation(double variation)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClickPlan(VariationPercent: variation).Validate());
    }

    [Fact]
    public void RejectsInvalidLimitsAndButton()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClickPlan(MaximumClicks: 0).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClickPlan(MaximumDuration: TimeSpan.Zero).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClickPlan(InitialDelay: TimeSpan.FromSeconds(-1)).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClickPlan(Button: (ClickButton)100).Validate());
    }

    private sealed class FixedRandom(double sample) : IRandomSource
    {
        public double NextDouble() => sample;
    }
}

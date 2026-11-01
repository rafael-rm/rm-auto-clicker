using RM.AutoClicker.Core.Abstractions;
using RM.AutoClicker.Core.Models;

namespace RM.AutoClicker.Core.Execution;

public sealed class IntervalPolicy(IRandomSource random)
{
    public TimeSpan GetNextInterval(ClickPlan plan)
    {
        if (plan.VariationPercent == 0)
            return plan.BaseInterval;

        var sample = random.NextDouble();
        if (!double.IsFinite(sample) || sample is < 0 or > 1)
            throw new InvalidOperationException("The variation source returned an invalid value.");

        return plan.BaseInterval * (1 + (sample * 2 - 1) * plan.VariationPercent / 100);
    }
}

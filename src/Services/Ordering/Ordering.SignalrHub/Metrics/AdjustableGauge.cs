using System.Diagnostics.Metrics;
using System.Threading;

namespace Ordering.SignalrHub.Metrics;

public class AdjustableGauge
{
    private long _currentValue;

    public AdjustableGauge(Meter meter, string name, string? description = null)
    {
        meter.CreateObservableGauge(name, () => _currentValue, description: description);
    }

    public void ChangeBy(long delta) => Interlocked.Add(ref _currentValue, delta);
}
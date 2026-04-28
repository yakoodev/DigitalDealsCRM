using System.Collections.Concurrent;

namespace DDCRM.Worker.Api.Simulation;

public sealed class TransientFailureState
{
    private readonly ConcurrentDictionary<string, byte> _failures = new(StringComparer.Ordinal);

    public bool ShouldFail(string operationKey) => _failures.TryAdd(operationKey, 1);
}

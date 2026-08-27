using System.Collections.Generic;

namespace LuckyDogRise;

public interface IPlatformStatisticSyncOperations
{
    PlatformStatisticReadResult ReadStatistics(IEnumerable<string> statisticApiNames);
    PlatformStatisticWriteResult SubmitStatistics(IReadOnlyDictionary<string, int> valuesByApiName);
}

public readonly record struct PlatformStatisticState(
    string ApiName,
    bool IsConfigured,
    bool ReadSucceeded,
    int Value);

public sealed class PlatformStatisticReadResult
{
    public PlatformStatisticReadResult(
        bool succeeded,
        string message,
        IReadOnlyList<PlatformStatisticState> states)
    {
        Succeeded = succeeded;
        Message = message;
        States = states;
    }

    public bool Succeeded { get; }
    public string Message { get; }
    public IReadOnlyList<PlatformStatisticState> States { get; }
}

public sealed class PlatformStatisticWriteResult
{
    public PlatformStatisticWriteResult(
        bool succeeded,
        string message,
        IReadOnlyList<string> acceptedApiNames)
    {
        Succeeded = succeeded;
        Message = message;
        AcceptedApiNames = acceptedApiNames;
    }

    public bool Succeeded { get; }
    public string Message { get; }
    public IReadOnlyList<string> AcceptedApiNames { get; }
}

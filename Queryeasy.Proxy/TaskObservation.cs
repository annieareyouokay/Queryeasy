namespace Queryeasy.Proxy;

internal static class TaskObservation
{
    public static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }

    public static async Task<long> IgnoreCancellationAsync(Task<long> task)
    {
        try
        {
            return await task;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
    }
}

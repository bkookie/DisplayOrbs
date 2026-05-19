using System;
using System.Threading;
using System.Threading.Tasks;

namespace DisplayOrbs.DisplayOrbsCode.Orbs;

/// <summary>
/// Queues up async tasks to run one at a time in a FIFO manner.
/// </summary>
[Obsolete("Works as intended, I think, but not at all the correct approach.")]
public class OrderedTaskQueue
{
    // AI generated

    private Task _lastTask = Task.CompletedTask;

    public async Task EnqueueAsync(Func<Task> work)
    {
        TaskCompletionSource tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task taskToReturn = tcs.Task;

        // Atomically swap in the new TCS task
        Task priorTask = Interlocked.Exchange(ref _lastTask, taskToReturn);

        try
        {
            // Wait for all prior work to complete
            await AwaitWithoutThrow(priorTask).ConfigureAwait(false);

            Task? workTask;

            try
            {
                workTask = work(); // This could throw synchronously
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
                throw;
            }

            // Now await the actual work
            await work().ConfigureAwait(false);
            tcs.SetResult();
        }
        catch (Exception ex)
        {
            tcs.SetException(ex);
            throw;
        }
    }

    private static async Task AwaitWithoutThrow(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // Ignore exceptions from prior task
        }
    }
}
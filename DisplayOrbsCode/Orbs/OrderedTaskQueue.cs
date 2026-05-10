using System;
using System.Threading;
using System.Threading.Tasks;

namespace DisplayOrbs.DisplayOrbsCode.Orbs;

/// <summary>
/// Queues up async tasks to run one at a time in a FIFO manner.
/// </summary>
public class OrderedTaskQueue
{
    private Task _lastTask = Task.CompletedTask;

    public async Task EnqueueAsync(Func<Task> work)
    {
        TaskCompletionSource tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task taskToReturn = tcs.Task;

        // Atomically swap in the new TCS task
        Task priorTask = Interlocked.Exchange(ref _lastTask, taskToReturn);

        // Wait for all prior work to complete
        await priorTask.ConfigureAwait(false);

        try
        {
            await work().ConfigureAwait(false);
            tcs.SetResult();
            return;
        }
        catch (Exception ex)
        {
            tcs.SetException(ex);
            throw;
        }
    }
}
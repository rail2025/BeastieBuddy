using BeastieBuddy.VfxSystem;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BeastieBuddy.VfxSystem;

internal static class SemaphoreExt
{
    internal static OnDispose With(this SemaphoreSlim semaphore)
    {
        semaphore.Wait();
        return new OnDispose(() => semaphore.Release());
    }

    internal static bool With(this SemaphoreSlim semaphore, TimeSpan timeout, out OnDispose? releaser)
    {
        if (semaphore.Wait(timeout))
        {
            releaser = new OnDispose(() => semaphore.Release());
            return true;
        }

        releaser = null;
        return false;
    }

    internal static async Task<OnDispose> WithAsync(this SemaphoreSlim semaphore, CancellationToken token = default)
    {
        await semaphore.WaitAsync(token);
        return new OnDispose(() => semaphore.Release());
    }
}

using System;

namespace BeastieBuddy.VfxSystem;

internal class OnDispose : IDisposable
{
    private bool _disposed;
    private readonly Action _action;

    internal OnDispose(Action action)
    {
        _action = action;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _action();
    }
}

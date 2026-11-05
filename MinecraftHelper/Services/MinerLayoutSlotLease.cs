using System;
using System.Threading;

namespace MinecraftHelper.Services;

// An atomic, session-local reservation shared by separate Kopacz processes.
// Kernel handles release the slot on normal exit and on a process crash.
internal sealed class MinerLayoutSlotLease : IDisposable
{
    private EventWaitHandle? _handle;
    public int Index { get; }

    private MinerLayoutSlotLease(EventWaitHandle handle, int index)
    {
        _handle = handle;
        Index = index;
    }

    public static MinerLayoutSlotLease? TryAcquire(string group, int capacity)
    {
        for (int index = 0; index < capacity; index++)
        {
            try
            {
                var handle = new EventWaitHandle(false, EventResetMode.ManualReset,
                    $@"Local\MinecraftHelper.Miner.Layout.{group}.{index}", out bool created);
                if (created)
                    return new MinerLayoutSlotLease(handle, index);
                handle.Dispose();
            }
            catch (UnauthorizedAccessException) { return null; }
        }
        return null;
    }

    public void Dispose()
    {
        _handle?.Dispose();
        _handle = null;
    }
}

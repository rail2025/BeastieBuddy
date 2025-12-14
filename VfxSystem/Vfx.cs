using BeastieBuddy.VfxSystem;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using SceneObject = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Object;

namespace BeastieBuddy.VfxSystem;

// We reintroduce a minimal struct purely for the delegate signatures to match NorthStar's working architecture
internal unsafe struct VfxDelegateStruct { }

internal unsafe class Vfx : IDisposable
{
    private static readonly byte[] Pool = "Client.System.Scheduler.Instance.VfxObject\0"u8.ToArray();

#pragma warning disable CS0649
    // REVERT: Use the minimal VfxStruct/VfxDelegateStruct pointer type in the signatures
    // This is often required if the memory management signature differs from the general SceneObject type.
    [Signature("E8 ?? ?? ?? ?? F3 0F 10 35 ?? ?? ?? ?? 48 89 43 08")]
    private readonly delegate* unmanaged<byte*, byte*, VfxDelegateStruct*> _staticVfxCreate;

    [Signature("E8 ?? ?? ?? ?? B0 ?? EB ?? B0 ?? 88 83")]
    private readonly delegate* unmanaged<VfxDelegateStruct*, float, int, ulong> _staticVfxRun;

    [Signature("40 53 48 83 EC 20 48 8B D9 48 8B 89 ?? ?? ?? ?? 48 85 C9 74 28 33 D2 E8 ?? ?? ?? ?? 48 8B 8B ?? ?? ?? ?? 48 85 C9")]
    private readonly delegate* unmanaged<VfxDelegateStruct*, nint> _staticVfxRemove;

    // REMOVED UNUSED/UNSTABLE SIGNATURES: _sceneManagerInstance and _worldSceneAddObject
#pragma warning restore CS0649

    private Plugin PluginInstance { get; }
    internal SemaphoreSlim Mutex { get; } = new(1, 1);
    internal Dictionary<Guid, nint> Spawned { get; } = [];
    private Queue<IQueueAction> Queue { get; } = [];
    private bool _disposed;
    private readonly Stopwatch _queueTimer = Stopwatch.StartNew();

    internal Vfx(Plugin plugin)
    {
        PluginInstance = plugin;
        BeastieBuddy.Plugin.GameInteropProvider.InitializeFromAttributes(this);
        BeastieBuddy.Plugin.Framework.Update += HandleQueues;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        BeastieBuddy.Plugin.Framework.Update -= HandleQueues;
        RemoveAllSync();
    }

    private void HandleQueues(IFramework _) => HandleQueues();

    private void HandleQueues()
    {
        _queueTimer.Restart();
        while (_queueTimer.Elapsed < TimeSpan.FromMilliseconds(1))
        {
            if (!Queue.TryDequeue(out var action)) return;

            switch (action)
            {
                case AddQueueAction add:
                    using (Mutex.With())
                    {
                        if (Spawned.Remove(add.Id, out var existing))
                            Queue.Enqueue(new RemoveRawQueueAction(existing));

                        var vfx = SpawnStatic(add.Path, add.Position, add.Rotation);
                        // We must cast the result back to nint for storage
                        if (vfx != null) Spawned[add.Id] = (nint)vfx;
                    }
                    break;

                case RemoveQueueAction remove:
                    using (Mutex.With())
                    {
                        if (Spawned.Remove(remove.Id, out var ptr))
                            // We must cast the stored nint back to the delegate struct type for removal
                            RemoveStatic((VfxDelegateStruct*)ptr);
                    }
                    break;

                case RemoveRawQueueAction remove:
                    RemoveStatic((VfxDelegateStruct*)remove.Pointer);
                    break;
            }
        }
    }

    internal void RemoveAllSync()
    {
        using var guard = Mutex.With();
        foreach (var spawned in Spawned.Values.ToArray())
            RemoveStatic((VfxDelegateStruct*)spawned);
        Spawned.Clear();
    }

    internal void QueueSpawn(Guid id, string path, Vector3 pos, Quaternion rotation)
    {
        using var guard = Mutex.With();
        Queue.Enqueue(new AddQueueAction(id, path, pos, rotation));
    }

    internal void QueueRemove(Guid id)
    {
        using var guard = Mutex.With();
        Queue.Enqueue(new RemoveQueueAction(id));
    }

    internal void QueueRemoveAll()
    {
        using var guard = Mutex.With();
        foreach (var id in Spawned.Keys) Queue.Enqueue(new RemoveQueueAction(id));
    }

    private VfxDelegateStruct* SpawnStatic(string path, Vector3 pos, Quaternion rotation)
    {
        VfxDelegateStruct* vfxStruct;

        var bytes = Encoding.UTF8.GetBytes(path);
        var nt = new byte[bytes.Length + 1];
        bytes.CopyTo(nt, 0);

        fixed (byte* p = nt)
        fixed (byte* pool = Pool)
        {
            vfxStruct = _staticVfxCreate(p, pool);
        }

        if (vfxStruct == null)
        {
            BeastieBuddy.Plugin.Log.Error($"Failed to create VFX: {path}");
            return null;
        }

        // CRITICAL: Cast the resulting VfxDelegateStruct* (which is just a pointer to the memory)
        // to the correct DrawObject* type from ClientStructs for safe property access.
        var drawObj = (DrawObject*)vfxStruct;

        // Use ClientStructs to safely set transform data
        drawObj->Position = pos;
        drawObj->Rotation = rotation;
        drawObj->Scale = Vector3.One;

        // Use the safe property to set visibility
        drawObj->IsVisible = true;

        BeastieBuddy.Plugin.Log.Debug($"Spawning VFX at {pos}");

        // Initialize and potentially run the VFX
        _staticVfxRun(vfxStruct, 0.0f, -1);
        return vfxStruct;
    }

    private void RemoveStatic(VfxDelegateStruct* vfx)
    {
        if (vfx != null) _staticVfxRemove(vfx);
    }
}

internal interface IQueueAction;
internal sealed record AddQueueAction(Guid Id, string Path, Vector3 Position, Quaternion Rotation) : IQueueAction;
internal sealed record RemoveQueueAction(Guid Id) : IQueueAction;
internal sealed record RemoveRawQueueAction(nint Pointer) : IQueueAction;

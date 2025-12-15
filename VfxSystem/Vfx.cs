using BeastieBuddy;
using BeastieBuddy.VfxSystem;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace BeastieBuddy.VfxSystem;

internal unsafe class Vfx : IDisposable
{
    private static readonly byte[] Pool = "Client.System.Scheduler.Instance.VfxObject\0"u8.ToArray();

    [Signature("E8 ?? ?? ?? ?? F3 0F 10 35 ?? ?? ?? ?? 48 89 43 08")]
    private readonly delegate* unmanaged<byte*, byte*, VfxStruct*> _staticVfxCreate;

    [Signature("E8 ?? ?? ?? ?? B0 ?? EB ?? B0 ?? 88 83")]
    private readonly delegate* unmanaged<VfxStruct*, float, int, ulong> _staticVfxRun;

    [Signature("40 53 48 83 EC 20 48 8B D9 48 8B 89 ?? ?? ?? ?? 48 85 C9 74 28 33 D2 E8 ?? ?? ?? ?? 48 8B 8B ?? ?? ?? ?? 48 85 C9")]
    private readonly delegate* unmanaged<VfxStruct*, nint> _staticVfxRemove;

    private Plugin PluginInstance { get; }
    internal SemaphoreSlim Mutex { get; } = new(1, 1);
    internal Dictionary<Guid, nint> Spawned { get; } = [];
    private Queue<IQueueAction> Queue { get; } = [];
    private bool _disposed;
    private readonly Stopwatch _queueTimer = Stopwatch.StartNew();
    private IGameInteropProvider GameInteropProvider { get; }
    private IFramework Framework { get; }
    private IPluginLog Log { get; }

    internal Vfx(Plugin plugin, IGameInteropProvider gameInteropProvider, IFramework framework, IPluginLog log)
    {
        PluginInstance = plugin;
        GameInteropProvider = gameInteropProvider;
        Framework = framework;
        Log = log;

        GameInteropProvider.InitializeFromAttributes(this);
        Framework.Update += HandleQueues;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Framework.Update -= HandleQueues;
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
                        if (vfx != null) Spawned[add.Id] = (nint)vfx;
                    }
                    break;

                case RemoveQueueAction remove:
                    using (Mutex.With())
                    {
                        if (Spawned.Remove(remove.Id, out var ptr))
                            RemoveStatic((VfxStruct*)ptr);
                    }
                    break;

                case RemoveRawQueueAction remove:
                    RemoveStatic((VfxStruct*)remove.Pointer);
                    break;
            }
        }
    }

    internal void RemoveAllSync()
    {
        using var guard = Mutex.With();
        foreach (var spawned in Spawned.Values.ToArray())
            RemoveStatic((VfxStruct*)spawned);
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

    private VfxStruct* SpawnStatic(string path, Vector3 pos, Quaternion rotation)
    {
        VfxStruct* vfx;

        var bytes = Encoding.UTF8.GetBytes(path);
        var nt = new byte[bytes.Length + 1];
        bytes.CopyTo(nt, 0);

        fixed (byte* p = nt)
        fixed (byte* pool = Pool)
        {
            vfx = _staticVfxCreate == null ? null : _staticVfxCreate(p, pool);
        }

        if (vfx == null)
        {
            Log.Error($"Failed to create VFX: {path}");
            return null;
        }

        // Initialize Data
        vfx->Position = pos;
        vfx->Rotation = rotation;
        vfx->Scale = Vector3.One;

        // Critical Flags to prevent crash
        vfx->SomeFlags &= 0xF7;
        vfx->Flags |= 2;

        // Set Colors
        vfx->Red = 1.0f;
        vfx->Green = 1.0f;
        vfx->Blue = 1.0f;
        vfx->Alpha = 1.0f;

        Log.Debug($"Spawning VFX at {pos}");

        if (_staticVfxRun != null) _staticVfxRun(vfx, 0.0f, -1);
        return vfx;
    }

    private void RemoveStatic(VfxStruct* vfx)
    {
        if (vfx != null && _staticVfxRemove != null) _staticVfxRemove(vfx);
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct VfxStruct
    {
        [FieldOffset(0x38)] public byte Flags;
        [FieldOffset(0x50)] public Vector3 Position;
        [FieldOffset(0x60)] public Quaternion Rotation;
        [FieldOffset(0x70)] public Vector3 Scale;
        [FieldOffset(0x128)] public int ActorCaster;
        [FieldOffset(0x130)] public int ActorTarget;
        [FieldOffset(0x1B8)] public int StaticCaster;
        [FieldOffset(0x1C0)] public int StaticTarget;
        [FieldOffset(0x248)] public byte SomeFlags;
        [FieldOffset(0x260)] public float Red;
        [FieldOffset(0x264)] public float Green;
        [FieldOffset(0x268)] public float Blue;
        [FieldOffset(0x26C)] public float Alpha;
    }
}

internal interface IQueueAction;
internal sealed record AddQueueAction(Guid Id, string Path, Vector3 Position, Quaternion Rotation) : IQueueAction;
internal sealed record RemoveQueueAction(Guid Id) : IQueueAction;
internal sealed record RemoveRawQueueAction(nint Pointer) : IQueueAction;

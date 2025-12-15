using Dalamud.Hooking;
using Dalamud.Utility.Signatures;
using System;
using System.IO;

namespace BeastieBuddy.VfxSystem;

internal unsafe class VfxReplacer : IDisposable
{
    private delegate byte ReadSqPackDelegate(void* resourceManager, SeFileDescriptor* pFileDesc, int priority, bool isSync);

    [Signature("40 56 41 56 48 83 EC 28 0F BE 02", DetourName = nameof(ReadSqPackDetour))]
    private Hook<ReadSqPackDelegate>? _readSqPackHook;

    [Signature("48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 41 54 41 55 41 56 41 57 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 84 24 ?? ?? ?? ?? 48 63 42 28")]
    private delegate* unmanaged<void*, SeFileDescriptor*, int, bool, byte> _readFile;

    private readonly string _localVfxPath;

    internal VfxReplacer(string localVfxPath)
    {
        _localVfxPath = localVfxPath;
        // Use static GameInteropProvider
        BeastieBuddy.Plugin.GameInteropProvider.InitializeFromAttributes(this);
        _readSqPackHook?.Enable();
    }

    public void Dispose()
    {
        _readSqPackHook?.Dispose();
    }

    private byte ReadSqPackDetour(void* resourceManager, SeFileDescriptor* fileDescriptor, int priority, bool isSync)
    {
        try
        {
            return ReadSqPackDetourInner(resourceManager, fileDescriptor, priority, isSync);
        }
        catch (Exception ex)
        {
            BeastieBuddy.Plugin.Log.Error(ex, "Error in ReadSqPackDetour");
            return _readSqPackHook!.Original(resourceManager, fileDescriptor, priority, isSync);
        }
    }

    private byte ReadSqPackDetourInner(void* resourceManager, SeFileDescriptor* fileDescriptor, int priority, bool isSync)
    {
        if (fileDescriptor == null || fileDescriptor->ResourceHandle == null)
        {
            return _readSqPackHook!.Original(resourceManager, fileDescriptor, priority, isSync);
        }

        var fileName = fileDescriptor->ResourceHandle->FileName;
        if (fileName.BasicString.First == null)
        {
            return _readSqPackHook!.Original(resourceManager, fileDescriptor, priority, isSync);
        }

        var path = fileName.ToString();

        // Check against BeaconController's replacements
        if (BeaconController.Replacements.TryGetValue(path, out string? replacementPath))
        {
            BeastieBuddy.Plugin.Log.Debug($"Replacing VFX path {path} with {replacementPath}");

            // Access the path from the local field
            var p = Path.Join(_localVfxPath, replacementPath);

            return DefaultRootedResourceLoad(p, resourceManager, fileDescriptor, priority, isSync);
        }

        return _readSqPackHook!.Original(resourceManager, fileDescriptor, priority, isSync);
    }

    private byte DefaultRootedResourceLoad(string gamePath, void* resourceManager, SeFileDescriptor* fileDescriptor, int priority, bool isSync)
    {
        fileDescriptor->FileMode = FileMode.LoadUnpackedResource;

        var fd = stackalloc byte[0x20 + 2 * gamePath.Length + 0x16];
        fileDescriptor->FileDescriptor = fd;
        var fdPtr = (char*)(fd + 0x21);
        for (var i = 0; i < gamePath.Length; ++i)
        {
            (&fileDescriptor->Utf16FileName)[i] = gamePath[i];
            fdPtr[i] = gamePath[i];
        }

        (&fileDescriptor->Utf16FileName)[gamePath.Length] = '\0';
        fdPtr[gamePath.Length] = '\0';

        // Use the SE ReadFile function.
        return _readFile(resourceManager, fileDescriptor, priority, isSync);
    }
}

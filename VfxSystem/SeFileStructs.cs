using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using System.Runtime.InteropServices;

namespace BeastieBuddy.VfxSystem;

public enum FileMode : uint
{
    LoadUnpackedResource = 0,
    LoadFileResource = 1 // Default
}

// Required Struct for File Descriptor
[StructLayout(LayoutKind.Explicit)]
internal unsafe struct SeFileDescriptor
{
    [FieldOffset(0x00)]
    public FileMode FileMode;

    [FieldOffset(0x30)]
    public void* FileDescriptor;

    [FieldOffset(0x50)]
    public ResourceHandle* ResourceHandle;

    [FieldOffset(0x70)]
    public char Utf16FileName;
}

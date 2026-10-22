// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Native MEMORYSTATUSEX layout filled by kernel32 GlobalMemoryStatusEx. Length must be set to the struct size before
/// the call; AvailablePhysicalBytes is the physical memory that can be given to a process without paging.
/// </summary>
using System.Runtime.InteropServices;

namespace Klacks.Updater.Infrastructure;

[StructLayout(LayoutKind.Sequential)]
internal struct MemoryStatusEx
{
    public uint Length;
    public uint MemoryLoad;
    public ulong TotalPhysicalBytes;
    public ulong AvailablePhysicalBytes;
    public ulong TotalPageFileBytes;
    public ulong AvailablePageFileBytes;
    public ulong TotalVirtualBytes;
    public ulong AvailableVirtualBytes;
    public ulong AvailableExtendedVirtualBytes;
}

// SPDX-License-Identifier: GPL-3.0-only
namespace KcdMp.Client;

/// <summary>The relay's room contract or identity check refused this install (protocol v12, 0x47). Fatal for the attempt: reconnecting cannot change the payload.</summary>
public sealed class RoomRefusedException(string reason) : Exception("The room refused this install: " + reason)
{
    public string Reason { get; } = reason;
}

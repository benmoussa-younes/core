namespace Core.Platform.Linux;

/// <summary>
/// The process, user and group identifiers the kernel reports for the peer of a
/// Unix domain socket (<c>struct ucred</c>).
/// </summary>
/// <param name="ProcessId">The peer's process identifier.</param>
/// <param name="UserId">The peer's effective user identifier.</param>
/// <param name="GroupId">The peer's effective group identifier.</param>
public readonly record struct UnixPeerCredentials(int ProcessId, uint UserId, uint GroupId);

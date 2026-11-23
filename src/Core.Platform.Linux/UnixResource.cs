namespace Core.Platform.Linux;

/// <summary>A kernel resource limit (<c>RLIMIT_*</c>).</summary>
public enum UnixResource
{
    /// <summary>Total CPU time in seconds (<c>RLIMIT_CPU</c>, 0).</summary>
    Cpu = 0,

    /// <summary>Largest file a process may create (<c>RLIMIT_FSIZE</c>, 1).</summary>
    FileSize = 1,

    /// <summary>Largest data segment (<c>RLIMIT_DATA</c>, 2).</summary>
    Data = 2,

    /// <summary>Largest stack (<c>RLIMIT_STACK</c>, 3).</summary>
    Stack = 3,

    /// <summary>Largest core dump (<c>RLIMIT_CORE</c>, 4).</summary>
    Core = 4,

    /// <summary>Largest number of processes for the real user (<c>RLIMIT_NPROC</c>, 6).</summary>
    Processes = 6,

    /// <summary>Largest number of open files (<c>RLIMIT_NOFILE</c>, 7).</summary>
    NoFile = 7,

    /// <summary>Largest locked-in-memory address space (<c>RLIMIT_MEMLOCK</c>, 8).</summary>
    MemLock = 8,

    /// <summary>Largest address space (<c>RLIMIT_AS</c>, 9).</summary>
    AddressSpace = 9,
}

namespace Core.Platform.Linux;

/// <summary>
/// Describes one child process to start without a shell.
/// </summary>
/// <param name="Program">The absolute program path; it also becomes the child's <c>argv[0]</c>.</param>
/// <param name="Arguments">The arguments after <c>argv[0]</c>; empty when the program takes none.</param>
/// <param name="WorkingDirectory">The absolute working directory of the child.</param>
/// <param name="Environment">The explicit environment; the parent's environment is not inherited.</param>
/// <param name="Descriptors">The parent-to-child descriptor mappings; <see langword="null"/> means none.</param>
/// <param name="Terminal">
/// The pseudo-terminal whose slave becomes the child's descriptors 0, 1 and 2 and
/// its controlling terminal; <see langword="null"/> means none, and a terminal
/// request cannot map those three descriptors.
/// </param>
public sealed record UnixSpawnRequest(
    string Program,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment,
    IReadOnlyList<UnixDescriptorMapping>? Descriptors = null,
    UnixPseudoTerminal? Terminal = null);

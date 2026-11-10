namespace Wip.Tholdem.Agents.Runtime;

/// <summary>
/// Selects which surface carries the authorized option-A tool invitation. The default,
/// <see cref="System"/>, reproduces the original system-prompt-only behaviour byte for byte.
/// </summary>
public enum TholdemToolPromptMode
{
    /// <summary>The base system prompt only; no tool invitation on either surface.</summary>
    None = 0,

    /// <summary>The invitation stays in the system prompt and the task text is unchanged (the default).</summary>
    System = 1,

    /// <summary>The base system prompt plus exactly one optional invitation line in the task text.</summary>
    Task = 2,

    /// <summary>The invitation in the system prompt plus exactly one optional invitation line in the task text.</summary>
    Both = 3,

    /// <summary>The invitation in the system prompt plus a mandatory tool-call line in the task text.</summary>
    BothRequired = 4,
}

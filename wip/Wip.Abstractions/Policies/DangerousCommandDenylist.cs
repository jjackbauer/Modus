namespace Wip.Abstractions.Policies;

public sealed record DangerousCommandMatch(string RuleId, string Pattern);

public static class DangerousCommandDenylist
{
    private const string GitInternalsMutationRuleId = "git-internals-mutation";
    private const string GitInternalsMutationPattern = ".git/<internals>";

    private static readonly string[] GitInternalsMutationSignals =
    [
        "remove-item",
        "rm ",
        "del ",
        "erase ",
        "rmdir ",
        "rd ",
        "copy ",
        "cp ",
        "move ",
        "mv ",
        "git update-ref",
        "git symbolic-ref",
        ">",
        ">>"
    ];

    // Ordered list ensures deterministic first-match behavior.
    private static readonly DangerousRule[] Rules =
    [
        new("unix-rm-rf", "rm -rf"),
        new("windows-del-force", "del /f /q"),
        new("windows-erase-force", "erase /f /q"),
        new("windows-rmdir-force", "rmdir /s /q"),
        new("windows-rd-force", "rd /s /q"),
        new("powershell-remove-item-force", "remove-item -recurse -force"),
        new("git-push", "git push"),
        new("git-clean-force", "git clean -fdx"),
        new("git-reset-hard", "git reset --hard"),
        new("linux-mkfs", "mkfs"),
        new("windows-format", "format "),
        new("linux-dd-destructive", "dd if="),
        new("linux-wipefs", "wipefs -a"),
        new("windows-diskpart-script", "diskpart /s"),
        new("linux-fork-bomb", ":(){:|:&};:"),
        new("system-shutdown", "shutdown"),
        new("system-reboot", "reboot"),
        new("system-halt", "halt"),
        new("system-poweroff", "poweroff"),
        new("system-init-0", "init 0"),
        new("system-init-6", "init 6")
    ];

    public static bool TryMatch(string command, out DangerousCommandMatch match)
    {
        if (string.IsNullOrWhiteSpace(command))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(command));

        foreach (var rule in Rules)
        {
            if (!command.Contains(rule.Pattern, StringComparison.OrdinalIgnoreCase))
                continue;

            match = new DangerousCommandMatch(rule.RuleId, rule.Pattern);
            return true;
        }

        if (LooksLikeGitInternalsMutation(command))
        {
            match = new DangerousCommandMatch(GitInternalsMutationRuleId, GitInternalsMutationPattern);
            return true;
        }

        match = default!;
        return false;
    }

    public static string BuildPolicyBlockedReason(DangerousCommandMatch match)
    {
        ArgumentNullException.ThrowIfNull(match);
        return $"Blocked by local-safe policy: dangerous command rule '{match.RuleId}' matched pattern '{match.Pattern}' and was denied before execution.";
    }

    public static string BuildToolBlockedReason(DangerousCommandMatch match)
    {
        ArgumentNullException.ThrowIfNull(match);
        return $"Blocked by controlled shell tool: dangerous command rule '{match.RuleId}' matched pattern '{match.Pattern}' and was denied before execution.";
    }

    private static bool LooksLikeGitInternalsMutation(string command)
    {
        var referencesGitInternals = command.Contains(".git/", StringComparison.OrdinalIgnoreCase)
            || command.Contains(".git\\", StringComparison.OrdinalIgnoreCase);

        if (!referencesGitInternals)
            return false;

        foreach (var signal in GitInternalsMutationSignals)
        {
            if (command.Contains(signal, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private sealed record DangerousRule(string RuleId, string Pattern);
}
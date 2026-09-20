using System.Text;

namespace Gitland.Core;

/// <summary>One line of a rebase plan. Order in the list is the order Git will replay them.</summary>
/// <param name="Action">pick, reword, edit, squash, fixup or drop.</param>
/// <param name="Message">A replacement subject for reword; ignored otherwise.</param>
public sealed record RebaseStep(string Hash, string Subject, string Action = "pick", string Message = "");

public sealed partial class GitRepository {
    static readonly string[] RebaseActions = ["pick", "reword", "edit", "squash", "fixup", "drop"];

    /// <summary>Lists the commits between <paramref name="upstream"/> and HEAD as an editable plan,
    /// oldest first - the order git rebase -i presents and replays them in.</summary>
    public async Task<IReadOnlyList<RebaseStep>> ReadRebasePlanAsync(string upstream) {
        string target = await ResolveRef(upstream);
        string output = await Git("log", "--reverse", "--format=%H%x00%s%x00", target + "..HEAD");
        var fields = output.Split('\0');
        var steps = new List<RebaseStep>();
        for (int i = 0; i + 1 < fields.Length; i += 2) steps.Add(new(fields[i].Trim(), fields[i + 1]));
        if (steps.Count == 0) throw new InvalidOperationException("There are no commits between that revision and HEAD.");
        return steps;
    }

    /// <summary>Replays the plan. Reordering, squashing and dropping all happen here; a plan whose
    /// first step squashes has nothing to squash into and is rejected before anything runs.</summary>
    public async Task<GitOperationResult> RebaseInteractiveAsync(string upstream, IReadOnlyList<RebaseStep> plan, string expectedHead) {
        await RequireClean(); await CheckHead(expectedHead);
        if (plan.Count == 0) throw new InvalidOperationException("The rebase plan is empty.");
        foreach (var step in plan) {
            if (!RebaseActions.Contains(step.Action)) throw new InvalidOperationException($"'{step.Action}' is not a rebase action.");
            if (step.Hash.Length != 40 || !step.Hash.All(Uri.IsHexDigit)) throw new InvalidOperationException("The rebase plan holds an invalid commit.");
        }
        if (plan.All(s => s.Action == "drop")) throw new InvalidOperationException("A rebase must keep at least one commit.");
        var kept = plan.Where(s => s.Action != "drop").ToArray();
        if (kept[0].Action is "squash" or "fixup") throw new InvalidOperationException("The first commit has nothing to combine with. Move it down or pick it.");

        string target = await ResolveRef(upstream);
        var original = await ReadRebasePlanAsync(upstream);
        var known = original.Select(s => s.Hash).ToHashSet(StringComparer.Ordinal);
        foreach (var step in plan) if (!known.Contains(step.Hash)) throw new InvalidOperationException("The plan refers to a commit outside this rebase. Refresh and try again.");

        var todo = new StringBuilder();
        foreach (var step in plan) {
            if (step.Action == "drop") continue;
            todo.Append(step.Action).Append(' ').Append(step.Hash).Append('\n');
            // reword still opens the message editor, so supply the new message up front instead.
            if (step.Action == "reword" && step.Message.Trim().Length > 0)
                todo.Append("exec git commit --amend --only --file=").Append(await WriteMessageFileAsync(step.Message)).Append('\n');
        }

        string recovery = await RecoveryRef(expectedHead, "rebase");
        string todoPath = Path.Combine(Path.GetTempPath(), "gitland-rebase-" + Guid.NewGuid().ToString("N") + ".todo");
        await File.WriteAllTextAsync(todoPath, todo.ToString(), new UTF8Encoding(false));
        try {
            // Git runs the sequence editor through its bundled shell, so `cp <plan>` receives the
            // todo file as its second argument and replaces it wholesale. No editor ever opens.
            var environment = new Dictionary<string, string> {
                ["GIT_SEQUENCE_EDITOR"] = "cp " + ShellQuote(todoPath),
                ["GIT_EDITOR"] = "true",
            };
            var arguments = new[] { "-c", "core.editor=true", "rebase", "-i", "--no-autosquash", target };
            var result = await RunResultAsync(arguments, timeout: 300, environment: environment);
            if (result.ExitCode == 0) return new(true, "Rebase completed.", recovery);
            string operation = await OperationAsync();
            if (operation != "" || (await ReadStateAsync()).Changes.Any(c => c.IsConflict))
                return new(false, "Rebase stopped. Resolve the files, then Continue.", recovery);
            throw new CommandFailedException(result.Error.Trim(), result.ExitCode);
        } finally { try { File.Delete(todoPath); } catch (IOException) { } }
    }

    async Task<string> WriteMessageFileAsync(string message) {
        string path = Path.Combine(Path.GetTempPath(), "gitland-message-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(path, message.Trim() + "\n", new UTF8Encoding(false));
        return ShellQuote(path);
    }

    /// <summary>Quotes a path for the POSIX shell Git uses to run editor commands, on every platform.</summary>
    static string ShellQuote(string path) => "'" + path.Replace('\\', '/').Replace("'", "'\\''") + "'";
}

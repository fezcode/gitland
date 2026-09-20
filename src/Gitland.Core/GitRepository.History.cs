namespace Gitland.Core;

/// <summary>A history request. Empty filters are omitted, so the default is the plain branch log.</summary>
/// <param name="Path">Restrict to commits touching this file, giving a file's history.</param>
/// <param name="Author">Match the author name or email, case-insensitively.</param>
/// <param name="Text">Match the commit message, case-insensitively.</param>
/// <param name="Revision">Where to start; defaults to HEAD.</param>
public sealed record HistoryQuery(string Path = "", string Author = "", string Text = "", string Revision = "", bool AllBranches = false, bool FollowRenames = true, int Limit = 200);

public sealed record BlameLine(string Hash, string ShortHash, string Author, string Date, int Number, string Text);
public sealed record ReflogEntry(string Selector, string Hash, string ShortHash, string Action, string Subject);

public sealed partial class GitRepository {
    /// <summary>Runs a filtered log. Filtering happens in Git rather than over an already-loaded page,
    /// so a match older than the display limit is still found.</summary>
    public async Task<IReadOnlyList<GitCommit>> ReadHistoryAsync(HistoryQuery query) {
        if (!await HasHead()) return [];
        var arguments = new List<string> { "log", "-" + Math.Clamp(query.Limit, 1, 5000), "--topo-order", "--date=iso-strict", "--format=%H%x00%h%x00%an%x00%aI%x00%s%x00%D%x00%P%x00" };
        if (query.AllBranches) arguments.AddRange(["--branches", "--remotes", "--tags"]);
        if (query.Author.Length > 0) { arguments.Add("--regexp-ignore-case"); arguments.Add("--author=" + query.Author); }
        if (query.Text.Length > 0) { arguments.Add("--regexp-ignore-case"); arguments.Add("--grep=" + query.Text); }
        // --follow needs exactly one pathspec, and --topo-order across every branch fights it.
        bool follow = query.FollowRenames && query.Path.Length > 0 && !query.AllBranches;
        if (follow) arguments.Add("--follow");
        arguments.Add(query.Revision.Length > 0 ? await ResolveRef(query.Revision) : "HEAD");
        arguments.Add("--");
        if (query.Path.Length > 0) { ValidatePath(query.Path); arguments.Add(query.Path); }
        var fields = (await Git(arguments.ToArray())).Split('\0');
        var commits = new List<GitCommit>();
        for (int i = 0; i + 6 < fields.Length; i += 7) commits.Add(new(fields[i].Trim(), fields[i + 1], fields[i + 2], fields[i + 3], fields[i + 4], fields[i + 5], fields[i + 6]));
        return commits;
    }

    /// <summary>Attributes each line of a file to the commit that last changed it.</summary>
    public async Task<IReadOnlyList<BlameLine>> BlameAsync(string path, string revision = "") {
        ValidatePath(path);
        string target = revision.Length > 0 ? await ResolveRef(revision) : "HEAD";
        // --line-porcelain repeats the full header for every line, so no state carries between
        // groups: each content line is emitted with the header immediately above it.
        string output = await Git("blame", "--line-porcelain", target, "--", path);
        var lines = new List<BlameLine>();
        string hash = "", author = "", date = ""; int number = 0;
        foreach (string raw in output.Split('\n')) {
            string line = raw.TrimEnd('\r');
            if (line.StartsWith('\t')) { lines.Add(new(hash, hash.Length >= 8 ? hash[..8] : hash, author, date, number, line[1..])); continue; }
            if (line.StartsWith("author ")) { author = line[7..]; continue; }
            // Porcelain always reports the time as a Unix epoch, whatever --date is set to.
            if (line.StartsWith("author-time ") && long.TryParse(line[12..], out long epoch)) { date = DateTimeOffset.FromUnixTimeSeconds(epoch).ToString("yyyy-MM-dd"); continue; }
            // A group header is "<40-hex> <original line> <final line> [group size]".
            var parts = line.Split(' ');
            if (parts.Length >= 3 && parts[0].Length == 40 && int.TryParse(parts[2], out int final)) { hash = parts[0]; number = final; }
        }
        return lines;
    }

    /// <summary>Reads Git's own reflog, which still records where HEAD has been after a reset.</summary>
    public async Task<IReadOnlyList<ReflogEntry>> ReadReflogAsync(string reference = "HEAD", int limit = 200) {
        if (!await HasHead()) return [];
        if (reference != "HEAD") ValidateRefName(reference);
        var entries = new List<ReflogEntry>();
        var result = await RunResultAsync(["reflog", "show", "-" + Math.Clamp(limit, 1, 2000), "--format=%gd%x00%H%x00%h%x00%gs%x00", reference]);
        if (result.ExitCode != 0) return [];
        var fields = result.Output.Split('\0');
        for (int i = 0; i + 3 < fields.Length; i += 4) {
            string subject = fields[i + 3];
            // "checkout: moving from a to b" - the action is the part before the colon.
            int colon = subject.IndexOf(':');
            entries.Add(new(fields[i].Trim(), fields[i + 1], fields[i + 2], colon > 0 ? subject[..colon] : subject, colon > 0 ? subject[(colon + 1)..].Trim() : ""));
        }
        return entries;
    }
}

using Gitland.Core;

namespace Gitland.App;

public static class Demo {
    public const string Original = """
import { Repository } from './repository';
import { DiffOptions, DiffResult } from './types';
import { parsePatch } from './parser';

/**
 * Build a comparison between two revisions.
 * Keep the source content intact for staging.
 */
export class DiffService {
  private readonly repository: Repository;

  constructor(repository: Repository) {
    this.repository = repository;
  }

  async compare(options: DiffOptions): Promise<DiffResult> {
    const { base, target } = options;
    const contextLines = 3;

    const patch = await this.repository.diff({
      base,
      target,
      context: contextLines,
    });

    const files = parsePatch(patch);

    return {
      files,
      base,
      target,
    };
  }

  async getFileContent(path: string, revision: string) {
    return this.repository.readFile(path, revision);
  }

  async stageFile(path: string): Promise<void> {
    await this.repository.stage(path);
  }

  async refresh(): Promise<void> {
    await this.repository.refresh();
  }
}
""";
    public static readonly string Updated = Original
        .Replace("const { base, target } = options;", "const { base, target, ignoreWhitespace } = options;")
        .Replace("const contextLines = 3;", "const contextLines = options.contextLines ?? 5;")
        .Replace("      context: contextLines,", "      context: contextLines,\n      ignoreWhitespace,\n      detectRenames: true,")
        .Replace("    const files = parsePatch(patch);", "    const files = parsePatch(patch);\n    const stats = this.summarize(files);")
        .Replace("      files,", "      files,\n      stats,")
        .Replace("  async refresh(): Promise<void> {", "  async stageHunk(path: string, hunkId: string): Promise<void> {\n    const hunk = await this.repository.getHunk(path, hunkId);\n    await this.repository.applyToIndex(hunk.patch);\n  }\n\n  async refresh(): Promise<void> {");

    public static readonly IReadOnlyList<GitChange> Changes = [
        new("src/core/diff-service.ts", null, ' ', 'M'),
        new("src/core/merge.ts", null, 'U', 'U'),
        new("src/components/change-map.tsx", null, '?', '?'),
        new("src/styles/tokens.css", null, 'M', ' '),
        new("README.md", null, ' ', 'M')
    ];
    public static FileComparison Comparison(string path, bool staged = false) => path switch {
        "src/core/diff-service.ts" => new(path, Original, Updated, "Index", "Working tree"),
        "src/components/change-map.tsx" => new(path, "", "export function ChangeMap({ changes, onNavigate }) {\n  return (\n    <nav aria-label=\"Change overview\">\n      {changes.map(change => (\n        <button\n          key={change.id}\n          onClick={() => onNavigate(change.line)}\n        >\n          {change.kind}\n        </button>\n      ))}\n    </nav>\n  );\n}\n", "Index", "Working tree"),
        "src/styles/tokens.css" => new(path, ":root {\n  --background: #202124;\n  --accent: #4f8cff;\n  --radius: 4px;\n}\n", ":root {\n  --background: #14161a;\n  --accent: #8aa7ff;\n  --radius: 6px;\n  --font-ui: 'IBM Plex Sans';\n  --font-code: 'Geist Mono';\n}\n", "HEAD", "Index · staged"),
        _ => new(path, "# Gitland\n\nA workspace for reviewing code.\n", "# Gitland\n\nA workspace for reviewing code.\n\n## Review with context\n\nCompare revisions, stage individual hunks, and resolve conflicts.\n", "Index", "Working tree")
    };
    public const string MergeText = """
import { MergeOptions } from './types';

export function resolveMerge(options: MergeOptions) {
<<<<<<< HEAD
  const config = { strategy: 'interactive', keepBackup: false };
||||||| base
  const config = { strategy: 'manual', keepBackup: false };
=======
  const config = { strategy: 'manual', keepBackup: true };
>>>>>>> feature/smart-merge

  const result = applyStrategy(config.strategy, options);

<<<<<<< HEAD
  return { result, backup: config.keepBackup };
||||||| base
  return result;
=======
  return { result, resolved: true };
>>>>>>> feature/smart-merge
}
""";
    public static MergeFile Merge() {
        var doc = MergeDocument.Parse(MergeText);
        foreach (var c in doc.Conflicts) c.Choice = Resolution.Ours;
        var ours = doc.Render();
        foreach (var c in doc.Conflicts) c.Choice = Resolution.Theirs;
        var theirs = doc.Render();
        foreach (var c in doc.Conflicts) c.Choice = Resolution.Base;
        var ancestor = doc.Render();
        foreach (var c in doc.Conflicts) c.Choice = Resolution.Unresolved;
        return new("src/core/merge.ts", ancestor, ours, theirs, new(MergeText, "demo", false, "\n"), doc);
    }
    public static ManagementState Management() => new([
        new("a97bda84b617c822a7446229c5bc2a4f16651aca", "a97bda8", "Alex Morgan", "2026-09-15", "Add precise hunk staging and change navigation", "HEAD → feature/refine-diff"),
        new("e043f803b617c822a7446229c5bc2a4f16651aca", "e043f80", "Alex Morgan", "2026-09-14", "Refine the Xcode Dark workspace", "tag: v0.1.0"),
        new("b051f803b617c822a7446229c5bc2a4f16651aca", "b051f80", "Sam Chen", "2026-09-13", "Preserve file encoding during merge saves", "main")
    ], [new("feature/refine-diff", "a97bda8", "origin/feature/refine-diff", true), new("main", "b051f80", "origin/main", false)], [new("v0.1.0", "e043f803b617c822a7446229c5bc2a4f16651aca", "First native review workspace")], [], "sample", "a97bda84b617c822a7446229c5bc2a4f16651aca", false);
    public static WorkspaceFixture Fixture => new(new("Fixture repository", "feature/refine-diff", Changes, ["main", "feature/refine-diff"], [..Changes, new("LICENSE", null, ' ', ' '), new("src/core/types.ts", null, ' ', ' ')]), Comparison, Merge, Management());
}

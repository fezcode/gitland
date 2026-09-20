using Gitland.Core;
namespace Gitland.App;

// The offscreen validation host supplies fixtures. Production startup supplies none.
public sealed record WorkspaceFixture(RepositoryState State, Func<string, bool, FileComparison> Compare, Func<MergeFile> Merge, ManagementState Management);

namespace Josha.Models.Git
{
    internal sealed record GitBranch(string Name, bool IsRemote, bool IsCurrent)
    {
        public string DisplayName => IsRemote ? Name["remotes/".Length..] : Name;
    }
}

using Josha.Models.Git;
using System.Collections.Generic;

namespace Josha.Business.Git
{
    // Assigns each commit a lane and the connectors that link it to its
    // parents, the way `git log --graph` does. Commits must be ordered so
    // every parent comes after all of its children (--date-order gives that).
    internal static class GitGraphLayout
    {
        private sealed class LaneState
        {
            public readonly List<string?> ExpectedHash = new();
            public readonly List<int> Color = new();
            public int NextColor;

            public int IndexOf(string hash) => ExpectedHash.IndexOf(hash);

            public int Allocate(string hash)
            {
                var free = ExpectedHash.IndexOf(null);
                if (free < 0)
                {
                    ExpectedHash.Add(hash);
                    Color.Add(NextColor++);
                    return ExpectedHash.Count - 1;
                }

                ExpectedHash[free] = hash;
                Color[free] = NextColor++;
                return free;
            }
        }

        internal static int Apply(IReadOnlyList<GitCommit> commits)
        {
            var lanes = new LaneState();
            var maxLanes = 0;

            foreach (var commit in commits)
            {
                commit.Graph = LayoutRow(commit, lanes);
                if (lanes.ExpectedHash.Count > maxLanes) maxLanes = lanes.ExpectedHash.Count;
            }

            return maxLanes;
        }

        private static GitGraphRow LayoutRow(GitCommit commit, LaneState lanes)
        {
            var nodeLane = lanes.IndexOf(commit.Hash);
            if (nodeLane < 0) nodeLane = lanes.Allocate(commit.Hash);

            var row = new GitGraphRow { NodeLane = nodeLane, ColorIndex = lanes.Color[nodeLane] };

            CollectIncoming(commit, row, lanes);
            CollectPassThrough(row, lanes);
            ConnectParents(commit, row, lanes);

            return row;
        }

        private static void CollectIncoming(GitCommit commit, GitGraphRow row, LaneState lanes)
        {
            for (var lane = 0; lane < lanes.ExpectedHash.Count; lane++)
            {
                if (lane == row.NodeLane || lanes.ExpectedHash[lane] != commit.Hash) continue;

                row.Incoming.Add(new GitGraphLink(lane, lanes.Color[lane]));
                lanes.ExpectedHash[lane] = null;
            }
        }

        private static void CollectPassThrough(GitGraphRow row, LaneState lanes)
        {
            for (var lane = 0; lane < lanes.ExpectedHash.Count; lane++)
            {
                if (lane == row.NodeLane || lanes.ExpectedHash[lane] == null) continue;
                row.PassThrough.Add(new GitGraphLink(lane, lanes.Color[lane]));
            }
        }

        private static void ConnectParents(GitCommit commit, GitGraphRow row, LaneState lanes)
        {
            if (commit.ParentHashes.Count == 0)
            {
                lanes.ExpectedHash[row.NodeLane] = null;
                return;
            }

            ConnectFirstParent(commit.ParentHashes[0], row, lanes);
            for (var i = 1; i < commit.ParentHashes.Count; i++)
                ConnectExtraParent(commit.ParentHashes[i], row, lanes);
        }

        // The first parent continues in the node's own lane unless another
        // lane already waits for it, in which case this lane ends here and
        // the node joins that lane instead (a fork seen from the top).
        private static void ConnectFirstParent(string parent, GitGraphRow row, LaneState lanes)
        {
            var existing = lanes.IndexOf(parent);
            if (existing >= 0)
            {
                lanes.ExpectedHash[row.NodeLane] = null;
                row.Outgoing.Add(new GitGraphLink(existing, lanes.Color[existing]));
                return;
            }

            lanes.ExpectedHash[row.NodeLane] = parent;
            row.Outgoing.Add(new GitGraphLink(row.NodeLane, row.ColorIndex));
        }

        private static void ConnectExtraParent(string parent, GitGraphRow row, LaneState lanes)
        {
            var lane = lanes.IndexOf(parent);
            if (lane < 0) lane = lanes.Allocate(parent);

            row.Outgoing.Add(new GitGraphLink(lane, lanes.Color[lane]));
        }
    }
}

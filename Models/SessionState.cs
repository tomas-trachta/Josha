using System;
using System.Collections.Generic;

namespace Josha.Models
{
    internal sealed class WindowLayoutState
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool IsMaximized { get; set; }
    }

    internal sealed class PaneTabState
    {
        public string? LocalPath { get; set; }
        public Guid? SiteId { get; set; }
        public string? RemotePath { get; set; }
        public ViewMode ViewMode { get; set; }
    }

    internal sealed class PaneColumnState
    {
        public List<PaneTabState> Tabs { get; set; } = new();
        public int ActiveTabIndex { get; set; }
    }

    internal sealed class SessionState
    {
        public PaneColumnState Left { get; set; } = new();
        public PaneColumnState Right { get; set; } = new();
        public bool ActiveColumnIsLeft { get; set; } = true;
    }
}

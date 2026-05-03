using Josha.Business;
using Josha.Models;
using Josha.ViewModels;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Josha.Views;

public partial class TreeGraphControl : UserControl
{
    private const double NodeW = 280;
    private const double NodeH = 40;
    private const double IndentStep = 48;
    private const double RowGap = 3;
    private const double Pad = 16;
    private const double ViewBufferRows = 5;

    private const double ZoomMin = 0.1;
    private const double ZoomMax = 5.0;
    private const double ZoomStep = 1.15;
    private const double PanThreshold = 4;
    private const double ScrollStep = 50;

    #region Static frozen resources

    private static readonly Pen LinePen;
    private static readonly Brush TextBrush;
    private static readonly Brush SubtextBrush;
    private static readonly Brush DimBrush;
    private static readonly Brush NodeBackgroundBrush;
    private static readonly Brush NodeBorderBrush;
    private static readonly Brush HoverBrush;
    private static readonly Brush HighlightBrush;
    private static readonly Brush FileBodyBrush;
    private static readonly Brush FileFoldBrush;
    private static readonly Brush FolderOpenBody;
    private static readonly Brush FolderOpenTab;
    private static readonly Brush FolderClosedBody;
    private static readonly Brush FolderClosedTab;

    static TreeGraphControl()
    {
        // Aligned with Theme.xaml dark palette: OnSurface / OnSurfaceMuted / OnSurfaceSubtle
        // for text, Outline / SurfaceHover / Accent for chrome. Keeps the canvas tree
        // coherent with the surrounding WPF controls.
        TextBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xE6, 0xED, 0xF3)));
        SubtextBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0x9E)));
        DimBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x6E, 0x76, 0x81)));
        // Mirror Brush.SurfaceElevated — node cards on the dark canvas.
        NodeBackgroundBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x16, 0x1B, 0x22)));
        NodeBorderBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x30, 0x36, 0x3D)));
        HoverBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x1F, 0x25, 0x2E)));
        HighlightBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x2F, 0x81, 0xF7)));
        FileBodyBrush = Freeze(new SolidColorBrush(Color.FromRgb(144, 202, 249)));
        FileFoldBrush = Freeze(new SolidColorBrush(Color.FromRgb(66, 165, 245)));
        FolderOpenBody = Freeze(new SolidColorBrush(Color.FromRgb(255, 167, 38)));
        FolderOpenTab = Freeze(new SolidColorBrush(Color.FromRgb(255, 143, 0)));
        FolderClosedBody = Freeze(new SolidColorBrush(Color.FromRgb(255, 183, 77)));
        FolderClosedTab = Freeze(new SolidColorBrush(Color.FromRgb(255, 160, 0)));

        LinePen = new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x30, 0x36, 0x3D))), 1);
        LinePen.Freeze();
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    #endregion

    // Transforms
    private readonly ScaleTransform _zoom = new(1, 1);
    private readonly TranslateTransform _pan = new(0, 0);

    // Pan state
    private bool _isPanning;
    private bool _panStarted;
    private Point _panStart;
    private double _panOriginX, _panOriginY;

    // Layout data (rebuilt on every structural change)
    private readonly Dictionary<TreeItemViewModel, Rect> _rects = [];
    private readonly List<TreeItemViewModel> _flatNodes = [];
    private double _nextY;

    // Visual cache — persists across renders, keyed by VM identity
    private readonly Dictionary<TreeItemViewModel, UIElement> _visualCache = [];

    // Currently on-canvas node elements
    private readonly Dictionary<TreeItemViewModel, UIElement> _activeElements = [];

    // Single lightweight visual for all connector lines
    private readonly LinesHost _linesHost = new();

    // Highlight state
    private DispatcherTimer? _highlightTimer;
    private Action? _restoreHighlight;

    private readonly HashSet<TreeItemViewModel> _selectedNodes = new();

    internal IReadOnlyCollection<TreeItemViewModel> SelectedNodes => _selectedNodes;
    public event Action? SelectionChanged;

    private static readonly Brush SelectedBackgroundBrush =
        Freeze(new SolidColorBrush(Color.FromRgb(0x1B, 0x25, 0x33)));
    private static readonly Brush SelectedBorderBrush =
        Freeze(new SolidColorBrush(Color.FromRgb(0x2F, 0x81, 0xF7)));

    // Mirrors Brush.TreeBorderActive / Brush.TreeBorderInactive in Theme.xaml.
    private static readonly Brush ActiveBorderColor = Freeze(new SolidColorBrush(Color.FromRgb(0x2F, 0x81, 0xF7)));
    private static readonly Brush InactiveBorderColor = Freeze(new SolidColorBrush(Color.FromRgb(0x21, 0x26, 0x2D)));

    public static readonly DependencyProperty ShowSizesProperty =
        DependencyProperty.Register(nameof(ShowSizes), typeof(bool),
            typeof(TreeGraphControl), new PropertyMetadata(false));

    public bool ShowSizes
    {
        get => (bool)GetValue(ShowSizesProperty);
        set => SetValue(ShowSizesProperty, value);
    }

    public static readonly DependencyProperty IsScrollModeProperty =
        DependencyProperty.Register(nameof(IsScrollMode), typeof(bool),
            typeof(TreeGraphControl), new PropertyMetadata(false));

    public bool IsScrollMode
    {
        get => (bool)GetValue(IsScrollModeProperty);
        set => SetValue(IsScrollModeProperty, value);
    }

    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(nameof(IsActive), typeof(bool),
            typeof(TreeGraphControl), new PropertyMetadata(false, (d, _) =>
            {
                var ctrl = (TreeGraphControl)d;
                ctrl.ApplyActiveState();
            }));

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    private void ApplyActiveState()
    {
        ActiveBorder.BorderBrush = IsActive ? ActiveBorderColor : InactiveBorderColor;
        Viewport.Opacity = IsActive ? 1.0 : 0.5;
    }

    public static readonly DependencyProperty RootItemProperty =
        DependencyProperty.Register(nameof(RootItem), typeof(object),
            typeof(TreeGraphControl), new PropertyMetadata(null, (d, _) =>
            {
                var ctrl = (TreeGraphControl)d;
                ctrl._visualCache.Clear();
                ctrl.Render();
            }));

    internal void RefreshVisuals()
    {
        _visualCache.Clear();
        Render();
    }

    internal void PanToFirstChildren()
    {
        if (_flatNodes.Count < 2) return;

        double maxX = _flatNodes.Max(n => _rects[n].X);
        var target = _flatNodes.First(n => _rects[n].X == maxX);

        var rect = _rects[target];
        var scale = _zoom.ScaleX;
        _pan.X = Viewport.ActualWidth / 2 - (rect.X + rect.Width / 2) * scale;
        _pan.Y = Viewport.ActualHeight / 2 - (rect.Y + rect.Height / 2) * scale;
        UpdateView();
    }

    public object? RootItem
    {
        get => GetValue(RootItemProperty);
        set => SetValue(RootItemProperty, value);
    }

    private TreeItemViewModel? TypedRoot => RootItem as TreeItemViewModel;

    public TreeGraphControl()
    {
        InitializeComponent();

        TreeCanvas.LayoutTransform = _zoom;
        TreeCanvas.RenderTransform = _pan;
        TreeCanvas.Children.Add(_linesHost);

        Viewport.MouseWheel += OnMouseWheel;
        Viewport.MouseLeftButtonDown += OnViewportMouseDown;
        Viewport.MouseMove += OnViewportMouseMove;
        Viewport.MouseLeftButtonUp += OnViewportMouseUp;
        Viewport.SizeChanged += (_, _) => UpdateView();
    }

    #region Zoom & Pan

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (IsScrollMode && !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            double amount = e.Delta / 120.0 * ScrollStep;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                _pan.X += amount;
            else
                _pan.Y += amount;
            UpdateView();
            e.Handled = true;
            return;
        }

        var pos = e.GetPosition(Viewport);
        double oldScale = _zoom.ScaleX;
        double newScale = oldScale * (e.Delta > 0 ? ZoomStep : 1.0 / ZoomStep);
        newScale = Math.Clamp(newScale, ZoomMin, ZoomMax);

        double cx = (pos.X - _pan.X) / oldScale;
        double cy = (pos.Y - _pan.Y) / oldScale;

        _zoom.ScaleX = newScale;
        _zoom.ScaleY = newScale;
        _pan.X = pos.X - cx * newScale;
        _pan.Y = pos.Y - cy * newScale;

        UpdateView();
        e.Handled = true;
    }

    private void OnViewportMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (IsScrollMode) return;

        _panStart = e.GetPosition(Viewport);
        _panOriginX = _pan.X;
        _panOriginY = _pan.Y;
        _isPanning = false;
        _panStarted = true;
    }

    private void OnViewportMouseMove(object sender, MouseEventArgs e)
    {
        if (IsScrollMode || !_panStarted || e.LeftButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(Viewport);
        var dx = pos.X - _panStart.X;
        var dy = pos.Y - _panStart.Y;

        if (!_isPanning)
        {
            if (Math.Abs(dx) < PanThreshold && Math.Abs(dy) < PanThreshold) return;
            _isPanning = true;
            Viewport.CaptureMouse();
        }

        _pan.X = _panOriginX + dx;
        _pan.Y = _panOriginY + dy;
        UpdateView();
    }

    private void OnViewportMouseUp(object sender, MouseButtonEventArgs e)
    {
        _panStarted = false;
        if (_isPanning)
        {
            _isPanning = false;
            Viewport.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    #endregion

    #region Layout

    private void Render()
    {
        _rects.Clear();
        _flatNodes.Clear();

        if (TypedRoot == null)
        {
            ClearView();
            _visualCache.Clear();
            return;
        }

        _nextY = Pad;
        LayoutNode(TypedRoot, 0);

        TreeCanvas.Width = _flatNodes.Count > 0
            ? _rects.Values.Max(r => r.Right) + Pad
            : 0;
        TreeCanvas.Height = _nextY + Pad;

        RedrawLines();
        UpdateView();
    }

    private void LayoutNode(TreeItemViewModel node, int depth)
    {
        double x = Pad + depth * IndentStep;
        _rects[node] = new Rect(x, _nextY, NodeW, NodeH);
        _flatNodes.Add(node);
        _nextY += NodeH + RowGap;

        if (node.IsExpanded && HasVisibleChildren(node))
        {
            foreach (var child in node.Children)
                LayoutNode(child, depth + 1);
        }
    }

    private static bool HasVisibleChildren(TreeItemViewModel node)
    {
        return node.Children.Count > 0 &&
               !(node.Children.Count == 1 && string.IsNullOrEmpty(node.Children[0].DisplayName));
    }

    #endregion

    #region Viewport virtualization

    private Rect GetViewportCanvasRect()
    {
        double w = Viewport.ActualWidth;
        double h = Viewport.ActualHeight;
        if (w <= 0 || h <= 0) return Rect.Empty;

        double scale = _zoom.ScaleX;
        return new Rect(
            -_pan.X / scale,
            -_pan.Y / scale,
            w / scale,
            h / scale);
    }

    /// <summary>
    /// Binary search for the first index in _flatNodes whose node.Bottom > y.
    /// </summary>
    private int LowerBound(double y)
    {
        int lo = 0, hi = _flatNodes.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (_rects[_flatNodes[mid]].Bottom <= y)
                lo = mid + 1;
            else
                hi = mid;
        }
        return lo;
    }

    /// <summary>
    /// Binary search for the last index in _flatNodes whose node.Top &lt; y.
    /// </summary>
    private int UpperBound(double y)
    {
        int lo = -1, hi = _flatNodes.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (_rects[_flatNodes[mid]].Top >= y)
                hi = mid - 1;
            else
                lo = mid;
        }
        return lo;
    }

    private void UpdateView()
    {
        if (_flatNodes.Count == 0) return;

        var viewRect = GetViewportCanvasRect();
        if (viewRect == Rect.Empty) return;

        double buffer = (NodeH + RowGap) * ViewBufferRows;
        int start = LowerBound(viewRect.Top - buffer);
        int end = UpperBound(viewRect.Bottom + buffer);

        // Build set of nodes that should be on canvas
        var shouldBeVisible = new HashSet<TreeItemViewModel>(end - start + 2);
        for (int i = start; i <= end && i < _flatNodes.Count; i++)
            shouldBeVisible.Add(_flatNodes[i]);

        // Remove elements for nodes no longer in viewport
        var toRemove = new List<TreeItemViewModel>();
        foreach (var node in _activeElements.Keys)
        {
            if (!shouldBeVisible.Contains(node))
                toRemove.Add(node);
        }
        foreach (var node in toRemove)
        {
            TreeCanvas.Children.Remove(_activeElements[node]);
            _activeElements.Remove(node);
        }

        // Add or reposition visible nodes
        foreach (var node in shouldBeVisible)
        {
            var rect = _rects[node];
            var el = GetOrBuildVisual(node);

            // If the cached visual was invalidated and rebuilt, swap the stale element
            if (_activeElements.TryGetValue(node, out var existing) && existing != el)
            {
                TreeCanvas.Children.Remove(existing);
                _activeElements.Remove(node);
            }

            if (!_activeElements.ContainsKey(node))
            {
                TreeCanvas.Children.Add(el);
                _activeElements[node] = el;
            }

            Canvas.SetLeft(el, rect.X);
            Canvas.SetTop(el, rect.Y);
        }
    }

    private void ClearView()
    {
        foreach (var el in _activeElements.Values)
            TreeCanvas.Children.Remove(el);
        _activeElements.Clear();

        using var dc = _linesHost.RenderOpen();
        // empty draw clears previous content
    }

    #endregion

    #region Lines (single DrawingVisual — one draw call for all lines)

    private void RedrawLines()
    {
        using var dc = _linesHost.RenderOpen();
        if (TypedRoot != null)
            DrawLinesRecursive(dc, TypedRoot);
    }

    private void DrawLinesRecursive(DrawingContext dc, TreeItemViewModel node)
    {
        if (!node.IsExpanded || !HasVisibleChildren(node)) return;

        var parentRect = _rects[node];
        var children = node.Children;

        double lineX = parentRect.X + IndentStep / 2;
        double startY = parentRect.Bottom;
        var lastChildRect = _rects[children.Last()];
        double endY = lastChildRect.Y + NodeH / 2;

        // Vertical trunk
        dc.DrawLine(LinePen, new Point(lineX, startY), new Point(lineX, endY));

        // Horizontal stubs
        foreach (var child in children)
        {
            var cr = _rects[child];
            double cy = cr.Y + NodeH / 2;
            dc.DrawLine(LinePen, new Point(lineX, cy), new Point(cr.X, cy));
        }

        foreach (var child in children)
            DrawLinesRecursive(dc, child);
    }

    #endregion

    #region Nodes

    private UIElement GetOrBuildVisual(TreeItemViewModel node)
    {
        if (_visualCache.TryGetValue(node, out var cached))
            return cached;
        var visual = BuildNodeVisual(node);
        _visualCache[node] = visual;
        return visual;
    }

    private UIElement BuildNodeVisual(TreeItemViewModel node)
    {
        if (node.IsFile)
            return BuildFileNodeVisual(node);

        return BuildDirectoryNodeVisual(node);
    }

    private UIElement BuildDirectoryNodeVisual(TreeItemViewModel node)
    {
        var dirNode = (DirectoryTreeItemViewModel)node;
        bool expandable = dirNode.HasContent;
        bool expanded = dirNode.IsExpanded;

        var icon = BuildFolderIcon(expanded && expandable);

        var nameBlock = new TextBlock
        {
            Text = node.DisplayName,
            FontWeight = FontWeights.SemiBold,
            FontSize = 11.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = TextBrush,
        };

        var subtitleBlock = new TextBlock
        {
            Text = $"{dirNode.SubdirectoryCount} folders, {dirNode.FileCount} files",
            FontSize = 9,
            Foreground = DimBrush,
            Margin = new Thickness(0, 1, 0, 0),
        };

        var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        textStack.Children.Add(nameBlock);
        textStack.Children.Add(subtitleBlock);

        var sizeBlock = new TextBlock
        {
            Text = ShowSizes ? node.SizeDisplay : "",
            FontSize = 10,
            Foreground = SubtextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };

        var dock = new DockPanel { LastChildFill = true };

        DockPanel.SetDock(icon, Dock.Left);
        dock.Children.Add(icon);

        if (expandable)
        {
            var chevron = new TextBlock
            {
                Text = expanded ? "▾" : "▸",
                FontSize = 12,
                Foreground = DimBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 2, 0),
            };
            DockPanel.SetDock(chevron, Dock.Right);
            dock.Children.Add(chevron);
        }

        DockPanel.SetDock(sizeBlock, Dock.Right);
        dock.Children.Add(sizeBlock);

        dock.Children.Add(textStack);

        var border = new Border
        {
            Width = NodeW,
            Height = NodeH,
            Background = NodeBackgroundBrush,
            BorderBrush = NodeBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = dock,
            Padding = new Thickness(6, 4, 6, 4),
            Cursor = expandable ? Cursors.Hand : null,
        };

        // Block pan from starting when clicking any node
        border.MouseLeftButtonDown += (_, e) => e.Handled = true;

        border.MouseLeftButtonUp += (_, e) =>
        {
            UpdateSelection(node, ctrl: (Keyboard.Modifiers & ModifierKeys.Control) != 0);
            if (expandable)
            {
                _visualCache.Remove(node);
                node.IsExpanded = !node.IsExpanded;
                Render();
            }
            else
            {
                ApplySelectionVisuals(border, true);
            }
            e.Handled = true;
        };

        border.MouseRightButtonUp += (_, e) =>
        {
            // Right-click on a non-selected node replaces selection (Explorer).
            if (!_selectedNodes.Contains(node))
                UpdateSelection(node, ctrl: false);
            ApplySelectionVisuals(border, true);
            ShowShellMenuForSelection(e);
            e.Handled = true;
        };

        if (expandable)
        {
            border.MouseEnter += (_, _) =>
            {
                if (!_selectedNodes.Contains(node))
                    border.Background = HoverBrush;
                if (!node.IsExpanded)
                    node.PreloadChildren();
            };

            border.MouseLeave += (_, _) =>
            {
                ApplySelectionVisuals(border, _selectedNodes.Contains(node));
                if (!node.IsExpanded)
                    node.FlushPreloadedChildren();
            };
        }

        ApplySelectionVisuals(border, _selectedNodes.Contains(node));
        return border;
    }

    private UIElement BuildFileNodeVisual(TreeItemViewModel node)
    {
        var fileNode = (FileTreeItemViewModel)node;
        var icon = BuildFileIcon(fileNode.DisplayName);

        var nameBlock = new TextBlock
        {
            Text = node.DisplayName,
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = TextBrush,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var sizeBlock = new TextBlock
        {
            Text = ShowSizes ? node.SizeDisplay : "",
            FontSize = 10,
            Foreground = SubtextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };

        var dock = new DockPanel { LastChildFill = true };

        DockPanel.SetDock(icon, Dock.Left);
        dock.Children.Add(icon);

        DockPanel.SetDock(sizeBlock, Dock.Right);
        dock.Children.Add(sizeBlock);

        dock.Children.Add(nameBlock);

        var border = new Border
        {
            Width = NodeW,
            Height = NodeH,
            Background = NodeBackgroundBrush,
            BorderBrush = NodeBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = dock,
            Padding = new Thickness(6, 4, 6, 4),
        };

        border.MouseLeftButtonDown += (_, e) => e.Handled = true;

        border.MouseLeftButtonUp += (_, e) =>
        {
            UpdateSelection(node, ctrl: (Keyboard.Modifiers & ModifierKeys.Control) != 0);
            ApplySelectionVisuals(border, true);
            e.Handled = true;
        };

        border.MouseRightButtonUp += (_, e) =>
        {
            if (!_selectedNodes.Contains(node))
                UpdateSelection(node, ctrl: false);
            ApplySelectionVisuals(border, true);
            ShowShellMenuForSelection(e);
            e.Handled = true;
        };

        border.MouseEnter += (_, _) =>
        {
            if (!_selectedNodes.Contains(node))
                border.Background = HoverBrush;
        };
        border.MouseLeave += (_, _) =>
        {
            ApplySelectionVisuals(border, _selectedNodes.Contains(node));
        };

        ApplySelectionVisuals(border, _selectedNodes.Contains(node));
        return border;
    }

    private void UpdateSelection(TreeItemViewModel node, bool ctrl)
    {
        var changed = new List<TreeItemViewModel>();

        if (ctrl)
        {
            if (_selectedNodes.Add(node)) changed.Add(node);
            else if (_selectedNodes.Remove(node)) changed.Add(node);
        }
        else
        {
            foreach (var prev in _selectedNodes)
                if (prev != node) changed.Add(prev);
            _selectedNodes.Clear();
            _selectedNodes.Add(node);
            changed.Add(node);
        }

        foreach (var n in changed)
        {
            if (_activeElements.TryGetValue(n, out var el) && el is Border b)
                ApplySelectionVisuals(b, _selectedNodes.Contains(n));
        }

        SelectionChanged?.Invoke();
    }

    public void ClearSelection()
    {
        if (_selectedNodes.Count == 0) return;
        var toRefresh = _selectedNodes.ToList();
        _selectedNodes.Clear();
        foreach (var n in toRefresh)
        {
            if (_activeElements.TryGetValue(n, out var el) && el is Border b)
                ApplySelectionVisuals(b, false);
        }
        SelectionChanged?.Invoke();
    }

    private static void ApplySelectionVisuals(Border border, bool isSelected)
    {
        if (isSelected)
        {
            border.Background = SelectedBackgroundBrush;
            border.BorderBrush = SelectedBorderBrush;
        }
        else
        {
            border.Background = NodeBackgroundBrush;
            border.BorderBrush = NodeBorderBrush;
        }
    }

    private void ShowShellMenuForSelection(MouseEventArgs e)
    {
        var paths = new List<string>();
        foreach (var n in _selectedNodes)
        {
            switch (n)
            {
                case DirectoryTreeItemViewModel d when d.Model != null:
                    paths.Add(d.Model.Path);
                    break;
                case FileTreeItemViewModel f:
                    paths.Add(f.FullPath);
                    break;
            }
        }
        if (paths.Count == 0) return;

        var hwnd = (PresentationSource.FromVisual(this) as HwndSource)?.Handle ?? IntPtr.Zero;
        var screen = PointToScreen(e.GetPosition(this));
        ShellContextMenuComponent.Show(paths, hwnd, (int)screen.X, (int)screen.Y);
    }


    private static FrameworkElement BuildFolderIcon(bool isOpen)
    {
        var grid = new Grid
        {
            Width = 20,
            Height = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };

        var tab = new Border
        {
            Width = 9,
            Height = 3,
            Background = isOpen ? FolderOpenTab : FolderClosedTab,
            CornerRadius = new CornerRadius(1, 1, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(1, 0, 0, 0)
        };

        var body = new Border
        {
            Background = isOpen ? FolderOpenBody : FolderClosedBody,
            CornerRadius = new CornerRadius(0, 2, 2, 2),
            Margin = new Thickness(0, 3, 0, 0)
        };

        grid.Children.Add(body);
        grid.Children.Add(tab);
        return grid;
    }

    private static FrameworkElement BuildFileIcon(string fileName)
    {
        var style = FileIconMap.GetStyle(fileName);

        var grid = new Grid
        {
            Width = 14,
            Height = 18,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(3, 0, 9, 0)
        };

        var body = new Border
        {
            Background = style.BodyBrush,
            CornerRadius = new CornerRadius(1, 0, 1, 1),
        };

        var fold = new Border
        {
            Width = 5,
            Height = 5,
            Background = style.FoldBrush,
            CornerRadius = new CornerRadius(0, 0, 0, 1),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
        };

        grid.Children.Add(body);
        grid.Children.Add(fold);

        if (!string.IsNullOrEmpty(style.Label))
        {
            var label = new TextBlock
            {
                Text = style.Label,
                FontSize = 4.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 1.5),
            };
            grid.Children.Add(label);
        }

        return grid;
    }

    #endregion

    #region Navigation

    internal void NavigateTo(SearchResult target)
    {
        if (TypedRoot is not DirectoryTreeItemViewModel rootVm || rootVm.Model == null)
            return;

        var targetDir = target.Directory;
        var rootPath = rootVm.Model.Path.TrimEnd('\\');
        var targetPath = targetDir.Path.TrimEnd('\\');

        if (!targetPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            return;

        var relative = targetPath[rootPath.Length..].Trim('\\');
        var segments = relative.Split('\\', StringSplitOptions.RemoveEmptyEntries);

        // Expand from root down to target directory
        var currentVm = rootVm;
        EnsureExpanded(currentVm);

        foreach (var segment in segments)
        {
            var childVm = currentVm.Children
                .OfType<DirectoryTreeItemViewModel>()
                .FirstOrDefault(c => string.Equals(c.Model?.Name, segment, StringComparison.OrdinalIgnoreCase));

            if (childVm == null) return;

            EnsureExpanded(childVm);
            currentVm = childVm;
        }

        // Find the target node (file or directory)
        TreeItemViewModel targetNode = currentVm;

        if (target.ResultType == SearchResultType.File)
        {
            EnsureExpanded(currentVm);

            var fileVm = currentVm.Children
                .OfType<FileTreeItemViewModel>()
                .FirstOrDefault(f => string.Equals(f.DisplayName, target.Name, StringComparison.OrdinalIgnoreCase));

            if (fileVm != null)
                targetNode = fileVm;
        }

        // Re-render to account for expansions
        Render();

        // Pan to center the target node
        if (_rects.TryGetValue(targetNode, out var rect))
        {
            var scale = _zoom.ScaleX;
            _pan.X = Viewport.ActualWidth / 2 - (rect.X + rect.Width / 2) * scale;
            _pan.Y = Viewport.ActualHeight / 2 - (rect.Y + rect.Height / 2) * scale;
            UpdateView();

            HighlightNode(targetNode);
        }
    }

    private void EnsureExpanded(DirectoryTreeItemViewModel dirVm)
    {
        if (!dirVm.IsExpanded && dirVm.HasContent)
        {
            _visualCache.Remove(dirVm);
            dirVm.IsExpanded = true;
        }
    }

    private void HighlightNode(TreeItemViewModel node)
    {
        _highlightTimer?.Stop();
        _restoreHighlight?.Invoke();

        if (!_activeElements.TryGetValue(node, out var element) || element is not Border border)
            return;

        var originalBrush = border.BorderBrush;
        var originalThickness = border.BorderThickness;

        border.BorderBrush = HighlightBrush;
        border.BorderThickness = new Thickness(2);

        _restoreHighlight = () =>
        {
            border.BorderBrush = originalBrush;
            border.BorderThickness = originalThickness;
            _restoreHighlight = null;
        };

        _highlightTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _highlightTimer.Tick += (_, _) =>
        {
            _restoreHighlight?.Invoke();
            _highlightTimer.Stop();
        };
        _highlightTimer.Start();
    }

    #endregion

    #region LinesHost (single DrawingVisual — replaces thousands of Line elements)

    private class LinesHost : FrameworkElement
    {
        private readonly DrawingVisual _visual = new();

        public LinesHost()
        {
            AddVisualChild(_visual);
            IsHitTestVisible = false;
        }

        public DrawingContext RenderOpen() => _visual.RenderOpen();

        protected override int VisualChildrenCount => 1;
        protected override Visual GetVisualChild(int index) => _visual;
    }

    #endregion
}

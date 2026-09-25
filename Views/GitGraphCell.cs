using Josha.Models.Git;
using System.Windows;
using System.Windows.Media;

namespace Josha.Views
{
    // Draws one commit's slice of the branch graph: lines passing through,
    // lines merging into the node from above, lines leaving it towards its
    // parents below, and the node itself. Rows stack without gaps so the
    // lane lines join up into continuous branches.
    internal sealed class GitGraphCell : FrameworkElement
    {
        public const double LaneWidth = 14;
        private const double NodeRadius = 3.5;
        private const double LineThickness = 2;

        private static readonly SolidColorBrush[] Palette = CreatePalette(
            "#FF2F81F7", "#FF3FB950", "#FFDB6D28", "#FFA371F7", "#FFF778BA", "#FF39C5CF", "#FFE3B341", "#FFF85149");
        private static readonly Pen[] Pens = CreatePens(Palette);

        public static readonly DependencyProperty RowProperty = DependencyProperty.Register(
            nameof(Row), typeof(GitGraphRow), typeof(GitGraphCell),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public GitGraphRow? Row
        {
            get => (GitGraphRow?)GetValue(RowProperty);
            set => SetValue(RowProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            var row = Row;
            if (row == null) return;

            var height = ActualHeight;
            var middle = height / 2;

            DrawPassThrough(dc, row, height);
            DrawIncoming(dc, row, middle);
            DrawOutgoing(dc, row, middle, height);
            DrawNode(dc, row, middle);
        }

        private static void DrawPassThrough(DrawingContext dc, GitGraphRow row, double height)
        {
            foreach (var link in row.PassThrough)
            {
                var x = LaneCenter(link.Lane);
                dc.DrawLine(PenFor(link.ColorIndex), new Point(x, 0), new Point(x, height));
            }
        }

        private static void DrawIncoming(DrawingContext dc, GitGraphRow row, double middle)
        {
            var nodeX = LaneCenter(row.NodeLane);
            foreach (var link in row.Incoming)
                DrawCurve(dc, PenFor(link.ColorIndex), new Point(LaneCenter(link.Lane), 0), new Point(nodeX, middle));
        }

        private static void DrawOutgoing(DrawingContext dc, GitGraphRow row, double middle, double height)
        {
            var nodeX = LaneCenter(row.NodeLane);
            foreach (var link in row.Outgoing)
                DrawCurve(dc, PenFor(link.ColorIndex), new Point(nodeX, middle), new Point(LaneCenter(link.Lane), height));
        }

        private static void DrawNode(DrawingContext dc, GitGraphRow row, double middle)
        {
            var center = new Point(LaneCenter(row.NodeLane), middle);
            dc.DrawEllipse(BrushFor(row.ColorIndex), null, center, NodeRadius, NodeRadius);
        }

        private static void DrawCurve(DrawingContext dc, Pen pen, Point from, Point to)
        {
            if (from.X == to.X)
            {
                dc.DrawLine(pen, from, to);
                return;
            }

            var midY = (from.Y + to.Y) / 2;
            var figure = new PathFigure { StartPoint = from };
            figure.Segments.Add(new BezierSegment(new Point(from.X, midY), new Point(to.X, midY), to, true));

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            geometry.Freeze();

            dc.DrawGeometry(null, pen, geometry);
        }

        private static double LaneCenter(int lane) => lane * LaneWidth + LaneWidth / 2;

        private static SolidColorBrush BrushFor(int colorIndex) => Palette[colorIndex % Palette.Length];

        private static Pen PenFor(int colorIndex) => Pens[colorIndex % Pens.Length];

        private static SolidColorBrush[] CreatePalette(params string[] colors)
        {
            var brushes = new SolidColorBrush[colors.Length];
            for (var i = 0; i < colors.Length; i++)
            {
                brushes[i] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
                brushes[i].Freeze();
            }
            return brushes;
        }

        private static Pen[] CreatePens(SolidColorBrush[] brushes)
        {
            var pens = new Pen[brushes.Length];
            for (var i = 0; i < brushes.Length; i++)
            {
                pens[i] = new Pen(brushes[i], LineThickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                pens[i].Freeze();
            }
            return pens;
        }
    }
}

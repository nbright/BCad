﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BCad.Entities;
using BCad.EventArguments;
using BCad.Helpers;
using BCad.SnapPoints;
using SlimDX;
using SlimDX.Direct3D9;
using Media = System.Windows.Media;

namespace BCad.UI.Views
{
    /// <summary>
    /// Interaction logic for Direct3DViewControl.xaml
    /// </summary>
    [ExportViewControl("Direct3D")]
    public partial class Direct3DViewControl : ViewControl, IRenderEngine
    {
        #region Constructors

        public Direct3DViewControl()
        {
            InitializeComponent();
            this.Loaded += (_, __) => this.content.SetRenderEngine(this);
            this.Unloaded += (_, __) => this.content.Shutdown();
        }

        [ImportingConstructor]
        public Direct3DViewControl(IWorkspace workspace, IInputService inputService, IView view)
            : this()
        {
            this.workspace = workspace;
            this.inputService = inputService;
            this.view = view;

            this.MouseWheel += OnMouseWheel;
            this.view.ViewPortChanged += ViewPortChanged;
            this.workspace.PropertyChanged += WorkspacePropertyChanged;
            this.workspace.SettingsManager.PropertyChanged += SettingsManagerPropertyChanged;
            this.workspace.CommandExecuted += CommandExecuted;

            // load settings
            foreach (var setting in new[] { "BackgroundColor" })
                SettingsManagerPropertyChanged(null, new PropertyChangedEventArgs(setting));
        }

        #endregion

        #region TransformedSnapPoint class

        private class TransformedSnapPoint
        {
            public Point WorldPoint;
            public Vector3 ControlPoint;
            public SnapPointKind Kind;

            public TransformedSnapPoint(Point worldPoint, Vector3 controlPoint, SnapPointKind kind)
            {
                this.WorldPoint = worldPoint;
                this.ControlPoint = controlPoint;
                this.Kind = kind;
            }
        }

        #endregion

        #region LineVertex struct

        private struct LineVertex
        {
            public Vector3 Position;
            public int Color;
        }

        #endregion

        #region Member variables

        private IWorkspace workspace = null;
        private IInputService inputService = null;
        private IView view = null;
        private Matrix worldMatrix = Matrix.Identity;
        private Matrix viewMatrix = Matrix.Scaling(1.0f, 1.0f, 0.0f);
        private Matrix projectionMatrix = Matrix.Identity;
        private TransformedSnapPoint[] snapPoints = new TransformedSnapPoint[0];
        private object documentGate = new object();
        private Document document = null;
        private Device Device { get { return this.content.Device; } }
        private int autoColor = 0xFFFFFF;
        private Dictionary<uint, LineVertex[]> lines = new Dictionary<uint, LineVertex[]>();
        private IEnumerable<LineVertex[]> rubberBandLines = null;
        private bool panning = false;
        private System.Windows.Point lastPanPoint = new System.Windows.Point();
        private bool lastGeneratorNonNull = false;

        #endregion

        #region Constants

        private const int FullCircleDrawingSegments = 101;
        private ResourceDictionary resources = null;
        private ResourceDictionary SnapPointResources
        {
            get
            {
                if (resources == null)
                {
                    resources = new ResourceDictionary();
                    resources.Source = new Uri("/BCad.Core;component/SnapPoints/SnapPointIcons.xaml", UriKind.Relative);
                }

                return resources;
            }
        }

        #endregion

        #region ViewControl implementation

        public override Point GetCursorPoint()
        {
            var cursor = Mouse.GetPosition(this);
            var sp = GetActiveModelPoint(cursor.ToVector3());
            return sp.WorldPoint;
        }

        #endregion

        #region IRenderEngine implementation

        public void OnDeviceCreated(object sender, EventArgs e)
        {
        }

        public void OnDeviceDestroyed(object sender, EventArgs e)
        {
        }

        public void OnDeviceLost(object sender, EventArgs e)
        {
        }

        public void OnDeviceReset(object sender, EventArgs e)
        {
            Device.VertexDeclaration = new VertexDeclaration(Device, new[] {
                    new VertexElement(0, 0, DeclarationType.Float3, DeclarationMethod.Default, DeclarationUsage.Position, 0),
                    new VertexElement(0, 12, DeclarationType.Color, DeclarationMethod.Default, DeclarationUsage.Color, 0) });
            Device.SetRenderState(RenderState.Lighting, false);
        }

        public void OnMainLoop(object sender, EventArgs e)
        {
            lock (documentGate)
            {
                foreach (var lineSet in lines.Values)
                {
                    if (lineSet.Length > 0)
                        Device.DrawUserPrimitives(PrimitiveType.LineStrip, lineSet.Length - 1, lineSet);
                }

                if (rubberBandLines != null)
                {
                    foreach (var prim in rubberBandLines)
                    {
                        if (prim != null && prim.Length > 0)
                        {
                            Device.DrawUserPrimitives(PrimitiveType.LineStrip, prim.Length - 1, prim);
                        }
                    }
                }
            }
        }

        #endregion

        #region PropertyChanged functions

        private void SettingsManagerPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            bool redraw = false;
            switch (e.PropertyName)
            {
                case "BackgroundColor":
                    var bg = workspace.SettingsManager.BackgroundColor;
                    this.content.ClearColor = bg;
                    var backgroundColor = (bg.R << 16) | (bg.G << 8) | bg.B;
                    var brightness = System.Drawing.Color.FromArgb(backgroundColor).GetBrightness();
                    autoColor = brightness < 0.67 ? 0xFFFFFF : 0x000000;
                    ForceRender();
                    break;
                case "AngleSnap":
                case "Ortho":
                case "PointSnap":
                    redraw = true;
                    break;
                default:
                    break;
            }

            if (redraw)
            {
                var cursor = Mouse.GetPosition(this);
                var sp = GetActiveModelPoint(cursor.ToVector3());
                GenerateRubberBandLines(sp.WorldPoint);
                DrawSnapPoint(sp);
            }
        }

        private void WorkspacePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "Document":
                    DocumentChanged(workspace.Document);
                    break;
                default:
                    break;
            }
        }

        private void DocumentChanged(Document document)
        {
            lock (documentGate)
            {
                this.document = document;
                var sw = new System.Diagnostics.Stopwatch();
                sw.Start();
                lines.Clear();
                var red = System.Drawing.Color.Red.ToArgb();
                foreach (var layer in document.Layers.Values.Where(l => l.IsVisible))
                {
                    // TODO: parallelize this.  requires `lines` to be concurrent dictionary
                    var start = DateTime.UtcNow;
                    foreach (var entity in layer.Entities)
                    {
                        lines[entity.Id] = GenerateEntitySegments(entity, layer.Color).ToArray();
                    }
                    var elapsed = (DateTime.UtcNow - start).TotalMilliseconds;
                }

                snapPoints = document.Layers.Values.SelectMany(l => l.Entities.SelectMany(o => o.GetSnapPoints()))
                    .Select(sp => new TransformedSnapPoint(sp.Point, sp.Point.ToVector3(), sp.Kind)).ToArray();
                UpdateSnapPoints(projectionMatrix);
                rubberBandLines = null;
                sw.Stop();
                var loadTime = sw.ElapsedMilliseconds;
            }

            ForceRender();
        }

        void ViewPortChanged(object sender, ViewPortChangedEventArgs e)
        {
            var width = (float)view.ViewWidth;
            var height = (float)(view.ViewWidth * this.ActualHeight / this.ActualWidth);
            projectionMatrix = Matrix.Identity
                * Matrix.Translation((float)-view.BottomLeft.X, (float)-view.BottomLeft.Y, 0)
                * Matrix.Translation(-width / 2.0f, -height / 2.0f, 0)
                * Matrix.Scaling(2.0f / width, 2.0f / height, 1.0f);

            Device.SetTransform(TransformState.Projection, projectionMatrix);
            Device.SetTransform(TransformState.View, viewMatrix);
            UpdateSnapPoints(projectionMatrix);
            ForceRender();
        }

        void CommandExecuted(object sender, CommandExecutedEventArgs e)
        {
            this.Dispatcher.BeginInvoke((Action)(() => this.snapLayer.Children.Clear()));
            rubberBandLines = null;
            ForceRender();
        }

        #endregion

        #region Primitive generator functions

        private int GetDisplayColor(Color layerColor, Color primitiveColor)
        {
            if (!primitiveColor.IsAuto)
                return primitiveColor.ToInt();
            if (!layerColor.IsAuto)
                return layerColor.ToInt();
            return autoColor;
        }

        private void GenerateRubberBandLines(Point worldPoint)
        {
            var generator = inputService.PrimitiveGenerator;
            rubberBandLines = generator == null
                ? null
                : generator(worldPoint).Select(p => GeneratePrimitiveSegments(p, autoColor).ToArray());

            if (generator != null || lastGeneratorNonNull)
            {
                ForceRender();
            }

            lastGeneratorNonNull = generator != null;
        }

        private IEnumerable<LineVertex> GenerateEntitySegments(Entity entity, Color layerColor)
        {
            return entity.GetPrimitives().SelectMany(p => GeneratePrimitiveSegments(p, GetDisplayColor(layerColor, p.Color)));
        }

        private IEnumerable<LineVertex> GeneratePrimitiveSegments(IPrimitive primitive, int color)
        {
            LineVertex[] segments;
            switch (primitive.Kind)
            {
                case PrimitiveKind.Line:
                    var line = (BCad.Entities.Line)primitive;
                    segments = new[] {
                        new LineVertex() { Position = line.P1.ToVector3(), Color = color },
                        new LineVertex() { Position = line.P2.ToVector3(), Color = color }
                    };
                    break;
                case PrimitiveKind.Arc:
                case PrimitiveKind.Circle:
                case PrimitiveKind.Ellipse:
                    double startAngle, endAngle, radiusX, radiusY;
                    Point center;
                    Vector normal;
                    switch (primitive.Kind)
                    {
                        case PrimitiveKind.Arc:
                            var arc = (Arc)primitive;
                            startAngle = arc.StartAngle;
                            endAngle = arc.EndAngle;
                            radiusX = radiusY = arc.Radius;
                            center = arc.Center;
                            normal = arc.Normal;
                            break;
                        case PrimitiveKind.Circle:
                            var circle = (Circle)primitive;
                            startAngle = 0.0;
                            endAngle = 360.0;
                            radiusX = radiusY = circle.Radius;
                            center = circle.Center;
                            normal = circle.Normal;
                            break;
                        case PrimitiveKind.Ellipse:
                            var el = (Ellipse)primitive;
                            startAngle = el.StartAngle;
                            endAngle = el.EndAngle;
                            radiusX = el.MajorAxis.Length;
                            radiusY = radiusX * el.MinorAxisRatio;
                            center = el.Center;
                            normal = el.Normal;
                            break;
                        default:
                            throw new InvalidOperationException("Only arc, circle, and ellipse allowed here");
                    }

                    startAngle *= MathHelper.DegreesToRadians;
                    endAngle *= MathHelper.DegreesToRadians;
                    var coveringAngle = endAngle - startAngle;
                    if (coveringAngle < 0.0) coveringAngle += MathHelper.TwoPI;
                    var segCount = Math.Max(3, (int)(coveringAngle / MathHelper.TwoPI * (double)FullCircleDrawingSegments));
                    segments = new LineVertex[segCount];
                    var angleDelta = coveringAngle / (double)(segCount - 1);
                    var angle = startAngle;
                    var transformation =
                        Matrix.Scaling(new Vector3((float)radiusX, (float)radiusY, 1.0f))
                        * Matrix.RotationZ(-(float)Math.Atan2(normal.Y, normal.X))
                        * Matrix.RotationX(-(float)Math.Atan2(normal.Y, normal.Z))
                        * Matrix.RotationY((float)Math.Atan2(normal.X, normal.Z))
                        * Matrix.Translation(center.ToVector3());
                    var start = DateTime.UtcNow;
                    for (int i = 0; i < segCount; i++, angle += angleDelta)
                    {
                        var result = Vector3.Transform(
                            new Vector3((float)Math.Cos(angle), (float)Math.Sin(angle), 0.0f),
                            transformation);
                        segments[i] = new LineVertex()
                        {
                            Position = new Vector3(result.X / result.W, result.Y / result.W, result.Z / result.W),
                            Color = color
                        };
                    }
                    var elapsed = (DateTime.UtcNow - start).TotalMilliseconds;
                    break;
                default:
                    throw new ArgumentException("entity.Kind");
            }

            return segments;
        }

        #endregion

        #region SnapPointFunctions

        private void DrawSnapPoint(TransformedSnapPoint snapPoint)
        {
            snapLayer.Children.Clear();
            if (snapPoint.Kind == SnapPointKind.None)
                return;
            snapLayer.Children.Add(GetSnapIcon(snapPoint));
        }

        private void UpdateSnapPoints(Matrix matrix)
        {
            var start = DateTime.UtcNow;
            if (snapPoints.Length > 0)
            {
                Parallel.For(0, snapPoints.Length,
                    (i) =>
                    {
                        var wp = snapPoints[i].WorldPoint.ToVector3();
                        Vector3.Project(
                            ref wp, // input
                            Device.Viewport.X, // x
                            Device.Viewport.Y, // y
                            Device.Viewport.Width, // viewport width
                            Device.Viewport.Height, // viewport height
                            Device.Viewport.MinZ, // z-min
                            Device.Viewport.MaxZ, // z-max
                            ref matrix, // transformation matrix
                            out snapPoints[i].ControlPoint); // output
                        snapPoints[i].ControlPoint.Z = 0.0f;
                    });
            }
            var elapsed = (DateTime.UtcNow - start).TotalMilliseconds;
        }

        private Image GetSnapIcon(TransformedSnapPoint snapPoint)
        {
            string name;
            switch (snapPoint.Kind)
            {
                case SnapPointKind.None:
                    name = null;
                    break;
                case SnapPointKind.Center:
                    name = "CenterPointIcon";
                    break;
                case SnapPointKind.EndPoint:
                    name = "EndPointIcon";
                    break;
                case SnapPointKind.MidPoint:
                    name = "MidPointIcon";
                    break;
                case SnapPointKind.Quadrant:
                    name = "QuadrantPointIcon";
                    break;
                default:
                    throw new ArgumentException("snapPoint.Kind");
            }

            if (name == null)
                return null;

            var geometry = (Media.GeometryDrawing)SnapPointResources[name];
            var scale = workspace.SettingsManager.SnapPointSize;
            geometry.Pen = new Media.Pen(new Media.SolidColorBrush(workspace.SettingsManager.SnapPointColor), 0.2);
            var di = new Media.DrawingImage(geometry);
            var icon = new Image();
            icon.Source = di;
            icon.Stretch = Media.Stretch.None;
            icon.LayoutTransform = new Media.ScaleTransform(scale, scale);
            Canvas.SetLeft(icon, snapPoint.ControlPoint.X - geometry.Bounds.Width * scale / 2.0);
            Canvas.SetTop(icon, snapPoint.ControlPoint.Y - geometry.Bounds.Height * scale / 2.0);
            return icon;
        }

        #endregion

        #region GetPoint functions

        private TransformedSnapPoint GetActiveModelPoint(Vector3 cursor)
        {
            return ActiveObjectSnapPoints(cursor)
                ?? GetOrthoPoint(cursor)
                ?? GetAngleSnapPoint(cursor)
                ?? GetRawModelPoint(cursor);
        }

        private TransformedSnapPoint GetRawModelPoint(Vector3 cursor)
        {
            var matrix = projectionMatrix * worldMatrix;
            var worldPoint = Unproject(cursor);
            return new TransformedSnapPoint(worldPoint.ToPoint(), cursor, SnapPointKind.None);
        }

        private TransformedSnapPoint GetAngleSnapPoint(Vector3 cursor)
        {
            if (inputService.IsDrawing && workspace.SettingsManager.AngleSnap)
            {
                // get distance to last point
                var last = inputService.LastPoint;
                var current = Unproject(cursor).ToPoint();
                var vector = current - last;
                var dist = vector.Length;

                // for each snap angle, find the point `dist` out on the angle vector
                Func<double, Vector> snapVector = rad =>
                {
                    Vector radVector = null;
                    switch (workspace.DrawingPlane)
                    {
                        case DrawingPlane.XY:
                            radVector = new Vector(Math.Cos(rad), Math.Sin(rad), workspace.DrawingPlaneOffset);
                            break;
                        case DrawingPlane.XZ:
                            radVector = new Vector(Math.Cos(rad), workspace.DrawingPlaneOffset, Math.Sin(rad));
                            break;
                        case DrawingPlane.YZ:
                            radVector = new Vector(workspace.DrawingPlaneOffset, Math.Cos(rad), Math.Sin(rad));
                            break;
                        default:
                            Debug.Fail("invalid value for drawing plane");
                            break;
                    }

                    return radVector.Normalize() * dist;
                };

                var points = from sa in workspace.SettingsManager.SnapAngles
                             let rad = sa * MathHelper.DegreesToRadians
                             let radVector = snapVector(rad)
                             let snapPoint = (last + radVector).ToPoint()
                             let di = (cursor - Project(snapPoint.ToVector3())).Length()
                             where di <= workspace.SettingsManager.SnapAngleDistance
                             orderby di
                             select new TransformedSnapPoint(snapPoint, Project(snapPoint.ToVector3()), SnapPointKind.None);

                // return the closest one
                return points.FirstOrDefault();
            }

            return null;
        }

        private TransformedSnapPoint GetOrthoPoint(Vector3 cursor)
        {
            if (inputService.IsDrawing && workspace.SettingsManager.Ortho)
            {
                // if both are on the drawing plane
                var last = inputService.LastPoint;
                var current = Unproject(cursor).ToPoint();
                var delta = current - last;
                var offset = workspace.DrawingPlaneOffset;
                Point world;
                switch (workspace.DrawingPlane)
                {
                    case DrawingPlane.XY:
                        if (offset != last.Z && offset != current.Z)
                            return null;
                        if (Math.Abs(delta.X) > Math.Abs(delta.Y))
                            world = (last + new Vector(delta.X, 0.0, 0.0)).ToPoint();
                        else
                            world = (last + new Vector(0.0, delta.Y, 0.0)).ToPoint();
                        break;
                    case DrawingPlane.XZ:
                        if (offset != last.Y && offset != current.Y)
                            return null;
                        if (Math.Abs(delta.X) > Math.Abs(delta.Z))
                            world = (last + new Vector(delta.X, 0.0, 0.0)).ToPoint();
                        else
                            world = (last + new Vector(0.0, 0.0, delta.Z)).ToPoint();
                        break;
                    case DrawingPlane.YZ:
                        if (offset != last.X && offset != current.X)
                            return null;
                        if (Math.Abs(delta.Y) > Math.Abs(delta.Z))
                            world = (last + new Vector(0.0, delta.Y, 0.0)).ToPoint();
                        else
                            world = (last + new Vector(0.0, 0.0, delta.Z)).ToPoint();
                        break;
                    default:
                        throw new NotSupportedException("Invalid drawing plane");
                }

                Debug.Assert(world != null, "should have returned null");
                return new TransformedSnapPoint(world, cursor, SnapPointKind.None);
            }

            return null;
        }

        private TransformedSnapPoint ActiveObjectSnapPoints(Vector3 cursor)
        {
            if (workspace.SettingsManager.PointSnap && inputService.DesiredInputType == InputType.Point)
            {
                var maxDistSq = (float)(workspace.SettingsManager.SnapPointDistance * workspace.SettingsManager.SnapPointDistance);
                var points = from sp in snapPoints
                             let dist = (cursor - sp.ControlPoint).LengthSquared()
                             where dist <= maxDistSq
                             orderby dist
                             select sp;
                return points.FirstOrDefault();
            }

            return null;
        }

        #endregion

        #region Mouse functions

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            var cursor = e.GetPosition(this);
            var cursorVector = cursor.ToVector3();
            var sp = GetActiveModelPoint(cursorVector);
            var selectionDist = workspace.SettingsManager.ObjectSelectionRadius;
            switch (e.ChangedButton)
            {
                case MouseButton.Left:
                    switch (inputService.DesiredInputType)
                    {
                        case InputType.Point:
                            inputService.PushValue(sp.WorldPoint);
                            break;
                        case InputType.Object:
                            var start = DateTime.UtcNow;
                            uint hitEntity = 0;
                            foreach (var entityId in lines.Keys)
                            {
                                var segments = lines[entityId];
                                for (int i = 0; i < segments.Length - 1; i++)
                                {
                                    // translate line segment to screen coordinates
                                    var p1 = Project(segments[i].Position);
                                    var p2 = Project(segments[i + 1].Position);
                                    // check that cursor is in expanded bounding box of line segment
                                    var minx = Math.Min(p1.X, p2.X) - selectionDist;
                                    var maxx = Math.Max(p1.X, p2.X) + selectionDist;
                                    var miny = Math.Min(p1.Y, p2.Y) - selectionDist;
                                    var maxy = Math.Max(p1.Y, p2.Y) + selectionDist;
                                    if (MathHelper.Between(minx, maxx, cursor.X) && MathHelper.Between(miny, maxy, cursor.Y))
                                    {
                                        // in bounding rectangle, check distance to line
                                        var x1 = p1.X - cursor.X;
                                        var x2 = p2.X - cursor.X;
                                        var y1 = p1.Y - cursor.Y;
                                        var y2 = p2.Y - cursor.Y;
                                        var dx = x2 - x1;
                                        var dy = y2 - y1;
                                        var dr2 = dx * dx + dy * dy;
                                        var D = x1 * y2 - x2 * y1;
                                        var det = (selectionDist * selectionDist * dr2) - (D * D);
                                        if (det >= 0.0)
                                        {
                                            hitEntity = entityId;
                                            break;
                                        }
                                    }
                                }

                                if (hitEntity > 0)
                                    break;
                            }

                            if (hitEntity > 0)
                            {
                                // found it
                                var ent = document.Layers.Values.SelectMany(l => l.Entities).FirstOrDefault(en => en.Id == hitEntity);
                                Debug.Assert(ent != null, "hit object not in document");
                                var elapsed = (DateTime.UtcNow - start).TotalMilliseconds;
                                inputService.PushValue(ent);
                            }
                            break;
                    }
                    break;
                case MouseButton.Middle:
                    panning = true;
                    lastPanPoint = cursor;
                    break;
                case MouseButton.Right:
                    inputService.PushValue(null);
                    break;
            }

            GenerateRubberBandLines(sp.WorldPoint);
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            var cursor = e.GetPosition(this);
            switch (e.ChangedButton)
            {
                case MouseButton.Middle:
                    panning = false;
                    break;
            }

            var sp = GetActiveModelPoint(cursor.ToVector3());
            GenerateRubberBandLines(sp.WorldPoint);
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            var cursor = e.GetPosition(this);
            if (panning)
            {
                var delta = lastPanPoint - cursor;
                var scale = view.ViewWidth / this.ActualWidth;
                var dx = view.BottomLeft.X + delta.X * scale;
                var dy = view.BottomLeft.Y - delta.Y * scale;
                view.UpdateView(bottomLeft: new Point(dx, dy, view.BottomLeft.Z));
                lastPanPoint = cursor;
                ForceRender();
            }

            if (inputService.DesiredInputType == InputType.Point)
            {
                var sp = GetActiveModelPoint(cursor.ToVector3());
                GenerateRubberBandLines(sp.WorldPoint);
                DrawSnapPoint(sp);
            }
        }

        void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // scale everything
            var scale = 1.25f;
            if (e.Delta > 0.0f) scale = 0.8f; // 1.0f / 1.25f

            // center zoom operation on mouse
            var cursorPoint = e.GetPosition(this);
            var controlPoint = new Point(cursorPoint);
            var cursorPos = Unproject(controlPoint.ToVector3()).ToPoint();
            var botLeft = view.BottomLeft;

            // find relative scales
            var viewHeight = this.ActualHeight / this.ActualWidth * view.ViewWidth;
            var relHoriz = controlPoint.X / this.ActualWidth;
            var relVert = controlPoint.Y / this.ActualHeight;
            var viewWidthDelta = view.ViewWidth * (scale - 1.0);
            var viewHeightDelta = viewHeight * (scale - 1.0);

            // set values
            view.UpdateView(viewWidth: view.ViewWidth * scale, bottomLeft: (botLeft - new Vector(viewWidthDelta * relHoriz, viewHeightDelta * relVert, 0.0)).ToPoint());
            var cursor = GetActiveModelPoint(e.GetPosition(this).ToVector3());
            DrawSnapPoint(cursor);
            GenerateRubberBandLines(cursor.WorldPoint);

            ForceRender();
        }

        #endregion

        #region Misc functions

        private void ForceRender()
        {
            this.content.ForceRendering();
        }

        private Vector3 Project(Vector3 point)
        {
            var matrix = projectionMatrix * viewMatrix * worldMatrix;
            var screenPoint = Vector3.Project(
                point,
                Device.Viewport.X,
                Device.Viewport.Y,
                Device.Viewport.Width,
                Device.Viewport.Height,
                Device.Viewport.MinZ,
                Device.Viewport.MaxZ,
                matrix);
            return screenPoint;
        }

        private Vector3 Unproject(Vector3 point)
        {
            // not using view matrix because that scales z at 0 for display
            var matrix = projectionMatrix * worldMatrix;
            var worldPoint = Vector3.Unproject(
                point,
                Device.Viewport.X,
                Device.Viewport.Y,
                Device.Viewport.Width,
                Device.Viewport.Height,
                Device.Viewport.MinZ,
                Device.Viewport.MaxZ,
                matrix);
            return worldPoint;
        }

        #endregion

    }
}

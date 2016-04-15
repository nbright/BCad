﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Composition;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BCad.Collections;
using BCad.Entities;
using BCad.EventArguments;
using BCad.Extensions;
using BCad.Helpers;
using BCad.Primitives;
using BCad.Services;
using BCad.SnapPoints;
using BCad.UI.Shared.Extensions;

#if WINDOWS_UWP
// universal
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media;
using MouseWheelEventArgs = Windows.UI.Xaml.Input.PointerRoutedEventArgs;
using PointerButtonEventArgs = Windows.UI.Xaml.Input.PointerRoutedEventArgs;
using PointerDeviceType = Windows.Devices.Input.PointerDeviceType;
using PointerEventArgs = Windows.UI.Xaml.Input.PointerRoutedEventArgs;
using PointerUpdateKind = Windows.UI.Input.PointerUpdateKind;
using ResourceDictionary = Windows.UI.Xaml.ResourceDictionary;
using Shapes = Windows.UI.Xaml.Shapes;
#elif WPF
// WPF
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Cursors = System.Windows.Input.Cursors;
using DependencyObject = System.Windows.DependencyObject;
using DependencyProperty = System.Windows.DependencyProperty;
using FrameworkElement = System.Windows.FrameworkElement;
using PointerButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using PointerEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButton = System.Windows.Input.MouseButton;
using MouseWheelEventArgs = System.Windows.Input.MouseWheelEventArgs;
using PropertyPath = System.Windows.PropertyPath;
using ResourceDictionary = System.Windows.ResourceDictionary;
using Shapes = System.Windows.Shapes;
using SizeChangedInfo = System.Windows.SizeChangedInfo;
using UIElement = System.Windows.UIElement;
using Visibility = System.Windows.Visibility;
#endif

namespace BCad.UI.Shared
{
    /// <summary>
    /// Interaction logic for ViewPane.xaml
    /// </summary>
    public partial class ViewPane : UserControl, IViewControl
    {
        private AbstractCadRenderer _renderer;
        private bool panning;
        private bool selecting;
        private bool selectingRectangle;
        private Point lastPanPoint;
        private Point firstSelectionPoint;
        private Point currentSelectionPoint;
        private TaskCompletionSource<SelectionRectangle> selectionDone;
        private Matrix4 windowsTransformationMatrix;
        private Matrix4 unprojectMatrix;
        private QuadTree<TransformedSnapPoint> snapPointsQuadTree;
        private DoubleCollection solidLine = new DoubleCollection();
        private DoubleCollection dashedLine = new DoubleCollection() { 4.0, 4.0 };
        private ResourceDictionary resources;
        private CancellationTokenSource updateSnapPointsCancellationTokenSource = new CancellationTokenSource();
        private CancellationTokenSource mouseMoveCancellationTokenSource = new CancellationTokenSource();
        private CancellationTokenSource mouseDownCancellationTokenSource = new CancellationTokenSource();
        private CancellationTokenSource mouseWheelCancellationTokenSource = new CancellationTokenSource();
        private Task updateSnapPointsTask = new Task(() => { });
        private long lastDrawnSnapPointId;
        private long drawSnapPointId = 1;
        private object drawSnapPointIdGate = new object();
        private object lastDrawnSnapPointIdGate = new object();

        private Dictionary<SnapPointKind, FrameworkElement> snapPointGeometry = new Dictionary<SnapPointKind, FrameworkElement>();

        private string SnapPointResourcesUriPrefix =>
#if WINDOWS_UWP
            "ms-appx:"
#elif WPF
            ""
#endif
        ;

        private ResourceDictionary SnapPointResources
        {
            get
            {
                if (resources == null)
                {
                    resources = new ResourceDictionary();
                    resources.Source = new Uri($"{SnapPointResourcesUriPrefix}/SnapPointIcons.xaml", UriKind.RelativeOrAbsolute);
                }

                return resources;
            }
        }

        public BindingClass BindObject { get; private set; }

        [Import]
        public IWorkspace Workspace { get; set; }

#if WPF
        [ImportMany]
        public IEnumerable<Lazy<IRendererFactory, RenderFactoryMetadata>> RendererFactories { get; set; }
#endif

        public ViewPane()
        {
            InitializeComponent();

            var cursors = new[]
                {
                    pointCursor,
                    entityCursor,
                    textCursor
                };
            Loaded += (_, __) =>
            {
                foreach (var cursorImage in cursors)
                {
                    Canvas.SetLeft(cursorImage, -(int)(cursorImage.ActualWidth / 2.0));
                    Canvas.SetTop(cursorImage, -(int)(cursorImage.ActualHeight / 2.0));
                }
            };

#if WINDOWS_UWP
            clicker.PointerMoved += OnMouseMove;
            clicker.PointerPressed += OnMouseDown;
            clicker.PointerReleased += OnMouseUp;
            clicker.PointerWheelChanged += OnMouseWheel;
#endif

#if WPF
            clicker.Cursor = Cursors.None;
            clicker.MouseMove += OnMouseMove;
            clicker.MouseDown += OnMouseDown;
            clicker.MouseUp += OnMouseUp;
            clicker.MouseWheel += OnMouseWheel;
#endif

            CompositionContainer.Container.SatisfyImports(this);
        }

        [OnImportsSatisfied]
        public void OnImportsSatisfied()
        {
            BindObject = new BindingClass(Workspace);
            DataContext = BindObject;
            Workspace.WorkspaceChanged += Workspace_WorkspaceChanged;
            Workspace.CommandExecuted += Workspace_CommandExecuted;
            Workspace.SettingsManager.PropertyChanged += SettingsManager_PropertyChanged;
            Workspace.SelectedEntities.CollectionChanged += SelectedEntities_CollectionChanged;
            Workspace.InputService.ValueRequested += InputService_ValueRequested;
            Workspace.InputService.ValueReceived += InputService_ValueReceived;
            Workspace.InputService.InputCanceled += InputService_InputCanceled;

            SettingsManager_PropertyChanged(this, new PropertyChangedEventArgs(string.Empty));
            SetCursorVisibility();

#if WINDOWS_UWP
            _renderer = new Universal.UI.Win2DRenderer(Workspace);
#else
            var factory = RendererFactories.FirstOrDefault(f => f.Metadata.FactoryName == Workspace.SettingsManager.RendererId);
            if (factory != null)
            {
                _renderer = factory.Value.CreateRenderer(this, Workspace);
            }
#endif

            renderControl.Content = _renderer;

            // prepare snap point icons
            foreach (var kind in new[] { SnapPointKind.Center, SnapPointKind.EndPoint, SnapPointKind.MidPoint, SnapPointKind.Quadrant, SnapPointKind.Focus })
            {
                snapPointGeometry[kind] = GetSnapGeometry(kind);
                snapLayer.Children.Add(snapPointGeometry[kind]);
            }
        }

        private void SelectedEntities_CollectionChanged(object sender, EventArgs e)
        {
            UpdateHotPoints();
        }

        public int DisplayHeight
        {
            get { return (int)ActualHeight; }
        }

        public int DisplayWidth
        {
            get { return (int)ActualWidth; }
        }

        public Task<SelectionRectangle> GetSelectionRectangle()
        {
            if (selectingRectangle)
                throw new InvalidOperationException("Already selecting a rectangle");
            selectingRectangle = true;
            Workspace.OutputService.WriteLine("Select first point");
            SetCursorVisibility();
            selectionDone = new TaskCompletionSource<SelectionRectangle>();
            return selectionDone.Task;
        }

        private void SettingsManager_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(Workspace.SettingsManager.BackgroundColor))
            {
                var autoColor = Workspace.SettingsManager.BackgroundColor.GetAutoContrastingColor();
                var selectionColor = CadColor.FromInt32(autoColor.ToInt32());
                selectionColor.A = 25;
                var autoColorUI = autoColor.ToUIColor();
                BindObject.AutoBrush = new SolidColorBrush(autoColorUI);
                BindObject.SelectionBrush = new SolidColorBrush(selectionColor.ToUIColor());
            }
            if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(Workspace.SettingsManager.CursorSize))
            {
                var cursorSize = Workspace.SettingsManager.CursorSize / 2.0 + 0.5;
                BindObject.LeftCursorExtent = new Point(-cursorSize, 0, 0);
                BindObject.RightCursorExtent = new Point(cursorSize, 0, 0);
                BindObject.TopCursorExtent = new Point(0, -cursorSize, 0);
                BindObject.BottomCursorExtent = new Point(0, cursorSize, 0);

                // only update the cursor location after the previous four binding calls have appropriately propagated
                // to the UI
                Invoke(UpdateCursorLocation);
            }
            if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(Workspace.SettingsManager.EntitySelectionRadius))
            {
                var entitySize = Workspace.SettingsManager.EntitySelectionRadius;
                BindObject.EntitySelectionTopLeft = new Point(-entitySize, -entitySize, 0);
                BindObject.EntitySelectionTopRight = new Point(entitySize, -entitySize, 0);
                BindObject.EntitySelectionBottomLeft = new Point(-entitySize, entitySize, 0);
                BindObject.EntitySelectionBottomRight = new Point(entitySize, entitySize, 0);

                // only update the cursor location after the previous four binding calls have appropriately propagated
                // to the UI
                Invoke(UpdateCursorLocation);
            }
            if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(Workspace.SettingsManager.TextCursorSize))
            {
                var textSize = Workspace.SettingsManager.TextCursorSize / 2.0 + 0.5;
                BindObject.TextCursorStart = new Point(0, -textSize, 0);
                BindObject.TextCursorStart = new Point(0, textSize, 0);

                // only update the cursor location after the previous two binding calls have appropriately propagated
                // to the UI
                Invoke(UpdateCursorLocation);
            }
            if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(Workspace.SettingsManager.HotPointColor))
            {
                BindObject.HotPointBrush = new SolidColorBrush(Workspace.SettingsManager.HotPointColor.ToUIColor());
            }
            if (string.IsNullOrEmpty(e.PropertyName) ||
                e.PropertyName == nameof(Workspace.SettingsManager.SnapPointColor) ||
                e.PropertyName == nameof(Workspace.SettingsManager.SnapPointSize))
            {
                BindObject.SnapPointTransform = new ScaleTransform() { ScaleX = Workspace.SettingsManager.SnapPointSize, ScaleY = Workspace.SettingsManager.SnapPointSize };
                BindObject.SnapPointBrush = new SolidColorBrush(Workspace.SettingsManager.SnapPointColor.ToUIColor());
                BindObject.SnapPointStrokeThickness = Workspace.SettingsManager.SnapPointSize == 0.0
                    ? 1.0
                    : 3.0 / Workspace.SettingsManager.SnapPointSize;
            }
        }

        private void Invoke(Action action)
        {
#if WPF
            Dispatcher.Invoke(action, DispatcherPriority.Background);
#endif

#if WINDOWS_UWP
            Dispatcher.RunAsync(CoreDispatcherPriority.Low, new DispatchedHandler(action)).AsTask();//.GetAwaiter().GetResult();
#endif
        }

        private T Invoke<T>(Func<T> func)
        {
#if WPF
            return Dispatcher.Invoke(func);
#endif

#if WINDOWS_UWP
            var result = default(T);
            Dispatcher.RunAsync(CoreDispatcherPriority.Normal, new DispatchedHandler(() =>
            {
                result = func();
            })).AsTask();//.GetAwaiter().GetResult();
            return result;
#endif
        }

        private void BeginInvoke(Action action)
        {
#if WPF
            Dispatcher.BeginInvoke(action);
#endif

#if WINDOWS_UWP
            var unused = Dispatcher.RunAsync(CoreDispatcherPriority.Low, new DispatchedHandler(action));
#endif
        }

        private async void InputService_ValueReceived(object sender, ValueReceivedEventArgs e)
        {
            selecting = false;
            var point = await GetCursorPoint();
            ClearSnapPoints();
            SetCursorVisibility();
            SetSelectionLineVisibility(Visibility.Collapsed);
        }

        private void InputService_ValueRequested(object sender, ValueRequestedEventArgs e)
        {
            selecting = false;
            SetCursorVisibility();
            SetSelectionLineVisibility(Visibility.Collapsed);
        }

        void InputService_InputCanceled(object sender, EventArgs e)
        {
            if (selecting)
            {
                selecting = false;
                SetCursorVisibility();
                SetSelectionLineVisibility(Visibility.Collapsed);
            }
            else
            {
                Workspace.SelectedEntities.Clear();
            }
        }

        private async void Workspace_CommandExecuted(object sender, CadCommandExecutedEventArgs e)
        {
            selecting = false;
            var point = await GetCursorPoint();
            ClearSnapPoints();
            SetCursorVisibility();
            SetSelectionLineVisibility(Visibility.Collapsed);
        }

#if WPF
        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);

            if (Workspace != null)
                ViewPortChanged();
        }
#endif

        private void Workspace_WorkspaceChanged(object sender, WorkspaceChangeEventArgs e)
        {
            if (e.IsActiveViewPortChange)
            {
                ViewPortChanged();
            }
            if (e.IsDrawingChange)
            {
                DrawingChanged();
                BindObject.Refresh();
            }
        }

        private async void ViewPortChanged()
        {
            windowsTransformationMatrix = Workspace.ActiveViewPort.GetTransformationMatrixWindowsStyle(ActualWidth, ActualHeight);
            unprojectMatrix = windowsTransformationMatrix;
            unprojectMatrix.Invert();
            windowsTransformationMatrix = Matrix4.CreateScale(1, 1, 0) * windowsTransformationMatrix;
            UpdateSnapPoints();
            UpdateHotPoints();
            var point = await GetCursorPoint();
        }

        private void DrawingChanged()
        {
            UpdateSnapPoints();
        }

        private void ClearSnapPoints()
        {
            foreach (UIElement child in snapLayer.Children)
            {
                child.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateSnapPoints()
        {
            updateSnapPointsCancellationTokenSource.Cancel();
            var width = ActualWidth;
            var height = ActualHeight;
            updateSnapPointsTask = Task.Run(() =>
            {
                updateSnapPointsCancellationTokenSource = new CancellationTokenSource();

                // populate the snap points
                var cancellationToken = updateSnapPointsCancellationTokenSource.Token;
                var transformedQuadTree = new Collections.QuadTree<TransformedSnapPoint>(new Collections.Rect(0, 0, width, height), t => new Collections.Rect(t.ControlPoint.X, t.ControlPoint.Y, 0.0, 0.0));
                foreach (var layer in Workspace.Drawing.GetLayers(cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    foreach (var entity in layer.GetEntities(cancellationToken))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        foreach (var snapPoint in entity.GetSnapPoints())
                        {
                            transformedQuadTree.AddItem(new TransformedSnapPoint(snapPoint.Point, Project(snapPoint.Point), snapPoint.Kind));
                        }
                    }
                }

                snapPointsQuadTree = transformedQuadTree;
            });
        }

        private Point Project(Point point)
        {
            return windowsTransformationMatrix.Transform(point);
        }

        private Point Unproject(Point point)
        {
            return unprojectMatrix.Transform(point);
        }

        public async Task<Point> GetCursorPoint()
        {
            var mouse = Invoke(GetPointerPosition);
            var model = await updateSnapPointsTask.ContinueWith(_ => GetActiveModelPoint(mouse, CancellationToken.None)).ConfigureAwait(false);
            return model.WorldPoint;
        }

        private Point GetPointerPosition(PointerEventArgs e)
        {
#if WPF
            return e.GetPosition(clicker).ToPoint();
#endif

#if WINDOWS_UWP
            return e.GetCurrentPoint(clicker).Position.ToPoint();
#endif
        }

        private Point GetPointerPosition()
        {
#if WPF
            return System.Windows.Input.Mouse.GetPosition(clicker).ToPoint();
#endif

#if WINDOWS_UWP
            //return e.GetCurrentPoint(clicker).Position.ToPoint();
            return Point.Origin;
#endif
        }

        private bool IsLeftButtonPressed(PointerButtonEventArgs e)
        {
#if WPF
            return e.ChangedButton == MouseButton.Left;
#endif

#if WINDOWS_UWP
            if (e.Pointer.PointerDeviceType == PointerDeviceType.Mouse)
            {
                var properties = e.GetCurrentPoint(clicker).Properties;
                return properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed
                    || properties.PointerUpdateKind == PointerUpdateKind.LeftButtonReleased;
            }

            return false;
#endif
        }

        private bool IsMiddleButtonPressed(PointerButtonEventArgs e)
        {
#if WPF
            return e.ChangedButton == MouseButton.Middle;
#endif

#if WINDOWS_UWP
            if (e.Pointer.PointerDeviceType == PointerDeviceType.Mouse)
            {
                var properties = e.GetCurrentPoint(clicker).Properties;
                return properties.PointerUpdateKind == PointerUpdateKind.MiddleButtonPressed
                    || properties.PointerUpdateKind == PointerUpdateKind.MiddleButtonReleased;
            }

            return false;
#endif
        }

        private bool IsRightButtonPressed(PointerButtonEventArgs e)
        {
#if WPF
            return e.ChangedButton == MouseButton.Right;
#endif

#if WINDOWS_UWP
            if (e.Pointer.PointerDeviceType == PointerDeviceType.Mouse)
            {
                var properties = e.GetCurrentPoint(clicker).Properties;
                return properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed
                    || properties.PointerUpdateKind == PointerUpdateKind.RightButtonReleased;
            }

            return false;
#endif
        }

        private async void OnMouseDown(object sender, PointerButtonEventArgs e)
        {
            var cursor = GetPointerPosition(e);
            mouseMoveCancellationTokenSource.Cancel();
            mouseMoveCancellationTokenSource = new CancellationTokenSource();
            var token = mouseMoveCancellationTokenSource.Token;
            var sp = await updateSnapPointsTask.ContinueWith(_ => GetActiveModelPoint(cursor, token), token);
            if (IsLeftButtonPressed(e))
            {
                if ((Workspace.InputService.AllowedInputTypes & InputType.Point) == InputType.Point)
                {
                    Workspace.InputService.PushPoint(sp.WorldPoint);
                }
                else if ((Workspace.InputService.AllowedInputTypes & InputType.Entity) == InputType.Entity)
                {
                    var selected = GetHitEntity(cursor);
                    if (selected != null)
                    {
                        Workspace.InputService.PushEntity(selected);
                    }
                }
                else if ((Workspace.InputService.AllowedInputTypes & InputType.Entities) == InputType.Entities || selectingRectangle || !Workspace.IsCommandExecuting)
                {
                    if (selecting)
                    {
                        // finish selection
                        IEnumerable<Entity> entities = null;
                        if (selectingRectangle)
                        {
                            selectingRectangle = false;
                            var topLeftScreen = new Point(Math.Min(firstSelectionPoint.X, cursor.X), Math.Min(firstSelectionPoint.Y, cursor.Y), 0.0);
                            var bottomRightScreen = new Point(Math.Max(firstSelectionPoint.X, cursor.X), Math.Max(firstSelectionPoint.Y, cursor.Y), 0.0);
                            var selection = new SelectionRectangle(
                                topLeftScreen,
                                bottomRightScreen,
                                Unproject(topLeftScreen),
                                Unproject(bottomRightScreen));
                            selectionDone.SetResult(selection);
                        }
                        else
                        {
                            var rect = new Rect(
                                Math.Min(firstSelectionPoint.X, currentSelectionPoint.X),
                                Math.Min(firstSelectionPoint.Y, currentSelectionPoint.Y),
                                Math.Abs(firstSelectionPoint.X - currentSelectionPoint.X),
                                Math.Abs(firstSelectionPoint.Y - currentSelectionPoint.Y));
                            entities = GetContainedEntities(rect, currentSelectionPoint.X < firstSelectionPoint.X);
                        }

                        selecting = false;
                        SetSelectionLineVisibility(Visibility.Collapsed);
                        if (entities != null)
                        {
                            if (!Workspace.IsCommandExecuting)
                            {
                                Workspace.SelectedEntities.AddRange(entities);
                            }
                            else
                            {
                                Workspace.InputService.PushEntities(entities);
                            }
                        }
                    }
                    else
                    {
                        SelectedEntity selected = null;
                        if (selectingRectangle)
                        {
                            Workspace.OutputService.WriteLine("Select second point");
                        }
                        else
                        {
                            selected = GetHitEntity(cursor);
                        }

                        if (selected != null)
                        {
                            if (!Workspace.IsCommandExecuting)
                            {
                                Workspace.SelectedEntities.Add(selected.Entity);
                            }
                            else
                            {
                                Workspace.InputService.PushEntities(new[] { selected.Entity });
                            }
                        }
                        else
                        {
                            selecting = true;
                            firstSelectionPoint = cursor;
                            currentSelectionPoint = cursor;
                            SetSelectionLineVisibility(Visibility.Visible);
                        }
                    }
                }
                else if (Workspace.InputService.AllowedInputTypes == InputType.None || !Workspace.IsCommandExecuting)
                {
                    // do hot-point tracking
                    var selected = GetHitEntity(cursor);
                    if (selected != null)
                    {
                        Workspace.SelectedEntities.Add(selected.Entity);
                    }
                }
            }
            else if (IsMiddleButtonPressed(e))
            {
                panning = true;
                lastPanPoint = cursor;
            }
            else if (IsRightButtonPressed(e))
            {
                Workspace.InputService.PushNone();
            }
        }

        private void OnMouseUp(object sender, PointerButtonEventArgs e)
        {
            if (IsMiddleButtonPressed(e))
            {
                panning = false;
            }
        }

        private void OnMouseMove(object sender, PointerEventArgs e)
        {
            if (Workspace == null || Workspace.InputService == null)
                return;

            var cursor = GetPointerPosition(e);
            var delta = lastPanPoint - cursor;
            if (panning)
            {
                var vp = Workspace.ActiveViewPort;
                var scale = vp.ViewHeight / this.ActualHeight;
                var dx = vp.BottomLeft.X + delta.X * scale;
                var dy = vp.BottomLeft.Y - delta.Y * scale;
                Workspace.Update(activeViewPort: vp.Update(bottomLeft: new Point(dx, dy, vp.BottomLeft.Z)));
                lastPanPoint = cursor;
                firstSelectionPoint -= delta;
            }

            if (selecting)
            {
                currentSelectionPoint = cursor;
                UpdateSelectionLines();
            }

            BindObject.CursorScreen = cursor;
            UpdateCursorLocation();

            mouseMoveCancellationTokenSource.Cancel();
            mouseMoveCancellationTokenSource = new CancellationTokenSource();
            var token = mouseMoveCancellationTokenSource.Token;
            updateSnapPointsTask.ContinueWith(_ =>
            {
                var snapPoint = GetActiveModelPoint(cursor, token);
                Invoke(() => BindObject.CursorWorld = snapPoint.WorldPoint);
                _renderer.UpdateRubberBandLines();
                if ((Workspace.InputService.AllowedInputTypes & InputType.Point) == InputType.Point)
                    DrawSnapPoint(snapPoint, GetNextDrawSnapPointId());
            }, token).ConfigureAwait(false);
        }

        private void UpdateCursorLocation()
        {
            foreach (var cursorImage in new[] { pointCursor, entityCursor, textCursor})
            {
                Canvas.SetLeft(cursorImage, (int)(BindObject.CursorScreen.X - (cursorImage.ActualWidth / 2.0)));
                Canvas.SetTop(cursorImage, (int)(BindObject.CursorScreen.Y - (cursorImage.ActualHeight / 2.0)));
            }
        }

        private long GetNextDrawSnapPointId()
        {
            lock (drawSnapPointIdGate)
            {
                var next = drawSnapPointId++;
                return next;
            }
        }

        private void SetSelectionLineVisibility(Visibility vis)
        {
            if (vis == Visibility.Visible)
            {
                UpdateSelectionLines();
            }

            BeginInvoke((Action)(() =>
            {
                selectionLine1.Visibility = vis;
                selectionLine2.Visibility = vis;
                selectionLine3.Visibility = vis;
                selectionLine4.Visibility = vis;
                selectionRect.Visibility = vis;
            }));
        }

        private void UpdateSelectionLines()
        {
            selectionLine1.X1 = currentSelectionPoint.X;
            selectionLine1.Y1 = currentSelectionPoint.Y;
            selectionLine1.X2 = currentSelectionPoint.X;
            selectionLine1.Y2 = firstSelectionPoint.Y;

            selectionLine2.X1 = currentSelectionPoint.X;
            selectionLine2.Y1 = firstSelectionPoint.Y;
            selectionLine2.X2 = firstSelectionPoint.X;
            selectionLine2.Y2 = firstSelectionPoint.Y;

            selectionLine3.X1 = firstSelectionPoint.X;
            selectionLine3.Y1 = firstSelectionPoint.Y;
            selectionLine3.X2 = firstSelectionPoint.X;
            selectionLine3.Y2 = currentSelectionPoint.Y;

            selectionLine4.X1 = firstSelectionPoint.X;
            selectionLine4.Y1 = currentSelectionPoint.Y;
            selectionLine4.X2 = currentSelectionPoint.X;
            selectionLine4.Y2 = currentSelectionPoint.Y;

            var dash = !selectingRectangle && currentSelectionPoint.X < firstSelectionPoint.X
                ? dashedLine
                : solidLine;
            selectionLine1.StrokeDashArray = dash;
            selectionLine2.StrokeDashArray = dash;
            selectionLine3.StrokeDashArray = dash;
            selectionLine4.StrokeDashArray = dash;

            var left = Math.Min(currentSelectionPoint.X, firstSelectionPoint.X);
            var top = Math.Min(currentSelectionPoint.Y, firstSelectionPoint.Y);
            selectionRect.Width = Math.Abs(currentSelectionPoint.X - firstSelectionPoint.X);
            selectionRect.Height = Math.Abs(currentSelectionPoint.Y - firstSelectionPoint.Y);
            Canvas.SetLeft(selectionRect, left);
            Canvas.SetTop(selectionRect, top);
        }

        private void SetCursorVisibility()
        {
            if (selectingRectangle)
            {
                Invoke(() =>
                {
                    pointCursor.Visibility = Visibility.Visible;
                    entityCursor.Visibility = Visibility.Collapsed;
                    textCursor.Visibility = Visibility.Collapsed;
                });
            }
            else
            {
                Func<InputType[], Visibility> getVisibility = types =>
                    types.Any(t => (Workspace.InputService.AllowedInputTypes & t) == t)
                        ? Visibility.Visible
                        : Visibility.Collapsed;

                Invoke(() =>
                {
                    pointCursor.Visibility = getVisibility(new[]
                    {
                        InputType.Command,
                        InputType.Distance,
                        InputType.Point
                    });
                    entityCursor.Visibility = getVisibility(new[]
                    {
                        InputType.Command,
                        InputType.Entities,
                        InputType.Entity
                    });
                    textCursor.Visibility = getVisibility(new[]
                    {
                        InputType.Text
                    });
                });
            }
        }

        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var delta =
#if WPF
                e.Delta
#endif

#if WINDOWS_UWP
                e.GetCurrentPoint(clicker).Properties.MouseWheelDelta;
#endif
                ;

            // scale everything
            var scale = 1.25;
            if (delta > 0) scale = 0.8; // 1.0 / 1.25

            // center zoom operation on mouse
            var cursorPoint = GetPointerPosition(e);
            var vp = Workspace.ActiveViewPort;
            var oldHeight = vp.ViewHeight;
            var oldWidth = ActualWidth * oldHeight / ActualHeight;
            var newHeight = oldHeight * scale;
            var newWidth = oldWidth * scale;
            var heightDelta = newHeight - oldHeight;
            var widthDelta = newWidth - oldWidth;

            var relHoriz = cursorPoint.X / ActualWidth;
            var relVert = (ActualHeight - cursorPoint.Y) / ActualHeight;
            var botLeftDelta = new Vector(relHoriz * widthDelta, relVert * heightDelta, 0.0);
            var newVp = vp.Update(
                bottomLeft: (Point)(vp.BottomLeft - botLeftDelta),
                viewHeight: vp.ViewHeight * scale);
            Workspace.Update(activeViewPort: newVp);

            mouseWheelCancellationTokenSource.Cancel();
            mouseWheelCancellationTokenSource = new CancellationTokenSource();
            var token = mouseWheelCancellationTokenSource.Token;
            updateSnapPointsTask.ContinueWith(_ =>
            {
                var snapPoint = GetActiveModelPoint(cursorPoint, token);
                Invoke(() => BindObject.CursorWorld = snapPoint.WorldPoint);
                _renderer.UpdateRubberBandLines();
                if ((Workspace.InputService.AllowedInputTypes & InputType.Point) == InputType.Point)
                    DrawSnapPoint(snapPoint, GetNextDrawSnapPointId());
            }, token).ConfigureAwait(false);
        }

        private TransformedSnapPoint GetActiveModelPoint(Point cursor, CancellationToken cancellationToken)
        {
            return GetActiveSnapPoint(cursor, cancellationToken)
                ?? GetOrthoPoint(cursor)
                ?? GetAngleSnapPoint(cursor, cancellationToken)
                ?? GetRawModelPoint(cursor);
        }

        private TransformedSnapPoint GetActiveSnapPoint(Point cursor, CancellationToken cancellationToken)
        {
            if (Workspace.SettingsManager.PointSnap && (Workspace.InputService.AllowedInputTypes & InputType.Point) == InputType.Point)
            {
                var snapPointDistance = Workspace.SettingsManager.SnapPointDistance;
                var size = snapPointDistance * 2;
                var nearPoints = snapPointsQuadTree
                    .GetContainedItems(new Collections.Rect(cursor.X - snapPointDistance, cursor.Y - snapPointDistance, size, size));
                var points = nearPoints
                    .Select(p => Tuple.Create((cursor - p.ControlPoint).LengthSquared, p))
                    .OrderBy(t => t.Item1, new CancellableComparer<double>(cancellationToken));
                return points.FirstOrDefault()?.Item2;
            }

            return null;
        }

        private TransformedSnapPoint GetOrthoPoint(Point cursor)
        {
            if (Workspace.IsDrawing && Workspace.SettingsManager.Ortho)
            {
                // if both are on the drawing plane
                var last = Workspace.InputService.LastPoint;
                var current = Unproject(cursor);
                var delta = current - last;
                var drawingPlane = Workspace.DrawingPlane;
                var offset = drawingPlane.Point;
                Point world;

                if (drawingPlane.Normal == Vector.ZAxis)
                {
                    if (offset.Z != last.Z && offset.Z != current.Z)
                        return null;
                    if (Math.Abs(delta.X) > Math.Abs(delta.Y))
                        world = last + new Vector(delta.X, 0.0, 0.0);
                    else
                        world = last + new Vector(0.0, delta.Y, 0.0);
                }
                else if (drawingPlane.Normal == Vector.YAxis)
                {
                    if (offset.Y != last.Y && offset.Y != current.Y)
                        return null;
                    if (Math.Abs(delta.X) > Math.Abs(delta.Z))
                        world = last + new Vector(delta.X, 0.0, 0.0);
                    else
                        world = last + new Vector(0.0, 0.0, delta.Z);
                }
                else if (drawingPlane.Normal == Vector.XAxis)
                {
                    if (offset.X != last.X && offset.X != current.X)
                        return null;
                    if (Math.Abs(delta.Y) > Math.Abs(delta.Z))
                        world = last + new Vector(0.0, delta.Y, 0.0);
                    else
                        world = last + new Vector(0.0, 0.0, delta.Z);
                }
                else
                {
                    throw new NotSupportedException("Invalid drawing plane");
                }

                return new TransformedSnapPoint(world, cursor, SnapPointKind.None);
            }

            return null;
        }

        private TransformedSnapPoint GetAngleSnapPoint(Point cursor, CancellationToken cancellationToken)
        {
            if (Workspace.IsDrawing && Workspace.SettingsManager.AngleSnap)
            {
                // get distance to last point
                var last = Workspace.InputService.LastPoint;
                var current = Unproject(cursor);
                var vector = current - last;
                var dist = vector.Length;

                // for each snap angle, find the point `dist` out on the angle vector
                Func<double, Vector> snapVector = rad =>
                {
                    Vector radVector = default(Vector);
                    var drawingPlane = Workspace.DrawingPlane;
                    var offset = drawingPlane.Point;
                    if (drawingPlane.Normal == Vector.ZAxis)
                    {
                        radVector = new Vector(Math.Cos(rad), Math.Sin(rad), offset.Z);
                    }
                    else if (drawingPlane.Normal == Vector.YAxis)
                    {
                        radVector = new Vector(Math.Cos(rad), offset.Y, Math.Sin(rad));
                    }
                    else if (drawingPlane.Normal == Vector.XAxis)
                    {
                        radVector = new Vector(offset.X, Math.Cos(rad), Math.Sin(rad));
                    }
                    else
                    {
                        Debug.Fail("invalid value for drawing plane");
                    }

                    return radVector.Normalize() * dist;
                };

                var points = new List<Tuple<double, TransformedSnapPoint>>();
                foreach (var snapAngle in Workspace.SettingsManager.SnapAngles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var radians = snapAngle * MathHelper.DegreesToRadians;
                    var radVector = snapVector(radians);
                    var snapPoint = last + radVector;
                    var distance = (cursor - Project(snapPoint)).Length;
                    if (distance < Workspace.SettingsManager.SnapAngleDistance)
                    {
                        points.Add(Tuple.Create(distance, new TransformedSnapPoint(snapPoint, Project(snapPoint), SnapPointKind.None)));
                    }
                }

                return points.OrderBy(p => p.Item1).FirstOrDefault()?.Item2;
            }

            return null;
        }

        private TransformedSnapPoint GetRawModelPoint(Point cursor)
        {
            var world = Unproject(cursor);
            return new TransformedSnapPoint(world, cursor, SnapPointKind.None);
        }

        private IEnumerable<Point> ProjectedChain(Entity entity)
        {
            return entity.GetPrimitives().SelectMany(p => p.GetProjectedVerticies(windowsTransformationMatrix));
        }

        private IEnumerable<Point> ProjectedChain(IPrimitive primitive)
        {
            switch (primitive.Kind)
            {
                case PrimitiveKind.Ellipse:
                    return ((PrimitiveEllipse)primitive).GetProjectedVerticies(windowsTransformationMatrix, 360);
                default:
                    return primitive.GetProjectedVerticies(windowsTransformationMatrix);
            }
        }

        private IEnumerable<Entity> GetContainedEntities(Rect selectionRect, bool includePartial)
        {
            var entities = Workspace.Drawing.GetLayers().Where(l => l.IsVisible).SelectMany(l => l.GetEntities()).Where(e => selectionRect.Contains(ProjectedChain(e), includePartial));
            return entities;
        }

        private SelectedEntity GetHitEntity(Point screenPoint)
        {
            var selectionRadius = Workspace.SettingsManager.EntitySelectionRadius;
            var selectionRadius2 = selectionRadius * selectionRadius;
            var entities = from layer in Workspace.Drawing.GetLayers().Where(l => l.IsVisible)
                           from entity in layer.GetEntities()
                           let dist = ClosestPoint(entity, screenPoint)
                           where dist.Item1 < selectionRadius2
                           orderby dist.Item1
                           select new SelectedEntity(entity, dist.Item2);
            var selected = entities.FirstOrDefault();
            return selected;
        }

        private Tuple<double, Point> ClosestPoint(Entity entity, Point screenPoint)
        {
            return entity.GetPrimitives()
                .Select(prim => ClosestPoint(prim, screenPoint))
                .OrderBy(p => p.Item1)
                .FirstOrDefault();
        }

        private Tuple<double, Point> ClosestPoint(IPrimitive primitive, Point screenPoint)
        {
            switch (primitive.Kind)
            {
                case PrimitiveKind.Ellipse:
                    var el = (PrimitiveEllipse)primitive;
                    return ClosestPoint(el.GetProjectedVerticies(windowsTransformationMatrix, 360).ToArray(), screenPoint);
                case PrimitiveKind.Line:
                    var line = (PrimitiveLine)primitive;
                    return ClosestPoint(new[]
                    {
                        windowsTransformationMatrix.Transform(line.P1),
                        windowsTransformationMatrix.Transform(line.P2)
                    }, screenPoint);
                case PrimitiveKind.Point:
                    // the closest point is the only point present
                    var point = (PrimitivePoint)primitive;
                    var displayPoint = windowsTransformationMatrix.Transform(point.Location);
                    var dist = (displayPoint - screenPoint).Length;
                    return Tuple.Create(dist, point.Location);
                case PrimitiveKind.Text:
                    var text = (PrimitiveText)primitive;
                    var rad = text.Rotation * MathHelper.DegreesToRadians;
                    var right = new Vector(Math.Cos(rad), Math.Sin(rad), 0.0).Normalize() * text.Width;
                    var up = text.Normal.Cross(right).Normalize() * text.Height;
                    var borderPoints = new[]
                    {
                        windowsTransformationMatrix.Transform(text.Location),
                        windowsTransformationMatrix.Transform(text.Location + right),
                        windowsTransformationMatrix.Transform(text.Location + up),
                        windowsTransformationMatrix.Transform(text.Location + right + up)
                    };
                    if (borderPoints.ConvexHull().PolygonContains(screenPoint))
                        return Tuple.Create(0.0, screenPoint);
                    return ClosestPoint(borderPoints, screenPoint);
                default:
                    throw new InvalidOperationException();
            }
        }

        private Tuple<double, Point> ClosestPoint(Point[] screenVerticies, Point screenPoint)
        {
            var points = from i in Enumerable.Range(0, screenVerticies.Length - 1)
                         // translate line segment to screen coordinates
                         let p1 = (screenVerticies[i])
                         let p2 = (screenVerticies[i + 1])
                         let segment = new PrimitiveLine(p1, p2)
                         let closest = segment.ClosestPoint(screenPoint)
                         let dist = (closest - screenPoint).LengthSquared
                         orderby dist
                         // simple unproject via interpolation
                         let pct = (closest - p1).Length / (p2 - p1).Length
                         let vec = screenVerticies[i + 1] - screenVerticies[i]
                         let newLen = vec.Length * pct
                         let offset = vec.Normalize() * newLen
                         select Tuple.Create(dist, Unproject(screenVerticies[i] + offset));
            var selected = points.FirstOrDefault();
            return selected;
        }

        private void UpdateHotPoints()
        {
            hotPointLayer.Children.Clear();
            if (Workspace.IsCommandExecuting)
                return;
            foreach (var primitive in Workspace.SelectedEntities.SelectMany(entity => entity.GetPrimitives()))
            {
                switch (primitive.Kind)
                {
                    case PrimitiveKind.Ellipse:
                        var el = (PrimitiveEllipse)primitive;
                        AddHotPointIcon(el.Center);
                        if (el.IsClosed)
                        {
                            AddHotPointIcon(el.GetPoint(0.0));
                            AddHotPointIcon(el.GetPoint(90.0));
                            AddHotPointIcon(el.GetPoint(180.0));
                            AddHotPointIcon(el.GetPoint(270.0));
                        }
                        else
                        {
                            AddHotPointIcon(el.GetStartPoint());
                            AddHotPointIcon(el.MidPoint());
                            AddHotPointIcon(el.GetEndPoint());
                        }
                        break;
                    case PrimitiveKind.Line:
                        var line = (PrimitiveLine)primitive;
                        AddHotPointIcon(line.P1);
                        AddHotPointIcon((line.P1 + line.P2) * 0.5);
                        AddHotPointIcon(line.P2);
                        break;
                    case PrimitiveKind.Point:
                        var point = (PrimitivePoint)primitive;
                        AddHotPointIcon(point.Location);
                        break;
                    case PrimitiveKind.Text:
                        var text = (PrimitiveText)primitive;
                        AddHotPointIcon(text.Location);
                        break;
                }
            }
        }

        private void AddHotPointIcon(Point location)
        {
            var screen = Project(location);
            var size = Workspace.SettingsManager.EntitySelectionRadius;
            var a = new Point(screen.X - size, screen.Y + size, 0.0); // top left
            var b = new Point(screen.X + size, screen.Y + size, 0.0); // top right
            var c = new Point(screen.X + size, screen.Y - size, 0.0); // bottom right
            var d = new Point(screen.X - size, screen.Y - size, 0.0); // bottom left
            AddHotPointLine(a, b);
            AddHotPointLine(b, c);
            AddHotPointLine(c, d);
            AddHotPointLine(d, a);
        }

        private void AddHotPointLine(Point start, Point end)
        {
            var line = new Shapes.Line() { X1 = start.X, Y1 = start.Y, X2 = end.X, Y2 = end.Y, StrokeThickness = 2 };
            SetAutoBinding(line, Shapes.Shape.StrokeProperty, nameof(BindObject.HotPointBrush));
            hotPointLayer.Children.Add(line);
        }

        private void SetAutoBinding(DependencyObject element, DependencyProperty property, string path)
        {
            var binding = new Binding() { Path = new PropertyPath(path) };
            binding.Source = BindObject;
            BindingOperations.SetBinding(element, property, binding);
        }

        private void DrawSnapPoint(TransformedSnapPoint snapPoint, long drawId)
        {
            lock (lastDrawnSnapPointIdGate)
            {
                if (drawId > lastDrawnSnapPointId)
                {
                    lastDrawnSnapPointId = drawId;
                    BeginInvoke(() =>
                    {
                        ClearSnapPoints();
                        var dist = (snapPoint.ControlPoint - GetPointerPosition()).Length;
                        if (dist <= Workspace.SettingsManager.SnapPointDistance && snapPoint.Kind != SnapPointKind.None)
                        {
                            var geometry = snapPointGeometry[snapPoint.Kind];
                            var scale = Workspace.SettingsManager.SnapPointSize;
                            Canvas.SetLeft(geometry, snapPoint.ControlPoint.X - geometry.ActualWidth * scale / 2.0);
                            Canvas.SetTop(geometry, snapPoint.ControlPoint.Y - geometry.ActualHeight * scale / 2.0);
                            geometry.Visibility = Visibility.Visible;
                        }
                    });
                }
            }
        }

        private FrameworkElement GetSnapGeometry(SnapPointKind kind)
        {
            string name;
            switch (kind)
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
                case SnapPointKind.Focus:
                    name = "FocusPointIcon";
                    break;
                default:
                    throw new ArgumentException("snapPoint.Kind");
            }

            if (name == null)
                return null;

            var geometry = (Canvas)SnapPointResources[name];

#if WINDOWS_UWP
            // HACK FOR UWP: clone the canvas items by manually copying the children
            var shapes = new List<UIElement>();
            foreach (var shape in geometry.Children)
            {
                shapes.Add(shape);
            }

            geometry.Children.Clear();
            var created = new Canvas();
            foreach (var shape in shapes)
            {
                created.Children.Add(shape);
            }

            geometry = created;
#endif

            geometry.Visibility = Visibility.Collapsed;
            SetAutoBinding(geometry, RenderTransformProperty, nameof(BindObject.SnapPointTransform));
            geometry.DataContext = BindObject;
            return geometry;
        }
    }
}
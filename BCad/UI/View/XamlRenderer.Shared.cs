﻿using System;
using System.ComponentModel;
using BCad.EventArguments;
using BCad.Extensions;
using BCad.Primitives;

#if NETFX_CORE
// Metro
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;
using BCad.Metro.Extensions;
#else
// WPF
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
#endif

namespace BCad.UI.View
{
    public partial class XamlRenderer : UserControl
    {
        private IWorkspace Workspace;
        private Plane DisplayPlane;
        private BindingClass BindObject = new BindingClass();

        private class BindingClass : INotifyPropertyChanged
        {
            private double thickness = 0.0;
            public double Thickness
            {
                get { return thickness; }
                set
                {
                    if (thickness == value)
                        return;
                    thickness = value;
                    OnPropertyChanged("Thickness");
                }
            }

            private Brush autoBrush = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
            public Brush AutoBrush
            {
                get { return autoBrush; }
                set
                {
                    if (autoBrush == value)
                        return;
                    autoBrush = value;
                    OnPropertyChanged("AutoBrush");
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;

            private void OnPropertyChanged(string property)
            {
                var handler = PropertyChanged;
                if (handler != null)
                {
                    handler(this, new PropertyChangedEventArgs(property));
                }
            }
        }

        public void Initialize(IWorkspace workspace)
        {
            this.Workspace = workspace;

            Workspace.CommandExecuted += (_, __) => this.RubberBandCanvas.Children.Clear();
            Workspace.WorkspaceChanged += Workspace_WorkspaceChanged;
            Workspace.SettingsManager.PropertyChanged += SettingsManager_PropertyChanged;

            this.Loaded += (_, __) =>
                {
                    foreach (var setting in new[] { Constants.BackgroundColorString })
                        SettingsManager_PropertyChanged(Workspace.SettingsManager, new PropertyChangedEventArgs(setting));

                    RecalcTransform();
                };
            this.SizeChanged += (_, __) => RecalcTransform();
        }

        private void SettingsManager_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case Constants.BackgroundColorString:
                    this.Background = new SolidColorBrush(Workspace.SettingsManager.BackgroundColor.ToMediaColor());
                    var autoColor = Workspace.SettingsManager.BackgroundColor.GetAutoContrastingColor().ToMediaColor();
                    this.BindObject.AutoBrush = new SolidColorBrush(autoColor);
                    break;
            }
        }

        private void Workspace_WorkspaceChanged(object sender, WorkspaceChangeEventArgs e)
        {
            var redraw = false;
            if (e.IsActiveViewPortChange)
            {
                RecalcTransform();
            }
            if (e.IsDrawingChange)
            {
                redraw = true;
                this.RubberBandCanvas.Children.Clear();
            }

            if (redraw)
            {
                BeginInvoke(Redraw);
            }
        }

        private void RecalcTransform()
        {
            if (Workspace == null || Workspace.ViewControl == null)
                return;

            DisplayPlane = new Plane(Workspace.ActiveViewPort.BottomLeft, Workspace.ActiveViewPort.Sight);

            var scale = Workspace.ViewControl.DisplayHeight / Workspace.ActiveViewPort.ViewHeight;
            var t = new TransformGroup();
            t.Children.Add(new TranslateTransform() { X = -Workspace.ActiveViewPort.BottomLeft.X, Y = -Workspace.ActiveViewPort.BottomLeft.Y });
            t.Children.Add(new ScaleTransform() { ScaleX = scale, ScaleY = -scale });
            t.Children.Add(new TranslateTransform() { X = 0, Y = Workspace.ViewControl.DisplayHeight });
            this.PrimitiveCanvas.RenderTransform = t;
            this.RubberBandCanvas.RenderTransform = t;
            BindObject.Thickness = 1.0 / scale;

#if !NETFX_CORE
            this.Dispatcher.BeginInvoke((Action)(() => RenderRubberBandLines()));
#endif
        }

        private void BeginInvoke(Action action)
        {
#if !NETFX_CORE
                this.Dispatcher.BeginInvoke((Action)(() =>
                {
#endif
            action();
#if !NETFX_CORE
                }));
#endif
        }

        private void Redraw()
        {
            var drawing = Workspace.Drawing;
            this.PrimitiveCanvas.Children.Clear();
            foreach (var layer in drawing.GetLayers())
            {
                foreach (var entity in layer.GetEntities())
                {
                    foreach (var prim in entity.GetPrimitives())
                    {
                        AddPrimitive(this.PrimitiveCanvas, prim, GetColor(prim.Color, layer.Color));
                    }
                }
            }
        }

        private void AddPrimitive(Canvas canvas, IPrimitive prim, IndexedColor color)
        {
            switch (prim.Kind)
            {
                case PrimitiveKind.Ellipse:
                    AddPrimitiveEllipse(canvas, (PrimitiveEllipse)prim, color);
                    break;
                case PrimitiveKind.Line:
                    AddPrimitiveLine(canvas, (PrimitiveLine)prim, color);
                    break;
                case PrimitiveKind.Point:
                    break;
                case PrimitiveKind.Text:
                    AddPrimitiveText(canvas, (PrimitiveText)prim, color);
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        private static IndexedColor GetColor(IndexedColor primitiveColor, IndexedColor layerColor)
        {
            return primitiveColor.IsAuto ? layerColor : primitiveColor;
        }

        private void AddPrimitiveLine(Canvas canvas, PrimitiveLine line, IndexedColor color)
        {
            // project onto the drawing plane.  a render transform will take care of the display later
            var p1 = ProjectToPlane(line.P1);
            var p2 = ProjectToPlane(line.P2);
            var newLine = new Line() { X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y };
            SetThicknessBinding(newLine);
            SetColorBinding(newLine, color);
            canvas.Children.Add(newLine);
        }

        private void AddPrimitiveEllipse(Canvas canvas, PrimitiveEllipse ellipse, IndexedColor color)
        {
            // TODO: do a proper projection
            var center = ProjectToPlane(ellipse.Center);
            var radius = ellipse.MajorAxis.Length;
            var newEllipse = new Ellipse() { Height = radius * 2, Width = radius * 2 };
            Canvas.SetLeft(newEllipse, center.X - radius);
            Canvas.SetTop(newEllipse, center.Y - radius);
            SetThicknessBinding(newEllipse);
            SetColorBinding(newEllipse, color);
            canvas.Children.Add(newEllipse);
        }

        private void AddPrimitiveText(Canvas canvas, PrimitiveText text, IndexedColor color)
        {
            var location = ProjectToPlane(text.Location);
            var t = new TextBlock();
            t.Text = text.Value;
            t.FontSize = text.Height;
            var trans = new TransformGroup();
            trans.Children.Add(new ScaleTransform() { ScaleX = 1, ScaleY = -1 });
            trans.Children.Add(new RotateTransform() { Angle = text.Rotation, CenterX = location.X, CenterY = location.Y - text.Height * 2 });
            t.RenderTransform = trans;
            Canvas.SetLeft(t, location.X);
            Canvas.SetTop(t, location.Y + text.Height);
            if (color.IsAuto)
                SetBinding(t, "AutoBrush", TextBlock.ForegroundProperty);
            else
                t.Foreground = new SolidColorBrush(color.RealColor.ToMediaColor());
            canvas.Children.Add(t);
        }

        private Point ProjectToPlane(Point point)
        {
            return DisplayPlane.ToXYPlane(point);
        }

        private void SetBinding(FrameworkElement element, string propertyName, DependencyProperty property)
        {
            var binding = new Binding() { Path = new PropertyPath(propertyName) };
            binding.Source = BindObject;
            element.SetBinding(property, binding);
        }

        private void SetThicknessBinding(Shape shape)
        {
            SetBinding(shape, "Thickness", Shape.StrokeThicknessProperty);
        }

        private void SetColorBinding(Shape shape, IndexedColor color)
        {
            if (color.IsAuto)
            {
                SetBinding(shape, "AutoBrush", Shape.StrokeProperty);
            }
            else
            {
                shape.Stroke = new SolidColorBrush(color.RealColor.ToMediaColor());
            }
        }
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using FormDesigner.Models;
using System;
using System.Collections.Generic;

namespace FormDesigner.Views;

public partial class MainWindow
{
    private readonly Canvas _gridDefinitionOverlay = new() { Name = "GridDefinitionOverlay", Background = null };
    private sealed record GridEditorPointerTarget(bool Rows, int Index, bool Resize);
    private GridEditorPointerTarget? _gridEditorPointerTarget;
    private Point _gridEditorPointerStart;
    private bool IsGridEditorPointerInteraction => _designerInteractionMode is DesignerInteractionMode.SelectingGridRow
        or DesignerInteractionMode.SelectingGridColumn or DesignerInteractionMode.ResizingGridRow or DesignerInteractionMode.ResizingGridColumn;

    private void RenderNativeGridEditorOverlay()
    {
        if (_gridDefinitionOverlay.Parent is null)
        {
            DesignSurfaceHost.Children.Add(_gridDefinitionOverlay);
            _gridDefinitionOverlay.PointerMoved += GridEditor_PointerMoved;
            _gridDefinitionOverlay.PointerReleased += GridEditor_PointerReleased;
            _gridDefinitionOverlay.ZIndex = 10000;
        }
        _gridDefinitionOverlay.Children.Clear();
        _gridDefinitionOverlay.IsVisible = VM.UsesNativeLayout && VM.IsActiveLayoutGrid && VM.ShowGridEditorGuides && !VM.IsUserPreviewMode;
        if (!_gridDefinitionOverlay.IsVisible || !VM.NativeGridMeasurements.TryGetValue(VM.ActiveGridEditorId, out var grid)) return;
        Canvas.SetTop(_gridDefinitionOverlay, Canvas.GetTop(DesignerCanvas));
        _gridDefinitionOverlay.Width = VM.PreviewFormWidth + 124;
        _gridDefinitionOverlay.Height = VM.PreviewFormHeight + 32;
        var b = grid.Bounds;
        var selectedRows = VM.SelectedGridTrackIsRow;
        var index = selectedRows ? VM.SelectedLayoutGridRow?.Index : VM.SelectedLayoutGridColumn?.Index;
        var selectedTracks = selectedRows ? grid.Rows : grid.Columns;
        if (index is >= 0 && index < selectedTracks.Count)
        {
            double offset = 0; for (var i = 0; i < index; i++) offset += selectedTracks[i].Size;
            AddGridDecoration(new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(18, 37, 99, 235)) },
                selectedRows ? new Rect(b.X, b.Y + offset, b.Width, selectedTracks[index.Value].Size)
                    : new Rect(b.X + offset, b.Y, selectedTracks[index.Value].Size, b.Height));
        }
        AddGridDecoration(new Rectangle { Stroke = Brushes.RoyalBlue, StrokeThickness = 1 }, b);
        DrawTracks(true, grid.Rows); DrawTracks(false, grid.Columns);

        void DrawTracks(bool rows, IReadOnlyList<FormDesigner.DesignerSystem.AxamlRoundTrip.AxamlLayoutProjection.GridTrackMeasurement> tracks)
        {
            double offset = 0;
            for (var i = 0; i < tracks.Count; i++)
            {
                var track = tracks[i];
                var selected = selectedRows == rows && index == i;
                // Interactive rulers live outside the entire form, including for nested Grids.
                // Lines, highlights and in-form content never intercept child pointer events.
                var label = new Border
                {
                    Name = $"Grid{(rows ? "Row" : "Column")}Header_{i}",
                    Background = selected ? Brushes.LightBlue : Brushes.AliceBlue,
                    BorderBrush = Brushes.LightSteelBlue, BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3), Padding = new Thickness(3, 1),
                    Child = new TextBlock { Text = $"{(rows ? "R" : "C")}{i} · {track.Definition}", FontSize = 10, Foreground = Brushes.DarkBlue, TextTrimming = TextTrimming.CharacterEllipsis },
                    Tag = new GridEditorPointerTarget(rows, i, false)
                };
                ToolTip.SetTip(label, $"{(rows ? "Строка" : "Колонка")} {i}: {track.Definition}; фактически {track.Size:0.##} DIP. Выберите для редактирования.");
                if (track.Size > 1)
                {
                    AddGridInteractive(label, rows ? new Rect(VM.PreviewFormWidth + 6, b.Y + offset + 3, 110, Math.Min(22, Math.Max(1, track.Size - 6)))
                        : new Rect(b.X + offset + 4, VM.PreviewFormHeight + 6, Math.Min(110, Math.Max(1, track.Size - 8)), 22));
                }
                offset += track.Size;
                var line = new Line
                {
                    StartPoint = rows ? new Point(b.X, b.Y + offset) : new Point(b.X + offset, b.Y),
                    EndPoint = rows ? new Point(b.Right, b.Y + offset) : new Point(b.X + offset, b.Bottom),
                    Stroke = Brushes.RoyalBlue, StrokeThickness = 1, StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 3, 3 }, IsHitTestVisible = false
                };
                _gridDefinitionOverlay.Children.Add(line);
                if (i + 1 >= tracks.Count) continue;
                var grip = new Border
                {
                    Name = $"Grid{(rows ? "Row" : "Column")}Boundary_{i}",
                    Background = Brushes.SteelBlue, CornerRadius = new CornerRadius(2),
                    Cursor = new Cursor(rows ? StandardCursorType.SizeNorthSouth : StandardCursorType.SizeWestEast),
                    Tag = new GridEditorPointerTarget(rows, i, true)
                };
                ToolTip.SetTip(grip, "Resize: Pixel — фиксированный размер; Star/Star — пропорции. Auto и смешанные Star-пары изменяются через редактор.");
                AddGridInteractive(grip, rows ? new Rect(VM.PreviewFormWidth + 4, b.Y + offset - 3, 114, 6)
                    : new Rect(b.X + offset - 3, VM.PreviewFormHeight + 4, 6, 26));
            }
        }
    }

    private void AddGridDecoration(Control view, Rect bounds)
    {
        view.IsHitTestVisible = false; PlaceGridOverlay(view, bounds);
    }
    private void AddGridInteractive(Border view, Rect bounds)
    {
        view.PointerPressed += GridEditor_PointerPressed; PlaceGridOverlay(view, bounds);
    }
    private void PlaceGridOverlay(Control view, Rect bounds)
    {
        view.Width = Math.Max(0, bounds.Width); view.Height = Math.Max(0, bounds.Height);
        Canvas.SetLeft(view, bounds.X); Canvas.SetTop(view, bounds.Y); _gridDefinitionOverlay.Children.Add(view);
    }

    private void GridEditor_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Handled || _designerInteractionMode != DesignerInteractionMode.None || sender is not Border { Tag: GridEditorPointerTarget target }
            || !e.GetCurrentPoint(_gridDefinitionOverlay).Properties.IsLeftButtonPressed) return;
        TraceDesignerInteraction("DESIGNER_POINTER_PRESSED", e);
        e.Handled = true;
        VM.SelectGridTrack(target.Rows, target.Index);
        if (target.Resize)
        {
            if (!VM.NativeGridMeasurements.TryGetValue(VM.ActiveGridEditorId, out var grid)) return;
            var tracks = target.Rows ? grid.Rows : grid.Columns;
            if (!VM.BeginGridTrackResize(target.Rows, target.Index, tracks[target.Index].Size, tracks[target.Index + 1].Size)) return;
        }
        _gridEditorPointerTarget = target; _gridEditorPointerStart = GetDesignCanvasPosition(e);
        var mode = target.Resize
            ? (target.Rows ? DesignerInteractionMode.ResizingGridRow : DesignerInteractionMode.ResizingGridColumn)
            : (target.Rows ? DesignerInteractionMode.SelectingGridRow : DesignerInteractionMode.SelectingGridColumn);
        // The stable canvas retains capture when measured visuals/handles are rebuilt.
        CaptureDesignerInteraction(mode, e, _gridDefinitionOverlay, VM.ActiveGridEditorId);
    }

    private void GridEditor_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!IsGridEditorPointerInteraction || _gridEditorPointerTarget is not { } target || !DesignerPointerCanContinue(e)) return;
        e.Handled = true;
        if (!target.Resize) return;
        var position = GetDesignCanvasPosition(e);
        VM.UpdateGridTrackResize(target.Rows ? position.Y - _gridEditorPointerStart.Y : position.X - _gridEditorPointerStart.X);
        RenderDesigner();
    }

    private void GridEditor_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!IsGridEditorPointerInteraction || !ReferenceEquals(e.Pointer, _designerPointer)) return;
        TraceDesignerInteraction("DESIGNER_POINTER_RELEASED", e);
        VM.FinishGridTrackResize(_designerInteractionSession == VM.DocumentSessionId);
        _gridEditorPointerTarget = null;
        FinishDesignerInteraction("grid-editor-release", e);
        RenderDesigner(); e.Handled = true;
    }
}

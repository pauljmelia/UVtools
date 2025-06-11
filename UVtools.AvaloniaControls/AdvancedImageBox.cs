﻿/*
*                               The MIT License (MIT)
* Permission is hereby granted, free of charge, to any person obtaining a copy of
* this software and associated documentation files (the "Software"), to deal in
* the Software without restriction, including without limitation the rights to
* use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
* the Software, and to permit persons to whom the Software is furnished to do so.
*/
// Port from: https://github.com/cyotek/Cyotek.Windows.Forms.ImageBox to AvaloniaUI
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Runtime.CompilerServices;
using Avalonia.Controls.Presenters;
using Avalonia.Interactivity;
using Bitmap = Avalonia.Media.Imaging.Bitmap;
using Color = Avalonia.Media.Color;
using Pen = Avalonia.Media.Pen;
using Point = Avalonia.Point;
using Size = Avalonia.Size;

namespace UVtools.AvaloniaControls;

[TemplatePart("PART_MainGrid", typeof(Grid))]
[TemplatePart("PART_ContentPresenter", typeof(ScrollContentPresenter))] // ViewPort
[TemplatePart("PART_HorizontalScrollBar", typeof(ScrollBar))]
[TemplatePart("PART_VerticalScrollBar", typeof(ScrollBar))]
[TemplatePart("PART_ScrollBarsSeparator", typeof(Panel))]
public class AdvancedImageBox : TemplatedControl, IScrollable
{
    #region BindableBase
    /// <summary>
    ///     Multicast event for property change notifications.
    /// </summary>
    private PropertyChangedEventHandler? _propertyChanged;

    public new event PropertyChangedEventHandler? PropertyChanged
    {
        add { _propertyChanged -= value; _propertyChanged += value; }
        remove => _propertyChanged -= value;
    }

    protected bool RaiseAndSetIfChanged<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        RaisePropertyChanged(propertyName!);
        return true;
    }

    protected virtual void OnPropertyChanged(PropertyChangedEventArgs e)
    {
    }

    /// <summary>
    ///     Notifies listeners that a property value has changed.
    /// </summary>
    /// <param name="propertyName">
    ///     Name of the property used to notify listeners.  This
    ///     value is optional and can be provided automatically when invoked from compilers
    ///     that support <see cref="CallerMemberNameAttribute" />.
    /// </param>
    protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
    {
        var e = new PropertyChangedEventArgs(propertyName);
        OnPropertyChanged(e);
        _propertyChanged?.Invoke(this, e);
    }
    #endregion

    #region Sub Classes

    /// <summary>
    /// Represents available levels of zoom in an <see cref="AdvancedImageBox"/> control
    /// </summary>
    public class ZoomLevelCollection : IList<int>
    {
        #region Public Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="ZoomLevelCollection"/> class.
        /// </summary>
        public ZoomLevelCollection()
        {
            List = new SortedList<int, int>();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ZoomLevelCollection"/> class.
        /// </summary>
        /// <param name="collection">The default values to populate the collection with.</param>
        /// <exception cref="System.ArgumentNullException">Thrown if the <c>collection</c> parameter is null</exception>
        public ZoomLevelCollection(IEnumerable<int> collection)
            : this()
        {
            if (collection == null)
            {
                throw new ArgumentNullException(nameof(collection));
            }

            AddRange(collection);
        }

        #endregion

        #region Public Class Properties

        /// <summary>
        /// Returns the default zoom levels
        /// </summary>
        public static ZoomLevelCollection Default =>
            new([
                7, 10, 15, 20, 25, 30, 50, 70, 100, 150, 200, 300, 400, 500, 600, 700, 800, 1200, 1600, 3200, 6400
            ]);

        #endregion

        #region Public Properties

        /// <summary>
        /// Gets the number of elements contained in the <see cref="ZoomLevelCollection" />.
        /// </summary>
        /// <returns>
        /// The number of elements contained in the <see cref="ZoomLevelCollection" />.
        /// </returns>
        public int Count => List.Count;

        /// <summary>
        /// Gets a value indicating whether the <see cref="T:System.Collections.Generic.ICollection`1" /> is read-only.
        /// </summary>
        /// <value><c>true</c> if this instance is read only; otherwise, <c>false</c>.</value>
        /// <returns>true if the <see cref="T:System.Collections.Generic.ICollection`1" /> is read-only; otherwise, false.
        /// </returns>
        public bool IsReadOnly => false;

        /// <summary>
        /// Gets or sets the zoom level at the specified index.
        /// </summary>
        /// <param name="index">The index.</param>
        public int this[int index]
        {
            get => List.Values[index];
            set
            {
                List.RemoveAt(index);
                Add(value);
            }
        }

        #endregion

        #region Protected Properties

        /// <summary>
        /// Gets or sets the backing list.
        /// </summary>
        protected SortedList<int, int> List { get; set; }

        #endregion

        #region Public Members

        /// <summary>
        /// Adds an item to the <see cref="T:System.Collections.Generic.ICollection`1" />.
        /// </summary>
        /// <param name="item">The object to add to the <see cref="T:System.Collections.Generic.ICollection`1" />.</param>
        public void Add(int item)
        {
            List.Add(item, item);
        }

        /// <summary>
        /// Adds a range of items to the <see cref="ZoomLevelCollection"/>.
        /// </summary>
        /// <param name="collection">The items to add to the collection.</param>
        /// <exception cref="System.ArgumentNullException">Thrown if the <c>collection</c> parameter is null.</exception>
        public void AddRange(IEnumerable<int> collection)
        {
            if (collection == null)
            {
                throw new ArgumentNullException(nameof(collection));
            }

            foreach (int value in collection)
            {
                Add(value);
            }
        }

        /// <summary>
        /// Removes all items from the <see cref="T:System.Collections.Generic.ICollection`1" />.
        /// </summary>
        public void Clear()
        {
            List.Clear();
        }

        /// <summary>
        /// Determines whether the <see cref="T:System.Collections.Generic.ICollection`1" /> contains a specific value.
        /// </summary>
        /// <param name="item">The object to locate in the <see cref="T:System.Collections.Generic.ICollection`1" />.</param>
        /// <returns>true if <paramref name="item" /> is found in the <see cref="T:System.Collections.Generic.ICollection`1" />; otherwise, false.</returns>
        public bool Contains(int item)
        {
            return List.ContainsKey(item);
        }

        /// <summary>
        /// Copies a range of elements this collection into a destination <see cref="Array"/>.
        /// </summary>
        /// <param name="array">The <see cref="Array"/> that receives the data.</param>
        /// <param name="arrayIndex">A 64-bit integer that represents the index in the <see cref="Array"/> at which storing begins.</param>
        public void CopyTo(int[] array, int arrayIndex)
        {
            for (int i = 0; i < Count; i++)
            {
                array[arrayIndex + i] = List.Values[i];
            }
        }

        /// <summary>
        /// Finds the index of a zoom level matching or nearest to the specified value.
        /// </summary>
        /// <param name="zoomLevel">The zoom level.</param>
        public int FindNearest(int zoomLevel)
        {
            int nearestValue = List.Values[0];
            int nearestDifference = Math.Abs(nearestValue - zoomLevel);
            for (int i = 1; i < Count; i++)
            {
                int value = List.Values[i];
                int difference = Math.Abs(value - zoomLevel);
                if (difference < nearestDifference)
                {
                    nearestValue = value;
                    nearestDifference = difference;
                }
            }
            return nearestValue;
        }

        /// <summary>
        /// Returns an enumerator that iterates through the collection.
        /// </summary>
        /// <returns>A <see cref="T:System.Collections.Generic.IEnumerator`1" /> that can be used to iterate through the collection.</returns>
        public IEnumerator<int> GetEnumerator()
        {
            return List.Values.GetEnumerator();
        }

        /// <summary>
        /// Determines the index of a specific item in the <see cref="T:System.Collections.Generic.IList`1" />.
        /// </summary>
        /// <param name="item">The object to locate in the <see cref="T:System.Collections.Generic.IList`1" />.</param>
        /// <returns>The index of <paramref name="item" /> if found in the list; otherwise, -1.</returns>
        public int IndexOf(int item)
        {
            return List.IndexOfKey(item);
        }

        /// <summary>
        /// Not implemented.
        /// </summary>
        /// <param name="index">The index.</param>
        /// <param name="item">The item.</param>
        /// <exception cref="System.NotImplementedException">Not implemented</exception>
        public void Insert(int index, int item)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Returns the next increased zoom level for the given current zoom.
        /// </summary>
        /// <param name="zoomLevel">The current zoom level.</param>
        /// <param name="constrainZoomLevel">When positive, constrain maximum zoom to this value</param>
        /// <returns>The next matching increased zoom level for the given current zoom if applicable, otherwise the nearest zoom.</returns>
        public int NextZoom(int zoomLevel, int constrainZoomLevel = 0)
        {
            var index = IndexOf(FindNearest(zoomLevel));
            if (index < Count - 1) index++;

            return constrainZoomLevel > 0 && this[index] >= constrainZoomLevel ? constrainZoomLevel : this[index];
        }

        /// <summary>
        /// Returns the next decreased zoom level for the given current zoom.
        /// </summary>
        /// <param name="zoomLevel">The current zoom level.</param>
        /// <param name="constrainZoomLevel">When positive, constrain minimum zoom to this value</param>
        /// <returns>The next matching decreased zoom level for the given current zoom if applicable, otherwise the nearest zoom.</returns>
        public int PreviousZoom(int zoomLevel, int constrainZoomLevel = 0)
        {
            var index = IndexOf(FindNearest(zoomLevel));
            if (index > 0) index--;

            return constrainZoomLevel > 0 && this[index] <= constrainZoomLevel ? constrainZoomLevel : this[index];
        }

        /// <summary>
        /// Removes the first occurrence of a specific object from the <see cref="T:System.Collections.Generic.ICollection`1" />.
        /// </summary>
        /// <param name="item">The object to remove from the <see cref="T:System.Collections.Generic.ICollection`1" />.</param>
        /// <returns>true if <paramref name="item" /> was successfully removed from the <see cref="T:System.Collections.Generic.ICollection`1" />; otherwise, false. This method also returns false if <paramref name="item" /> is not found in the original <see cref="T:System.Collections.Generic.ICollection`1" />.</returns>
        public bool Remove(int item)
        {
            return List.Remove(item);
        }

        /// <summary>
        /// Removes the element at the specified index of the <see cref="ZoomLevelCollection"/>.
        /// </summary>
        /// <param name="index">The zero-based index of the element to remove.</param>
        public void RemoveAt(int index)
        {
            List.RemoveAt(index);
        }

        /// <summary>
        /// Copies the elements of the <see cref="ZoomLevelCollection"/> to a new array.
        /// </summary>
        /// <returns>An array containing copies of the elements of the <see cref="ZoomLevelCollection"/>.</returns>
        public int[] ToArray()
        {
            var results = new int[Count];
            CopyTo(results, 0);

            return results;
        }

        #endregion

        #region IList<int> Members

        /// <summary>
        /// Returns an enumerator that iterates through a collection.
        /// </summary>
        /// <returns>An <see cref="ZoomLevelCollection" /> object that can be used to iterate through the collection.</returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        #endregion
    }

    #endregion

    #region Enums

    /// <summary>
    /// Determines the sizing mode of an image hosted in an <see cref="AdvancedImageBox" /> control.
    /// </summary>
    public enum SizeModes
    {
        /// <summary>
        /// The image is displayed according to current zoom and scroll properties.
        /// </summary>
        Normal,

        /// <summary>
        /// The image is stretched to fill the client area of the control.
        /// </summary>
        Stretch,

        /// <summary>
        /// The image is stretched to fill as much of the client area of the control as possible, whilst retaining the same aspect ratio for the width and height.
        /// </summary>
        Fit
    }

    [Flags]
    public enum MouseButtons
    {
        None = 0,
        LeftButton = 1,
        MiddleButton = 2,
        RightButton = 4
    }

    public enum MouseWheelZoomBehaviours
    {
        /// <summary>
        /// No action is performed when using the mouse wheel.
        /// </summary>
        None,

        /// <summary>
        /// Zoom in and out in a native way using the mouse wheel delta.
        /// </summary>
        ZoomNative,

        /// <summary>
        /// Zoom in and out in a native way using the mouse wheel delta, but change to tick levels when holding ALT key.
        /// </summary>
        ZoomNativeAltLevels,

        /// <summary>
        /// Zoom in and out using tick levels defined in the <see cref="AdvancedImageBox.ZoomLevels"/> collection.
        /// </summary>
        ZoomLevels,

        /// <summary>
        /// Zoom in and out using tick levels defined in the <see cref="AdvancedImageBox.ZoomLevels"/> collection, but change to native when holding ALT key.
        /// </summary>
        ZoomLevelsAltNative,
    }

    /// <summary>
    /// Describes the zoom action occurring
    /// </summary>
    [Flags]
    public enum ZoomActions
    {
        /// <summary>
        /// No action.
        /// </summary>
        None = 0,

        /// <summary>
        /// The control is increasing the zoom.
        /// </summary>
        ZoomIn = 1,

        /// <summary>
        /// The control is decreasing the zoom.
        /// </summary>
        ZoomOut = 2,

        /// <summary>
        /// The control zoom was reset.
        /// </summary>
        ActualSize = 4
    }

    public enum SelectionModes
    {
        /// <summary>
        ///   No selection.
        /// </summary>
        None,

        /// <summary>
        ///   Rectangle selection.
        /// </summary>
        Rectangle,

        /// <summary>
        ///   Zoom selection.
        /// </summary>
        Zoom
    }

    #endregion

    #region UI Controls
    /// <inheritdoc />
    public Size Extent
    {
        get
        {
            var viewPort = ViewPort;
            if (viewPort is null) return default;
            return new(Math.Max(viewPort.Bounds.Width, ScaledImageWidth),
                Math.Max(viewPort.Bounds.Height, ScaledImageHeight));
        }
    }

    /// <inheritdoc />
    public Vector Offset
    {
        get
        {
            var horizontalScrollBar = HorizontalScrollBar;
            if (horizontalScrollBar is null) return default;

            var verticalScrollBar = VerticalScrollBar;
            if (verticalScrollBar is null) return default;

            return new Vector(horizontalScrollBar.Value, verticalScrollBar.Value);
        }
        set
        {
            var horizontalScrollBar = HorizontalScrollBar;
            if (horizontalScrollBar is null) return;

            var verticalScrollBar = VerticalScrollBar;
            if (verticalScrollBar is null) return;

            horizontalScrollBar.Value = value.X;
            verticalScrollBar.Value = value.Y;
            RaisePropertyChanged();
            TriggerRender();
        }
    }

    /// <inheritdoc />
    public Size Viewport => ViewPort?.Bounds.Size ?? Bounds.Size;
    #endregion

    #region Private Members

    protected internal ScrollContentPresenter? ViewPort;
    protected internal ScrollBar? HorizontalScrollBar;
    protected internal ScrollBar? VerticalScrollBar;

    private Point _startMousePosition;
    private Vector _startScrollPosition;
    private bool _isPanning;
    private bool _isSelecting;
    private Bitmap? _trackerImage;
    private bool _canRender = true;
    private Point _pointerPosition;
    ZoomLevelCollection _zoomLevels = ZoomLevelCollection.Default;
    private int _oldZoom = 100;

    #endregion

    #region Properties
    public static readonly DirectProperty<AdvancedImageBox, bool> CanRenderProperty =
        AvaloniaProperty.RegisterDirect<AdvancedImageBox, bool>(
            nameof(CanRender),
            o => o.CanRender);

    /// <summary>
    /// Gets or sets if control can render the image
    /// </summary>
    public bool CanRender
    {
        get => _canRender;
        set
        {
            if (!SetAndRaise(CanRenderProperty, ref _canRender, value)) return;
            if (_canRender) TriggerRender();
        }
    }

    public static readonly StyledProperty<byte> GridCellSizeProperty =
        AvaloniaProperty.Register<AdvancedImageBox, byte>(nameof(GridCellSize), 15);

    /// <summary>
    /// Gets or sets the grid cell size
    /// </summary>
    public byte GridCellSize
    {
        get => GetValue(GridCellSizeProperty);
        set => SetValue(GridCellSizeProperty, value);
    }

    public static readonly StyledProperty<ISolidColorBrush> GridColorProperty =
        AvaloniaProperty.Register<AdvancedImageBox, ISolidColorBrush>(nameof(GridColor), Brushes.Gainsboro);

    /// <summary>
    /// Gets or sets the color used to create the checkerboard style background
    /// </summary>
    public ISolidColorBrush GridColor
    {
        get => GetValue(GridColorProperty);
        set => SetValue(GridColorProperty, value);
    }

    public static readonly StyledProperty<ISolidColorBrush> GridColorAlternateProperty =
        AvaloniaProperty.Register<AdvancedImageBox, ISolidColorBrush>(nameof(GridColorAlternate), Brushes.White);

    /// <summary>
    /// Gets or sets the color used to create the checkerboard style background
    /// </summary>
    public ISolidColorBrush GridColorAlternate
    {
        get => GetValue(GridColorAlternateProperty);
        set => SetValue(GridColorAlternateProperty, value);
    }

    public static readonly StyledProperty<Bitmap?> ImageProperty =
        AvaloniaProperty.Register<AdvancedImageBox, Bitmap?>(nameof(Image));

    /// <summary>
    /// Gets or sets the image to be displayed
    /// </summary>
    public Bitmap? Image
    {
        get => GetValue(ImageProperty);
        set => SetValue(ImageProperty, value);
    }

    /// <summary>
    /// Gets the image as a writeable bitmap
    /// </summary>
    public WriteableBitmap? ImageAsWriteableBitmap
    {
        get
        {
            var image = Image;
            if (image is null) return null;
            return (WriteableBitmap)image;
        }
    }

    /// <summary>
    /// Returns true if image is loaded, otherwise false.
    /// </summary>
    [MemberNotNullWhen(true, nameof(Image))]
    public bool IsImageLoaded => Image is not null;

    public static readonly DirectProperty<AdvancedImageBox, Bitmap?> TrackerImageProperty =
        AvaloniaProperty.RegisterDirect<AdvancedImageBox, Bitmap?>(
            nameof(TrackerImage),
            o => o.TrackerImage,
            (o, v) => o.TrackerImage = v);

    /// <summary>
    /// Gets or sets an image to follow the mouse pointer
    /// </summary>
    public Bitmap? TrackerImage
    {
        get => _trackerImage;
        set
        {
            if (!SetAndRaise(TrackerImageProperty, ref _trackerImage, value)) return;
            TriggerRender();
            RaisePropertyChanged(nameof(HaveTrackerImage));
        }
    }

    public bool HaveTrackerImage => _trackerImage is not null;

    public static readonly StyledProperty<bool> TrackerImageAutoZoomProperty =
        AvaloniaProperty.Register<AdvancedImageBox, bool>(nameof(TrackerImageAutoZoom), true);

    /// <summary>
    /// Gets or sets if the tracker image will be scaled to the current zoom
    /// </summary>
    public bool TrackerImageAutoZoom
    {
        get => GetValue(TrackerImageAutoZoomProperty);
        set => SetValue(TrackerImageAutoZoomProperty, value);
    }

    public bool IsHorizontalBarVisible
    {
        get
        {
            if (!IsImageLoaded) return false;
            if (SizeMode != SizeModes.Normal) return false;
            return ScaledImageWidth > Viewport.Width;
        }
    }

    public bool IsVerticalBarVisible
    {
        get
        {
            if (Image is null) return false;
            if (SizeMode != SizeModes.Normal) return false;
            return ScaledImageHeight > Viewport.Height;
        }
    }

    public static readonly StyledProperty<bool> ShowGridProperty =
        AvaloniaProperty.Register<AdvancedImageBox, bool>(nameof(ShowGrid), true);

    /// <summary>
    /// Gets or sets the grid visibility when reach high zoom levels
    /// </summary>
    public bool ShowGrid
    {
        get => GetValue(ShowGridProperty);
        set => SetValue(ShowGridProperty, value);
    }

    public static readonly DirectProperty<AdvancedImageBox, Point> PointerPositionProperty =
        AvaloniaProperty.RegisterDirect<AdvancedImageBox, Point>(
            nameof(PointerPosition),
            o => o.PointerPosition);

    /// <summary>
    /// Gets the current pointer position
    /// </summary>
    public Point PointerPosition
    {
        get => _pointerPosition;
        private set => SetAndRaise(PointerPositionProperty, ref _pointerPosition, value);
    }

    public static readonly DirectProperty<AdvancedImageBox, bool> IsPanningProperty =
        AvaloniaProperty.RegisterDirect<AdvancedImageBox, bool>(
            nameof(IsPanning),
            o => o.IsPanning);

    /// <summary>
    /// Gets if control is currently panning
    /// </summary>
    public bool IsPanning
    {
        get => _isPanning;
        protected set
        {
            if (!SetAndRaise(IsPanningProperty, ref _isPanning, value)) return;
            _startScrollPosition = Offset;

            if (value)
            {
                Cursor = new Cursor(StandardCursorType.SizeAll);
                //this.OnPanStart(EventArgs.Empty);
            }
            else
            {
                Cursor = Cursor.Default;
                //this.OnPanEnd(EventArgs.Empty);
            }
        }
    }

    public static readonly DirectProperty<AdvancedImageBox, bool> IsSelectingProperty =
        AvaloniaProperty.RegisterDirect<AdvancedImageBox, bool>(
            nameof(IsSelecting),
            o => o.IsSelecting);

    /// <summary>
    /// Gets if control is currently selecting a ROI
    /// </summary>
    public bool IsSelecting
    {
        get => _isSelecting;
        protected set => SetAndRaise(IsSelectingProperty, ref _isSelecting, value);
    }

    /// <summary>
    /// Gets the center point of the viewport
    /// </summary>
    public Point CenterPoint
    {
        get
        {
            var viewport = GetImageViewPort();
            return new(viewport.Width / 2, viewport.Height / 2);
        }
    }

    public static readonly StyledProperty<bool> AutoPanProperty =
        AvaloniaProperty.Register<AdvancedImageBox, bool>(nameof(AutoPan), true);

    /// <summary>
    /// Gets or sets if the control can pan with the mouse
    /// </summary>
    public bool AutoPan
    {
        get => GetValue(AutoPanProperty);
        set => SetValue(AutoPanProperty, value);
    }

    public static readonly StyledProperty<MouseButtons> PanWithMouseButtonsProperty =
        AvaloniaProperty.Register<AdvancedImageBox, MouseButtons>(nameof(PanWithMouseButtons), MouseButtons.LeftButton | MouseButtons.MiddleButton | MouseButtons.RightButton);

    /// <summary>
    /// Gets or sets the mouse buttons to pan the image
    /// </summary>
    public MouseButtons PanWithMouseButtons
    {
        get => GetValue(PanWithMouseButtonsProperty);
        set => SetValue(PanWithMouseButtonsProperty, value);
    }

    public static readonly StyledProperty<int> PanOffsetProperty =
        AvaloniaProperty.Register<AdvancedImageBox, int>(nameof(PanOffset), 20);

    /// <summary>
    /// Gets or sets the pan offset to displace everytime a key is pressed
    /// </summary>
    public int PanOffset
    {
        get => GetValue(PanOffsetProperty);
        set => SetValue(PanOffsetProperty, value);
    }

    public static readonly StyledProperty<bool> PanWithArrowsProperty =
        AvaloniaProperty.Register<AdvancedImageBox, bool>(nameof(PanWithArrows), true);

    /// <summary>
    /// Gets or sets if the control can pan with the keyboard arrows
    /// </summary>
    public bool PanWithArrows
    {
        get => GetValue(PanWithArrowsProperty);
        set => SetValue(PanWithArrowsProperty, value);
    }


    public static readonly StyledProperty<Key?> PanLeftKeyProperty =
        AvaloniaProperty.Register<AdvancedImageBox, Key?>(nameof(PanLeftKey));

    /// <summary>
    /// Gets or sets the key to pan left
    /// </summary>
    public Key? PanLeftKey
    {
        get => GetValue(PanLeftKeyProperty);
        set => SetValue(PanLeftKeyProperty, value);
    }

    public static readonly StyledProperty<Key?> PanUpKeyProperty =
        AvaloniaProperty.Register<AdvancedImageBox, Key?>(nameof(PanUpKey));

    /// <summary>
    /// Gets or sets the key to pan up
    /// </summary>
    public Key? PanUpKey
    {
        get => GetValue(PanUpKeyProperty);
        set => SetValue(PanUpKeyProperty, value);
    }

    public static readonly StyledProperty<Key?> PanRightKeyProperty =
        AvaloniaProperty.Register<AdvancedImageBox, Key?>(nameof(PanRightKey));

    /// <summary>
    /// Gets or sets the key to pan right
    /// </summary>
    public Key? PanRightKey
    {
        get => GetValue(PanRightKeyProperty);
        set => SetValue(PanRightKeyProperty, value);
    }

    public static readonly StyledProperty<Key?> PanDownKeyProperty =
        AvaloniaProperty.Register<AdvancedImageBox, Key?>(nameof(PanDownKey));

    /// <summary>
    /// Gets or sets the key to pan down
    /// </summary>
    public Key? PanDownKey
    {
        get => GetValue(PanDownKeyProperty);
        set => SetValue(PanDownKeyProperty, value);
    }

    public static readonly StyledProperty<MouseButtons> SelectWithMouseButtonsProperty =
        AvaloniaProperty.Register<AdvancedImageBox, MouseButtons>(nameof(SelectWithMouseButtons), MouseButtons.LeftButton | MouseButtons.RightButton);

    /// <summary>
    /// Gets or sets the mouse buttons to select a region on image
    /// </summary>
    public MouseButtons SelectWithMouseButtons
    {
        get => GetValue(SelectWithMouseButtonsProperty);
        set => SetValue(SelectWithMouseButtonsProperty, value);
    }

    public static readonly StyledProperty<bool> InvertMousePanProperty =
        AvaloniaProperty.Register<AdvancedImageBox, bool>(nameof(InvertMousePan), false);

    /// <summary>
    /// Gets or sets if mouse pan is inverted
    /// </summary>
    public bool InvertMousePan
    {
        get => GetValue(InvertMousePanProperty);
        set => SetValue(InvertMousePanProperty, value);
    }

    public static readonly StyledProperty<bool> AutoCenterProperty =
        AvaloniaProperty.Register<AdvancedImageBox, bool>(nameof(AutoCenter), true);

    /// <summary>
    /// Gets or sets if image is auto centered
    /// </summary>
    public bool AutoCenter
    {
        get => GetValue(AutoCenterProperty);
        set => SetValue(AutoCenterProperty, value);
    }

    public static readonly StyledProperty<SizeModes> SizeModeProperty =
        AvaloniaProperty.Register<AdvancedImageBox, SizeModes>(nameof(SizeMode), SizeModes.Normal);

    /// <summary>
    /// Gets or sets the image size mode
    /// </summary>
    public SizeModes SizeMode
    {
        get => GetValue(SizeModeProperty);
        set => SetValue(SizeModeProperty, value);
    }

    private void SizeModeChanged()
    {
        var horizontalScrollBar = HorizontalScrollBar;
        if (horizontalScrollBar is null) return;

        var verticalScrollBar = VerticalScrollBar;
        if (verticalScrollBar is null) return;

        switch (SizeMode)
        {
            case SizeModes.Normal:
                horizontalScrollBar.Visibility = ScrollBarVisibility.Auto;
                verticalScrollBar.Visibility = ScrollBarVisibility.Auto;
                break;
            case SizeModes.Stretch:
            case SizeModes.Fit:
                horizontalScrollBar.Visibility = ScrollBarVisibility.Hidden;
                verticalScrollBar.Visibility = ScrollBarVisibility.Hidden;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(SizeMode), SizeMode, null);
        }
    }

    public static readonly StyledProperty<int> HorizontalScrollWithMouseFactorProperty =
        AvaloniaProperty.Register<AdvancedImageBox, int>(nameof(HorizontalScrollWithMouseFactor), 100);

    /// <summary>
    /// Gets or sets the factor over the delta to scroll horizontally with the mouse (Left and right button on supported mice).
    /// </summary>
    /// <remarks>Set to 0 to disable horizontal scroll with mouse buttons.</remarks>
    public int HorizontalScrollWithMouseFactor
    {
        get => GetValue(HorizontalScrollWithMouseFactorProperty);
        set => SetValue(HorizontalScrollWithMouseFactorProperty, value);
    }

    public static readonly StyledProperty<int> HorizontalScrollWithMouseAlternativeFactorProperty =
        AvaloniaProperty.Register<AdvancedImageBox, int>(nameof(HorizontalScrollWithMouseAlternativeFactor), 50);

    /// <summary>
    /// Gets or sets the alternative (ALT modifier) factor over the delta to scroll horizontally with the mouse (Left and right button on supported mice).
    /// </summary>
    /// <remarks>Set to 0 to disable the alternative horizontal scroll with mouse buttons.</remarks>
    public int HorizontalScrollWithMouseAlternativeFactor
    {
        get => GetValue(HorizontalScrollWithMouseAlternativeFactorProperty);
        set => SetValue(HorizontalScrollWithMouseAlternativeFactorProperty, value);
    }


    public static readonly StyledProperty<int> VerticalScrollWithMouseFactorProperty =
        AvaloniaProperty.Register<AdvancedImageBox, int>(nameof(VerticalScrollWithMouseFactor), 100);

    public static readonly StyledProperty<KeyModifiers?> VerticalScrollWithMouseWheelKeyModifierProperty =
        AvaloniaProperty.Register<AdvancedImageBox, KeyModifiers?>(nameof(VerticalScrollWithMouseWheelKeyModifier), KeyModifiers.Control);

    /// <summary>
    /// Gets or sets the required <see cref="KeyModifiers"/> to enable the vertical scroll with the mouse wheel.
    /// </summary>
    /// <remarks>Set <c>null</c> to disable vertical scroll with the mouse wheel.</remarks>
    public KeyModifiers? VerticalScrollWithMouseWheelKeyModifier
    {
        get => GetValue(VerticalScrollWithMouseWheelKeyModifierProperty);
        set => SetValue(VerticalScrollWithMouseWheelKeyModifierProperty, value);
    }

    /// <summary>
    /// Gets or sets the factor over the delta to scroll vertically with the mouse wheel.
    /// </summary>
    /// <remarks>Set to 0 to disable horizontal scroll with mouse buttons.</remarks>
    public int VerticalScrollWithMouseFactor
    {
        get => GetValue(VerticalScrollWithMouseFactorProperty);
        set => SetValue(VerticalScrollWithMouseFactorProperty, value);
    }

    public static readonly StyledProperty<int> VerticalScrollWithMouseAlternativeFactorProperty =
        AvaloniaProperty.Register<AdvancedImageBox, int>(nameof(VerticalScrollWithMouseAlternativeFactor), 50);

    /// <summary>
    /// Gets or sets the alternative (ALT modifier) factor over the delta to scroll vertically with the mouse wheel.
    /// </summary>
    /// <remarks>Set to 0 to disable the alternative horizontal scroll with mouse buttons.</remarks>
    public int VerticalScrollWithMouseAlternativeFactor
    {
        get => GetValue(VerticalScrollWithMouseAlternativeFactorProperty);
        set => SetValue(VerticalScrollWithMouseAlternativeFactorProperty, value);
    }

    public static readonly StyledProperty<MouseWheelZoomBehaviours> ZoomWithMouseWheelBehaviourProperty =
        AvaloniaProperty.Register<AdvancedImageBox, MouseWheelZoomBehaviours>(nameof(ZoomWithMouseWheelBehaviour), MouseWheelZoomBehaviours.ZoomNativeAltLevels);

    /// <summary>
    /// Gets or sets the mouse wheel behaviour.
    /// </summary>
    public MouseWheelZoomBehaviours ZoomWithMouseWheelBehaviour
    {
        get => GetValue(ZoomWithMouseWheelBehaviourProperty);
        set => SetValue(ZoomWithMouseWheelBehaviourProperty, value);
    }

    public static readonly StyledProperty<KeyModifiers> ZoomWithMouseWheelKeyModifierProperty =
        AvaloniaProperty.Register<AdvancedImageBox, KeyModifiers>(nameof(ZoomWithMouseWheelKeyModifier));

    /// <summary>
    /// Gets or sets the required <see cref="KeyModifiers"/> to work with any of <see cref="ZoomWithMouseWheelBehaviour"/> zoom behaviours.
    /// </summary>
    /// <remarks>Set <para>null</para> to ignore modifiers.</remarks>
    public KeyModifiers ZoomWithMouseWheelKeyModifier
    {
        get => GetValue(ZoomWithMouseWheelKeyModifierProperty);
        set => SetValue(ZoomWithMouseWheelKeyModifierProperty, value);
    }

    public static readonly StyledProperty<bool> ZoomWithMouseWheelStrictKeyModifierProperty =
        AvaloniaProperty.Register<AdvancedImageBox, bool>(nameof(ZoomWithMouseWheelStrictKeyModifier));

    /// <summary>
    /// Gets or sets to use strict key modifier to work with <see cref="ZoomWithMouseWheelKeyModifier"/>.
    /// When true it will check if modifiers exactly match the required modifier,
    /// otherwise it will perform a bitwise inclusion check.
    /// </summary>
    public bool ZoomWithMouseWheelStrictKeyModifier
    {
        get => GetValue(ZoomWithMouseWheelStrictKeyModifierProperty);
        set => SetValue(ZoomWithMouseWheelStrictKeyModifierProperty, value);
    }

    public static readonly StyledProperty<int> ZoomWithMouseWheelDebounceMillisecondsProperty =
        AvaloniaProperty.Register<AdvancedImageBox, int>(nameof(ZoomWithMouseWheelDebounceMilliseconds), 20);

    /// <summary>
    /// Gets or sets the debounce milliseconds to perform zoom with mouse wheel
    /// </summary>
    public int ZoomWithMouseWheelDebounceMilliseconds
    {
        get => GetValue(ZoomWithMouseWheelDebounceMillisecondsProperty);
        set => SetValue(ZoomWithMouseWheelDebounceMillisecondsProperty, value);
    }

    private ulong _lastZoomWithMouseWheelTimestamp;

    public static readonly DirectProperty<AdvancedImageBox, ZoomLevelCollection> ZoomLevelsProperty =
        AvaloniaProperty.RegisterDirect<AdvancedImageBox, ZoomLevelCollection>(
            nameof(ZoomLevels),
            o => o.ZoomLevels,
            (o, v) => o.ZoomLevels = v);

    /// <summary>
    ///   Gets or sets the zoom levels.
    /// </summary>
    /// <value>The zoom levels.</value>
    public ZoomLevelCollection ZoomLevels
    {
        get => _zoomLevels;
        set => SetAndRaise(ZoomLevelsProperty, ref _zoomLevels, value);
    }

    public static readonly StyledProperty<int> MinZoomProperty =
        AvaloniaProperty.Register<AdvancedImageBox, int>(nameof(MinZoom), 10);

    /// <summary>
    /// Gets or sets the minimum possible zoom.
    /// </summary>
    /// <value>The zoom.</value>
    public int MinZoom
    {
        get => GetValue(MinZoomProperty);
        set => SetValue(MinZoomProperty, value);
    }

    public static readonly StyledProperty<int> MaxZoomProperty =
        AvaloniaProperty.Register<AdvancedImageBox, int>(nameof(MaxZoom), 6400);

    /// <summary>
    /// Gets or sets the maximum possible zoom.
    /// </summary>
    /// <value>The zoom.</value>
    public int MaxZoom
    {
        get => GetValue(MaxZoomProperty);
        set => SetValue(MaxZoomProperty, value);
    }

    public static readonly StyledProperty<bool> ConstrainZoomOutToFitLevelProperty =
        AvaloniaProperty.Register<AdvancedImageBox, bool>(nameof(ConstrainZoomOutToFitLevel));

    /// <summary>
    /// Gets or sets if the zoom out should constrain to fit image as the lowest zoom level.
    /// </summary>
    public bool ConstrainZoomOutToFitLevel
    {
        get => GetValue(ConstrainZoomOutToFitLevelProperty);
        set => SetValue(ConstrainZoomOutToFitLevelProperty, value);
    }


    public static readonly DirectProperty<AdvancedImageBox, int> OldZoomProperty =
        AvaloniaProperty.RegisterDirect<AdvancedImageBox, int>(
            nameof(OldZoom),
            o => o.OldZoom);

    /// <summary>
    /// Gets the previous zoom value
    /// </summary>
    /// <value>The zoom.</value>
    public int OldZoom
    {
        get => _oldZoom;
        private set => SetAndRaise(OldZoomProperty, ref _oldZoom, value);
    }

    public static readonly StyledProperty<int> ZoomProperty =
        AvaloniaProperty.Register<AdvancedImageBox, int>(nameof(Zoom), 100);

    /// <summary>
    ///  Gets or sets the zoom.
    /// </summary>
    /// <value>The zoom.</value>
    public int Zoom
    {
        get => GetValue(ZoomProperty);
        set
        {
            var minZoom = MinZoom;
            if (ConstrainZoomOutToFitLevel) minZoom = Math.Max(ZoomLevelToFit, minZoom);
            var newZoom = Math.Clamp(value, minZoom, MaxZoom);

            var previousZoom = Zoom;
            if (previousZoom == newZoom) return;
            OldZoom = previousZoom;
            SetValue(ZoomProperty, newZoom);
        }
    }

    /// <summary>
    /// <para>Gets if the image have zoom.</para>
    /// <para>True if zoomed in or out</para>
    /// <para>False if no zoom applied</para>
    /// </summary>
    public bool IsActualSize => Zoom == 100;

    /// <summary>
    /// Gets the zoom factor, the zoom / 100.0
    /// </summary>
    public double ZoomFactor => Zoom / 100.0;

    /// <summary>
    /// Gets the zoom to fit level which shows all the image
    /// </summary>
    public int ZoomLevelToFit
    {
        get
        {
            var image = Image;
            if (image is null) return 100;

            double zoom;
            double aspectRatio;
            var viewportWidth = Bounds.Size.Width;
            var viewportHeight = Bounds.Size.Height;

            if (image.Size.Width > image.Size.Height)
            {
                aspectRatio = viewportWidth / image.Size.Width;
                zoom = aspectRatio * 100.0;

                if (viewportHeight < image.Size.Height * zoom / 100.0)
                {
                    aspectRatio = viewportHeight / image.Size.Height;
                    zoom = aspectRatio * 100.0;
                }
            }
            else
            {
                aspectRatio = viewportHeight / image.Size.Height;
                zoom = aspectRatio * 100.0;

                if (viewportWidth < image.Size.Width * zoom / 100.0)
                {
                    aspectRatio = viewportWidth / image.Size.Width;
                    zoom = aspectRatio * 100.0;
                }
            }

            return zoom <= 0 ? 100 : (int) zoom;
        }
    }

    public static readonly StyledProperty<bool> AutoZoomToFitProperty =
        AvaloniaProperty.Register<AdvancedImageBox, bool>(nameof(AutoZoomToFitProperty));

    /// <summary>
    /// Gets or sets if the zoom level should be auto set to fit when loading a new image.
    /// </summary>
    /// <remarks>Requires <see cref="SizeMode"/> to be <see cref="SizeModes.Normal"/>.</remarks>
    public bool AutoZoomToFit
    {
        get => GetValue(AutoZoomToFitProperty);
        set => SetValue(AutoZoomToFitProperty, value);
    }


    public static readonly StyledProperty<KeyGesture[]?> ZoomInKeyGesturesProperty =
        AvaloniaProperty.Register<AdvancedImageBox, KeyGesture[]?>(nameof(ZoomInKeyGestures),
            OperatingSystem.IsMacOS()
                ? [new KeyGesture(Key.Add, KeyModifiers.Meta), new KeyGesture(Key.OemPlus, KeyModifiers.Meta)]
                : [new KeyGesture(Key.Add, KeyModifiers.Control), new KeyGesture(Key.OemPlus, KeyModifiers.Control)]
            );

    /// <summary>
    /// Gets or sets the hot key to zoom in
    /// </summary>
    public KeyGesture[]? ZoomInKeyGestures
    {
        get => GetValue(ZoomInKeyGesturesProperty);
        set => SetValue(ZoomInKeyGesturesProperty, value);
    }

    public static readonly StyledProperty<KeyGesture[]?> ZoomOutKeyGesturesProperty =
        AvaloniaProperty.Register<AdvancedImageBox, KeyGesture[]?>(nameof(ZoomOutKeyGestures),
            OperatingSystem.IsMacOS()
                ? [new KeyGesture(Key.Subtract, KeyModifiers.Meta), new KeyGesture(Key.OemMinus, KeyModifiers.Meta)]
                : [new KeyGesture(Key.Subtract, KeyModifiers.Control), new KeyGesture(Key.OemMinus, KeyModifiers.Control)]
            );

    /// <summary>
    /// Gets or sets the hot key to zoom out
    /// </summary>
    public KeyGesture[]? ZoomOutKeyGestures
    {
        get => GetValue(ZoomOutKeyGesturesProperty);
        set => SetValue(ZoomOutKeyGesturesProperty, value);
    }

    public static readonly StyledProperty<KeyGesture[]?> ZoomTo100KeyGesturesProperty =
        AvaloniaProperty.Register<AdvancedImageBox, KeyGesture[]?>(nameof(ZoomTo100KeyGestures),
            OperatingSystem.IsMacOS()
                ? [new KeyGesture(Key.D0, KeyModifiers.Meta), new KeyGesture(Key.NumPad0, KeyModifiers.Meta)]
                : [new KeyGesture(Key.D0, KeyModifiers.Control), new KeyGesture(Key.NumPad0, KeyModifiers.Control)]
            );

    /// <summary>
    /// Gets or sets the hot key to zoom to 100%
    /// </summary>
    public KeyGesture[]? ZoomTo100KeyGestures
    {
        get => GetValue(ZoomTo100KeyGesturesProperty);
        set => SetValue(ZoomTo100KeyGesturesProperty, value);
    }

    public static readonly StyledProperty<KeyGesture[]?> ZoomToFitKeyGesturesProperty =
        AvaloniaProperty.Register<AdvancedImageBox, KeyGesture[]?>(nameof(ZoomToFitKeyGestures),
            OperatingSystem.IsMacOS()
                ? [new KeyGesture(Key.D0, KeyModifiers.Meta | KeyModifiers.Alt), new KeyGesture(Key.NumPad0, KeyModifiers.Meta | KeyModifiers.Alt)]
                : [new KeyGesture(Key.D0, KeyModifiers.Control | KeyModifiers.Alt), new KeyGesture(Key.NumPad0, KeyModifiers.Control | KeyModifiers.Alt)]
        );

    /// <summary>
    /// Gets or sets the hot key to zoom to fit
    /// </summary>
    public KeyGesture[]? ZoomToFitKeyGestures
    {
        get => GetValue(ZoomToFitKeyGesturesProperty);
        set => SetValue(ZoomToFitKeyGesturesProperty, value);
    }


    /// <summary>
    /// Gets the size of the scaled image.
    /// </summary>
    /// <value>The size of the scaled image.</value>
    public Size ScaledImageSize => new(ScaledImageWidth, ScaledImageHeight);

    /// <summary>
    /// Gets the width of the scaled image.
    /// </summary>
    /// <value>The width of the scaled image.</value>
    public double ScaledImageWidth => Image?.Size.Width * ZoomFactor ?? 0;

    /// <summary>
    /// Gets the height of the scaled image.
    /// </summary>
    /// <value>The height of the scaled image.</value>
    public double ScaledImageHeight => Image?.Size.Height * ZoomFactor ?? 0;

    public static readonly StyledProperty<ISolidColorBrush> PixelGridColorProperty =
        AvaloniaProperty.Register<AdvancedImageBox, ISolidColorBrush>(nameof(PixelGridColor), Brushes.DimGray);

    /// <summary>
    /// Gets or sets the color of the pixel grid.
    /// </summary>
    /// <value>The color of the pixel grid.</value>
    public ISolidColorBrush PixelGridColor
    {
        get => GetValue(PixelGridColorProperty);
        set => SetValue(PixelGridColorProperty, value);
    }

    public static readonly StyledProperty<int> PixelGridZoomThresholdProperty =
        AvaloniaProperty.Register<AdvancedImageBox, int>(nameof(PixelGridZoomThreshold), 5);

    /// <summary>
    /// Gets or sets the minimum size of zoomed pixel's before the pixel grid will be drawn
    /// </summary>
    /// <value>The pixel grid threshold.</value>

    public int PixelGridZoomThreshold
    {
        get => GetValue(PixelGridZoomThresholdProperty);
        set => SetValue(PixelGridZoomThresholdProperty, value);
    }

    public static readonly StyledProperty<SelectionModes> SelectionModeProperty =
        AvaloniaProperty.Register<AdvancedImageBox, SelectionModes>(nameof(SelectionMode), SelectionModes.None);

    public SelectionModes SelectionMode
    {
        get => GetValue(SelectionModeProperty);
        set => SetValue(SelectionModeProperty, value);
    }

    public static readonly StyledProperty<ISolidColorBrush> SelectionColorProperty =
        AvaloniaProperty.Register<AdvancedImageBox, ISolidColorBrush>(nameof(SelectionColor), new SolidColorBrush(new Color(127, 0, 128, 255)));

    public ISolidColorBrush SelectionColor
    {
        get => GetValue(SelectionColorProperty);
        set => SetValue(SelectionColorProperty, value);
    }

    public static readonly StyledProperty<Rect> SelectionRegionProperty =
        AvaloniaProperty.Register<AdvancedImageBox, Rect>(nameof(SelectionRegion));


    public Rect SelectionRegion
    {
        get => GetValue(SelectionRegionProperty);
        set
        {
            SetValue(SelectionRegionProperty, value);
            //if (!RaiseAndSetIfChanged(ref _selectionRegion, value)) return;
            TriggerRender();
            RaisePropertyChanged(nameof(HaveSelection));
            RaisePropertyChanged(nameof(SelectionRegionNet));
            RaisePropertyChanged(nameof(SelectionRegionPixel));
        }
    }

    public Rectangle SelectionRegionNet
    {
        get
        {
            var rect = SelectionRegion;
            return new ((int) Math.Ceiling(rect.X), (int)Math.Ceiling(rect.Y), (int)rect.Width, (int)rect.Height);
        }
    }

    public PixelRect SelectionRegionPixel
    {
        get
        {
            var rect = SelectionRegion;
            return new ((int)Math.Ceiling(rect.X), (int)Math.Ceiling(rect.Y), (int)rect.Width, (int)rect.Height);
        }
    }

    public bool HaveSelection => SelectionRegion != default;
    #endregion

    #region Constructor

    static AdvancedImageBox()
    {
        FocusableProperty.OverrideDefaultValue(typeof(AdvancedImageBox), true);
        AffectsRender<AdvancedImageBox>(
            ShowGridProperty,
            GridCellSizeProperty,
            GridColorProperty,
            GridColorAlternateProperty,
            PixelGridColorProperty,
            //ImageProperty,
            SelectionRegionProperty
            );
    }

    public AdvancedImageBox()
    {
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
    }

    #endregion

    #region Overrides

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (ViewPort is not null)
        {
            ViewPort.PointerPressed -= ViewPortOnPointerPressed;
            ViewPort.PointerExited -= ViewPortOnPointerExited;
            ViewPort.PointerMoved -= ViewPortOnPointerMoved;
            ViewPort.PointerWheelChanged -= ViewPortOnPointerWheelChanged;
        }

        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (HorizontalScrollBar is not null)
        {
            HorizontalScrollBar.Scroll += ScrollBarOnScroll;
        }

        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (VerticalScrollBar is not null)
        {
            VerticalScrollBar.Scroll += ScrollBarOnScroll;
        }

        ViewPort = e.NameScope.Find<ScrollContentPresenter>("PART_ContentPresenter")!;
        HorizontalScrollBar = e.NameScope.Find<ScrollBar>("PART_HorizontalScrollBar")!;
        VerticalScrollBar = e.NameScope.Find<ScrollBar>("PART_VerticalScrollBar")!;

        SizeModeChanged();

        ViewPort.PointerPressed += ViewPortOnPointerPressed;
        ViewPort.PointerExited += ViewPortOnPointerExited;
        ViewPort.PointerMoved += ViewPortOnPointerMoved;
        ViewPort.PointerWheelChanged += ViewPortOnPointerWheelChanged;

        HorizontalScrollBar.Scroll += ScrollBarOnScroll;
        VerticalScrollBar.Scroll += ScrollBarOnScroll;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        UpdateViewPort();
        if (IsImageLoaded)
        {
            if (AutoZoomToFit)
            {
                Zoom = ZoomLevelToFit;
            }
            else if (ConstrainZoomOutToFitLevel)
            {
                var zoomLevelToFit = ZoomLevelToFit;
                if (Zoom < zoomLevelToFit)
                {
                    Zoom = zoomLevelToFit;
                }
            }
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateViewPort();
        e.Handled = true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (!IsLoaded) return;
        if (ReferenceEquals(e.Property, ImageProperty))
        {
            UpdateViewPort();

            if (!IsImageLoaded)
            {
                SelectNone();
            }
            else
            {
                if (AutoZoomToFit)
                {
                    Zoom = ZoomLevelToFit;
                }
                else if (ConstrainZoomOutToFitLevel)
                {
                    var zoomLevelToFit = ZoomLevelToFit;
                    if (Zoom < zoomLevelToFit)
                    {
                        Zoom = zoomLevelToFit;
                    }
                }
            }

            TriggerRender();

            RaisePropertyChanged(nameof(ImageAsWriteableBitmap));
            RaisePropertyChanged(nameof(IsImageLoaded));
            RaisePropertyChanged(nameof(ScaledImageWidth));
            RaisePropertyChanged(nameof(ScaledImageHeight));
            RaisePropertyChanged(nameof(ScaledImageSize));
            RaisePropertyChanged(nameof(Extent));
            RaisePropertyChanged(nameof(ZoomLevelToFit));
        }
        else if (ReferenceEquals(e.Property, SizeModeProperty))
        {
            SizeModeChanged();
            TriggerRender();
            RaisePropertyChanged(nameof(IsHorizontalBarVisible));
            RaisePropertyChanged(nameof(IsVerticalBarVisible));
        }
        else if (ReferenceEquals(e.Property, ZoomProperty))
        {
            UpdateViewPort();
            TriggerRender();
            RaisePropertyChanged(nameof(IsHorizontalBarVisible));
            RaisePropertyChanged(nameof(IsVerticalBarVisible));
            RaisePropertyChanged(nameof(IsActualSize));
            RaisePropertyChanged(nameof(ZoomFactor));
            RaisePropertyChanged(nameof(ScaledImageWidth));
            RaisePropertyChanged(nameof(ScaledImageHeight));
            RaisePropertyChanged(nameof(ScaledImageSize));
            RaisePropertyChanged(nameof(Extent));
        }
        else if(ReferenceEquals(e.Property, PaddingProperty))
        {
            UpdateViewPort();
            TriggerRender();
        }
    }

    #endregion

    #region Render methods
    public void TriggerRender(bool renderOnlyCursorTracker = false)
    {
        if (!_canRender) return;
        if (renderOnlyCursorTracker && _trackerImage is null) return;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        //Debug.WriteLine($"Render: {DateTime.Now.Ticks}");
        base.Render(context);

        var viewPortSize = Viewport;
        // Draw Grid
        var gridCellSize = GridCellSize;
        if (ShowGrid & gridCellSize > 0 && (!IsHorizontalBarVisible || !IsVerticalBarVisible))
        {
            // draw the background
            var gridColor = GridColor;
            var altColor = GridColorAlternate;
            var currentColor = gridColor;
            for (int y = 0; y < viewPortSize.Height; y += gridCellSize)
            {
                var firstRowColor = currentColor;

                for (int x = 0; x < viewPortSize.Width; x += gridCellSize)
                {
                    context.FillRectangle(currentColor, new Rect(x, y, gridCellSize, gridCellSize));
                    currentColor = ReferenceEquals(currentColor, gridColor) ? altColor : gridColor;
                }

                if (Equals(firstRowColor, currentColor))
                    currentColor = ReferenceEquals(currentColor, gridColor) ? altColor : gridColor;
            }

        }
        /*else
        {
            context.FillRectangle(Background, new Rect(0, 0, Viewport.Width, Viewport.Height));
        }*/

        var image = Image;
        if (image is null) return;
        var imageViewPort = GetImageViewPort();

        // Draw image
        context.DrawImage(image,
            GetSourceImageRegion(),
            imageViewPort
        );

        var zoomFactor = ZoomFactor;

        if (HaveTrackerImage && _pointerPosition is {X: >= 0, Y: >= 0})
        {
            var destSize = TrackerImageAutoZoom
                ? new Size(_trackerImage!.Size.Width * zoomFactor, _trackerImage.Size.Height * zoomFactor)
                : image.Size;

            var destPos = new Point(
                _pointerPosition.X - destSize.Width / 2,
                _pointerPosition.Y - destSize.Height / 2
            );
            context.DrawImage(_trackerImage!, new Rect(destPos, destSize));
        }

        //SkiaContext.SkCanvas.dr
        // Draw pixel grid
        if (SizeMode == SizeModes.Normal && zoomFactor > PixelGridZoomThreshold)
        {
            var offsetX = Offset.X % zoomFactor;
            var offsetY = Offset.Y % zoomFactor;

            Pen pen = new(PixelGridColor);
            for (double x = imageViewPort.X + zoomFactor - offsetX; x < imageViewPort.Right; x += zoomFactor)
            {
                context.DrawLine(pen, new Point(x, imageViewPort.Y), new Point(x, imageViewPort.Bottom));
            }

            for (double y = imageViewPort.Y + zoomFactor - offsetY; y < imageViewPort.Bottom; y += zoomFactor)
            {
                context.DrawLine(pen, new Point(imageViewPort.X, y), new Point(imageViewPort.Right, y));
            }

            context.DrawRectangle(pen, imageViewPort);
        }

        if (SelectionRegion != default)
        {
            var rect = GetOffsetRectangle(SelectionRegion);
            var selectionColor = SelectionColor;
            context.FillRectangle(selectionColor, rect);
            var color = Color.FromArgb(255, selectionColor.Color.R, selectionColor.Color.G, selectionColor.Color.B);
            context.DrawRectangle(new Pen(color.ToUInt32()), rect);
        }
    }

    private bool UpdateViewPort()
    {
        var horizontalScrollBar = HorizontalScrollBar;
        if (horizontalScrollBar is null) return false;

        var verticalScrollBar = VerticalScrollBar;
        if (verticalScrollBar is null) return false;

        if (!IsImageLoaded || SizeMode != SizeModes.Normal)
        {
            horizontalScrollBar.Maximum = 0;
            verticalScrollBar.Maximum = 0;
            return true;
        }

        var scaledImageWidth = ScaledImageWidth;
        var scaledImageHeight = ScaledImageHeight;
        var width = Math.Max(0, scaledImageWidth - horizontalScrollBar.ViewportSize);
        var height = Math.Max(0, scaledImageHeight - verticalScrollBar.ViewportSize);
        //var width = scaledImageWidth <= Viewport.Width ? Viewport.Width : scaledImageWidth;
        //var height = scaledImageHeight <= Viewport.Height ? Viewport.Height : scaledImageHeight;

        bool changed = false;
        if (Math.Abs(horizontalScrollBar.Maximum - width) > 0.01)
        {
            horizontalScrollBar.Maximum = width;
            changed = true;
        }

        if (Math.Abs(verticalScrollBar.Maximum - height) > 0.01)
        {
            verticalScrollBar.Maximum = height;
            changed = true;
        }

        /*if (changed)
        {
            var newContainer = new ContentControl
            {
                Width = width,
                Height = height
            };
            FillContainer.Content = SizedContainer = newContainer;
            Debug.WriteLine($"Updated ViewPort: {DateTime.Now.Ticks}");
            //TriggerRender();
        }*/

        return changed;
    }
    #endregion

    #region Events and Overrides

    private void ScrollBarOnScroll(object? sender, ScrollEventArgs e)
    {
        TriggerRender();
    }

    /*protected override void OnScrollChanged(ScrollChangedEventArgs e)
    {
        Debug.WriteLine($"ViewportDelta: {e.ViewportDelta} | OffsetDelta: {e.OffsetDelta} | ExtentDelta: {e.ExtentDelta}");
        if (!e.ViewportDelta.IsDefault)
        {
            UpdateViewPort();
        }

        TriggerRender();

        base.OnScrollChanged(e);
    }*/

    private void ViewPortOnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!IsImageLoaded || SizeMode != SizeModes.Normal) return;

        // Process horizontal scroll
        if (e.Delta.X != 0 && IsHorizontalBarVisible)
        {
            var factor = (e.KeyModifiers & KeyModifiers.Alt) != 0 ? HorizontalScrollWithMouseAlternativeFactor : HorizontalScrollWithMouseFactor;
            if (factor != 0)
            {
                Offset = Offset.WithX(Offset.X - e.Delta.X * factor);
                e.Handled = true;
            }
        }

        // Process vertical scroll
        if (e.Delta.Y == 0) return;

        var verticalScrollWithMouseWheelKeyModifier = VerticalScrollWithMouseWheelKeyModifier;
        if (verticalScrollWithMouseWheelKeyModifier.HasValue && (e.KeyModifiers & verticalScrollWithMouseWheelKeyModifier) == verticalScrollWithMouseWheelKeyModifier)
        {
            if (!IsVerticalBarVisible) return;
            var factor = (e.KeyModifiers & KeyModifiers.Alt) != 0 ? VerticalScrollWithMouseAlternativeFactor : VerticalScrollWithMouseFactor;
            if (factor != 0)
            {
                Offset = Offset.WithY(Offset.Y - e.Delta.Y * factor);
            }

            e.Handled = true;
            return;
        }


        var mouseWheelBehaviour = ZoomWithMouseWheelBehaviour;
        if (mouseWheelBehaviour == MouseWheelZoomBehaviours.None) return;

        /*
#if DEBUG
        //File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "UVtoolsScroll.txt")
        //, $"WheelEvent {{Timestamp: {e.Timestamp}, Handled: {e.Handled}, Delta: {e.Delta}}}{Environment.NewLine}");
#endif
        */

        var zoomWithMouseWheelKeyModifier = ZoomWithMouseWheelKeyModifier;
        var canZoom = ZoomWithMouseWheelStrictKeyModifier switch
        {
            false => (e.KeyModifiers & zoomWithMouseWheelKeyModifier) == zoomWithMouseWheelKeyModifier,
            true => e.KeyModifiers == zoomWithMouseWheelKeyModifier
        };

        if (!canZoom) return;
        e.Handled = true;

        // Debounce for sensitive touchpads
        var zoomWithMouseWheelDebounceMilliseconds = ZoomWithMouseWheelDebounceMilliseconds;
        if (zoomWithMouseWheelDebounceMilliseconds > 0 && e.Timestamp - _lastZoomWithMouseWheelTimestamp < (ulong)zoomWithMouseWheelDebounceMilliseconds) return;

        // The MouseWheel event can contain multiple "spins" of the wheel so we need to adjust accordingly
        //double spins = Math.Abs(e.Delta.Y);
        //Debug.WriteLine(e.GetPosition(this));
        // TODO: Really should update the source method to handle multiple increments rather than calling it multiple times
        /*for (int i = 0; i < spins; i++)
        {*/


        switch (mouseWheelBehaviour)
        {
            case MouseWheelZoomBehaviours.ZoomNative:
                SetZoom(Zoom + (int)(e.Delta.Y * 100), e.GetPosition(ViewPort));
                break;
            case MouseWheelZoomBehaviours.ZoomNativeAltLevels:
                if ((e.KeyModifiers & KeyModifiers.Alt) == 0)
                {
                    SetZoom(Zoom + (int)(e.Delta.Y * 100), e.GetPosition(ViewPort));
                }
                else
                {
                    PerformZoom(e.Delta.Y > 0 ? ZoomActions.ZoomIn : ZoomActions.ZoomOut, e.GetPosition(ViewPort));
                }
                break;
            case MouseWheelZoomBehaviours.ZoomLevels:
                PerformZoom(e.Delta.Y > 0 ? ZoomActions.ZoomIn : ZoomActions.ZoomOut, e.GetPosition(ViewPort));
                break;
            case MouseWheelZoomBehaviours.ZoomLevelsAltNative:
                if ((e.KeyModifiers & KeyModifiers.Alt) == 0)
                {
                    PerformZoom(e.Delta.Y > 0 ? ZoomActions.ZoomIn : ZoomActions.ZoomOut, e.GetPosition(ViewPort));
                }
                else
                {
                    SetZoom(Zoom + (int)(e.Delta.Y * 100), e.GetPosition(ViewPort));
                }
                break;
        }

        _lastZoomWithMouseWheelTimestamp = e.Timestamp;
        //}
    }

    private void ViewPortOnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Handled
            || _isPanning
            || _isSelecting
            || Image is null) return;

        var pointer = e.GetCurrentPoint(this);

        if (SelectionMode != SelectionModes.None)
        {
            if (!(
                    pointer.Properties.IsLeftButtonPressed && (SelectWithMouseButtons & MouseButtons.LeftButton) != 0 ||
                    pointer.Properties.IsMiddleButtonPressed && (SelectWithMouseButtons & MouseButtons.MiddleButton) != 0 ||
                    pointer.Properties.IsRightButtonPressed && (SelectWithMouseButtons & MouseButtons.RightButton) != 0
                )
               ) return;
            IsSelecting = true;
        }
        else
        {
            if (!(
                    pointer.Properties.IsLeftButtonPressed && (PanWithMouseButtons & MouseButtons.LeftButton) != 0 ||
                    pointer.Properties.IsMiddleButtonPressed && (PanWithMouseButtons & MouseButtons.MiddleButton) != 0 ||
                    pointer.Properties.IsRightButtonPressed && (PanWithMouseButtons & MouseButtons.RightButton) != 0
                )
                || !AutoPan
                || SizeMode != SizeModes.Normal

               ) return;

            IsPanning = true;
        }

        var location = pointer.Position;

        if (location.X > Viewport.Width) return;
        if (location.Y > Viewport.Height) return;
        _startMousePosition = location;
    }

    /*protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (e.Handled
            || _isPanning
            || _isSelecting
            || Image is null) return;

        var pointer = e.GetCurrentPoint(this);

        if (SelectionMode != SelectionModes.None)
        {
            if (!(
                    pointer.Properties.IsLeftButtonPressed && (SelectWithMouseButtons & MouseButtons.LeftButton) != 0 ||
                    pointer.Properties.IsMiddleButtonPressed && (SelectWithMouseButtons & MouseButtons.MiddleButton) != 0 ||
                    pointer.Properties.IsRightButtonPressed && (SelectWithMouseButtons & MouseButtons.RightButton) != 0
                )
               ) return;
            IsSelecting = true;
        }
        else
        {
            if (!(
                    pointer.Properties.IsLeftButtonPressed && (PanWithMouseButtons & MouseButtons.LeftButton) != 0 ||
                    pointer.Properties.IsMiddleButtonPressed && (PanWithMouseButtons & MouseButtons.MiddleButton) != 0 ||
                    pointer.Properties.IsRightButtonPressed && (PanWithMouseButtons & MouseButtons.RightButton) != 0
                )
                || !AutoPan
                || SizeMode != SizeModes.Normal

               ) return;

            IsPanning = true;
        }

        var location = pointer.Position;

        if (location.X > ViewPortSize.Width) return;
        if (location.Y > ViewPortSize.Height) return;
        _startMousePosition = location;
    }*/

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.Handled) return;

        IsPanning = false;
        IsSelecting = false;
    }

    private void ViewPortOnPointerExited(object? sender, PointerEventArgs e)
    {
        PointerPosition = new Point(-1, -1);
        TriggerRender(true);
        e.Handled = true;
    }

    /*protected override void OnPointerLeave(PointerEventArgs e)
    {
        base.OnPointerLeave(e);
        PointerPosition = new Point(-1, -1);
        TriggerRender(true);
        e.Handled = true;
    }*/

    private void ViewPortOnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (e.Handled) return;

        var viewPort = ViewPort;
        if (viewPort is null)
        {
            e.Handled = true;
            return;
        }

        var pointer = e.GetCurrentPoint(viewPort);
        PointerPosition = pointer.Position;

        if (!_isPanning && !_isSelecting)
        {
            TriggerRender(true);
            return;
        }

        if (_isPanning)
        {
            double x;
            double y;

            if (!InvertMousePan)
            {
                x = _startScrollPosition.X + (_startMousePosition.X - _pointerPosition.X);
                y = _startScrollPosition.Y + (_startMousePosition.Y - _pointerPosition.Y);
            }
            else
            {
                x = (_startScrollPosition.X - (_startMousePosition.X - _pointerPosition.X));
                y = (_startScrollPosition.Y - (_startMousePosition.Y - _pointerPosition.Y));
            }

            Offset = new Vector(x, y);
        }
        else if (_isSelecting)
        {
            var viewPortPoint = new Point(
                Math.Min(_pointerPosition.X, viewPort.Bounds.Right),
                Math.Min(_pointerPosition.Y, viewPort.Bounds.Bottom));

            double x;
            double y;
            double w;
            double h;

            var imageOffset = GetImageViewPort().Position;

            if (viewPortPoint.X < _startMousePosition.X)
            {
                x = viewPortPoint.X;
                w = _startMousePosition.X - viewPortPoint.X;
            }
            else
            {
                x = _startMousePosition.X;
                w = viewPortPoint.X - _startMousePosition.X;
            }

            if (viewPortPoint.Y < _startMousePosition.Y)
            {
                y = viewPortPoint.Y;
                h = _startMousePosition.Y - viewPortPoint.Y;
            }
            else
            {
                y = _startMousePosition.Y;
                h = viewPortPoint.Y - _startMousePosition.Y;
            }

            x -= imageOffset.X - Offset.X;
            y -= imageOffset.Y - Offset.Y;

            var zoomFactor = ZoomFactor;
            x /= zoomFactor;
            y /= zoomFactor;
            w /= zoomFactor;
            h /= zoomFactor;

            if (w > 0 && h > 0)
            {
                SelectionRegion = FitRectangle(new Rect(x, y, w, h));
            }
        }

        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (!IsImageLoaded || SizeMode != SizeModes.Normal) return;

        var zoomInKeyGestures = ZoomInKeyGestures;
        if (zoomInKeyGestures is not null)
        {
            foreach (var zoomInKeyGesture in zoomInKeyGestures)
            {
                if (e.KeyModifiers == zoomInKeyGesture.KeyModifiers && e.Key == zoomInKeyGesture.Key)
                {
                    ZoomIn();
                    e.Handled = true;
                    return;
                }
            }
        }

        var zoomOutKeyGestures = ZoomOutKeyGestures;
        if (zoomOutKeyGestures is not null)
        {
            foreach (var zoomOutKeyGesture in zoomOutKeyGestures)
            {
                if (e.KeyModifiers == zoomOutKeyGesture.KeyModifiers && e.Key == zoomOutKeyGesture.Key)
                {
                    ZoomOut();
                    e.Handled = true;
                    return;
                }
            }
        }

        if (e.KeyModifiers != KeyModifiers.None) return;

        var panLeft = false;
        var panUp = false;
        var panRight = false;
        var panDown = false;

        if (PanWithArrows)
        {
            switch (e.Key)
            {
                case Key.Left:
                    panLeft = true;
                    break;
                case Key.Up:
                    panUp = true;
                    break;
                case Key.Right:
                    panRight = true;
                    break;
                case Key.Down:
                    panDown = true;
                    break;
            }
        }

        if (e.Key == PanLeftKey)
        {
            panLeft = true;
        }
        else if (e.Key == PanUpKey)
        {
            panUp = true;
        }
        else if (e.Key == PanRightKey)
        {
            panRight = true;
        }
        else if (e.Key == PanDownKey)
        {
            panDown = true;
        }

        if (panLeft)
        {
            Offset = new Vector(Offset.X - PanOffset * ZoomFactor, Offset.Y);
            e.Handled = true;
            return;
        }

        if (panUp)
        {
            Offset = new Vector(Offset.X, Offset.Y - PanOffset * ZoomFactor);
            e.Handled = true;
            return;
        }

        if (panRight)
        {
            Offset = new Vector(Offset.X + PanOffset * ZoomFactor, Offset.Y);
            e.Handled = true;
            return;
        }

        if (panDown)
        {
            Offset = new Vector(Offset.X, Offset.Y + PanOffset * ZoomFactor);
            e.Handled = true;
            return;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (!IsImageLoaded || SizeMode != SizeModes.Normal) return;

        var zoomTo100KeyGestures = ZoomTo100KeyGestures;
        if (zoomTo100KeyGestures is not null && Zoom != 100)
        {
            foreach (var zoomTo100KeyGesture in zoomTo100KeyGestures)
            {
                if (e.KeyModifiers == zoomTo100KeyGesture.KeyModifiers && e.Key == zoomTo100KeyGesture.Key)
                {
                    Zoom = 100;
                    e.Handled = true;
                    return;
                }
            }
        }

        var zoomToFitKeyGestures = ZoomToFitKeyGestures;
        if (zoomToFitKeyGestures is not null)
        {
            foreach (var zoomToFitKeyGesture in zoomToFitKeyGestures)
            {
                if (e.KeyModifiers == zoomToFitKeyGesture.KeyModifiers && e.Key == zoomToFitKeyGesture.Key)
                {
                    Zoom = ZoomLevelToFit;
                    e.Handled = true;
                    return;
                }
            }
        }
    }


    /*protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.Handled || !ViewPort.IsPointerOver) return;

        var pointer = e.GetCurrentPoint(ViewPort);
        PointerPosition = pointer.Position;

        if (!_isPanning && !_isSelecting)
        {
            TriggerRender(true);
            return;
        }

        if (_isPanning)
        {
            double x;
            double y;

            if (!InvertMousePan)
            {
                x = _startScrollPosition.X + (_startMousePosition.X - _pointerPosition.X);
                y = _startScrollPosition.Y + (_startMousePosition.Y - _pointerPosition.Y);
            }
            else
            {
                x = (_startScrollPosition.X - (_startMousePosition.X - _pointerPosition.X));
                y = (_startScrollPosition.Y - (_startMousePosition.Y - _pointerPosition.Y));
            }

            Offset = new Vector(x, y);
        }
        else if (_isSelecting)
        {
            double x;
            double y;
            double w;
            double h;

            var imageOffset = GetImageViewPort().Position;

            if (_pointerPosition.X < _startMousePosition.X)
            {
                x = _pointerPosition.X;
                w = _startMousePosition.X - _pointerPosition.X;
            }
            else
            {
                x = _startMousePosition.X;
                w = _pointerPosition.X - _startMousePosition.X;
            }

            if (_pointerPosition.Y < _startMousePosition.Y)
            {
                y = _pointerPosition.Y;
                h = _startMousePosition.Y - _pointerPosition.Y;
            }
            else
            {
                y = _startMousePosition.Y;
                h = _pointerPosition.Y - _startMousePosition.Y;
            }

            x -= imageOffset.X - Offset.X;
            y -= imageOffset.Y - Offset.Y;

            var zoomFactor = ZoomFactor;
            x /= zoomFactor;
            y /= zoomFactor;
            w /= zoomFactor;
            h /= zoomFactor;

            if (w > 0 && h > 0)
            {

                SelectionRegion = FitRectangle(new Rect(x, y, w, h));
            }
        }

        e.Handled = true;
    }*/
    #endregion

    #region Zoom and Size modes
    /// <summary>
    /// Resets the <see cref="SizeModes"/> property whilsts retaining the original <see cref="Zoom"/>.
    /// </summary>
    protected void RestoreSizeMode()
    {
        if (SizeMode != SizeModes.Normal)
        {
            var previousZoom = Zoom;
            SizeMode = SizeModes.Normal;
            Zoom = previousZoom; // Stop the zoom getting reset to 100% before calculating the new zoom
        }
    }

    /// <summary>
    /// Returns an appropriate zoom level based on the specified action, relative to the current zoom level.
    /// </summary>
    /// <param name="action">The action to determine the zoom level.</param>
    /// <exception cref="System.ArgumentOutOfRangeException">Thrown if an unsupported action is specified.</exception>
    public int GetZoomLevel(ZoomActions action)
    {
        var result = action switch
        {
            ZoomActions.None => Zoom,
            ZoomActions.ZoomIn => _zoomLevels.NextZoom(Zoom),
            ZoomActions.ZoomOut => _zoomLevels.PreviousZoom(Zoom),
            ZoomActions.ActualSize => 100,
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
        };
        return result;
    }

    /// <summary>
    ///  Performs the specified zoom action.
    /// </summary>
    /// <param name="action"></param>
    /// <param name="relativePoint">Preserve position at given relative point. If null, <see cref="CenterPoint"/>> will be used.</param>
    public void PerformZoom(ZoomActions action, Point? relativePoint)
    {
        SetZoom(GetZoomLevel(action), true, relativePoint);
    }

    /// <summary>
    /// Performs the specified zoom action.
    /// </summary>
    /// <param name="action"></param>
    /// <param name="preservePosition"><c>true</c> if the current scrolling position should be preserved relative to the new zoom level, <c>false</c> to reset.</param>
    /// <param name="relativePoint">Preserve position at given relative point. If null, <see cref="CenterPoint"/>> will be used.</param>
    public void PerformZoom(ZoomActions action, bool preservePosition = true, Point? relativePoint = null)
    {
        SetZoom(GetZoomLevel(action), preservePosition, relativePoint);
    }

    /// <summary>
    /// Sets the zoom level to the specified value.
    /// </summary>
    /// <param name="zoom"></param>
    /// <param name="relativePoint">Preserve position at given relative point. If null, <see cref="CenterPoint"/>> will be used.</param>
    public void SetZoom(int zoom, Point? relativePoint)
    {
        SetZoom(zoom, true, relativePoint);
    }

    /// <summary>
    /// Sets the zoom level to the specified value.
    /// </summary>
    /// <param name="zoom"></param>
    /// <param name="preservePosition"><c>true</c> if the current scrolling position should be preserved relative to the new zoom level, <c>false</c> to reset.</param>
    /// <param name="relativePoint">Preserve position at given relative point. If null, <see cref="CenterPoint"/>> will be used.</param>
    public void SetZoom(int zoom, bool preservePosition = true, Point? relativePoint = null)
    {
        relativePoint ??= CenterPoint;
        int currentZoom = Zoom;
        Point currentPixel = PointToImage(relativePoint.Value);

        RestoreSizeMode();
        Zoom = zoom;

        if (preservePosition && Zoom != currentZoom)
        {
            ScrollTo(currentPixel, relativePoint.Value);
        }
    }

    /// <summary>
    ///   Zooms into the image
    /// </summary>
    public void ZoomIn()
    {
        ZoomIn(true);
    }

    /// <summary>
    ///   Zooms into the image
    /// </summary>
    /// <param name="preservePosition"><c>true</c> if the current scrolling position should be preserved relative to the new zoom level, <c>false</c> to reset.</param>
    public void ZoomIn(bool preservePosition)
    {
        PerformZoom(ZoomActions.ZoomIn, preservePosition);
    }

    /// <summary>
    ///   Zooms out of the image
    /// </summary>
    public void ZoomOut()
    {
        ZoomOut(true);
    }

    /// <summary>
    ///   Zooms out of the image
    /// </summary>
    /// <param name="preservePosition"><c>true</c> if the current scrolling position should be preserved relative to the new zoom level, <c>false</c> to reset.</param>
    public void ZoomOut(bool preservePosition)
    {
        PerformZoom(ZoomActions.ZoomOut, preservePosition);
    }

    /// <summary>
    /// Zooms to the maximum size for displaying the entire image within the bounds of the control.
    /// </summary>
    public void ZoomToFit()
    {
        Zoom = ZoomLevelToFit;
    }

    /// <summary>
    ///   Adjusts the view port to fit the given region
    /// </summary>
    /// <param name="x">The X co-ordinate of the selection region.</param>
    /// <param name="y">The Y co-ordinate of the selection region.</param>
    /// <param name="width">The width of the selection region.</param>
    /// <param name="height">The height of the selection region.</param>
    /// <param name="margin">Give a margin to rectangle by a value to zoom-out that pixel value</param>
    public void ZoomToRegion(double x, double y, double width, double height, double margin = 0)
    {
        ZoomToRegion(new Rect(x, y, width, height), margin);
    }

    /// <summary>
    ///   Adjusts the view port to fit the given region
    /// </summary>
    /// <param name="x">The X co-ordinate of the selection region.</param>
    /// <param name="y">The Y co-ordinate of the selection region.</param>
    /// <param name="width">The width of the selection region.</param>
    /// <param name="height">The height of the selection region.</param>
    /// <param name="margin">Give a margin to rectangle by a value to zoom-out that pixel value</param>
    public void ZoomToRegion(int x, int y, int width, int height, double margin = 0)
    {
        ZoomToRegion(new Rect(x, y, width, height), margin);
    }

    /// <summary>
    ///   Adjusts the view port to fit the given region
    /// </summary>
    /// <param name="rectangle">The rectangle to fit the view port to.</param>
    /// <param name="margin">Give a margin to rectangle by a value to zoom-out that pixel value</param>
    public void ZoomToRegion(Rectangle rectangle, double margin = 0)
    {
        ZoomToRegion(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height, margin);
    }

    /// <summary>
    ///   Adjusts the view port to fit the given region
    /// </summary>
    /// <param name="rectangle">The rectangle to fit the view port to.</param>
    /// <param name="margin">Give a margin to rectangle by a value to zoom-out that pixel value</param>
    public void ZoomToRegion(Rect rectangle, double margin = 0)
    {
        if (!IsImageLoaded) return;
        if (margin > 0) rectangle = rectangle.Inflate(margin);
        var ratioX = Viewport.Width / rectangle.Width;
        var ratioY = Viewport.Height / rectangle.Height;
        var zoomFactor = Math.Min(ratioX, ratioY);
        var cx = rectangle.X + rectangle.Width / 2;
        var cy = rectangle.Y + rectangle.Height / 2;

        CanRender = false;
        Zoom = (int)(zoomFactor * 100); // This function sets the zoom so viewport will change

        //Dispatcher.UIThread.Post(() => CenterAt(new Point(cx, cy)));
        CenterAt(new Point(cx, cy)); // If I call this here, it will move to the wrong position due wrong viewport, dispatcher would solve but slower?
    }

    /// <summary>
    /// Zooms to current selection region
    /// </summary>
    public void ZoomToSelectionRegion(double margin = 0)
    {
        if (!HaveSelection) return;
        ZoomToRegion(SelectionRegion, margin);
    }

    /// <summary>
    /// Resets the zoom to 100%.
    /// </summary>
    public void PerformActualSize()
    {
        SizeMode = SizeModes.Normal;
        //SetZoom(100, ImageZoomActions.ActualSize | (Zoom < 100 ? ImageZoomActions.ZoomIn : ImageZoomActions.ZoomOut));
        Zoom = 100;
    }
    #endregion

    #region Utility methods
    /// <summary>
    ///   Determines whether the specified point is located within the image view port
    /// </summary>
    /// <param name="point">The point.</param>
    /// <returns>
    ///   <c>true</c> if the specified point is located within the image view port; otherwise, <c>false</c>.
    /// </returns>
    public bool IsPointInImage(Point point)
    {
        return GetImageViewPort().Contains(point);
    }

    /// <summary>
    ///   Determines whether the specified point is located within the image view port
    /// </summary>
    /// <param name="x">The X co-ordinate of the point to check.</param>
    /// <param name="y">The Y co-ordinate of the point to check.</param>
    /// <returns>
    ///   <c>true</c> if the specified point is located within the image view port; otherwise, <c>false</c>.
    /// </returns>
    public bool IsPointInImage(int x, int y)
    {
        return IsPointInImage(new Point(x, y));
    }

    /// <summary>
    ///   Determines whether the specified point is located within the image view port
    /// </summary>
    /// <param name="x">The X co-ordinate of the point to check.</param>
    /// <param name="y">The Y co-ordinate of the point to check.</param>
    /// <returns>
    ///   <c>true</c> if the specified point is located within the image view port; otherwise, <c>false</c>.
    /// </returns>
    public bool IsPointInImage(double x, double y)
    {
        return IsPointInImage(new Point(x, y));
    }

    /// <summary>
    ///   Converts the given client size point to represent a coordinate on the source image.
    /// </summary>
    /// <param name="x">The X co-ordinate of the point to convert.</param>
    /// <param name="y">The Y co-ordinate of the point to convert.</param>
    /// <param name="fitToBounds">
    ///   if set to <c>true</c> and the point is outside the bounds of the source image, it will be mapped to the nearest edge.
    /// </param>
    /// <returns><c>Point.Empty</c> if the point could not be matched to the source image, otherwise the new translated point</returns>
    public Point PointToImage(double x, double y, bool fitToBounds = true)
    {
        return PointToImage(new Point(x, y), fitToBounds);
    }

    /// <summary>
    ///   Converts the given client size point to represent a coordinate on the source image.
    /// </summary>
    /// <param name="x">The X co-ordinate of the point to convert.</param>
    /// <param name="y">The Y co-ordinate of the point to convert.</param>
    /// <param name="fitToBounds">
    ///   if set to <c>true</c> and the point is outside the bounds of the source image, it will be mapped to the nearest edge.
    /// </param>
    /// <returns><c>Point.Empty</c> if the point could not be matched to the source image, otherwise the new translated point</returns>
    public Point PointToImage(int x, int y, bool fitToBounds = true)
    {
        return PointToImage(new Point(x, y), fitToBounds);
    }

    /// <summary>
    ///   Converts the given client size point to represent a coordinate on the source image.
    /// </summary>
    /// <param name="point">The source point.</param>
    /// <param name="fitToBounds">
    ///   if set to <c>true</c> and the point is outside the bounds of the source image, it will be mapped to the nearest edge.
    /// </param>
    /// <returns><c>Point.Empty</c> if the point could not be matched to the source image, otherwise the new translated point</returns>
    public Point PointToImage(Point point, bool fitToBounds = true)
    {
        double x;
        double y;

        var viewport = GetImageViewPort();

        if (!fitToBounds || viewport.Contains(point))
        {
            x = (point.X + Offset.X - viewport.X) / ZoomFactor;
            y = (point.Y + Offset.Y - viewport.Y) / ZoomFactor;

            var image = Image;
            if (fitToBounds)
            {
                x = Math.Clamp(x, 0, image!.Size.Width-1);
                y = Math.Clamp(y, 0, image.Size.Height-1);
            }
        }
        else
        {
            x = 0; // Return Point.Empty if we couldn't match
            y = 0;
        }

        return new(x, y);
    }

    /// <summary>
    ///   Returns the source <see cref="T:System.Drawing.Point" /> repositioned to include the current image offset and scaled by the current zoom level
    /// </summary>
    /// <param name="source">The source <see cref="Point"/> to offset.</param>
    /// <returns>A <see cref="Point"/> which has been repositioned to match the current zoom level and image offset</returns>
    public Point GetOffsetPoint(System.Drawing.Point source)
    {
        var offset = GetOffsetPoint(new Point(source.X, source.Y));

        return new((int)offset.X, (int)offset.Y);
    }

    /// <summary>
    ///   Returns the source co-ordinates repositioned to include the current image offset and scaled by the current zoom level
    /// </summary>
    /// <param name="x">The source X co-ordinate.</param>
    /// <param name="y">The source Y co-ordinate.</param>
    /// <returns>A <see cref="Point"/> which has been repositioned to match the current zoom level and image offset</returns>
    public Point GetOffsetPoint(int x, int y)
    {
        return GetOffsetPoint(new System.Drawing.Point(x, y));
    }

    /// <summary>
    ///   Returns the source co-ordinates repositioned to include the current image offset and scaled by the current zoom level
    /// </summary>
    /// <param name="x">The source X co-ordinate.</param>
    /// <param name="y">The source Y co-ordinate.</param>
    /// <returns>A <see cref="Point"/> which has been repositioned to match the current zoom level and image offset</returns>
    public Point GetOffsetPoint(double x, double y)
    {
        return GetOffsetPoint(new Point(x, y));
    }

    /// <summary>
    ///   Returns the source <see cref="T:System.Drawing.PointF" /> repositioned to include the current image offset and scaled by the current zoom level
    /// </summary>
    /// <param name="source">The source <see cref="PointF"/> to offset.</param>
    /// <returns>A <see cref="PointF"/> which has been repositioned to match the current zoom level and image offset</returns>
    public Point GetOffsetPoint(Point source)
    {
        Rect viewport = GetImageViewPort();
        var scaled = GetScaledPoint(source);
        var offsetX = viewport.Left + Offset.X;
        var offsetY = viewport.Top + Offset.Y;

        return new(scaled.X + offsetX, scaled.Y + offsetY);
    }

    /// <summary>
    ///   Returns the source <see cref="T:System.Drawing.RectangleF" /> scaled according to the current zoom level and repositioned to include the current image offset
    /// </summary>
    /// <param name="source">The source <see cref="RectangleF"/> to offset.</param>
    /// <returns>A <see cref="RectangleF"/> which has been resized and repositioned to match the current zoom level and image offset</returns>
    public Rect GetOffsetRectangle(Rect source)
    {
        var viewport = GetImageViewPort();
        var scaled = GetScaledRectangle(source);
        var offsetX = viewport.Left - Offset.X;
        var offsetY = viewport.Top - Offset.Y;

        return new(new Point(scaled.Left + offsetX, scaled.Top + offsetY), scaled.Size);
    }

    /// <summary>
    ///   Returns the source rectangle scaled according to the current zoom level and repositioned to include the current image offset
    /// </summary>
    /// <param name="x">The X co-ordinate of the source rectangle.</param>
    /// <param name="y">The Y co-ordinate of the source rectangle.</param>
    /// <param name="width">The width of the rectangle.</param>
    /// <param name="height">The height of the rectangle.</param>
    /// <returns>A <see cref="Rectangle"/> which has been resized and repositioned to match the current zoom level and image offset</returns>
    public Rectangle GetOffsetRectangle(int x, int y, int width, int height)
    {
        return GetOffsetRectangle(new Rectangle(x, y, width, height));
    }

    /// <summary>
    ///   Returns the source rectangle scaled according to the current zoom level and repositioned to include the current image offset
    /// </summary>
    /// <param name="x">The X co-ordinate of the source rectangle.</param>
    /// <param name="y">The Y co-ordinate of the source rectangle.</param>
    /// <param name="width">The width of the rectangle.</param>
    /// <param name="height">The height of the rectangle.</param>
    /// <returns>A <see cref="RectangleF"/> which has been resized and repositioned to match the current zoom level and image offset</returns>
    public Rect GetOffsetRectangle(double x, double y, double width, double height)
    {
        return GetOffsetRectangle(new Rect(x, y, width, height));
    }

    /// <summary>
    ///   Returns the source <see cref="T:System.Drawing.Rectangle" /> scaled according to the current zoom level and repositioned to include the current image offset
    /// </summary>
    /// <param name="source">The source <see cref="Rectangle"/> to offset.</param>
    /// <returns>A <see cref="Rectangle"/> which has been resized and repositioned to match the current zoom level and image offset</returns>
    public Rectangle GetOffsetRectangle(Rectangle source)
    {
        var viewport = GetImageViewPort();
        var scaled = GetScaledRectangle(source);
        var offsetX = viewport.Left + Offset.X;
        var offsetY = viewport.Top + Offset.Y;

        return new(new System.Drawing.Point((int)(scaled.Left + offsetX), (int)(scaled.Top + offsetY)), new System.Drawing.Size((int)scaled.Size.Width, (int)scaled.Size.Height));
    }

    /// <summary>
    ///   Fits a given <see cref="T:System.Drawing.Rectangle" /> to match image boundaries
    /// </summary>
    /// <param name="rectangle">The rectangle.</param>
    /// <returns>
    ///   A <see cref="T:System.Drawing.Rectangle" /> structure remapped to fit the image boundaries
    /// </returns>
    public Rectangle FitRectangle(Rectangle rectangle)
    {
        var image = Image;
        if (image is null) return Rectangle.Empty;
        var x = rectangle.X;
        var y = rectangle.Y;
        var w = rectangle.Width;
        var h = rectangle.Height;

        if (x < 0)
        {
            x = 0;
        }

        if (y < 0)
        {
            y = 0;
        }

        if (x + w > image.Size.Width)
        {
            w = (int)(image.Size.Width - x);
        }

        if (y + h > image.Size.Height)
        {
            h = (int)(image.Size.Height - y);
        }

        return new(x, y, w, h);
    }

    /// <summary>
    ///   Fits a given <see cref="T:System.Drawing.RectangleF" /> to match image boundaries
    /// </summary>
    /// <param name="rectangle">The rectangle.</param>
    /// <returns>
    ///   A <see cref="T:System.Drawing.RectangleF" /> structure remapped to fit the image boundaries
    /// </returns>
    public Rect FitRectangle(Rect rectangle)
    {
        var image = Image;
        if (image is null) return default;
        var x = rectangle.X;
        var y = rectangle.Y;
        var w = rectangle.Width;
        var h = rectangle.Height;

        if (x < 0)
        {
            w -= -x;
            x = 0;
        }

        if (y < 0)
        {
            h -= -y;
            y = 0;
        }

        if (x + w > image.Size.Width)
        {
            w = image.Size.Width - x;
        }

        if (y + h > image.Size.Height)
        {
            h = image.Size.Height - y;
        }

        return new(x, y, w, h);
    }
    #endregion

    #region Navigate / Scroll methods
    /// <summary>
    ///   Scrolls the control to the given point in the image, offset at the specified display point
    /// </summary>
    /// <param name="x">The X co-ordinate of the point to scroll to.</param>
    /// <param name="y">The Y co-ordinate of the point to scroll to.</param>
    /// <param name="relativeX">The X co-ordinate relative to the <c>x</c> parameter.</param>
    /// <param name="relativeY">The Y co-ordinate relative to the <c>y</c> parameter.</param>
    public void ScrollTo(double x, double y, double relativeX, double relativeY)
    {
        ScrollTo(new Point(x, y), new Point(relativeX, relativeY));
    }

    /// <summary>
    ///   Scrolls the control to the given point in the image, offset at the specified display point
    /// </summary>
    /// <param name="x">The X co-ordinate of the point to scroll to.</param>
    /// <param name="y">The Y co-ordinate of the point to scroll to.</param>
    /// <param name="relativeX">The X co-ordinate relative to the <c>x</c> parameter.</param>
    /// <param name="relativeY">The Y co-ordinate relative to the <c>y</c> parameter.</param>
    public void ScrollTo(int x, int y, int relativeX, int relativeY)
    {
        ScrollTo(new Point(x, y), new Point(relativeX, relativeY));
    }

    /// <summary>
    ///   Scrolls the control to the given point in the image, offset at the specified display point
    /// </summary>
    /// <param name="imageLocation">The point of the image to attempt to scroll to.</param>
    /// <param name="relativeDisplayPoint">The relative display point to offset scrolling by.</param>
    public void ScrollTo(Point imageLocation, Point relativeDisplayPoint)
    {
        //CanRender = false;
        var zoomFactor = ZoomFactor;
        var x = imageLocation.X * zoomFactor - relativeDisplayPoint.X;
        var y = imageLocation.Y * zoomFactor - relativeDisplayPoint.Y;


        _canRender = true;
        Offset = new Vector(x, y);

        /*Debug.WriteLine(
            $"X/Y: {x},{y} | \n" +
            $"Offset: {Offset} | \n" +
            $"ZoomFactor: {ZoomFactor} | \n" +
            $"Image Location: {imageLocation}\n" +
            $"MAX: {HorizontalScrollBar.Maximum},{VerticalScrollBar.Maximum} \n" +
            $"ViewPort: {Viewport.Width},{Viewport.Height} \n" +
            $"Container: {HorizontalScrollBar.ViewportSize},{VerticalScrollBar.ViewportSize} \n" +
            $"Relative: {relativeDisplayPoint}");*/
    }

    /// <summary>
    ///   Centers the given point in the image in the center of the control
    /// </summary>
    /// <param name="imageLocation">The point of the image to attempt to center.</param>
    public void CenterAt(System.Drawing.Point imageLocation)
    {
        ScrollTo(new Point(imageLocation.X, imageLocation.Y), new Point(Viewport.Width / 2, Viewport.Height / 2));
    }

    /// <summary>
    ///   Centers the given point in the image in the center of the control
    /// </summary>
    /// <param name="imageLocation">The point of the image to attempt to center.</param>
    public void CenterAt(Point imageLocation)
    {
        ScrollTo(imageLocation, new Point(Viewport.Width / 2, Viewport.Height / 2));
    }

    /// <summary>
    ///   Centers the given point in the image in the center of the control
    /// </summary>
    /// <param name="x">The X co-ordinate of the point to center.</param>
    /// <param name="y">The Y co-ordinate of the point to center.</param>
    public void CenterAt(int x, int y)
    {
        CenterAt(new Point(x, y));
    }

    /// <summary>
    ///   Centers the given point in the image in the center of the control
    /// </summary>
    /// <param name="x">The X co-ordinate of the point to center.</param>
    /// <param name="y">The Y co-ordinate of the point to center.</param>
    public void CenterAt(double x, double y)
    {
        CenterAt(new Point(x, y));
    }

    /// <summary>
    /// Resets the viewport to show the center of the image.
    /// </summary>
    public void CenterToImage()
    {
        var horizontalScrollBar = HorizontalScrollBar;
        if (horizontalScrollBar is null) return;

        var verticalScrollBar = VerticalScrollBar;
        if (verticalScrollBar is null) return;

        Offset = new Vector(horizontalScrollBar.Maximum / 2.0, verticalScrollBar.Maximum / 2.0);
    }
    #endregion

    #region Selection / ROI methods

    /// <summary>
    ///   Returns the source <see cref="T:System.Drawing.Point" /> scaled according to the current zoom level
    /// </summary>
    /// <param name="x">The X co-ordinate of the point to scale.</param>
    /// <param name="y">The Y co-ordinate of the point to scale.</param>
    /// <returns>A <see cref="Point"/> which has been scaled to match the current zoom level</returns>
    public Point GetScaledPoint(int x, int y)
    {
        return GetScaledPoint(new Point(x, y));
    }

    /// <summary>
    ///   Returns the source <see cref="T:System.Drawing.Point" /> scaled according to the current zoom level
    /// </summary>
    /// <param name="x">The X co-ordinate of the point to scale.</param>
    /// <param name="y">The Y co-ordinate of the point to scale.</param>
    /// <returns>A <see cref="Point"/> which has been scaled to match the current zoom level</returns>
    public PointF GetScaledPoint(float x, float y)
    {
        return GetScaledPoint(new PointF(x, y));
    }

    /// <summary>
    ///   Returns the source <see cref="T:System.Drawing.Point" /> scaled according to the current zoom level
    /// </summary>
    /// <param name="source">The source <see cref="Point"/> to scale.</param>
    /// <returns>A <see cref="Point"/> which has been scaled to match the current zoom level</returns>
    public Point GetScaledPoint(Point source)
    {
        return new(source.X * ZoomFactor, source.Y * ZoomFactor);
    }

    /// <summary>
    ///   Returns the source <see cref="T:System.Drawing.PointF" /> scaled according to the current zoom level
    /// </summary>
    /// <param name="source">The source <see cref="PointF"/> to scale.</param>
    /// <returns>A <see cref="PointF"/> which has been scaled to match the current zoom level</returns>
    public PointF GetScaledPoint(PointF source)
    {
        return new((float)(source.X * ZoomFactor), (float)(source.Y * ZoomFactor));
    }

    /// <summary>
    ///   Returns the source rectangle scaled according to the current zoom level
    /// </summary>
    /// <param name="x">The X co-ordinate of the source rectangle.</param>
    /// <param name="y">The Y co-ordinate of the source rectangle.</param>
    /// <param name="width">The width of the rectangle.</param>
    /// <param name="height">The height of the rectangle.</param>
    /// <returns>A <see cref="Rectangle"/> which has been scaled to match the current zoom level</returns>
    public Rect GetScaledRectangle(int x, int y, int width, int height)
    {
        return GetScaledRectangle(new Rect(x, y, width, height));
    }

    /// <summary>
    ///   Returns the source rectangle scaled according to the current zoom level
    /// </summary>
    /// <param name="x">The X co-ordinate of the source rectangle.</param>
    /// <param name="y">The Y co-ordinate of the source rectangle.</param>
    /// <param name="width">The width of the rectangle.</param>
    /// <param name="height">The height of the rectangle.</param>
    /// <returns>A <see cref="RectangleF"/> which has been scaled to match the current zoom level</returns>
    public RectangleF GetScaledRectangle(float x, float y, float width, float height)
    {
        return GetScaledRectangle(new RectangleF(x, y, width, height));
    }

    /// <summary>
    ///   Returns the source rectangle scaled according to the current zoom level
    /// </summary>
    /// <param name="location">The location of the source rectangle.</param>
    /// <param name="size">The size of the source rectangle.</param>
    /// <returns>A <see cref="Rectangle"/> which has been scaled to match the current zoom level</returns>
    public Rect GetScaledRectangle(Point location, Size size)
    {
        return GetScaledRectangle(new Rect(location, size));
    }

    /// <summary>
    ///   Returns the source rectangle scaled according to the current zoom level
    /// </summary>
    /// <param name="location">The location of the source rectangle.</param>
    /// <param name="size">The size of the source rectangle.</param>
    /// <returns>A <see cref="Rectangle"/> which has been scaled to match the current zoom level</returns>
    public RectangleF GetScaledRectangle(PointF location, SizeF size)
    {
        return GetScaledRectangle(new RectangleF(location, size));
    }

    /// <summary>
    ///   Returns the source <see cref="T:System.Drawing.Rectangle" /> scaled according to the current zoom level
    /// </summary>
    /// <param name="source">The source <see cref="Rectangle"/> to scale.</param>
    /// <returns>A <see cref="Rectangle"/> which has been scaled to match the current zoom level</returns>
    public Rect GetScaledRectangle(Rect source)
    {
        return new(source.Left * ZoomFactor, source.Top * ZoomFactor, source.Width * ZoomFactor, source.Height * ZoomFactor);
    }

    /// <summary>
    ///   Returns the source <see cref="T:System.Drawing.RectangleF" /> scaled according to the current zoom level
    /// </summary>
    /// <param name="source">The source <see cref="RectangleF"/> to scale.</param>
    /// <returns>A <see cref="RectangleF"/> which has been scaled to match the current zoom level</returns>
    public RectangleF GetScaledRectangle(RectangleF source)
    {
        return new((float)(source.Left * ZoomFactor), (float)(source.Top * ZoomFactor), (float)(source.Width * ZoomFactor), (float)(source.Height * ZoomFactor));
    }

    /// <summary>
    ///   Returns the source size scaled according to the current zoom level
    /// </summary>
    /// <param name="width">The width of the size to scale.</param>
    /// <param name="height">The height of the size to scale.</param>
    /// <returns>A <see cref="SizeF"/> which has been resized to match the current zoom level</returns>
    public SizeF GetScaledSize(float width, float height)
    {
        return GetScaledSize(new SizeF(width, height));
    }

    /// <summary>
    ///   Returns the source size scaled according to the current zoom level
    /// </summary>
    /// <param name="width">The width of the size to scale.</param>
    /// <param name="height">The height of the size to scale.</param>
    /// <returns>A <see cref="Size"/> which has been resized to match the current zoom level</returns>
    public Size GetScaledSize(int width, int height)
    {
        return GetScaledSize(new Size(width, height));
    }

    /// <summary>
    ///   Returns the source <see cref="T:System.Drawing.SizeF" /> scaled according to the current zoom level
    /// </summary>
    /// <param name="source">The source <see cref="SizeF"/> to scale.</param>
    /// <returns>A <see cref="SizeF"/> which has been resized to match the current zoom level</returns>
    public SizeF GetScaledSize(SizeF source)
    {
        return new((float)(source.Width * ZoomFactor), (float)(source.Height * ZoomFactor));
    }

    /// <summary>
    ///   Returns the source <see cref="T:System.Drawing.Size" /> scaled according to the current zoom level
    /// </summary>
    /// <param name="source">The source <see cref="Size"/> to scale.</param>
    /// <returns>A <see cref="Size"/> which has been resized to match the current zoom level</returns>
    public Size GetScaledSize(Size source)
    {
        return new(source.Width * ZoomFactor, source.Height * ZoomFactor);
    }

    /// <summary>
    ///   Creates a selection region which encompasses the entire image
    /// </summary>
    /// <exception cref="System.InvalidOperationException">Thrown if no image is currently set</exception>
    public void SelectAll()
    {
        var image = Image;
        if (image is null) return;
        SelectionRegion = new Rect(0, 0, image.Size.Width, image.Size.Height);
    }

    /// <summary>
    /// Clears any existing selection region
    /// </summary>
    public void SelectNone()
    {
        SelectionRegion = default;
    }

    #endregion

    #region Viewport and image region methods
    /// <summary>
    ///   Gets the source image region.
    /// </summary>
    /// <returns></returns>
    public Rect GetSourceImageRegion()
    {
        var image = Image;
        if (image is null) return default;

        switch (SizeMode)
        {
            case SizeModes.Normal:
                var offset = Offset;
                var viewPort = GetImageViewPort();
                var zoomFactor = ZoomFactor;
                double sourceLeft = (offset.X / zoomFactor);
                double sourceTop = (offset.Y / zoomFactor);
                double sourceWidth = (viewPort.Width / zoomFactor);
                double sourceHeight = (viewPort.Height / zoomFactor);

                return new(sourceLeft, sourceTop, sourceWidth, sourceHeight);
        }

        return new(0, 0, image.Size.Width, image.Size.Height);

    }

    /// <summary>
    /// Gets the image view port.
    /// </summary>
    /// <returns>The image viewport rectangle.</returns>
    public Rect GetImageViewPort()
    {
        var image = Image;
        if (image is null) return default;

        var viewPortSize = Viewport;
        if (viewPortSize is {Width: 0, Height: 0}) return default;

        double xOffset = 0.0;
        double yOffset = 0.0;
        double width = 0.0;
        double height = 0.0;

        var padding = Padding;

        switch (SizeMode)
        {
            case SizeModes.Normal:
                if (AutoCenter)
                {
                    xOffset = (!IsHorizontalBarVisible ? (viewPortSize.Width - ScaledImageWidth) / 2.0 : 0.0);
                    yOffset = (!IsVerticalBarVisible ? (viewPortSize.Height - ScaledImageHeight) / 2.0 : 0.0);
                }

                width = Math.Min(ScaledImageWidth - Math.Abs(Offset.X), viewPortSize.Width);
                height = Math.Min(ScaledImageHeight - Math.Abs(Offset.Y), viewPortSize.Height);
                break;
            case SizeModes.Stretch:
                width = viewPortSize.Width - padding.Left - padding.Right;
                if (width <= 0) return new Rect();
                height = viewPortSize.Height - padding.Top - padding.Bottom;
                if (height <= 0) return new Rect();

                xOffset = padding.Left;
                yOffset = padding.Top;
                break;
            case SizeModes.Fit:
                double scaleFactor = Math.Min((viewPortSize.Width - padding.Left - padding.Right) / image.Size.Width, (viewPortSize.Height - padding.Top - padding.Bottom) / image.Size.Height);

                if (scaleFactor <= 0) return new Rect();

                width = Math.Floor(image.Size.Width * scaleFactor);
                height = Math.Floor(image.Size.Height * scaleFactor);

                if (AutoCenter)
                {
                    xOffset = (viewPortSize.Width - width) / 2.0;
                    yOffset = (viewPortSize.Height - height) / 2.0;
                }
                else
                {
                    xOffset = padding.Left;
                    yOffset = padding.Top;
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(SizeMode), SizeMode, null);
        }

        return new Rect(xOffset, yOffset, width, height);
    }
    #endregion

    #region Image methods
    /// <summary>
    /// Loads the image from the specified path.
    /// </summary>
    /// <param name="path">Image path from disk</param>
    public void LoadImage(string path)
    {
        Image = new Bitmap(path);
    }

    public Bitmap? GetSelectedBitmap()
    {
        var image = ImageAsWriteableBitmap;
        if (image is null || !HaveSelection) return null;

        var selection = SelectionRegionPixel;

        using var srcBuffer = image.Lock();
        var cropBitmap = new WriteableBitmap(selection.Size, image.Dpi, srcBuffer.Format, AlphaFormat.Unpremul);
        using var dstBuffer = cropBitmap.Lock();

        unsafe
        {
            var ySrc = srcBuffer.Address + srcBuffer.RowBytes * selection.Y + selection.X * (srcBuffer.Format.BitsPerPixel / 8);
            var yDst = dstBuffer.Address;

            for (int y = selection.Y; y < selection.Bottom; y++)
            {
                Buffer.MemoryCopy(
                    ySrc.ToPointer(),
                    yDst.ToPointer(),
                    dstBuffer.RowBytes,
                    dstBuffer.RowBytes);

                ySrc += srcBuffer.RowBytes;
                yDst += dstBuffer.RowBytes;
            }
        }

        return cropBitmap;
    }
    #endregion

}
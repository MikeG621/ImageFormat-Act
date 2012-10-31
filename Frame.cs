/*
 * Idmr.ImageFormat.Act, Allows editing capability of LucasArts *.ACT files.
 * Copyright (C) 2009-2012 Michael Gaisser (mjgaisser@gmail.com)
 * Licensed under the GPL v3.0 or later
 * 
 * Full notice in Act.cs
 * VERSION: 2.0
 */

/* CHANGE LOG
 * v2.0, 121014
 * - Release
 */

using System;
using System.Drawing;
using System.Drawing.Imaging;
using Idmr.Common;

namespace Idmr.ImageFormat.Act
{
	/// <summary>Object for individual Frame information</summary>
	public class Frame
	{
		internal ActImage _parent;
		
		internal byte[] _header = new byte[_headerLength];
		internal Color[] _colors;
		Point _location = new Point(621, 621);
		internal byte[] _rows;
		internal Bitmap _image;	// Format8bppIndexed
		
		internal const int _headerLength = 0x2C;
		internal const int _headerReserved = 0x18;
		
		#region constructors
		/// <summary>Populates the Frame with information from raw data</summary>
		/// <param name="parent">The <see cref="ActImage"/> the Frame belongs to</param>
		/// <param name="raw">Complete raw byte data of a Frame</param>
		/// <exception cref="ArgumentException">Validation error</exception>
		internal Frame(ActImage parent, byte[] raw)
		{
			ArrayFunctions.TrimArray(raw, 0, _header);
			if (BitConverter.ToInt32(_header, 4) != _headerLength) throw new ArgumentException(ActImage._validationErrorMessage, "file");
			if (BitConverter.ToInt32(_header, 0x24) != _headerReserved) throw new ArgumentException(ActImage._validationErrorMessage, "file");
			
			_parent = parent;
			_colors = new Color[BitConverter.ToInt32(_header, 0x28)];
			System.Diagnostics.Debug.WriteLine("Colors: " + NumberOfColors);
			for (int c = 0, pos = _headerLength; c < _colors.Length; c++, pos += 4)
				_colors[c] = Color.FromArgb(raw[pos], raw[pos + 1], raw[pos + 2]);	// Red, Green, Blue, Alpha unused
			int offset = _headerLength + _colors.Length * 4;
			_location = new Point(BitConverter.ToInt32(raw, offset), BitConverter.ToInt32(raw, offset + 4));	// FrameLeft, FrameTop
			System.Diagnostics.Debug.WriteLine("Location: " + X + "," + Y + "\r\nSize: " + Width + "," + Height);
			offset += 0x10;
			_rows = new byte[raw.Length - offset];
			ArrayFunctions.TrimArray(raw, offset, _rows);
			_image = ActImage.DecodeImage(_rows, Width, Height, _colors, _shift);
		}
		
		/// <summary>Creates a new Frame with the given <see cref="PixelFormat.Format8bppIndexed"/> image</summary>
		/// <param name="parent">The <see cref="ActImage"/> the Frame belongs to</param>
		/// <param name="image">Image to be used for the Frame, must be <see cref="PixelFormat.Format8bppIndexed"/></param>
		/// <exception cref="FormatException"><i>image</i> is not <see cref="PixelFormat.Format8bppIndexed"/></exception>
		/// <exception cref="Idmr.Common.BoundaryException"><i>image</i> exceeds maximum allowable dimensions</exception>
		internal Frame(ActImage parent, Bitmap image)
		{
			if (image.PixelFormat != PixelFormat.Format8bppIndexed)
				throw new FormatException("Image must be 8bppIndexed (256 color)");
			_parent = parent;
			initializeHeader();
			Image = image;
		}

		/// <summary>Creates an empty Frame</summary>
		/// <param name="parent">The <see cref="ActImage"/> the Frame belongs to</param>
		internal Frame(ActImage parent)
		{
			_parent = parent;
			initializeHeader();
		}
		#endregion constructors
		
		#region public methods
		/// <summary>Modifies the given palette entry</summary>
		/// <param name="index">Color index</param>
		/// <param name="color">Color to be used</param>
		/// <exception cref="IndexOutOfRangeException">Invalid <i>index</i> value</exception>
		/// <exception cref="NullReferenceException"><see cref="Image"/> has not been initialized</exception>
		public void SetColor(int index, Color color)
		{
			_colors[index] = color;
			ColorPalette pal = _image.Palette;
			pal.Entries[index] = color;
			_image.Palette = pal;
		}
		/// <summary>Modifies the given palette entry</summary>
		/// <param name="index">Color index</param>
		/// <param name="red">R value of color to be used</param>
		/// <param name="green">G value of color to be used</param>
		/// <param name="blue">B value of color to be used</param>
		/// <exception cref="IndexOutOfRangeException">Invalid <i>index</i> value</exception>
		/// <exception cref="NullReferenceException"><see cref="Image"/> has not been initialized</exception>
		public void SetColor(int index, byte red, byte green, byte blue) { SetColor(index, Color.FromArgb(red, green, blue)); }
		#endregion public methods
		
		#region public properties
		/// <summary>Gets a copy of the used color array</summary>
		/// <exception cref="NullReferenceException"><see cref="Image"/> has not been initialized</exception>
		public Color[] Colors { get { return (Color[])_colors.Clone(); } }
		
		/// <summary>Gets the height of the Frame</summary>
		public int Height
		{
			get { return BitConverter.ToInt32(_header, 0x14); }
			internal set { ArrayFunctions.WriteToArray(value, _header, 0x14); }
		}
		
		/// <summary>Gets or sets the <see cref="PixelFormat.Format8bppIndexed"/> frame image</summary>
		/// <exception cref="FormatException"><i>Image</i> is not <see cref="PixelFormat.Format8bppIndexed"/></exception>
		/// <exception cref="Idmr.Common.BoundaryException"><i>Image.Size</i> exceeds maximum allowable dimensions</exception>
		/// <remarks><see cref="Colors"/> is updated with the trimmed color array, <i>Image</i> is also updated with new indexes as necessary.<br/>
		/// If <see cref="Location"/> is its default value, it will initialize to center the Frame around <see cref="ActImage.Center"/>.<br/>
		/// Parent <see cref="ActImage.Size"/> will adjust as necessary.</remarks>
		public Bitmap Image
		{
			get { return _image; }
			set
			{
				if (value.PixelFormat != PixelFormat.Format8bppIndexed)
					throw new FormatException("Image must be 8bppIndexed (256 color)");
				if (value.Width > MaximumWidth || value.Height > MaximumHeight)
					throw new BoundaryException("Image.Size", MaximumWidth + "x" + MaximumHeight);
				if (_parent.NumberOfFrames == 1) _parent.Size = value.Size;
				
				_image = value;
				_colors = GraphicsFunctions.GetTrimmedColors(_image);
				setShift();
				_rows = ActImage.EncodeImage(_image, _colors, _shift);
				updateHeader();
				if (_location.X == 621 && _location.Y == 621) Location = new Point(-Width / 2, -Height / 2);
				_parent._recalculateSize();
			}
		}
		
		/// <summary>Gets or sets the frame origin position relative to <see cref="ActImage.Center"/></summary>
		/// <exception cref="Idmr.Common.BoundaryException">Value causes a portion of the frame to extend beyond <see cref="ActImage"/> limits</exception>
		/// <remarks>If <see cref="Image"/> is uninitialized, value is <b>(621, 621)</b>.<br/>
		/// Parent <see cref="ActImage.Size"/> will adjust as necessary.</remarks>
		public Point Location
		{
			get { return _location; }
			set
			{
				_location.X = value.X;
				_location.Y = value.Y;
			}
		}

		/// <summary>Gets or sets the X component of <see cref="Location"/></summary>
		/// <exception cref="Idmr.Common.BoundaryException">Value causes a portion of the frame to extend beyond <see cref="ActImage"/> limits</exception>
		/// <remarks>If <see cref="Image"/> is uninitialized, value is <b>621</b>.<br/>
		/// Parent <see cref="ActImage.Size"/> will adjust as necessary.</remarks>
		public int X
		{
			get { return _location.X; }
			set
			{
				int upper = MaximumWidth - Width - _parent.Center.X;
				if (value < _parent.Center.X * -1 && value > upper)
					throw new BoundaryException("X", (_parent.Center.X * -1) + "-" + upper);
				_location.X = value;
				_parent._recalculateSize();
			}
		}

		/// <summary>Gets or sets the Y component of <see cref="Location"/></summary>
		/// <exception cref="Idmr.Common.BoundaryException">Value causes a portion of the frame to extend beyond <see cref="ActImage"/> limits</exception>
		/// <remarks>If <see cref="Image"/> is uninitialized, value is <b>621</b>.<br/>
		/// Parent <see cref="ActImage.Size"/> will adjust as necessary.</remarks>
		public int Y
		{
			get { return _location.Y; }
			set
			{
				int upper = MaximumHeight - Height - _parent.Center.Y;
				if (value < _parent.Center.Y * -1 && value > upper)
					throw new BoundaryException("Y", (_parent.Center.Y * -1) + "-" + upper);
				_location.Y = value;
				_parent._recalculateSize();
			}
		}
		
		/// <summary>Maximum height allowed within a single Frame</summary>
		/// <remarks>Value is <b>256</b></remarks>
		public const int MaximumHeight = 256;
		
		/// <summary>Maximum width allowed within a single Frame</summary>
		/// <remarks>Value is <b>256</b></remarks>
		public const int MaximumWidth = 256;
		
		/// <summary>Gets the number of colors defined by the Frame</summary>
		public int NumberOfColors { get { return _colors.Length; } }
		
		/// <summary>Gets the width of the Frame</summary>
		public int Width
		{
			get { return BitConverter.ToInt32(_header, 0x10); }
			internal set { ArrayFunctions.WriteToArray(value, _header, 0x10); }
		}
		#endregion public properties
		
		#region private methods
		void setShift()
		{
			int shift = 3;	// Shift=3, allows 8px lines, 00-1F ColorIndex
			if (_image.Width <= 16) shift = 4;	// Shift=4 allows 16px lines, 00-0F ColorIndex
			if (_colors.Length <= 8) shift = 5;	// Shift=5 allows 32px, 00-08 ColorIndex
			else if (_colors.Length <= 16) shift = 4;
			_shift = shift;
		}
		
		void updateHeader()
		{
			int imageDataOffset = _imageDataOffset;
			ArrayFunctions.WriteToArray(imageDataOffset + _rows.Length, _header, 0);
			ArrayFunctions.WriteToArray(imageDataOffset, _header, 8);
			ArrayFunctions.WriteToArray(imageDataOffset + _rows.Length, _header, 0xC);
			Width = _image.Width;
			Height = _image.Height;
			ArrayFunctions.WriteToArray(_colors.Length, _header, 0x28);	
		}
		
		void initializeHeader()
		{
			ArrayFunctions.WriteToArray(_headerLength, _header, 4);
			ArrayFunctions.WriteToArray(3, _header, 0x20);	// temp shift value
			ArrayFunctions.WriteToArray(_headerReserved, _header, 0x24);
		}
		#endregion private methods
		
		#region private properties
		internal int _shift
		{
			get { return BitConverter.ToInt32(_header, 0x20); }
			set { ArrayFunctions.WriteToArray(value, _header, 0x20); }
		}
		int _imageDataOffset { get { return _headerLength + NumberOfColors * 4; } }
		internal int _length { get { return _imageDataOffset + 0x10 + _rows.Length; } }
		#endregion private properties
	}
}
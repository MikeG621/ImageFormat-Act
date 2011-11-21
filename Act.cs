/*
 * Idmr.ImageFormat.Act, Allows editing capability of LucasArts *.ACT files.
 * Copyright (C) 2009-2011 Michael Gaisser (mjgaisser@gmail.com)
 *
 * This library is free software; you can redistribute it and/or modify it
 * under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 3.0 of the License, or (at
 * your option) any later version.
 *
 * This library is distributed in the hope that it will be useful, but
 * WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General
 * Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this library; if not, write to;
 * Free Software Foundation, Inc.
 * 59 Temple Place, Suite 330
 * Boston, MA 02111-1307 USA 
 *
 * VERSION 2.0
 */

/* CHANGE LOG
 * 110810 - Changed license to GPL
 * 111109 - Rewritten, 2.0
 */

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Idmr.Common;

namespace Idmr.ImageFormat
{
	/// <summary>Object to work with *.ACT image files found in TIE95, XvT and BoP</summary>
	/// <remarks>Acts can be loaded from file or created starting with a Bitmap. Unlike other image formats, each frame retains an individual palette</remarks>
	public class Act
	{
		string _filePath;
		Frame[] _frames;
		byte[] _header = new byte[_fileHeaderLength];
		const int _fileHeaderLength = 0x34;
		const int _frameHeaderLength = 0x2C;
		const int _frameHeaderReserved = 0x18;
		static string _validationErrorMessage = "Validation error, file is not a LucasArts Act Image file or is corrupted.";

		/// <summary>Loads an Act image from file</summary>
		/// <param name="file">Full path to the ACT file</param>
		/// <exception cref="Idmr.Common.LoadFileException">Error loading <i>file</i>, check InnerException</exception>
		public Act(string file)
		{
			FileStream fs = null;
			try
			{
				if (!file.ToUpper().EndsWith(".ACT")) throw new ArgumentException("Invalid file extension, must be *.ACT.", "file");
				fs = File.OpenRead(file);
				DecodeFile(new BinaryReader(fs).ReadBytes((int)fs.Length));
				fs.Close();
			}
			catch (Exception x)
			{
				if (fs != null) fs.Close();
				throw new LoadFileException(x);
			}
			_filePath = file;
		}

		/// <summary>Creates a new Act image from bitmap</summary>
		/// <remarks><i>FilePath</i> defaults to "NewImage.act", the palette is initialized as 256 colors and <i>image</i> is processed via SetFrameImage( ).
		/// <i>Center</i> defaults to center of <i>image</i></remarks>
		/// <param name="image">Image to be used as the new ACT</param>
		/// <exception cref="Idmr.Common.BoundaryException"><i>image</i> exceed allowable size</exception>
		public Act(Bitmap image)
		{
			_frames = new Frame[1];
			SetFrameImage(0, image);
			_filePath = "NewImage.act";
			Center = new Point(Width/2, Height/2);
			_frames[0].Location = new Point(-Center.X, -Center.Y);
		}

		/// <summary>Populates the Act object form the raw byte data</summary>
		/// <param name="raw">Entire contents of an *.ACT file</param>
		/// <exception cref="System.ArgumentException">Data validation failure</exception>
		public void DecodeFile(byte[] raw)
		{
			ArrayFunctions.TrimArray(raw, 0, _header);
			if (BitConverter.ToInt32(_header, 0x10) != _fileHeaderLength) throw new ArgumentException(_validationErrorMessage, "raw");
			_frames = new Frame[BitConverter.ToInt32(_header, 0x18)];	// FrameCount
			int[] frameOffsets = new int[NumberOfFrames];
			ArrayFunctions.TrimArray(raw, _fileHeaderLength, frameOffsets);
			// Frames
			for (int f = 0; f < NumberOfFrames; f++)
			{
				// FrameHeader
				byte[] rawFrame = new byte[BitConverter.ToInt32(raw, frameOffsets[f])];
				ArrayFunctions.TrimArray(raw, frameOffsets[f], rawFrame);
				_frames[f] = new Frame(rawFrame);
			}
			// EOF
		}
		
		/// <summary>Gets the image from the given information</summary>
		/// <param name="raw">The encoded Rows data</param>
		/// <param name="width">Width of the Frame</param>
		/// <param name="height">Height of the Frame</param>
		/// <param name="colors">Defined Color array to be used</param>
		/// <param name="shift">Shift value to decode with</param>
		/// <exception cref="System.ArgumentException">Data validation failure</exception>
		public static Bitmap DecodeImage(byte[] raw, int width, int height, Color[] colors, int shift)
		{
			Bitmap image = new Bitmap(width, height, PixelFormat.Format8bppIndexed);
			BitmapData bd = GraphicsFunctions.GetBitmapData(image);
			byte[] pixelData = new byte[bd.Stride * bd.Height];
			byte b, indexShift = 0;
			int numShift = 0, offset = 0;
			// Rows
			for (int y = (bd.Height - 1); y >= 0; y--)
			{
				for (int x = 0, pos = bd.Stride * y; x < bd.Width; )
				{
					// OpCodes
					b = raw[offset++];	// OpCode
					if (b == 0xFD)	// Repeat code
					{
						byte numRepeats = raw[offset++];
						byte colorIndex = raw[offset++];
						for (int j = 0; j <= numRepeats; j++, x++) pixelData[pos + x] = colorIndex;
					}
					else if (b == 0xFC) // Blank code
					{
						byte numRepeats = raw[offset++];
						x += numRepeats + 1;
					}
					else if (b == 0xFB) // Shift code
					{
						indexShift = raw[offset++];
						numShift = raw[offset++];	// TODO: check LA files to make sure this is always 1
					}
					else	// Short code
					{
						byte p = (byte)(b >> shift);
						byte n = (byte)(Math.Pow(2, shift) - 1);
						for (int j = 0; j <= (b & n); j++, x++) pixelData[pos + x] = (byte)(p + indexShift);
					}
				}
				if (raw[offset++] != 0xFE) throw new ArgumentException(_validationErrorMessage, "raw");	// EndRow
			}
			if (raw[offset++] != 0xFF) throw new ArgumentException(_validationErrorMessage, "raw");	// EndFrame
			// end Frame
			GraphicsFunctions.CopyBytesToImage(pixelData, bd);
			image.UnlockBits(bd);
			image.RotateFlip(RotateFlipType.RotateNoneFlipX);
			ColorPalette pal = image.Palette;
			for (int c = 0; c < colors.Length; c++) pal.Entries[c] = colors[c];
			for (int c = colors.Length; c < 256; c++) pal.Entries[c] = Color.Blue;
			image.Palette = pal;
			return image;
		}
		
		/// <summary>Gets the encoded byte array</summary>
		/// <param name="image">Image to be encoded</param>
		/// <param name="colors">Defined Color array to be used</param>
		/// <param name="shift">Shift value to encode with</param>
		/// <exception cref="System.ArgumentException">Image is not 8bppIndexed or invalid shift value</exception>
		/// <remarks>image must be 8bppIndexed.  shift restricted to 3-5.</remarks>
		public static byte[] EncodeImage(Bitmap image, Color[] colors, int shift)
		{
			if (image.PixelFormat != PixelFormat.Format8bppIndexed) throw new ArgumentException("image must be 8bppIndexed", "image");
			if (shift < 3 || shift > 5) throw new ArgumentException("shift value must be 3-5", "shift");
			byte[] raw = new byte[image.Width * image.Height * 2];
			int offset = 0;
			image.RotateFlip(RotateFlipType.RotateNoneFlipX);
			// Rows
			BitmapData bd = GraphicsFunctions.GetBitmapData(image);
			byte[] pixels = new byte[bd.Stride * bd.Height];
			GraphicsFunctions.CopyImageToBytes(bd, pixels);
			image.UnlockBits(bd);
			image.RotateFlip(RotateFlipType.RotateNoneFlipX);
			for (int y = (bd.Height - 1); y >= 0; y--)
			{
				for (int x = 0, pos = bd.Stride * y, len = 1; x < bd.Width; )
				{
					try
					{	// throws on last row
						if ((x + len) != bd.Width && pixels[pos + x] == pixels[pos + x + len])
						{
							len++;
							continue;
						}
					}
					catch { /* do nothing */ }
					if ((len <= Math.Pow(2, shift) && pixels[pos + x] < (0xFF >> shift)) || (len <= (shift == 3 ? 3 : 10) && pixels[pos + x] == (0xFF >> shift)))	// allow 0xF8-0xFA
					{	// Short code
						byte b = (byte)(len - 1);
						b |= (byte)(pixels[pos + x] << shift);
						raw[offset++] = b;
					}
					else if (pixels[pos + x] == 0)
					{	// Blank code
						raw[offset++] = 0xFC;
						raw[offset++] = (byte)(len - 1);
					}
					else
					{	// Repeat code
						raw[offset++] = 0xFD;
						raw[offset++] = (byte)(len - 1);
						raw[offset++] = pixels[pos + x];
					}
					// not going to use Shift codes
					x += len;
					len = 1;
				}
				raw[offset++] = 0xFE;	// EndRow
			}
			raw[offset++] = 0xFF;	// EndFrame
			byte[] trimmedRaw = new byte[offset];
			ArrayFunctions.TrimArray(raw, 0, trimmedRaw);
			return trimmedRaw;
		}
		
		/// <summary>Writes the Act object to its original location</summary>
		/// <exception cref="Idmr.Common.SaveFileException">Error saving file, check InnerException. Original unchanged if applicable</exception>
		public void Save()
		{
			FileStream fs = null;
			string tempFile = _filePath + ".tmp";
			try
			{
				if (File.Exists(_filePath)) File.Copy(_filePath, tempFile);	// create backup
				File.Delete(_filePath);
				
				// FileHeader
				int length = _fileHeaderLength;
				int totalColorCount = 0;
				for (int f = 0; f < NumberOfFrames; f++)
				{
					length += _frames[f]._length;
					totalColorCount += _frames[f].NumberOfColors;
				}
				ArrayFunctions.WriteToArray(length, _header, 0);
				ArrayFunctions.WriteToArray(totalColorCount, _header, 4);
				ArrayFunctions.WriteToArray(NumberOfFrames, _header, 0x18);
				// Width, Height, Center always up-to-date
				// FrameOffsets
				int[] frameOffsets = new int[NumberOfFrames];
				byte[] frameOffsetsBytes = new byte[NumberOfFrames * 4];
				frameOffsets[0] = _fileHeaderLength + NumberOfFrames * 4;
				for (int f = 1; f < NumberOfFrames; f++) frameOffsets[f] = frameOffsets[f - 1] + _frames[f - 1]._length;
				Buffer.BlockCopy(frameOffsets, 0, frameOffsetsBytes, 0, frameOffsetsBytes.Length);
				
				fs = File.OpenWrite(_filePath);
				BinaryWriter bw = new BinaryWriter(fs);
				bw.Write(_header);
				bw.Write(frameOffsetsBytes);
				for (int f = 0; f < NumberOfFrames; f++)
				{
					bw.Write(_frames[f]._header);
					for (int c = 0; c < _frames[f].NumberOfColors; c++)
					{
						fs.WriteByte(_frames[f]._colors[c].R);
						fs.WriteByte(_frames[f]._colors[c].G);
						fs.WriteByte(_frames[f]._colors[c].B);
					}
					bw.Write(_frames[f]._rows);
				}
				fs.SetLength(length);
				fs.Close();
				File.Delete(tempFile);	// delete backup if it exists
			}
			catch (Exception x)
			{
				if (fs != null) fs.Close();
				if (File.Exists(tempFile)) File.Copy(tempFile, _filePath);	// restore backup if it exists
				File.Delete(tempFile);	// delete backup if it exists
				throw new SaveFileException(x);
			}
		}

		/// <summary>Writes the Act object to a new location</summary>
		/// <param name="file">Full path to the new ACT file</param>
		/// <exception cref="Idmr.Common.SaveFileException">Error saving file, check InnerException. Original unchanged if applicable</exception>
		public void Save(string file)
		{
			_filePath = file;
			Save();
		}
		
		#region public properties
		/// <summary>Gets or Sets the pixel location used to "pin" the Act object in-game</summary>
		/// <exception cref="Idmr.Common.BoundaryException">Value does not fall within Act dimensions</exception>
		public Point Center
		{
			get { return new Point(BitConverter.ToInt32(_header, 0x24), BitConverter.ToInt32(_header, 0x28)); }
			set
			{
				if (value.X < Width && value.X >= 0 && value.Y < Height && value.Y >= 0)
				{
					ArrayFunctions.WriteToArray(value.X, _header, 0x24);
					ArrayFunctions.WriteToArray(value.Y, _header, 0x28);
				}
				else throw new BoundaryException("value", "0,0 - " + Width + "," + Height);
			}
		}

		/// <summary>Gets the file name of the Act object</summary>
		public string FileName { get { return StringFunctions.GetFileName(_filePath); } }

		/// <summary>Gets the full path of the Act object</summary>
		public string FilePath { get { return _filePath; } }

		/// <summary>Gets the overall height of the Act object</summary>
		public int Height { get { return BitConverter.ToInt32(_header, 0x20) + 1; } }

		/// <summary>Gets the number of images contained within the Act object</summary>
		public int NumberOfFrames { get { return _frames.Length; } }

		/// <summary>Gets the overall size of the Act object</summary>
		public Size Size
		{
			get { return new Size(Width, Height); }
			private set
			{
				ArrayFunctions.WriteToArray(value.Width - 1, _header, 0x1C);
				ArrayFunctions.WriteToArray(value.Height - 1, _header, 0x20);
			}
		}

		/// <summary>Gets the overall width of the Act object</summary>
		public int Width { get { return BitConverter.ToInt32(_header, 0x1C) + 1; } }
		#endregion

		/// <summary>Replaces indicated frame with new image</summary>
		/// <param name="index">Zero-indexed frame</param>
		/// <param name="image">New image. If 8bppIndexed, image's palette is used, else existing is</param>
		/// <exception cref="System.IndexOutOfRangeException">Invalid <i>index</i> value</exception>
		/// <exception cref="Idmr.Common.BoundaryException"><i>image</i> exceeds allowable size</exception>
		public void SetFrameImage(int index, Bitmap image)
		{
			if (image.Width > Frame.MaximumWidth || image.Height > Frame.MaximumHeight) throw new BoundaryException("image.Size", Frame.MaximumWidth + "x" + Frame.MaximumHeight);
			if (NumberOfFrames == 1) Size = image.Size;
			if (image.Width > Width || image.Height > Height) throw new BoundaryException("image.Size", Width + "x" + Height);
			
			// initial palette prep
			ColorPalette pal;
			if (image.PixelFormat == PixelFormat.Format8bppIndexed) pal = image.Palette;
			else 
			{
				pal = new Bitmap(1, 1, PixelFormat.Format8bppIndexed).Palette;
				if (_frames[index]._colors != null)	// if we're updating, get the old colors
					for (int c = 0; c < _frames[index]._colors.Length; c++)
						pal.Entries[c] = _frames[index]._colors[c];
			}
			
			if (_frames[index]._header == null)
			{
				// new frame, hasn't been initialized
				_frames[index] = new Frame(image, pal.Entries);
			}
			else
			{
				if (image.PixelFormat != PixelFormat.Format8bppIndexed) image = GraphicsFunctions.ConvertTo8bpp(image, pal);
				_frames[index].Image = image;
			}
		}
		
		/// <summary>Container for the Frame information</summary>
		public struct Frame
		{
			internal byte[] _header;
			internal Color[] _colors;
			Point _location;
			// int Right
			// int Top again
			internal byte[] _rows;
			
			internal Bitmap _image;	// Format8bppIndexed
			internal int _shift
			{
				get { return BitConverter.ToInt32(_header, 0x20); }
				set { ArrayFunctions.WriteToArray(value, _header, 0x20); }
			}
			int _imageDataOffset { get { return _frameHeaderLength + NumberOfColors * 3; } }
			internal int _length { get { return _imageDataOffset + 0x10 + _rows.Length; } }
			
			/// <summary>Maximum width allowed within a single Frame</summary>
			public const int MaximumWidth = 256;
			/// <summary>Maximum height allowed within a single Frame</summary>
			public const int MaximumHeight = 256;
			
			/// <summary>Populate the Frame with information from raw data</summary>
			/// <param name="raw">Complete raw byte data of a Frame</param>
			public Frame(byte[] raw)
			{
				_header = new byte[_frameHeaderLength];
				ArrayFunctions.TrimArray(raw, 0, _header);
				if (BitConverter.ToInt32(_header, 4) != _frameHeaderLength) throw new ArgumentException(_validationErrorMessage, "file");
				if (BitConverter.ToInt32(_header, 0x24) != _frameHeaderReserved) throw new ArgumentException(_validationErrorMessage, "file");
				_colors = new Color[BitConverter.ToInt32(_header, 0x28)];
				for (int c = 0, pos = _frameHeaderLength; c < _colors.Length; c++, pos += 3)
					_colors[c] = Color.FromArgb(raw[pos], raw[pos + 1], raw[pos + 2]);	// Red, Green, Blue
				int offset = _frameHeaderLength + _colors.Length * 3;
				_location = new Point(BitConverter.ToInt32(raw, offset), BitConverter.ToInt32(raw, offset + 4));	// FrameLeft, FrameTop
				offset += 0x10;
				_rows = new byte[raw.Length - offset];
				ArrayFunctions.TrimArray(raw, offset, _rows);
				_image = DecodeImage(_rows, BitConverter.ToInt32(_header, 0x10), BitConverter.ToInt32(_header, 0x14), _colors, BitConverter.ToInt32(_header, 0x20));
			}
			/// <summary>Create a new Frame with the given image and color array</summary>
			/// <param name="image">Image to be used for the Frame, will use <i>colors</i> as the palette</param>
			/// <param name="colors">Colors to be used for the Frame</param>
			public Frame(Bitmap image, Color[] colors)
			{
				_colors = colors;
				if (image.PixelFormat != PixelFormat.Format8bppIndexed) _image = GraphicsFunctions.ConvertTo8bpp(image, _colors);
				else _image = image;
				_colors = GraphicsFunctions.GetTrimmedColors(_image);
				
				_header = new byte[_frameHeaderLength];
				ArrayFunctions.WriteToArray(_frameHeaderLength, _header, 4);
				int shift = 3;	// Shift=3, allows 8px lines, 00-1F ColorIndex
				if (_image.Width <= 16) shift = 4;	// Shift=4 allows 16px lines, 00-0F ColorIndex
				if (_colors.Length <= 8) shift = 5;	// Shift=5 allows 32px, 00-08 ColorIndex
				else if (_colors.Length <= 16) shift = 4;
				ArrayFunctions.WriteToArray(shift, _header, 0x20);
				ArrayFunctions.WriteToArray(_frameHeaderReserved, _header, 0x24);
				
				_location = new Point(-_image.Width/2, -_image.Height/2);
				_rows = EncodeImage(_image, _colors, shift);
				
				_updateHeader();
			}
			
			void _setShift()
			{
				int shift = 3;	// Shift=3, allows 8px lines, 00-1F ColorIndex
				if (_image.Width <= 16) shift = 4;	// Shift=4 allows 16px lines, 00-0F ColorIndex
				if (_colors.Length <= 8) shift = 5;	// Shift=5 allows 32px, 00-08 ColorIndex
				else if (_colors.Length <= 16) shift = 4;
				ArrayFunctions.WriteToArray(shift, _header, 0x20);
			}
			
			void _updateHeader()
			{
				int imageDataOffset = _imageDataOffset;
				ArrayFunctions.WriteToArray(imageDataOffset + _rows.Length, _header, 0);
				ArrayFunctions.WriteToArray(imageDataOffset, _header, 8);
				ArrayFunctions.WriteToArray(imageDataOffset + _rows.Length, _header, 0xC);
				Width = _image.Width;
				Height = _image.Height;
				ArrayFunctions.WriteToArray(_colors.Length, _header, 0x28);	
			}
			
			/// <summary>Modifies the given palette entry</summary>
			/// <param name="index">Color index</param>
			/// <param name="color">Color to be used</param>
			/// <exception cref="System.IndexOutOfRangeException">Invalid <i>index</i> value</exception>
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
			/// <exception cref="System.IndexOutOfRangeException">Invalid <i>index</i> value</exception>
			public void SetColor(int index, byte red, byte green, byte blue) { SetColor(index, Color.FromArgb(red, green, blue)); }
			
			/// <summary>Gets a copy of the used color array</summary>
			public Color[] Colors { get { return (Color[])_colors.Clone(); } }
			
			/// <summary>Gets the height of the Frame</summary>
			public int Height
			{
				get { return BitConverter.ToInt32(_header, 0x14); }
				internal set { ArrayFunctions.WriteToArray(value, _header, 0x14); }
			}
			
			/// <summary>Gets the frame image</summary>
			public Bitmap Image
			{
				get { return _image; }
				internal set
				{
					_image = value;
					_colors = GraphicsFunctions.GetTrimmedColors(_image);
					_setShift();
					_rows = EncodeImage(_image, _colors, _shift);
					_updateHeader();
				}
			}
			
			/// <summary>Gets or sets the frame origin position relative to Act.Center</summary>
			public Point Location
			{
				get { return _location; }
				set { _location = value; }	// TODO: Location validation
			}
			
			/// <summary>Gets the number of colors defined by the Frame</summary>
			public int NumberOfColors { get { return BitConverter.ToInt32(_header, 0x28); } }
			
			/// <summary>Gets the width of the Frame</summary>
			public int Width
			{
				get { return BitConverter.ToInt32(_header, 0x10); }
				internal set { ArrayFunctions.WriteToArray(value, _header, 0x10); }
			}
		}
	}
}
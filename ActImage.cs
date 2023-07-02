/*
 * Idmr.ImageFormat.Act, Allows editing capability of LucasArts *.ACT files.
 * Copyright (C) 2009-2023 Michael Gaisser (mjgaisser@gmail.com)
 *
 * This library is free software; you can redistribute it and/or modify it
 * under the terms of the Mozilla Public License; either version 2.0 of the
 * License, or (at your option) any later version.
 *
 * This library is "as is" without warranty of any kind; including that the
 * library is free of defects, merchantable, fit for a particular purpose or
 * non-infringing. See the full license text for more details.
 *
 * If a copy of the MPL (MPL.txt) was not distributed with this file,
 * you can obtain one at http://mozilla.org/MPL/2.0/.
 * 
 * VERSION: 2.1+
 */

/* CHANGE LOG
 * [NEW] Global colors read support
 * [UPD] Proper use of jumps instead of fixed header lengths
 * [UPD] renamed frame shift to lengthBitCount (private)
 * v2.1, 141214
 * [UPD] switch to MPL
 * v2.0, 121024
 * [UPD] major re-write...
 */
 
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Idmr.Common;

namespace Idmr.ImageFormat.Act
{
	/// <summary>Object to work with *.ACT image files found in TIE95, XvT and BoP. Also reads legacy XACT resources from TIE LFD files.</summary>
	/// <remarks>Acts can be loaded from file or created starting with a Bitmap. Each frame retains an individual palette.<br/>
	/// XACT resources must be saved as separate .ACT files instead of within their original LFD resource. To edit XACT resource, use Idmr.LfdReader.dll.</remarks>
	public class ActImage
	{
		string _filePath;
		FrameCollection _frames;
        readonly byte[] _header = new byte[_fileHeaderLength];
		Point _center;
		const int _fileHeaderLength = 0x34;
		internal static string _validationErrorMessage = "Validation error, data is not a LucasArts Act Image or is corrupted.";
		static readonly string _extension = ".ACT";
		Color[] _colors;
        bool _useGlobalColors;

        #region constructors
        /// <summary>Loads an Act image from file</summary>
        /// <param name="file">Full path to the ACT file</param>
        /// <exception cref="LoadFileException">Error loading <i>file</i></exception>
        public ActImage(string file)
		{
			FileStream fs = null;
			try
			{
				if (!file.ToUpper().EndsWith(_extension)) throw new ArgumentException("Invalid file extension, must be " + _extension, "file");
				fs = File.OpenRead(file);
				DecodeFile(new BinaryReader(fs).ReadBytes((int)fs.Length));
				fs.Close();
			}
			catch (Exception x)
			{
				fs?.Close();
				System.Diagnostics.Debug.WriteLine(x.StackTrace);
				throw new LoadFileException(x);
			}
			_filePath = file;
		}
		
		/// <summary>Loads an XACT resource from file</summary>
		/// <param name="raw">RawData from an LFD resource</param>
		/// <exception cref="ArgumentException">Error processing <i>raw</i></exception>
		public ActImage(byte[] raw)
		{
			try { DecodeFile(raw); }
			catch (Exception x)
			{
				System.Diagnostics.Debug.WriteLine(x.StackTrace);
				throw x;
			}
			_filePath = "LFD";
		}

        /// <summary>Creates a new Act image from bitmap</summary>
        /// <remarks><see cref="FilePath"/> defaults to <b>"NewImage.act"</b>, <see cref="Frames"/> is initialized as a single <see cref="Frame"/> using <paramref name="image"/>.<br/>
        /// <see cref="Center"/> defaults to the center pixel of <paramref name="image"/>.</remarks>
        /// <param name="image">Initial <see cref="PixelFormat.Format8bppIndexed"/> image to be used</param>
        /// <exception cref="BoundaryException"><paramref name="image"/> exceeds allowable size.</exception>
        public ActImage(Bitmap image)
		{
            _frames = new FrameCollection(this)
            {
                new Frame(this, image)
            };
            _filePath = "NewImage.act";
			_center = new Point(Width/2, Height/2);
			_frames[0].Location = new Point(-Center.X, -Center.Y);
		}
		#endregion constructors
		
		#region public methods
		/// <summary>Populates the Act object from the raw byte data</summary>
		/// <param name="raw">Entire contents of an *.ACT file</param>
		/// <exception cref="ArgumentException">Data validation failure</exception>
		public void DecodeFile(byte[] raw)
		{
			ArrayFunctions.TrimArray(raw, 0, _header);
			var offsetsJump = BitConverter.ToInt32(_header, 0x10);	// usually same as_fileHeaderLength, but could technically be different
			_center = new Point(BitConverter.ToInt32(_header, 0x24), BitConverter.ToInt32(_header, 0x28));
			// Global colors, not likely to be used
			_useGlobalColors = _header[0x2c] == 0x18;
			if (_useGlobalColors)
			{
				_colors = new Color[BitConverter.ToInt32(_header, 0x30)];
				var colorsJump = BitConverter.ToInt32(_header, 0xc);
                System.Diagnostics.Debug.WriteLine("Global Colors: " + NumberOfColors);
                for (int c = 0, pos = colorsJump; c < _colors.Length; c++, pos += 4)
                    _colors[c] = Color.FromArgb(raw[pos], raw[pos + 1], raw[pos + 2]);  // Red, Green, Blue, Alpha unused
            }
            // Frames
            int numFrames = BitConverter.ToInt32(_header, 0x18);
			int[] frameOffsets = new int[numFrames];
			System.Diagnostics.Debug.WriteLine("Frames: " + numFrames);
			ArrayFunctions.TrimArray(raw, offsetsJump, frameOffsets);
			_frames = new FrameCollection(this);
			byte[] rawFrame;
			for (int f = 0; f < numFrames; f++)
			{
				// FrameHeader
				rawFrame = new byte[BitConverter.ToInt32(raw, frameOffsets[f])];
				ArrayFunctions.TrimArray(raw, frameOffsets[f], rawFrame);
				_frames.Add(new Frame(this, rawFrame));
			}
			// EOF
		}
		
		/// <summary>Gets the image from the given information</summary>
		/// <param name="raw">The encoded Rows data</param>
		/// <param name="width">Width of the Frame</param>
		/// <param name="height">Height of the Frame</param>
		/// <param name="colors">Defined Color array to be used</param>
		/// <param name="lengthBitCount">Shift value to decode with</param>
		/// <exception cref="ArgumentException">Data validation failure</exception>
		/// <returns>8bppIndexed image</returns>
		public static Bitmap DecodeImage(byte[] raw, int width, int height, Color[] colors, int lengthBitCount)
		{
			Bitmap image = new Bitmap(width, height, PixelFormat.Format8bppIndexed);
			BitmapData bd = GraphicsFunctions.GetBitmapData(image);
			byte[] pixelData = new byte[bd.Stride * bd.Height];
			byte b, indexShift = 0;
			int offset = 0;
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
						var unused = raw[offset++];   // this appears to always be zero
						if (unused != 0) System.Diagnostics.Debug.WriteLine("16-bit Shift value detected: index: " + unused * 0x100 + indexShift);
					}
					else	// Short code
					{
						byte p = (byte)(b >> lengthBitCount);
						byte n = (byte)(Math.Pow(2, lengthBitCount) - 1);
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
        /// <param name="image">Image to be encoded.</param>
        /// <param name="colors">Defined Color array to be used.</param>
        /// <param name="lengthBitCount">Shift value to encode with.</param>
        /// <exception cref="ArgumentException"><paramref name="image"/> is not 8bppIndexed<br/><b>-or-</b><br/>Invalid <paramref name="lengthBitCount"/> value.</exception>
        /// <remarks><paramref name="image"/> must be 8bppIndexed. <paramref name="lengthBitCount"/> restricted to 3-5.</remarks>
        /// <returns>Encoded byte array of the image ready to be written to disk</returns>
        public static byte[] EncodeImage(Bitmap image, Color[] colors, int lengthBitCount)
		{
			if (image.PixelFormat != PixelFormat.Format8bppIndexed) throw new ArgumentException("image must be 8bppIndexed", "image");
			if (lengthBitCount < 3 || lengthBitCount > 5) throw new ArgumentException("Bit count must be 3-5", "lengthBitCount");
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
					if ((len <= Math.Pow(2, lengthBitCount) && pixels[pos + x] < (0xFF >> lengthBitCount)) || (len <= (lengthBitCount == 3 ? 3 : 10) && pixels[pos + x] == (0xFF >> lengthBitCount)))	// allow 0xF8-0xFA
					{	// Short code
						byte b = (byte)(len - 1);
						b |= (byte)(pixels[pos + x] << lengthBitCount);
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
			//TODO: also need to trim colors
		}

        /// <summary>Writes the Act object to its original location</summary>
        /// <exception cref="SaveFileException">Error saving file. Original unchanged if applicable</exception>
        /// <exception cref="InvalidOperationException">Attempted to save XACT resource without defining a new location</exception>
        public void Save()
		{
			if (!_filePath.ToUpper().EndsWith(_extension))
				throw new InvalidOperationException("Must define temporary location for LFD XACT resources");
			FileStream fs = null;
			string tempFile = _filePath + ".tmp";
			//TODO: convert Global Colors to frame colors
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
				ArrayFunctions.WriteToArray(_center.X, _header, 0x24);
				ArrayFunctions.WriteToArray(_center.Y, _header, 0x28);
				// Width, Height
				// FrameOffsets
				int[] frameOffsets = new int[NumberOfFrames];
				byte[] frameOffsetsBytes = new byte[NumberOfFrames * 4];
				frameOffsets[0] = _fileHeaderLength + NumberOfFrames * 4;
				for (int f = 1; f < NumberOfFrames; f++) frameOffsets[f] = frameOffsets[f - 1] + _frames[f - 1]._length;
				ArrayFunctions.TrimArray(frameOffsets, 0, frameOffsetsBytes);
				
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
						fs.Position++;
					}
					//BUG: missing the frame extents
					bw.Write(_frames[f]._rows);
				}
				fs.SetLength(length);
				fs.Close();
				File.Delete(tempFile);	// delete backup if it exists
			}
			catch (Exception x)
			{
				fs?.Close();
				if (File.Exists(tempFile)) File.Copy(tempFile, _filePath);	// restore backup if it exists
				File.Delete(tempFile);	// delete backup if it exists
				System.Diagnostics.Debug.WriteLine(x.StackTrace);
				throw new SaveFileException(x);
			}
		}

        /// <summary>Writes the Act object to a new location</summary>
        /// <param name="file">Full path to the new file location</param>
        /// <exception cref="ArgumentException">Invalid file extension</exception>
        /// <exception cref="SaveFileException">Error saving file. Original unchanged if applicable</exception>
        public void Save(string file)
		{
			if (!file.ToUpper().EndsWith(_extension))
				throw new ArgumentException("New file extension must be \"" + _extension + "\". XACT objects from LFD resources must be saved as a separate " + _extension + ", temporary or otherwise. The LfdFile object will handle LFD resource saving as necessary.", "file");
			_filePath = file;
			Save();
		}
		#endregion public methods
		
		#region public properties
		/// <summary>Gets or sets the collection of <see cref="Frames">Frames</see> contained within the Act</summary>
		public FrameCollection Frames
        {
            get => _frames;
            set
            {
                _frames = value;
                _frames._parent = this;
                for (int f = 0; f < _frames.Count; f++) _frames[f]._parent = this;
                recalculateSize();
            }
        }
        /// <summary>Gets or sets the pixel location used to "pin" the object in-game</summary>
        /// <exception cref="BoundaryException"><i>value</i> does not fall within <see cref="Size"/></exception>
        /// <remarks><see cref="Frame.Location"/> values will update as necessary</remarks>
        public Point Center
        {
            get => _center;
            set
            {
                if (value.X < Width && value.X >= 0 && value.Y < Height && value.Y >= 0)
                {
                    int offX = _center.X - value.X;
                    int offY = _center.Y - value.Y;
                    for (int f = 0; f < NumberOfFrames; f++)
                    {
                        _frames[f].X += offX;
                        _frames[f].Y += offY;
                    }
                    _center = value;
                }
                else throw new BoundaryException("value", "0,0 - " + Width + "," + Height);
            }
        }

        /// <summary>Gets the file name of the Act object</summary>
        public string FileName => StringFunctions.GetFileName(_filePath);

        /// <summary>Gets the full path of the Act object</summary>
        public string FilePath => _filePath;

        /// <summary>Gets the overall height of the Act object</summary>
        public int Height => BitConverter.ToInt32(_header, 0x20) + 1;

        /// <summary>Gets the number of images contained within the Act object</summary>
        public int NumberOfFrames => _frames.Count;

        /// <summary>Gets the overall size of the Act object</summary>
        public Size Size
        {
            get => new Size(Width, Height);
            internal set
            {
                ArrayFunctions.WriteToArray(value.Width - 1, _header, 0x1C);
                ArrayFunctions.WriteToArray(value.Height - 1, _header, 0x20);
            }
        }

        /// <summary>Gets the overall width of the Act object</summary>
        public int Width => BitConverter.ToInt32(_header, 0x1C) + 1;

        /// <summary>Gets a copy of the global color array.</summary>
        /// <exception cref="NullReferenceException">Global colors are not used.</exception>
        public Color[] GlobalColors => (Color[])_colors.Clone();

        /// <summary>Gets or sets if the global color array is used.</summary>
        /// <remarks>If setting to <b>true</b>, initializes a 256 color array.</remarks>
        public bool UseGlobalColors {
			get => _useGlobalColors;
			set
			{
				if (value) _colors = new Color[256];
				else _colors = null;
				_useGlobalColors = value;
			}
		}

        /// <summary>Gets the number of global colors defined.</summary>
        public int NumberOfColors {
			get
			{
				if (_useGlobalColors) return _colors.Length;
				else return 0;
			}
		}
        #endregion

        internal void recalculateSize()
		{
			int right = 0, bottom = 0;
			int centX = _center.X;
			int centY = _center.Y;
			int left = centX + _frames[0].X;
			int top = centY + _frames[0].Y;
			for (int f = 0; f < NumberOfFrames; f++)
			{
				int fLeft = centX + _frames[f].X;
				int fTop = centY + _frames[f].Y;
				left = (fLeft < left ? fLeft : left);
				top = (fTop < top ? fTop : top);

				int fRight = fLeft + _frames[f].Width - 1;
				int fBottom = fTop + _frames[f].Height - 1;
				right = (fRight > right ? fRight : right);
				bottom = (fBottom > bottom ? fBottom : bottom);
			}
			// may be non-zero after Frame.Location adjustment or Frames/Frames[].set
			_center.X -= left;
			_center.Y -= top;
			
			Size = new Size(right - left + 1, bottom - top + 1);
		}
	}
}
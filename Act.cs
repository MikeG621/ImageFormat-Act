/*
 * Idmr.ImageFormat.Act, Allows editing capability of LucasArts *.ACT files.
 * Copyright (C) 2009 Michael Gaisser (mjgaisser@gmail.com)
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
 */
 
 // 101202 - Added indEx()
 // 110810 - Changed license to GPL

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace Idmr.ImageFormat
{
	/// <summary>Object to work with *.ACT image files found in TIE95, XvT and BoP</summary>
	/// <remarks>Acts can be loaded from file or created starting with a Bitmap. Unlike other image formats, each frame retains an individual palette</remarks>
	public class Act
	{
		private string _filePath;
		private Size _size;
		private Point _center;
		private Color[][] _colors;
		private Point[] _frameLocations;
		private Bitmap[] _frames;

		/// <summary>Loads an Act image from file</summary>
		/// <param name="file">Full path to the ACT file</param>
		/// <exception cref="System.ArgumentException">File is not a LucasArts Act Image File</exception>
		/// <exception cref="System.UnauthorizedAccessException">File may be open by another process</exception>
		/// <exception cref="System.IO.FileNotFoundException">File is not located at <i>file</i></exception>
		public Act(string file)
		{
			_filePath = file;
			if (!File.Exists(_filePath)) throw new FileNotFoundException();
			if (!_filePath.ToUpper().EndsWith(".ACT")) throw new ArgumentException("Invalid file extension");
			FileStream fs;
			try { fs = File.OpenRead(_filePath); }
			catch (UnauthorizedAccessException) { throw; }
			BinaryReader br = new BinaryReader(fs);
			fs.Position = 0x10;
			if (br.ReadInt32() != 0x34) throw new ArgumentException("File is not a LucasArts Act Image file.");	// HeaderLength
			// FrameHeader
			fs.Position = 0x18;
			_frames = new Bitmap[br.ReadInt32()];	// FrameCount
			int[] frameOffsets = new int[NumberOfFrames];
			_colors = new Color[NumberOfFrames][];
			_frameLocations = new Point[NumberOfFrames];
			_size = new Size(br.ReadInt32()+1, br.ReadInt32()+1);	// ImageWidth, ImageHeight
			_center = new Point(br.ReadInt32(), br.ReadInt32());	// CenterX, CenterY
			fs.Position = 0x34;
			// FrameOffsets
			for (int f=0;f<NumberOfFrames;f++) frameOffsets[f] = br.ReadInt32();	// FrameOffsets
			// Frames
			for (int f=0;f<NumberOfFrames;f++)
			{
				// FrameHeader
				fs.Position = frameOffsets[f] + 0x10;
				int frameWidth = br.ReadInt32();	// FrameWidth
				int frameHeight = br.ReadInt32();	// FrameHeight
				_frames[f] = new Bitmap(frameWidth, frameHeight, PixelFormat.Format8bppIndexed);
				BitmapData bd = _frames[f].LockBits(new Rectangle(new Point(), _frames[f].Size), ImageLockMode.ReadWrite, _frames[f].PixelFormat);
				byte[] pixelData = new byte[bd.Stride*bd.Height];
				fs.Position += 8;
				int shift = br.ReadInt32();	// Shift
				fs.Position += 4;
				_colors[f] = new Color[br.ReadInt32()];	// ColorCount
				for (int c=0;c<_colors[f].Length;c++)
				{
					_colors[f][c] = Color.FromArgb(br.ReadByte(), br.ReadByte(), br.ReadByte());	// Red, Green, Blue
					fs.Position++;
				}
				_frameLocations[f] = new Point(br.ReadInt32(), br.ReadInt32());	// FrameLeft, FrameTop
				fs.Position += 8;
				byte b, indexShift=0;
				int numShift=0;
				// Rows
				for (int y=(bd.Height-1);y>=0;y--)
				{
					for (int x=0, pos=bd.Stride*y;x<bd.Width;)
					{
						// OpCodes
						b = br.ReadByte();	// OpCode
						if (b == 0xFD)
						{
							byte numRepeats = br.ReadByte();
							byte colorIndex = br.ReadByte();
							for (int j=0;j<=numRepeats;j++, x++) pixelData[pos+x] = colorIndex;
						}
						else if (b == 0xFC)
						{
							byte numRepeats = br.ReadByte();
							x += numRepeats + 1;
						}
						else if (b == 0xFB)
						{
							indexShift = br.ReadByte();
							numShift = br.ReadByte();
						}
						else
						{
							byte p = (byte)(b >> shift);
							byte n = (byte)(Math.Pow(2, shift) - 1);
							for (int j=0;j<=(b&n);j++, x++) pixelData[pos+x] = (byte)(p + indexShift);
						}
					}
					fs.Position++;	// skip over EndRow (0xFE)
				}
				fs.Position++;	// skip over EndFrame (0xFF)
				// end Frame
				copyBytesToImage(pixelData, bd.Scan0);
				_frames[f].UnlockBits(bd);
				_frames[f].RotateFlip(RotateFlipType.RotateNoneFlipX);
				ColorPalette pal = new Bitmap(1, 1, PixelFormat.Format8bppIndexed).Palette;
				for (int c=0;c<_colors[f].Length;c++) pal.Entries[c] = GetColor(f, c);
				for (int c=_colors[f].Length;c<256;c++) pal.Entries[c] = Color.Blue;
				_frames[f].Palette = pal;
			}
			// EOF
			fs.Close();
		}

		/// <summary>Creates a new Act image from bitmap</summary>
		/// <remarks>FilePath defaults to "NewImage.act", the palette is initialized as 256 colors and <i>image</i> is processed via SetFrame( ).
		/// Center defaults to center of <i>image</i></remarks>
		/// <param name="image">Image to be used as the new ACT</param>
		/// <exception cref="System.ArgumentException"><i>image</i> is larger than 256x256</exception>
		public Act(Bitmap image)
		{
			_frames = new Bitmap[1];
			_colors = new Color[1][];
			_colors[0] = new Color[256];
			_frames[0] = new Bitmap(1, 1, PixelFormat.Format8bppIndexed);
			for (int c=0;c<_frames[0].Palette.Entries.Length;c++) _colors[0][c] = _frames[0].Palette.Entries[c];
			SetFrame(0, image);
			_filePath = "NewImage.act";
			_center = new Point(_size.Width/2, _size.Height/2);
			_frameLocations = new Point[1];
			_frameLocations[0] = new Point(-_center.X, -_center.Y);
		}

		/// <summary>Writes the Act object to its original location</summary>
		/// <exception cref="System.IO.DirectoryNotFoundException"></exception>
		/// <exception cref="System.UnauthorizedAccessException"></exception>
		public void Save()
		{
			FileStream fs;
			BinaryWriter bw;
			try
			{
				File.Delete(_filePath);
				fs = File.OpenWrite(_filePath);
			}
			catch { throw; }
			bw = new BinaryWriter(fs);
			// FileHeader
			fs.Position = 4;	// come back to Length
			int totalColorCount = 0;
			for (int f=0;f<NumberOfFrames;f++) totalColorCount += _colors[f].Length;
			bw.Write(totalColorCount);
			fs.Position += 8;	// skip Reserved
			bw.Write((int)0x34);
			fs.Position += 4;	// skip Reserved
			bw.Write(NumberOfFrames);
			bw.Write(_size.Width-1);
			bw.Write(_size.Height-1);
			bw.Write(_center.X);
			bw.Write(_center.Y);
			fs.Position += 8;	// skip Reserved
			// FrameOffsets
			for (int f=0;f<NumberOfFrames;f++) fs.Position += 4;	// come back to FrameOffsets
			// Frames
			for (int f=0;f<NumberOfFrames;f++)
			{
				int frameOffset = (int)fs.Position;
				// update FrameOffsets
				fs.Position = 0x34 + f*4;
				bw.Write(frameOffset);
				fs.Position = frameOffset;
				// FrameHeader
				fs.Position += 4;	// come back to FrameLength
				bw.Write((int)0x2C);	// FrameHeaderLength
				bw.Write(0x2C + _colors[f].Length*4);	// ImageDataOffset
				fs.Position += 4;	// come back to FrameLength
				bw.Write(_frames[f].Width);
				bw.Write(_frames[f].Height);
				fs.Position += 8;	// skip Reserved
				int shift = 3;	// Shift=3, allows 8px lines, 00-1F ColorIndex
				if (_frames[f].Width <= 16) shift = 4;	// Shift=4 allows 16px lines, 00-0F ColorIndex
				if (_colors[f].Length <= 16) shift = 4;
				bw.Write(shift);
				bw.Write((int)0x18);	// Reserved
				bw.Write(_colors[f].Length);
				for (int c=0;c<_colors[f].Length;c++)
				{
					bw.Write(_colors[f][c].R);
					bw.Write(_colors[f][c].G);
					bw.Write(_colors[f][c].B);
					fs.Position++;	// skip Reserved
				}
				bw.Write(_frameLocations[f].X);
				bw.Write(_frameLocations[f].Y);
				bw.Write(_frames[f].Width + _frameLocations[f].X - 1);	// FrameRight
				bw.Write(_frameLocations[f].Y);	// FrameTop again
				_frames[f].RotateFlip(RotateFlipType.RotateNoneFlipX);
				// Rows
				BitmapData bd = _frames[f].LockBits(new Rectangle(new Point(), _frames[f].Size), ImageLockMode.ReadWrite, _frames[f].PixelFormat);
				byte[] pixels = new byte[bd.Stride*bd.Height];
				copyImageToBytes(bd.Scan0, pixels);
				for (int y=(bd.Height-1);y>=0;y--)
				{
					for (int x=0, pos=bd.Stride*y, len=1;x<bd.Width;)
					{
						try
						{	// throws on last row
							if ((x+len) != bd.Width && pixels[pos+x] == pixels[pos+x+len])
							{
								len++;
								continue;
							}
						}
						catch { /* do nothing */ }
						if ((len <= Math.Pow(2, shift) && pixels[pos+x] < (0xFF >> shift)) || (len <= (shift == 3 ? 3 : 10) && pixels[pos+x] == (0xFF >> shift)))	// allow 0xF8-0xFA
						{	// Short code
							byte b = (byte)(len-1);
							b |= (byte)(pixels[pos+x] << shift);
							bw.Write(b);
						}
						else if (pixels[pos+x] == 0)
						{	// Blank code
							bw.Write((byte)0xFC);
							bw.Write((byte)(len-1));
						}
						else
						{	// Repeat code
							bw.Write((byte)0xFD);
							bw.Write((byte)(len-1));
							bw.Write(pixels[pos+x]);
						}
						// not going to use Shift codes
						x += len;
						len = 1;
					}
					bw.Write((byte)0xFE);	// EndRow
				}
				bw.Write((byte)0xFF);	// EndFrame
				_frames[f].UnlockBits(bd);
				_frames[f].RotateFlip(RotateFlipType.RotateNoneFlipX);
				long frameEnd = fs.Position;
				// update FrameLengths
				fs.Position = frameOffset;
				bw.Write((int)(frameEnd-frameOffset));
				fs.Position = frameOffset + 0xC;
				bw.Write((int)(frameEnd-frameOffset));
				fs.Position = frameEnd;
			}
			fs.SetLength(fs.Position);
			// update Length
			fs.Position = 0;
			bw.Write((int)fs.Length);
			fs.Close();
		}

		/// <summary>Writes the Act object to a new location</summary>
		/// <param name="file">Full path to the new ACT file</param>
		/// <exception cref="System.IO.DirectoryNotFoundException"></exception>
		/// <exception cref="System.UnauthorizedAccessException"></exception>
		public void Save(string file)
		{
			_filePath = file;
			Save();
		}

		#region public properties
		/// <value>Gets or Sets the pixel location used to "pin" the Act object in-game</value>
		/// <exception cref="System.ArgumentException">Value does not fall within Act dimensions</exception>
		public Point Center
		{
			get { return _center; }
			set
			{
				if (value.X < _size.Width && value.X >= 0 && value.Y < _size.Height && value.Y >= 0) _center = value;
				else throw new ArgumentException("Value must be non-negative and reside within Image dimensions (" + _size.Width + "," + _size.Height + ")");
			}
		}

		/// <value>Gets the file name of the Act object</value>
		public string FileName { get { return _filePath.Substring(_filePath.LastIndexOf("\\")+1); } }

		/// <value>Gets the full path of the Act object</value>
		public string FilePath { get { return _filePath; } }

		/// <value>Gets the array of (Left,Top) values of each frame, relative to Center</value>
		public Point[] FrameLocations { get { return _frameLocations; } }

		/// <value>Gets the overall height of the Act object</value>
		public int Height { get { return _size.Height; } }

		/// <value>Gets the number of images contained within the Act object</value>
		public int NumberOfFrames { get { return _frames.Length; } }

		/// <value>Gets the overall size of the Act object</value>
		public Size Size { get { return _size; } }

		/// <value>Gets the overall width of the Act object</value>
		public int Width { get { return _size.Width; } }
		#endregion

		#region public accessors
		/// <summary>Gets the Color of the given frame and palette index</summary>
		/// <param name="frame">Zero-indexed frame</param>
		/// <param name="index">Color index</param>
		/// <returns>Color of the indicated palette entry</returns>
		/// <exception cref="System.IndexOutOfRangeException">Invalid <i>frame</i> or <i>index</i> value</exception>
		public Color GetColor(int frame, int index)
		{
			if (frame < 0 || frame >= NumberOfFrames) throw indEx("frame", "less than " + NumberOfFrames);
			if (index < 0 || index >= GetNumberOfColors(frame)) throw indEx("index", "less than " + GetNumberOfColors(frame));
			return _colors[frame][index];
		}

		/// <summary>Gets the image for the given frame</summary>
		/// <param name="frame">Zero-indexed frame</param>
		/// <returns>8bppIndexed image of selected frame</returns>
		/// <exception cref="System.IndexOutOfRangeException">Invalid <i>frame</i> value</exception>
		public Bitmap GetFrame(int frame)
		{
			if (frame < 0 || frame >= NumberOfFrames) throw indEx("frame", "less than " + NumberOfFrames);
			return _frames[frame];
		}

		/// <summary>Gets the size of the given frame</summary>
		/// <param name="frame">Zero-indexed frame</param>
		/// <returns>Size of the given frame</returns>
		/// <exception cref="System.IndexOutOfRangeException">Invalid <i>frame</i> value</exception>
		public Size GetFrameSize(int frame)
		{
			if (frame < 0 || frame >= NumberOfFrames) throw indEx("frame", "less than " + NumberOfFrames);
			return _frames[frame].Size;
		}

		/// <summary>Gets the number of colors used in the given frame</summary>
		/// <param name="frame">Zero-indexed frame</param>
		/// <returns>Number of colors defined in the frame</returns>
		/// <exception cref="System.IndexOutOfRangeException">Invalid <i>frame</i> value</exception>
		public int GetNumberOfColors(int frame)
		{
			if (frame < 0 || frame >= NumberOfFrames) throw indEx("frame", "less than " + NumberOfFrames);
			return _colors[frame].Length;
		}
		#endregion

		#region public modifiers
		/// <summary>Modifies the given palette entry</summary>
		/// <param name="frame">Zero-indexed frame</param>
		/// <param name="index">Color index</param>
		/// <param name="color">Color to be used</param>
		/// <exception cref="System.IndexOutOfRangeException">Invalid <i>frame</i> or <i>index</i> value</exception>
		public void SetColor(int frame, int index, Color color)
		{
			//if (frame < 0 || frame >= NumberOfFrames) throw new IndexOutOfRangeException("Parameter 'frame' must be non-negative and less than " + NumberOfFrames);
			//if (index < 0 || index >= GetNumberOfColors(frame)) throw new IndexOutOfRangeException("Parameter 'index' must be non-negative and less than " + GetNumberOfColors(frame));
			if (frame < 0 || frame >= NumberOfFrames) throw indEx("frame", "less than " + NumberOfFrames);
			if (index < 0 || index >= GetNumberOfColors(frame)) throw indEx("index", "less than " + GetNumberOfColors(frame));
			_colors[frame][index] = color;
			ColorPalette pal = _frames[frame].Palette;
			pal.Entries[index] = color;
			_frames[frame].Palette = pal;
		}

		/// <summary>Modifies the given palette entry</summary>
		/// <param name="frame">Zero-indexed frame</param>
		/// <param name="index">Color index</param>
		/// <param name="r">R value of color to be used</param>
		/// <param name="g">G value of color to be used</param>
		/// <param name="b">B value of color to be used</param>
		/// <exception cref="System.IndexOutOfRangeException">Invalid <i>frame</i> or <i>index</i> value</exception>
		public void SetColor(int frame, int index, byte r, byte g, byte b) { SetColor(frame, index, Color.FromArgb(r, g, b)); }

		/// <summary>Replaces indicated frame with new image</summary>
		/// <param name="frame">Zero-indexed frame</param>
		/// <param name="image">New image. If 8bppIndexed, image's palette is used, else existing is</param>
		/// <exception cref="System.ArgumentException"><i>image</i> is larger than this.Size or 256x256</exception>
		/// <exception cref="System.IndexOutOfRangeException">Invalid <i>frame</i> value</exception>
		public void SetFrame(int frame, Bitmap image)
		{
			//if (frame < 0 || frame >= NumberOfFrames) throw new IndexOutOfRangeException("Parameter 'frame' must be non-negative and less than " + NumberOfFrames);
			if (frame < 0 || frame >= NumberOfFrames) throw indEx("frame",  "less than " + NumberOfFrames);
			if (image.Width > 256 || image.Height > 256) throw new ArgumentException("Image.Size exceeds 256x256 pixels");
			if (NumberOfFrames == 1) _size = image.Size;
			if (image.Width > Width || image.Height > Height) throw new ArgumentException("Image.Size exceeds Act.Size");
			// initial palette prep
			ColorPalette pal = new Bitmap(1, 1, PixelFormat.Format8bppIndexed).Palette;
			if (image.PixelFormat == PixelFormat.Format8bppIndexed) pal = image.Palette;
			else if (image.PixelFormat == PixelFormat.Format1bppIndexed)
			{
				pal.Entries[0] = image.Palette.Entries[0];
				pal.Entries[1] = image.Palette.Entries[1];
			}
			else if (image.PixelFormat == PixelFormat.Format4bppIndexed)
			{
				for (int i=0;i<image.Palette.Entries.Length;i++) pal.Entries[i] = image.Palette.Entries[i];
			}
			else for (int j=0;j<_colors[frame].Length;j++) pal.Entries[j] = GetColor(frame, j);
			// initialize and load _colors[frame] now, need it for the 8bpp conversion
			_colors[frame] = new Color[256];
			for (int c=0;c<pal.Entries.Length;c++) _colors[frame][c] = pal.Entries[c];
			// convert image to 8bppIndexed
			image = new Bitmap(image);	// force it to 32bppRGB
			_frames[frame] = new Bitmap(image.Width, image.Height, PixelFormat.Format8bppIndexed);
			BitmapData import = image.LockBits(new Rectangle(new Point(), image.Size), ImageLockMode.ReadWrite, image.PixelFormat);
			byte[] impPixels = new byte[import.Stride*import.Height];
			copyImageToBytes(import.Scan0, impPixels);
			BitmapData bd = _frames[frame].LockBits(new Rectangle(new Point(), image.Size), ImageLockMode.ReadWrite, _frames[frame].PixelFormat);
			byte[] pixels = new byte[bd.Stride*bd.Height];
			bool[] used = new bool[256];
			for (int y=0;y<bd.Height;y++)
				for (int x=0, impPos=y*import.Stride, pos=y*bd.Stride;x<bd.Width;x++)
				{
					pixels[pos+x] = findPaletteIndex(frame, impPixels[impPos+x*4+2], impPixels[impPos+x*4+1], impPixels[impPos+x*4]);
					used[pixels[pos+x]] = true;
				}
			// removed unused palette entries
			int count=1;
			for (int c=1;c<256;c++)
			{
				if (!used[c])
				{
					for (int i=count;i<pal.Entries.Length-1;i++) pal.Entries[i] = pal.Entries[i+1];
					for (int i=0;i<pixels.Length;i++) if (pixels[i] > count) pixels[i]--;
				}
				else count++;
			}
			// 8bpp conversion is done, now we can change this to the trimmed copy
			_colors[frame] = new Color[count];
			for (int c=0;c<count;c++) _colors[frame][c] = pal.Entries[c];
			copyBytesToImage(pixels, bd.Scan0);
			image.UnlockBits(import);
			_frames[frame].UnlockBits(bd);
			_frames[frame].Palette = pal;
		}
		#endregion

		private void copyBytesToImage(byte[] source, IntPtr destination)
		{
			System.Runtime.InteropServices.Marshal.Copy(source, 0, destination, source.Length);
		}
		private void copyImageToBytes(IntPtr source, byte[] destination)
		{
			System.Runtime.InteropServices.Marshal.Copy(source, destination, 0, destination.Length);
		}
		private byte findPaletteIndex(int frame, byte r, byte g, byte b)
		{
			int diff = 9999;	// high number for default
			double temp;
			byte index=0;
			for (int i=0;i<_colors[frame].Length;i++)
			{
				Color c = GetColor(frame, i);
				temp = Math.Pow((c.R-r), 2) + Math.Pow((c.G-g), 2) + Math.Pow((c.B-b), 2);
				if (temp < diff) { diff = (int)temp; index = (byte)i; }
				if (diff == 0) break;
			}
			return index;
		}
		
		private IndexOutOfRangeException indEx(string var, string limits)
		{
			IndexOutOfRangeException x = new IndexOutOfRangeException("Parameter '" + var + "' must be non-negative and " + limits);
			return x;
		}
	}
}
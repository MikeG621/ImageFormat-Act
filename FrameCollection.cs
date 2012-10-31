/*
 * Idmr.ImageFormat.Act, Allows editing capability of LucasArts *.ACT files.
 * Copyright (C) 2009-2012 Michael Gaisser (mjgaisser@gmail.com)
 * Licensed under the GPL v3.0 or later
 * 
 * Full notice in Act.cs
 * VERSION: 2.0
 */

/* CHANGE LOG
 * v2.0, 121024
 * - Release
 */

using System;
using System.Collections.Generic;
using Idmr.Common;

namespace Idmr.ImageFormat.Act
{
	/// <summary>Object to maintain Act image <see cref="Act.Frame">Frames</see></summary>
	/// <remarks><see cref="ResizableCollection{T}.ItemLimit"/> is set to <b>20</b></remarks>
	public class FrameCollection : ResizableCollection<Frame>
	{
		internal ActImage _parent;
		
		#region constructors
		/// <summary>Creates an empty Collection</summary>
		internal FrameCollection(ActImage parent)
		{
			_parent = parent;
			_itemLimit = 20;
			_items = new List<Frame>(_itemLimit);
		}
		#endregion constructors

		#region public methods
		/// <summary>Deletes the specified item from the Collection</summary>
		/// <param name="index">Item index</param>
		/// <returns><b>true</b> if successful, <b>false</b> for invalid <i>index</i> value</returns>
		/// <remarks>Cannot remove the lone <see cref="Frame"/> in a single=<see cref="Frame"/> collection.</remarks>
		public bool Remove(int index)
		{
			if (Count == 1) return false;
			bool success = (_removeAt(index) != -1);
			_parent._recalculateSize();
			return success;
		}
		#endregion public methods
		
		#region public properties
		/// <summary>Gets or sets a single item within the Collection</summary>
		/// <param name="index">The item location within the collection</param>
		/// <returns>A single item within the collection<br/>-or-<br/><b>null</b> for invalid values of <i>index</i></returns>
		/// <remarks>No action is taken when attempting to set with invalid values of <i>index</i>.</remarks>
		new public Frame this[int index]
		{
			get { return _getItem(index); }
			set
			{
				_setItem(index, value);
				if (index >= 0 && index < Count)
				{
					_items[index]._parent = _parent;
					_parent._recalculateSize();
				}
			}
		}
		
		/// <summary>Adds the given item to the end of the Collection</summary>
		/// <param name="item">The item to be added</param>
		/// <returns>The index of the added item if successful, otherwise <b>-1</b></returns>
		new public int Add(Frame item)
		{
			int index = _add(item);
			if (index != -1)
			{
				_items[index]._parent = _parent;
				_parent._recalculateSize();
			}
			return index;
		}
		
		/// <summary>Inserts the given item at the specified index</summary>
		/// <param name="index">Location of the item</param>
		/// <param name="item">The item to be added</param>
		/// <returns>The index of the added item if successful, otherwise <b>-1</b></returns>
		new public int Insert(int index, Frame item)
		{
			index = _insert(index, item);
			if (index != -1)
			{
				_items[index]._parent = _parent;
				_parent._recalculateSize();
			}
			return index;
		}
		#endregion public properties
	}
}
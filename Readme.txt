Idmr.ImageFormat.Act.dll
========================

Author: Michael Gaisser (mjgaisser@gmail.com)
Version: 2.2
Date: 2023.07.08

Library for editing LucasArts *.ACT backdrop files

==========
Version History

v2.2 - 08 Jul 2023
 - Added ability to read Global colors
   - GlobalColors, UseGlobalColors and NumberOfColors added
   - Not otherwise processed or used at this time
 - Added Frame.UseFrameColors
 - Will throw an error on load if both UseGlobalColors and UseFrameColors are false
 - New EncodeImage() overload. The previous one ignored colors anyway.

v2.1 - 14 Dec 2014
 - change to MPL
 - (FrameCollection) SetCOunt and IsModified implementation
v2.0 - 24 Oct 2012
 - Completely re-written

==========
Additional Information

Instructions:
 - Get latest version of Idmr.Common.dll (v1.1 or later)
 - Add Idmr.Common.dll and Idmr.ImageFormat.Act.dll to your references

File format structure can be found in ACT_Image_File.txt

Programmer's reference can be found in help/Idmr.ImageFormat.Act.chm

==========
Copyright Information

Copyright (C) Michael Gaisser, 2009-2023
This library file and related files are licensed under the Mozilla Public License
v2.0 or later.  See License.txt for further details.

"Star Wars" and related items are trademarks of LucasFilm Ltd and
LucasArts Entertainment Co.

THESE FILES HAVE BEEN TESTED AND DECLARED FUNCTIONAL, AS SUCH THE AUTHOR CANNOT
BE HELD RESPONSIBLE OR LIABLE FOR UNWANTED EFFECTS DUE ITS USE OR MISUSE. THIS
SOFTWARE IS OFFERED AS-IS WITHOUT WARRANTY OF ANY KIND.
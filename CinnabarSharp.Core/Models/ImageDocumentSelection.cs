// 
// Inspired from DocumentSelection.cs
//  
// Author:
//       Andrew Davis <andrew.3.1415@gmail.com>
// 
// Copyright (c) 2012 Andrew Davis, GSoC 2012
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
using System;
using System.Net;
using ImageMagick;
using ImageMagick.Drawing;
using CinnabarSharp.Core.Extensions;

namespace CinnabarSharp.Core.Models
{
	public class ImageDocumentSelection
	{
        private IPath selection_path;
        //public List<List<Point64>> SelectionPolygons = new List<List<Point64>>();

        public ImageDocumentSelection()
		{
		}

        public void Clip(object g)
        {
            throw new NotImplementedException("Clip");
            //g.AppendPath(SelectionPath);
            //g.FillRule = FillRule.EvenOdd;
            //g.Clip();
        }

        public IPath SelectionPath
        {
            get
            {
                if (selection_path == null)
                {
                    
                    ImageDocument doc = null!;//PintaCore.Workspace.ActiveDocument;
                    throw new NotImplementedException("PintaCore.Workspace.ActiveDocument");
/*
                    var g = new Context(doc.Layers.CurrentUserLayer.Surface);
                    selection_path = g.CreatePolygonPath(ConvertToPolygonSet(SelectionPolygons));
                    */
                }

                return selection_path;
            }
        }

        
/*
        public static PointI[][] ConvertToPolygonSet(List<List<Point64>> clipperPolygons)
        {
            var resultingPolygonSet = new PointI[clipperPolygons.Count][];

            int polygonNumber = 0;

            foreach (List<Point64> ipL in clipperPolygons)
            {
                resultingPolygonSet[polygonNumber] = new PointI[ipL.Count];

                int pointNumber = 0;

                foreach (Point64 ip in ipL)
                {
                    resultingPolygonSet[polygonNumber][pointNumber] = new PointI((int)ip.X, (int)ip.Y);

                    ++pointNumber;
                }

                ++polygonNumber;
            }

            return resultingPolygonSet;
        }
        */
    }
}


using System;
using System.Diagnostics.Metrics;
using System.Reflection;
using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Core.Services
{
    /// <summary>
    /// should be singleton
    /// </summary>
    public interface IDocumentsHistoryService
    {

        IImageDocumentHistory GetHistoryOf(ImageDocument document);
    }
}


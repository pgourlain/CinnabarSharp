using System;
using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Core.Services
{
	public class DocumentsHistoryService : IDocumentsHistoryService
    {
		public DocumentsHistoryService()
		{
		}

        public IImageDocumentHistory GetHistoryOf(ImageDocument document)
        {
            throw new NotImplementedException();
        }
    }
}


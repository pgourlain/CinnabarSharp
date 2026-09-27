using System;
namespace CinnabarSharp.Core.Models
{
	public class Translations
	{
        public static string GetString(string text, params object[] args)
        {
            return string.Format(text, args);
            //return catalog?.GetString(text, args) ?? text;
        }
    }
}


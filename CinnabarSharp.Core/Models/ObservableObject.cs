using System;
using System.ComponentModel;

namespace CinnabarSharp.Core.Models
{
    public abstract class ObservableObject
    {
        public ObservableObject()
        {
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void SetValue<T>(string propertyName, ref T member, T value)
        {
            member = value;
            FirePropertyChanged(propertyName);
        }

        protected void FirePropertyChanged(string? propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

}


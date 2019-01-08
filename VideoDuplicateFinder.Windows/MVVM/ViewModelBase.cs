using System;
using System.ComponentModel;
using System.Xml.Serialization;

namespace VideoDuplicateFinderWindows.MVVM
{
    public abstract class ViewModelBase : INotifyPropertyChanged, IDataErrorInfo
    {
        [field: NonSerialized]
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));

        protected void OnPropertyChanged(PropertyChangedEventArgs e) => PropertyChanged?.Invoke(this, e);

        [XmlIgnore]
        string IDataErrorInfo.Error => throw new NotImplementedException();

        [XmlIgnore]
        string IDataErrorInfo.this[string columnName] => Verify(columnName);

        [Browsable(false), XmlIgnore]
        public  virtual bool HasError => false;

        protected virtual string Verify(string columnName) => string.Empty;
        protected void HasErrorUpdated() => OnPropertyChanged(nameof(HasError));
     
    }
}

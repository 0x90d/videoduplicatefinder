using System.Collections.Generic;

namespace VideoDuplicateFinderWindows.Data
{
    using System.Collections.ObjectModel;
    using System.Collections.Specialized;
    using System.Windows.Threading;

    public class FastObservableCollection<T> : ObservableCollection<T>
    {
        private readonly object locker = new object();
        private bool suspendCollectionChangeNotification;
        public override event NotifyCollectionChangedEventHandler CollectionChanged;
        
        public void AddItems(IList<T> items)
        {
            lock (locker)
            {
                SuspendCollectionChangeNotification();
                foreach (var i in items)
                {
                    InsertItem(Count, i);
                }
                NotifyChanges();
            }
        }
        
        public void NotifyChanges()
        {
            ResumeCollectionChangeNotification();
            var arg = new NotifyCollectionChangedEventArgs (NotifyCollectionChangedAction.Reset);
            OnCollectionChanged(arg);
        }
        
        public void RemoveItems(IList<T> items)
        {
            lock (locker)
            {
                SuspendCollectionChangeNotification();
                foreach (var i in items)
                {
                    Remove(i);
                }
                NotifyChanges();
            }
        }

		public void ResumeCollectionChangeNotification() => suspendCollectionChangeNotification = false;

		public void SuspendCollectionChangeNotification() => suspendCollectionChangeNotification = true;

		protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            using (BlockReentrancy())
            {
                if (suspendCollectionChangeNotification) return;
                var eventHandler = CollectionChanged;
                if (eventHandler == null)
                    return;
                
                var delegates = eventHandler.GetInvocationList();

                foreach (var @delegate in delegates)
                {
                    var handler = (NotifyCollectionChangedEventHandler) @delegate;

                    if (handler.Target is DispatcherObject dispatcherObject && !dispatcherObject.CheckAccess())
                    {
                        dispatcherObject.Dispatcher.BeginInvoke (DispatcherPriority.DataBind, handler, this, e);
                    }
                    else
                    {
                        handler(this, e);
                    }
                }
            }
        }
    }
}

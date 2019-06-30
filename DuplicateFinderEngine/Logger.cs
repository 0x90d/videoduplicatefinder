using System;
using System.Collections.Concurrent;
using System.Text;

namespace DuplicateFinderEngine
{
    public sealed class Logger
    {
        private static Logger instance;
        public static Logger Instance => instance ?? (instance = new Logger());
        public event EventHandler LogItemAdded;

		public void ClearLog() => LogEntries.Clear();
		public override string? ToString()
        {
            var sb = new StringBuilder();
            foreach (var item in LogEntries)
            {
                sb.AppendLine("---------------");
                sb.AppendLine(item.ToString());
            }
            return sb.ToString();
        }
        public void Info(string text)
        {
            LogEntries.Add(new LogItem { DateTime = DateTime.Now.ToString("HH:mm:ss"), Message = text});
            LogItemAdded?.Invoke(null,new EventArgs());
        }
        public ConcurrentBag<LogItem> LogEntries { get; internal set; } = new ConcurrentBag<LogItem>();
    }

    public sealed class LogItem
    {
		public string? DateTime { get; set; }
        public string? Message { get; set; }
		public override string? ToString() => DateTime + '\t' + Message;
	}
}

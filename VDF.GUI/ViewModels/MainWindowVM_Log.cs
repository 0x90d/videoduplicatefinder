// /*
//     Copyright (C) 2026 0x90d
//     This file is part of VideoDuplicateFinder
//     VideoDuplicateFinder is free software: you can redistribute it and/or modify
//     it under the terms of the GNU Affero General Public License as published by
//     the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
//     VideoDuplicateFinder is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU Affero General Public License for more details.
//     You should have received a copy of the GNU Affero General Public License
//     along with VideoDuplicateFinder.  If not, see <http://www.gnu.org/licenses/>.
// */
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Text;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ReactiveUI;
using VDF.Core.Utils;
using VDF.GUI.Data;
using VDF.GUI.Utils;

namespace VDF.GUI.ViewModels {
	// Log view state (redesign stage 5): entries collected from the Core logger,
	// rebuilt into display rows by the pure Data.LogList engine.
	public partial class MainWindowVM {
		readonly List<LogEntry> logEntries = new();
		readonly HashSet<string> expandedLogGroups = new();
		DispatcherTimer? logRefreshTimer;
		int logInfoCount, logWarningCount, logErrorCount;

		IReadOnlyList<LogRow> _LogRows = Array.Empty<LogRow>();
		public IReadOnlyList<LogRow> LogRows {
			get => _LogRows;
			private set => this.RaiseAndSetIfChanged(ref _LogRows, value);
		}

		LogFilterKind logFilter = LogFilterKind.All;
		public bool LogFilterAllChecked => logFilter == LogFilterKind.All;
		public bool LogFilterInfoChecked => logFilter == LogFilterKind.Info;
		public bool LogFilterWarningsChecked => logFilter == LogFilterKind.Warnings;
		public bool LogFilterErrorsChecked => logFilter == LogFilterKind.Errors;

		public string LogWarningsChipText => $"{App.Lang["Log.Filter.Warnings"]} · {logWarningCount}";
		public string LogErrorsChipText => $"{App.Lang["Log.Filter.Errors"]} · {logErrorCount}";
		public string LogFileStatusText => $"{App.Lang["Log.FilePrefix"]} {Logger.LogFilePath}";

		string _LogSearchText = string.Empty;
		public string LogSearchText {
			get => _LogSearchText;
			set {
				this.RaiseAndSetIfChanged(ref _LogSearchText, value);
				RefreshLogRows();
			}
		}

		public ReactiveCommand<string, Unit> SetLogFilterCommand => ReactiveCommand.Create<string>(kind => {
			logFilter = Enum.Parse<LogFilterKind>(kind);
			this.RaisePropertyChanged(nameof(LogFilterAllChecked));
			this.RaisePropertyChanged(nameof(LogFilterInfoChecked));
			this.RaisePropertyChanged(nameof(LogFilterWarningsChecked));
			this.RaisePropertyChanged(nameof(LogFilterErrorsChecked));
			RefreshLogRows();
		});

		public ReactiveCommand<LogMessageRow, Unit> ToggleLogGroupCommand => ReactiveCommand.Create<LogMessageRow>(row => {
			if (row.GroupKey == null) return;
			if (!expandedLogGroups.Add(row.GroupKey))
				expandedLogGroups.Remove(row.GroupKey);
			RefreshLogRows();
		});

		void Instance_LogEntryAdded(LogEntry entry) =>
			Dispatcher.UIThread.InvokeAsync(() => {
				logEntries.Add(entry);
				if (!entry.IsSessionStart)
					AppendLogTail($"{entry.Timestamp:HH:mm:ss} · {entry.Message}");
				ScheduleLogRefresh();
			});

		// Entries arrive in bursts during a scan; coalesce rebuilds instead of
		// re-running the list builder per line.
		void ScheduleLogRefresh() {
			if (logRefreshTimer == null) {
				logRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
				logRefreshTimer.Tick += (_, __) => {
					logRefreshTimer!.Stop();
					RefreshLogRows();
				};
			}
			if (!logRefreshTimer.IsEnabled)
				logRefreshTimer.Start();
		}

		internal void RefreshLogRows() {
			var result = LogList.Build(logEntries, logFilter, LogSearchText, expandedLogGroups,
				DateTime.Now, App.Lang["Log.Today"]);
			logInfoCount = result.InfoCount;
			logWarningCount = result.WarningCount;
			logErrorCount = result.ErrorCount;
			LogRows = result.Rows;
			this.RaisePropertyChanged(nameof(LogWarningsChipText));
			this.RaisePropertyChanged(nameof(LogErrorsChipText));
		}

		public ReactiveCommand<Unit, Unit> ClearLogCommand => ReactiveCommand.Create(() => {
			logEntries.Clear();
			expandedLogGroups.Clear();
			RefreshLogRows();
		});

		// Copies the selected rows, or the whole filtered view when nothing is selected.
		// Collapsed rows copy all their members so a "× 12" line loses nothing.
		public ReactiveCommand<System.Collections.IList, Unit> CopyLogSelectionCommand => ReactiveCommand.Create<System.Collections.IList>(selected => {
			IEnumerable<LogRow> rows = selected is { Count: > 0 } ? selected.OfType<LogRow>() : LogRows;
			var sb = new StringBuilder();
			foreach (var row in rows) {
				switch (row) {
					case LogSessionRow session:
						sb.AppendLine($"---------- {session.Header} ----------");
						break;
					case LogMessageRow message:
						foreach (var entry in message.GroupEntries)
							sb.AppendLine(LogList.FormatLine(entry));
						break;
				}
			}
			if (sb.Length > 0)
				ApplicationHelpers.MainWindow.Clipboard?.SetTextAsync(sb.ToString());
		});

		public ReactiveCommand<Unit, Unit> SaveLogCommand => ReactiveCommand.CreateFromTask(async () => {
			var result = await Utils.PickerDialogUtils.SaveFilePicker(new FilePickerSaveOptions() {
				DefaultExtension = ".txt",
			});
			if (string.IsNullOrEmpty(result)) return;
			var sb = new StringBuilder();
			foreach (var entry in logEntries)
				sb.AppendLine(LogList.FormatLine(entry));
			try {
				File.WriteAllText(result, sb.ToString());
			}
			catch (Exception e) {
				Logger.Instance.Error(e.Message);
			}
		});
	}
}

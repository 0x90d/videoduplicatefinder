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

using Avalonia.Threading;
using VDF.Core;
using VDF.Core.Utils;
using VDF.GUI.Utils;

namespace VDF.GUI.ViewModels {
	/// <summary>
	/// What the window says to a screen reader about things that happen by themselves: a scan
	/// moving on, a scan ending, the busy curtain. A sighted user takes these in at a glance;
	/// without an announcement a blind user starts a scan and hears nothing until they go
	/// looking. The main window forwards <see cref="Announced"/> to its AnnouncerHost.
	/// </summary>
	public partial class MainWindowVM {
		/// <summary>Text for the screen reader, and whether it should cut off what is being spoken.</summary>
		public event Action<string, bool>? Announced;

		readonly ScanProgressAnnouncer scanProgressAnnouncer = new();
		readonly BusyAnnouncer busyAnnouncer = new();
		bool busyAnnouncementQueued;

		internal void Announce(string? text, bool interrupt = false) {
			if (!string.IsNullOrWhiteSpace(text))
				Announced?.Invoke(text, interrupt);
		}

		void InitAnnouncements() =>
			PropertyChanged += (_, e) => {
				switch (e.PropertyName) {
				case nameof(IsScanning) when IsScanning:
					scanProgressAnnouncer.Reset();
					break;
				}
				if (e.PropertyName is nameof(IsBusy) or nameof(IsScanning) or nameof(IsBusyOverlayText))
					QueueBusyAnnouncement();
			};

		// Queued, not evaluated on the spot: operations set IsBusy first and their text
		// second, so at the moment the curtain comes up it still carries the text of
		// whatever ran before. One evaluation per burst of changes sees the final state.
		void QueueBusyAnnouncement() {
			if (busyAnnouncementQueued) return;
			busyAnnouncementQueued = true;
			Dispatcher.UIThread.Post(() => {
				busyAnnouncementQueued = false;
				AnnounceBusyState(DateTime.UtcNow);
			}, DispatcherPriority.Background);
		}

		internal void AnnounceBusyState(DateTime now) =>
			Announce(busyAnnouncer.Next(IsBusy && !IsScanning, IsBusyOverlayText, now));

		internal void AnnounceScanProgress(ScanProgressSnapshot snapshot, DateTime now) =>
			Announce(scanProgressAnnouncer.Next(snapshot.CurrentStage, snapshot.CurrentPosition, snapshot.MaxPosition,
				snapshot.Remaining.Format(), now, App.Lang["Scan.Title"], App.Lang["A11y.Scan.Progress"]));

		/// <summary>The outcome of a scan, which a sighted user reads off the screen that comes up.</summary>
		internal void AnnounceScanDone() =>
			Announce(Duplicates.Count == 0
				? App.Lang["Setup.NoDuplicates.Text"]
				: string.Format(App.Lang["Notification.ScanComplete.Message"], TotalDuplicateGroups));
	}
}

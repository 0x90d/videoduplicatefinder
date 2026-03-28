// /*
//     Copyright (C) 2025 0x90d
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

using VDF.Core;
using VDF.Core.ViewModels;

namespace VDF.Web.Services {
	public enum ScanState { Idle, Scanning, Comparing, Done, Aborted }

	public class ScanProgressArgs {
		public string CurrentFile { get; init; } = string.Empty;
		public int Current { get; init; }
		public int Max { get; init; }
		public TimeSpan Elapsed { get; init; }
		public TimeSpan Remaining { get; init; }
	}

	/// <summary>
	/// Singleton service that owns the ScanEngine instance and exposes
	/// scan lifecycle operations to Blazor components via events and state.
	/// </summary>
	public sealed class ScanService : IDisposable {
		readonly ScanEngine _engine = new();
		CancellationTokenSource _cts = new();

		public ScanState State { get; private set; } = ScanState.Idle;
		public ScanProgressArgs? LastProgress { get; private set; }
		public IReadOnlyCollection<DuplicateItem> Duplicates => _engine.Duplicates;
		public Settings Settings => _engine.Settings;

		public event Action? StateChanged;

		public ScanService() {
			_engine.FilesEnumerated += (_, _) => Notify();
			_engine.BuildingHashesDone += (_, _) => {
				State = ScanState.Comparing;
				Notify();
			};
			_engine.Progress += (_, e) => {
				LastProgress = new ScanProgressArgs {
					CurrentFile = e.CurrentFile,
					Current = e.CurrentPosition,
					Max = e.MaxPosition,
					Elapsed = e.Elapsed,
					Remaining = e.Remaining
				};
				Notify();
			};
			_engine.ScanDone += (_, _) => {
				State = ScanState.Done;
				LastProgress = null;
				Notify();
			};
			_engine.ScanAborted += (_, _) => {
				State = ScanState.Aborted;
				LastProgress = null;
				Notify();
			};
		}

		public void StartScanAndCompare() {
			if (State == ScanState.Scanning || State == ScanState.Comparing) return;
			_cts = new CancellationTokenSource();
			State = ScanState.Scanning;
			LastProgress = null;
			_engine.Duplicates.Clear();
			_engine.StartSearch();
			Notify();
		}

		public void Pause() => _engine.Pause();
		public void Resume() => _engine.Resume();

		public void Stop() {
			_engine.Stop();
			_cts.Cancel();
		}

		public void Reset() {
			if (State == ScanState.Scanning || State == ScanState.Comparing) return;
			State = ScanState.Idle;
			LastProgress = null;
			_engine.Duplicates.Clear();
			_engine.Settings.IncludeList.Clear();
			_engine.Settings.BlackList.Clear();
			Notify();
		}

		void Notify() => StateChanged?.Invoke();

		public void Dispose() {
			_cts.Dispose();
		}
	}
}

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
using System.Globalization;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using ReactiveUI;
using VDF.Core.AI;
using VDF.GUI.Views;

namespace VDF.GUI.ViewModels {
	// AI component (ONNX Runtime + embedding model) download flow — the FFmpeg
	// downloader's little sibling: same busy-overlay pattern, but the URL planning,
	// extraction and integrity checks live in Core (AiComponents) so the CLI can
	// download headlessly with the identical logic.
	public partial class MainWindowVM : ReactiveObject {
		bool _isAiDownloadInProgress;
		public bool IsAiDownloadInProgress {
			get => _isAiDownloadInProgress;
			set => this.RaiseAndSetIfChanged(ref _isAiDownloadInProgress, value);
		}

		string _AiComponentsStatusText = string.Empty;
		public string AiComponentsStatusText {
			get {
				if (string.IsNullOrEmpty(_AiComponentsStatusText))
					_AiComponentsStatusText = BuildAiComponentsStatusText();
				return _AiComponentsStatusText;
			}
			set => this.RaiseAndSetIfChanged(ref _AiComponentsStatusText, value);
		}

		static string BuildAiComponentsStatusText() => AiComponents.GetState() switch {
			AiComponentsState.Ready => string.Format(CultureInfo.InvariantCulture, App.Lang["Settings.AiComponents.Ready"], AiComponents.RuntimeVersion),
			AiComponentsState.RuntimeMissing => App.Lang["Settings.AiComponents.RuntimeMissing"],
			AiComponentsState.ModelMissing => App.Lang["Settings.AiComponents.ModelMissing"],
			_ => App.Lang["Settings.AiComponents.Missing"],
		};

		void RefreshAiComponentsStatus() => AiComponentsStatusText = BuildAiComponentsStatusText();

		public ReactiveCommand<Unit, Unit> DownloadAiComponentsCommand => ReactiveCommand.CreateFromTask(async () => {
			await DownloadAiComponentsAsync();
		});

		internal async Task DownloadAiComponentsAsync() {
			if (IsAiDownloadInProgress) return;
			if (AiComponents.IsReady) {
				RefreshAiComponentsStatus();
				await MessageBoxService.Show(AiComponentsStatusText);
				return;
			}
			IsAiDownloadInProgress = true;
			IsBusy = true;
			IsBusyOverlayText = App.Lang["Message.AiDownloadPreparing"];
			try {
				var progress = new Progress<AiDownloadProgress>(p =>
					IsBusyOverlayText = string.Format(CultureInfo.InvariantCulture,
						App.Lang["Message.AiDownloadProgress"], p.Step, FormatBytes(p.BytesDone), FormatBytes(p.BytesTotal)));
				await AiComponents.DownloadAsync(progress, CancellationToken.None);
				await MessageBoxService.Show(string.Format(CultureInfo.InvariantCulture,
					App.Lang["Message.AiDownloadDone"], AiComponents.AiFolder));
			}
			catch (Exception ex) {
				await MessageBoxService.Show(string.Format(CultureInfo.InvariantCulture,
					App.Lang["Message.AiDownloadFailed"], ex.Message));
			}
			finally {
				IsBusy = false;
				IsBusyOverlayText = string.Empty;
				IsAiDownloadInProgress = false;
				RefreshAiComponentsStatus();
			}
		}
	}
}

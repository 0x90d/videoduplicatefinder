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

using System.Reactive;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using ReactiveUI;

namespace VDF.GUI.ViewModels {
	// Settings -> Test: pick two files and run the single-pair detection diagnostic.
	public partial class MainWindowVM : ReactiveObject {

		string _TestFileA = string.Empty;
		public string TestFileA {
			get => _TestFileA;
			set => this.RaiseAndSetIfChanged(ref _TestFileA, value);
		}
		string _TestFileB = string.Empty;
		public string TestFileB {
			get => _TestFileB;
			set => this.RaiseAndSetIfChanged(ref _TestFileB, value);
		}
		string _FilePairTestResult = string.Empty;
		public string FilePairTestResult {
			get => _FilePairTestResult;
			set => this.RaiseAndSetIfChanged(ref _FilePairTestResult, value);
		}
		bool _IsFilePairTestRunning;
		public bool IsFilePairTestRunning {
			get => _IsFilePairTestRunning;
			set => this.RaiseAndSetIfChanged(ref _IsFilePairTestRunning, value);
		}

		IObservable<bool> CanRunFilePairTest {
			[System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = WhenAnyValueTrimJustification)]
			get => this.WhenAnyValue(x => x.IsFilePairTestRunning, x => x.IsScanning, (running, scanning) => !running && !scanning);
		}

		static async Task<string?> PickTestFile() =>
			await Utils.PickerDialogUtils.OpenFilePicker(new FilePickerOpenOptions {
				Title = App.Lang["MainWindow.Settings.Test.SelectFile"]
			});

		public ReactiveCommand<Unit, Unit> SelectTestFileACommand => ReactiveCommand.CreateFromTask(async () => {
			string? result = await PickTestFile();
			if (!string.IsNullOrEmpty(result))
				TestFileA = result;
		});
		public ReactiveCommand<Unit, Unit> SelectTestFileBCommand => ReactiveCommand.CreateFromTask(async () => {
			string? result = await PickTestFile();
			if (!string.IsNullOrEmpty(result))
				TestFileB = result;
		});

		public ReactiveCommand<Unit, Unit> RunFilePairTestCommand => ReactiveCommand.CreateFromTask(async () => {
			IsFilePairTestRunning = true;
			try {
				SyncCoreSettings();
				FilePairTestResult = await Scanner.TestFilePairAsync(TestFileA, TestFileB);
			}
			catch (Exception ex) {
				FilePairTestResult = $"Test failed: {ex}";
			}
			finally {
				IsFilePairTestRunning = false;
			}
		}, CanRunFilePairTest);

		public ReactiveCommand<Unit, Unit> CopyFilePairTestResultCommand => ReactiveCommand.Create(() => {
			if (!string.IsNullOrEmpty(FilePairTestResult))
				ApplicationHelpers.MainWindow.Clipboard?.SetTextAsync(FilePairTestResult);
		});
	}
}

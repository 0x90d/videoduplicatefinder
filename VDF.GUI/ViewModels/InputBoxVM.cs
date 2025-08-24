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

using System.Reactive;
using ReactiveUI;
using VDF.GUI.Views;

namespace VDF.GUI.ViewModels {
	public sealed class InputBoxVM : ReactiveObject {

		public InputBoxView? host;

		bool _HasOKButton;
		public bool HasOKButton {
			get => _HasOKButton;
			set => this.RaiseAndSetIfChanged(ref _HasOKButton, value);
		}
		bool _HasYesButton;
		public bool HasYesButton {
			get => _HasYesButton;
			set => this.RaiseAndSetIfChanged(ref _HasYesButton, value);
		}
		bool _HasNoButton;
		public bool HasNoButton {
			get => _HasNoButton;
			set => this.RaiseAndSetIfChanged(ref _HasNoButton, value);
		}
		bool _HasCancelButton;
		public bool HasCancelButton {
			get => _HasCancelButton;
			set => this.RaiseAndSetIfChanged(ref _HasCancelButton, value);
		}
		public string Message { get; set; } = string.Empty;
		public string Input { get; set; } = string.Empty;
		public string WaterMark { get; set; } = string.Empty;
		public string Title { get; set; } = "Video Duplicate Finder";
		public ReactiveCommand<Unit, Unit> OKCommand => ReactiveCommand.Create(() => {
			host?.Close(Input);
		});
		public ReactiveCommand<Unit, Unit> YesCommand => ReactiveCommand.Create(() => {
			host?.Close(Input);
		});
		public ReactiveCommand<Unit, Unit> NoCommand => ReactiveCommand.Create(() => {
			host?.Close(null!);
		});
		public ReactiveCommand<Unit, Unit> CancelCommand => ReactiveCommand.Create(() => {
			host?.Close(null!);
		});
	}
}

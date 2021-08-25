// /*
//     Copyright (C) 2021 0x90d
//     This file is part of VideoDuplicateFinder
//     VideoDuplicateFinder is free software: you can redistribute it and/or modify
//     it under the terms of the GPLv3 as published by
//     the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
//     VideoDuplicateFinder is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU General Public License for more details.
//     You should have received a copy of the GNU General Public License
//     along with VideoDuplicateFinder.  If not, see <http://www.gnu.org/licenses/>.
// */
//

using System;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ReactiveUI;

namespace VDF.GUI.Views {

	public static class InputBoxService {
		public static async Task<String> Show(string message, string defaultInput="", string waterMark="", 
			InputBoxButtons buttons = InputBoxButtons.Ok | InputBoxButtons.Cancel, string title = null) {
			var dlg = new InputBoxView(message, defaultInput, waterMark, buttons, title) {
				Icon = ApplicationHelpers.MainWindow.Icon
			};
			return await dlg.ShowDialog<String>(ApplicationHelpers.MainWindow);
		}
	}


	public class InputBoxView : Window {

		public InputBoxView() {
			//Designer need this
			InitializeComponent();
		}
		public InputBoxView(string message, string defaultInput="", string waterMark="", 
			InputBoxButtons buttons = InputBoxButtons.Ok | InputBoxButtons.Cancel, string title = null) {
			DataContext = new InputBoxVM();
			((InputBoxVM)DataContext).host = this;
			((InputBoxVM)DataContext).Message = message;
			((InputBoxVM)DataContext).Input = defaultInput;
			((InputBoxVM)DataContext).WaterMark = waterMark;
			if (!string.IsNullOrEmpty(title))
				((InputBoxVM)DataContext).Title = title;
			((InputBoxVM)DataContext).HasCancelButton = (buttons & InputBoxButtons.Cancel) != 0;
			((InputBoxVM)DataContext).HasNoButton = (buttons & InputBoxButtons.No) != 0;
			((InputBoxVM)DataContext).HasOKButton = (buttons & InputBoxButtons.Ok) != 0;
			((InputBoxVM)DataContext).HasYesButton = (buttons & InputBoxButtons.Yes) != 0;

			InitializeComponent();

		}
		private void InitializeComponent() {
			AvaloniaXamlLoader.Load(this);
		}
	}

	[Flags]
	public enum InputBoxButtons {
		None = 0,
		Ok = 1,
		Cancel = 2,
		Yes = 4,
		No = 8
	}

	public sealed class InputBoxVM : ReactiveObject {

		public InputBoxView host;

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
		public string Message { get; set; }
		public string Input { get; set; }
		public string WaterMark { get; set; }
		public string Title { get; set; } = "Video Duplicate Finder";
		public ReactiveCommand<Unit, Unit> OKCommand => ReactiveCommand.Create(() => {
			host.Close(Input);
		});
		public ReactiveCommand<Unit, Unit> YesCommand => ReactiveCommand.Create(() => {
			host.Close(Input);
		});
		public ReactiveCommand<Unit, Unit> NoCommand => ReactiveCommand.Create(() => {
			host.Close(null);
		});
		public ReactiveCommand<Unit, Unit> CancelCommand => ReactiveCommand.Create(() => {
			host.Close(null);
		});
	}
}

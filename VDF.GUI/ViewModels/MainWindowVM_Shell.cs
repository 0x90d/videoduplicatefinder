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
using ReactiveUI;
using VDF.GUI.Data;

namespace VDF.GUI.ViewModels {
	// One-window shell (redesign stage 6): the tab strip is replaced by titlebar nav
	// links; Settings and Log are secondary views layered over the scanner.
	public partial class MainWindowVM {

		ShellView _ActiveShellView = ShellView.Main;
		public ShellView ActiveShellView {
			get => _ActiveShellView;
			set {
				if (value == _ActiveShellView) return;
				this.RaiseAndSetIfChanged(ref _ActiveShellView, value);
				this.RaisePropertyChanged(nameof(IsShellMainVisible));
				this.RaisePropertyChanged(nameof(IsShellSettingsVisible));
				this.RaisePropertyChanged(nameof(IsShellLogVisible));
				RaiseShellNavChanged();
			}
		}

		public bool IsShellMainVisible => ActiveShellView == ShellView.Main;
		public bool IsShellSettingsVisible => ActiveShellView == ShellView.Settings;
		public bool IsShellLogVisible => ActiveShellView == ShellView.Log;

		ShellNavLinks NavLinks => ShellNav.For(ActiveShellView, IsReviewState);
		public bool ShowNavNewScan => NavLinks.NewScan;
		public bool ShowNavBackToResults => NavLinks.BackToResults;
		public bool ShowNavLog => NavLinks.Log;
		public bool ShowNavSettings => NavLinks.Settings;

		void RaiseShellNavChanged() {
			this.RaisePropertyChanged(nameof(ShowNavNewScan));
			this.RaisePropertyChanged(nameof(ShowNavBackToResults));
			this.RaisePropertyChanged(nameof(ShowNavLog));
			this.RaisePropertyChanged(nameof(ShowNavSettings));
		}

		public ReactiveCommand<string, Unit> ShowShellViewCommand => ReactiveCommand.Create<string>(view => {
			ActiveShellView = Enum.Parse<ShellView>(view);
		});
	}
}

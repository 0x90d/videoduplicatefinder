using System;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ReactiveUI;

namespace VideoDuplicateFinderLinux {

	public static class MessageBoxService {
		public static async Task<MessageBoxButtons> Show(string message, MessageBoxButtons buttons = MessageBoxButtons.Ok,
			string title = null) {
			var dlg = new MessageBoxView(message, buttons, title) {
				Owner = ApplicationHelpers.MainWindow
			};
			return await dlg.ShowDialog<MessageBoxButtons>(ApplicationHelpers.MainWindow);
		}
		
	}


	public class MessageBoxView : Window {
		public MessageBoxView() : this("") {
			
		}

		public MessageBoxView(string message, MessageBoxButtons buttons = MessageBoxButtons.Ok, string title = null) {

			DataContext = new MessageBoxVM();
			((MessageBoxVM)DataContext).host = this;
			((MessageBoxVM)DataContext).Message = message;
			if (!string.IsNullOrEmpty(title))
				((MessageBoxVM)DataContext).Title = title;
			((MessageBoxVM)DataContext).HasCancelButton = (buttons & MessageBoxButtons.Cancel) != 0;
			((MessageBoxVM)DataContext).HasNoButton = (buttons & MessageBoxButtons.No) != 0;
			((MessageBoxVM)DataContext).HasOKButton = (buttons & MessageBoxButtons.Ok) != 0;
			((MessageBoxVM)DataContext).HasYesButton = (buttons & MessageBoxButtons.Yes) != 0;

			InitializeComponent();
#if DEBUG
			this.AttachDevTools();
#endif

		}
		private void InitializeComponent() {
			AvaloniaXamlLoader.Load(this);
		}
	}

	[Flags]
	public enum MessageBoxButtons {
		None = 0,
		Ok = 1,
		Cancel = 2,
		Yes = 4,
		No = 8
	}

	public sealed class MessageBoxVM : ReactiveObject {

		public MessageBoxView host;

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
		public string Title { get; set; } = "Video Duplicate Finder";
		public ReactiveCommand<Unit, Unit> OKCommand => ReactiveCommand.Create(() => {
			host.Close(MessageBoxButtons.Ok);
		});
		public ReactiveCommand<Unit, Unit> YesCommand => ReactiveCommand.Create(() => {
			host.Close(MessageBoxButtons.Yes);
		});
		public ReactiveCommand<Unit, Unit> NoCommand => ReactiveCommand.Create(() => {
			host.Close(MessageBoxButtons.No);
		});
		public ReactiveCommand<Unit, Unit> CancelCommand => ReactiveCommand.Create(() => {
			host.Close(MessageBoxButtons.Cancel);
		});
	}
}

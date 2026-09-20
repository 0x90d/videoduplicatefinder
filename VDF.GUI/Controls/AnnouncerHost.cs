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

using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Threading;

namespace VDF.GUI.Controls {
	/// <summary>
	/// Says things to a screen reader that happen without the keyboard focus moving: a scan
	/// stage, "scan complete", "copied". Wraps a window's whole content and is the live region
	/// itself, instead of <c>AutomationProperties.LiveSetting</c> on the text that changes.
	/// That is deliberate: Avalonia hands a live region change to the platform only for an
	/// element the screen reader has already navigated to, and a screen reader following the
	/// focus only ever touches the focused element and its ancestors. A status text next to
	/// the focus stays silent (measured with a UI Automation client that listens like NVDA
	/// does); an ancestor of everything in the window is always known. Invisible to layout
	/// and rendering.
	/// </summary>
	public class AnnouncerHost : Decorator {
		// Long enough for a screen reader to fetch the text after the event reached it,
		// short enough that the window does not keep yesterday's message as its group name.
		readonly DispatcherTimer clearTimer = new() { Interval = TimeSpan.FromSeconds(4) };

		// A screen reader learns of a message by an event and asks for the text afterwards.
		// Two messages in the same instant ("Checked: file", then "No more groups") would
		// have it ask twice and get the second text both times. So they are spaced out.
		readonly DispatcherTimer spacingTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
		readonly Queue<(string Text, bool Interrupt)> waiting = new();
		const int MaxWaiting = 6;

		/// <summary>How long a message stays this element's name.</summary>
		internal TimeSpan Lifetime {
			get => clearTimer.Interval;
			set => clearTimer.Interval = value;
		}

		/// <summary>The least time between two messages.</summary>
		internal TimeSpan Spacing {
			get => spacingTimer.Interval;
			set => spacingTimer.Interval = value;
		}

		public AnnouncerHost() {
			clearTimer.Tick += (_, _) => Clear();
			spacingTimer.Tick += (_, _) => {
				if (waiting.Count == 0) spacingTimer.Stop(); // the gap after the last message has passed
				else Say(waiting.Dequeue());
			};
		}

		/// <summary>The text a screen reader currently gets as this element's name; null when idle.</summary>
		public string? Message { get; private set; }

		/// <summary>True when <see cref="Message"/> should cut off what is being spoken.</summary>
		public bool Interrupts { get; private set; }

		/// <param name="interrupt">
		/// Speak at once instead of after the current sentence, and drop what is still waiting.
		/// For what must not be missed.
		/// </param>
		public void Announce(string? text, bool interrupt = false) {
			if (string.IsNullOrWhiteSpace(text)) return;
			if (interrupt) waiting.Clear();
			else if (spacingTimer.IsEnabled) {
				// Falling behind helps nobody: what is oldest is the most outdated.
				if (waiting.Count == MaxWaiting) waiting.Dequeue();
				waiting.Enqueue((text, false));
				return;
			}
			Say((text, interrupt));
		}

		void Say((string Text, bool Interrupt) message) {
			clearTimer.Stop();
			spacingTimer.Stop();
			SetMessage(message.Text, message.Interrupt);
			spacingTimer.Start();
			clearTimer.Start();
		}

		void Clear() {
			clearTimer.Stop();
			SetMessage(null, false);
		}

		void SetMessage(string? text, bool interrupt) {
			string? old = Message;
			Message = text;
			Interrupts = interrupt;
			// The platform node turns a name change on a live element into the live region
			// event. Raised for the same text twice on purpose: "checked" twice in a row is
			// two things that happened. Idle, the peer reports LiveSetting Off, so clearing
			// the name is a plain property change nobody speaks.
			ControlAutomationPeer.CreatePeerForElement(this)
				.RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, old, text);
		}

		protected override AutomationPeer OnCreateAutomationPeer() => new AnnouncerHostAutomationPeer(this);
	}

	public class AnnouncerHostAutomationPeer : ControlAutomationPeer {
		public AnnouncerHostAutomationPeer(AnnouncerHost owner) : base(owner) { }
		new AnnouncerHost Owner => (AnnouncerHost)base.Owner;
		// What the panels around it are: idle, it is one more nameless container.
		protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
		protected override string? GetNameCore() => Owner.Message;
		protected override AutomationLiveSetting GetLiveSettingCore() =>
			Owner.Message == null ? AutomationLiveSetting.Off
			: Owner.Interrupts ? AutomationLiveSetting.Assertive : AutomationLiveSetting.Polite;
	}
}

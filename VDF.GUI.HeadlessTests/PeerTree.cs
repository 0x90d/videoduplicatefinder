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

using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace VDF.GUI.HeadlessTests;

/// <summary>One node of the automation tree, which is what a screen reader is given.</summary>
public sealed record PeerNode(AutomationControlType Type, string Name, string HelpText, Control? Owner) {
	/// <summary>Where the control sits, for a failure message a human can act on.</summary>
	public string Describe() {
		string owner = Owner?.GetType().Name ?? "?";
		string id = string.IsNullOrEmpty(Owner?.Name) ? string.Empty : $" #{Owner!.Name}";
		string help = HelpText.Length > 0 ? $" help='{HelpText}'" : string.Empty;
		return $"[{Type}] {owner}{id} name='{Name}'{help}{Where()}";
	}

	/// <summary>Nearest x:Name that is not a template part, plus the closest visible label text.</summary>
	string Where() {
		if (Owner == null) return string.Empty;
		var ancestors = Owner.GetVisualAncestors().OfType<Control>().ToList();
		string? named = ancestors.Select(a => a.Name)
			.FirstOrDefault(n => !string.IsNullOrEmpty(n) && !n.StartsWith("PART_", StringComparison.Ordinal));
		string? label = null;
		foreach (var ancestor in ancestors.Take(6)) {
			label = ancestor.GetVisualDescendants().OfType<TextBlock>()
				.Where(t => t.IsEffectivelyVisible && !Owner.IsVisualAncestorOf(t))
				.Select(t => t.Text)
				.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t) && t.Any(char.IsLetter));
			if (label != null) break;
		}
		if (label is { Length: > 50 }) label = label[..50] + "...";
		return (named != null ? $" in #{named}" : string.Empty) + (label != null ? $" near '{label}'" : string.Empty);
	}
}

public static class PeerTree {
	/// <summary>Flattens the control elements of the automation tree below <paramref name="root"/>.</summary>
	public static List<PeerNode> Walk(Control root) {
		var nodes = new List<PeerNode>();
		Visit(ControlAutomationPeer.CreatePeerForElement(root), nodes);
		return nodes;
	}

	static void Visit(AutomationPeer peer, List<PeerNode> nodes) {
		if (peer.IsControlElement())
			nodes.Add(new PeerNode(peer.GetAutomationControlType(), peer.GetName() ?? string.Empty,
				peer.GetHelpText() ?? string.Empty, (peer as ControlAutomationPeer)?.Owner));
		foreach (var child in peer.GetChildren())
			Visit(child, nodes);
	}

	/// <summary>Every control keyboard focus can land on, described the way its peer presents it.</summary>
	public static List<PeerNode> TabStops(Control root) =>
		root.GetVisualDescendants().OfType<Control>()
			.Where(c => c.Focusable && c.IsEffectivelyVisible && c.IsEffectivelyEnabled && KeyboardNavigation.GetIsTabStop(c))
			.Select(c => {
				var peer = ControlAutomationPeer.CreatePeerForElement(c);
				return new PeerNode(peer.GetAutomationControlType(), peer.GetName() ?? string.Empty,
					peer.GetHelpText() ?? string.Empty, c);
			})
			.ToList();

	/// <summary>Control types a user operates, so a screen reader must be able to say what they are.</summary>
	public static bool IsInteractive(AutomationControlType type) => type is
		AutomationControlType.Button or AutomationControlType.CheckBox or AutomationControlType.ComboBox or
		AutomationControlType.Edit or AutomationControlType.RadioButton or AutomationControlType.Slider or
		AutomationControlType.Spinner or AutomationControlType.List or AutomationControlType.ListItem or
		AutomationControlType.ProgressBar or AutomationControlType.Hyperlink or AutomationControlType.TabItem;

	/// <summary>
	/// Why a name is useless to a screen reader user, or null when it is fine. The type-name
	/// case is what Avalonia falls back to when a control's Content is a panel or a view model.
	/// </summary>
	public static string? NameProblem(string name) {
		if (string.IsNullOrWhiteSpace(name))
			return "no name";
		if (name.StartsWith("Avalonia.", StringComparison.Ordinal) || name.StartsWith("VDF.", StringComparison.Ordinal) ||
			name.StartsWith("ActiproSoftware.", StringComparison.Ordinal))
			return "type name as name";
		if (!name.Any(char.IsLetterOrDigit))
			return "glyph-only name";
		// "Don't show again ✕" is read out as "don't show again multiplication x", or
		// whatever the speech engine makes of the symbol: icons belong next to the
		// translated text, not into it.
		if (name.Any(IsIconGlyph))
			return "icon glyph inside the name";
		return null;
	}

	// Dingbats, arrows, geometric shapes, technical symbols and enclosed alphanumerics: what
	// the views use as icons. Math operators stay out of it, "32×32" is text.
	static bool IsIconGlyph(char c) =>
		c is (>= '←' and <= '⇿') or (>= '⌀' and <= '⏿') or (>= '①' and <= '⓿')
			or (>= '■' and <= '◿') or (>= '☀' and <= '➿') or (>= '⬀' and <= '⯿');

	/// <summary>
	/// Mouse affordances inside another control's template (spinner arrows, scrollbar and
	/// slider track buttons): never a Tab stop, and the arrow keys on the outer control do
	/// the same. The text box inside a NumericUpDown is NOT exempt: it is what takes focus,
	/// so it is what a screen reader announces.
	/// </summary>
	public static bool IsMouseOnlyTemplatePart(PeerNode node) =>
		node.Owner is RepeatButton or ScrollBar
		|| node.Owner?.FindAncestorOfType<ScrollBar>() != null;
}

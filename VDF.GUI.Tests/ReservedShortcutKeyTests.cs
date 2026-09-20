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

using Avalonia.Input;
using VDF.GUI.Data;

namespace VDF.GUI.Tests;

/// <summary>Keys no action may be bound to, because the keyboard needs them to get around.</summary>
public class ReservedShortcutKeyTests {

	[Theory]
	[InlineData(KeyModifiers.None)]
	[InlineData(KeyModifiers.Shift)]   // backwards
	[InlineData(KeyModifiers.Control)] // between tabs / documents
	[InlineData(KeyModifiers.Control | KeyModifiers.Shift)]
	[InlineData(KeyModifiers.Alt)]
	public void Tab_IsNeverAShortcut_WhateverTheModifiers(KeyModifiers modifiers) =>
		Assert.True(KeyboardShortcutManager.IsReservedKey(Key.Tab, modifiers));

	[Fact]
	public void PlainEscape_IsReserved_ButCombinationsWithItAreNot() {
		Assert.True(KeyboardShortcutManager.IsReservedKey(Key.Escape, KeyModifiers.None));
		Assert.False(KeyboardShortcutManager.IsReservedKey(Key.Escape, KeyModifiers.Control));
	}

	[Theory]
	[InlineData(Key.Space, KeyModifiers.None)]
	[InlineData(Key.Enter, KeyModifiers.None)]
	[InlineData(Key.Delete, KeyModifiers.Shift)]
	[InlineData(Key.C, KeyModifiers.Alt)]
	public void TheDefaultShortcuts_StayAssignable(Key key, KeyModifiers modifiers) =>
		Assert.False(KeyboardShortcutManager.IsReservedKey(key, modifiers));
}

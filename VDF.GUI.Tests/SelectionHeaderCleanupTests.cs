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

using System.Collections;
using VDF.GUI.ViewModels;

namespace VDF.GUI.Tests {
	/// <summary>
	/// Regression tests for the results selection header cleanup. Avalonia raises
	/// SelectionChanged synchronously for every mutation of SelectedItems, so the
	/// cleanup used to re-enter itself on each RemoveAt and crash the dispatcher
	/// with an ArgumentOutOfRangeException (stale loop index) once more than one
	/// header was part of the selection.
	/// </summary>
	public class SelectionHeaderCleanupTests {

		static ResultsGroupHeader NewHeader() => new() {
			GroupId = Guid.NewGuid(),
			Rows = new List<ResultsItemRow>(),
		};

		/// <summary>
		/// IList whose RemoveAt synchronously invokes a callback after each removal,
		/// mimicking Avalonia's SelectionChanged-per-mutation behavior.
		/// </summary>
		sealed class ReentrantList : IList {
			readonly List<object?> items = new();
			public Action? OnRemoved;

			public object? this[int index] { get => items[index]; set => items[index] = value; }
			public int Count => items.Count;
			public bool IsFixedSize => false;
			public bool IsReadOnly => false;
			public bool IsSynchronized => false;
			public object SyncRoot => this;
			public int Add(object? value) { items.Add(value); return items.Count - 1; }
			public void Clear() => items.Clear();
			public bool Contains(object? value) => items.Contains(value);
			public void CopyTo(Array array, int index) => ((ICollection)items).CopyTo(array, index);
			public IEnumerator GetEnumerator() => items.GetEnumerator();
			public int IndexOf(object? value) => items.IndexOf(value);
			public void Insert(int index, object? value) => items.Insert(index, value);
			public void Remove(object? value) => items.Remove(value);
			public void RemoveAt(int index) {
				items.RemoveAt(index);
				OnRemoved?.Invoke();
			}
		}

		[Fact]
		public void MultipleHeaders_WithReentrantSelectionChanged_DoesNotThrow() {
			var cleanup = new SelectionHeaderCleanup();
			var list = new ReentrantList();
			list.Add(NewHeader());
			list.Add(NewHeader());
			list.Add(NewHeader());
			// Every removal re-runs the cleanup, like Avalonia re-raising SelectionChanged.
			list.OnRemoved = () => cleanup.Run(list);

			cleanup.Run(list);

			Assert.Empty(list);
		}

		[Fact]
		public void MixedSelection_RemovesOnlyHeaders_KeepsItems() {
			var cleanup = new SelectionHeaderCleanup();
			var list = new ReentrantList();
			var keep1 = new object();
			var keep2 = new object();
			list.Add(NewHeader());
			list.Add(keep1);
			list.Add(NewHeader());
			list.Add(keep2);
			list.Add(NewHeader());
			list.OnRemoved = () => cleanup.Run(list);

			cleanup.Run(list);

			Assert.Equal(2, list.Count);
			Assert.Same(keep1, list[0]);
			Assert.Same(keep2, list[1]);
		}

		[Fact]
		public void NullOrHeaderFreeSelection_IsANoOp() {
			var cleanup = new SelectionHeaderCleanup();
			cleanup.Run(null);

			var list = new ReentrantList();
			var item = new object();
			list.Add(item);
			cleanup.Run(list);
			Assert.Same(item, Assert.Single(list));
		}
	}
}

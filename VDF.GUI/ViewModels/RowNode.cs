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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Avalonia.Collections;
using ReactiveUI;

namespace VDF.GUI.ViewModels {
	[DebuggerDisplay("{Header,nq} - {Id}")]
	public sealed class RowNode : ReactiveObject {
		[JsonInclude]
		public bool IsGroup { get; private set; }
		private string _header = string.Empty;
		[JsonInclude]
		public string Header {
			get => _header;
			set => this.RaiseAndSetIfChanged(ref _header, value);
		}
		[JsonInclude]
		public DuplicateItemVM? Item { get; private set; }		
		[JsonIgnore]
		public AvaloniaList<RowNode> Children { get; } = new() { ResetBehavior = ResetBehavior.Reset }; //Visible children in the tree (after filtering)
		[JsonInclude]
		public AvaloniaList<RowNode> AllChildren { get; private set; } = new() { ResetBehavior = ResetBehavior.Reset	};

		private bool _isExpanded;
		[JsonInclude]
		public bool IsExpanded {
			get => _isExpanded;
			set => this.RaiseAndSetIfChanged(ref _isExpanded, value);
		}

		public RowNode() { }

		public RowNode(string header, AvaloniaList<RowNode> children, bool expanded)
			=> (IsGroup, Header, AllChildren, IsExpanded) = (true, header, children, expanded);

		public RowNode(DuplicateItemVM item)
			=> (IsGroup, Item, AllChildren) = (false, item, new());

		public static RowNode Group(string header, IEnumerable<DuplicateItemVM> items, bool expanded = true)
			=> new(header, new AvaloniaList<RowNode>(items.Select(i => new RowNode(i)).ToList()) { ResetBehavior = ResetBehavior.Reset }, expanded);
	}
}

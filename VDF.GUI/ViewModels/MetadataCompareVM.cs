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

using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using ReactiveUI;
using VDF.Core.Utils;

namespace VDF.GUI.ViewModels {

	/// <summary>Localizable texts of the metadata comparison. Defaults are English; the window substitutes translations.</summary>
	public sealed record MetadataCompareTexts {
		public string Container { get; init; } = "Container";
		public string Stream { get; init; } = "Stream {0}: {1}";
		public string Exif { get; init; } = "EXIF";
		public string Gps { get; init; } = "GPS";
		public string NotSet { get; init; } = "(not set)";
		public string Unavailable { get; init; } = "file not available";
		public string Differs { get; init; } = "differs";
		public string CheckFile { get; init; } = "Check {0}";
		public string Summary { get; init; } = "{0} of {1} fields differ";
		public static readonly MetadataCompareTexts Default = new();
	}

	/// <summary>One compared file: a column of the table, with the file's own Checked box.</summary>
	public sealed class MetadataFileColumn : ReactiveObject {
		public MetadataFileColumn(DuplicateItemVM item, string checkName) {
			Item = item;
			CheckName = checkName;
		}
		public DuplicateItemVM Item { get; }
		public string Path => Item.ItemInfo.Path;
		public string FileName => System.IO.Path.GetFileName(Item.ItemInfo.Path);
		public string CheckName { get; }
		public bool IsUnavailable {
			get;
			internal set => this.RaiseAndSetIfChanged(ref field, value);
		}
	}

	/// <summary>What the list item of either row type is announced as.</summary>
	public interface IMetadataRow {
		string AccessibleName { get; }
	}

	public sealed class MetadataSectionRow : IMetadataRow {
		public MetadataSectionRow(string title) => Title = title;
		public string Title { get; }
		public string AccessibleName => Title;
	}

	public sealed record MetadataCell(string Text, bool IsOdd, bool IsMissing);

	public sealed class MetadataValueRow : IMetadataRow {
		public required string Name { get; init; }
		public required IReadOnlyList<MetadataCell> Cells { get; init; }
		public bool Differs { get; init; }
		/// <summary>"creationdate, a.mp4 2023-08-15T12:34:56, b.mp4 2023-08-15T14:34:56+0200 differs, ..." for screen readers.</summary>
		public required string AccessibleName { get; init; }
	}

	/// <summary>
	/// Every metadata tag of a group's files side by side (#926), read when the window opens
	/// and never stored. Values that differ from what most files have are marked, so the one
	/// copy with the right (or wrong) date stands out.
	/// </summary>
	public sealed class MetadataCompareVM : ReactiveObject {
		readonly Func<string, IReadOnlyList<MetadataField>?> reader;
		readonly MetadataCompareTexts texts;
		List<MetadataComparisonRow> comparison = new();

		public MetadataCompareVM(IReadOnlyList<DuplicateItemVM> items, MetadataCompareTexts? texts = null,
			Func<string, IReadOnlyList<MetadataField>?>? reader = null) {
			this.texts = texts ?? MetadataCompareTexts.Default;
			this.reader = reader ?? FileMetadata.Read;
			Files = items.Select(i => new MetadataFileColumn(i,
				string.Format(CultureInfo.CurrentCulture, this.texts.CheckFile, Path.GetFileName(i.ItemInfo.Path)))).ToList();
		}

		public IReadOnlyList<MetadataFileColumn> Files { get; }
		/// <summary>Section headers and value rows, flattened for one virtualized list.</summary>
		public ObservableCollection<object> Rows { get; } = new();

		public bool OnlyDifferences {
			get;
			set {
				this.RaiseAndSetIfChanged(ref field, value);
				BuildRows();
			}
		} = true;

		public bool IsLoading {
			get;
			private set => this.RaiseAndSetIfChanged(ref field, value);
		}

		public string SummaryText {
			get;
			private set => this.RaiseAndSetIfChanged(ref field, value);
		} = string.Empty;

		/// <summary>No file carries a single tag (or none could be read).</summary>
		public bool HasNoMetadata {
			get;
			private set => this.RaiseAndSetIfChanged(ref field, value);
		}

		/// <summary>Tags exist, but they are the same in every file and only differences are shown.</summary>
		public bool AllEqual {
			get;
			private set => this.RaiseAndSetIfChanged(ref field, value);
		}

		/// <summary>Reads every file (in parallel, off the UI thread) and fills the table.</summary>
		public async Task LoadAsync() {
			if (isLoaded) return;
			IsLoading = true;
			try {
				var paths = Files.Select(f => f.Path).ToArray();
				var results = new IReadOnlyList<MetadataField>?[paths.Length];
				await Task.Run(() => Parallel.For(0, paths.Length, new ParallelOptions { MaxDegreeOfParallelism = 4 },
					i => results[i] = reader(paths[i])));
				Apply(results);
			}
			finally {
				IsLoading = false;
			}
		}
		bool isLoaded;

		/// <summary>Fills the table from what was read, one entry per file (null = could not be read).</summary>
		internal void Apply(IReadOnlyList<IReadOnlyList<MetadataField>?> results) {
			for (int i = 0; i < Files.Count; i++)
				Files[i].IsUnavailable = results[i] == null;
			comparison = MetadataComparison.Build(results);
			isLoaded = true;
			BuildRows();
		}

		void BuildRows() {
			Rows.Clear();
			int differing = comparison.Count(r => r.Differs);
			SummaryText = string.Format(CultureInfo.CurrentCulture, texts.Summary, differing, comparison.Count);
			HasNoMetadata = comparison.Count == 0;
			AllEqual = comparison.Count > 0 && differing == 0 && OnlyDifferences;

			MetadataSection? current = null;
			foreach (var row in comparison) {
				if (OnlyDifferences && !row.Differs)
					continue;
				if (current != row.Section) {
					current = row.Section;
					Rows.Add(new MetadataSectionRow(SectionTitle(row.Section)));
				}
				var cells = new MetadataCell[row.Values.Length];
				var spoken = new List<string> { row.Name };
				for (int f = 0; f < row.Values.Length; f++) {
					bool missing = row.Values[f] == null;
					string text = Files[f].IsUnavailable ? texts.Unavailable : row.Values[f] ?? texts.NotSet;
					cells[f] = new MetadataCell(text, row.IsOdd[f], missing);
					spoken.Add($"{Files[f].FileName} {text}" + (row.IsOdd[f] ? $" {texts.Differs}" : string.Empty));
				}
				Rows.Add(new MetadataValueRow {
					Name = row.Name, Cells = cells, Differs = row.Differs, AccessibleName = string.Join(", ", spoken),
				});
			}
		}

		string SectionTitle(MetadataSection section) => section.Kind switch {
			MetadataSectionKind.Container => texts.Container,
			MetadataSectionKind.Stream => string.Format(CultureInfo.CurrentCulture, texts.Stream, section.StreamIndex, section.StreamType),
			MetadataSectionKind.Exif => texts.Exif,
			_ => texts.Gps,
		};
	}
}

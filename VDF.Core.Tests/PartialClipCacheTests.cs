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
// The partial-clip comparison cache must be invisible except for speed: every run with a
// (possibly stale or damaged) cache has to give exactly the Duplicates a run without one gives.

using VDF.Core.Utils;

namespace VDF.Core.Tests;

[Collection("DatabaseUtils")] // reads the shared static database and redirects the database folder
public class PartialClipCacheTests : IDisposable {

	readonly List<FileEntry> added = new();
	readonly string dbFolder = Path.Combine(Path.GetTempPath(), "vdf-partialcache-" + Guid.NewGuid().ToString("N"));
	readonly string root = Path.Combine(Path.GetTempPath(), "vdf-pc-" + Guid.NewGuid().ToString("N"));

	public PartialClipCacheTests() {
		Directory.CreateDirectory(dbFolder);
		DatabaseUtils.CustomDatabaseFolder = dbFolder;
		DatabaseUtils.InvalidateDatabaseFolder();
	}

	public void Dispose() {
		foreach (var e in added)
			DatabaseUtils.Database.Remove(e);
		DatabaseUtils.CustomDatabaseFolder = null;
		DatabaseUtils.InvalidateDatabaseFolder();
		try { Directory.Delete(dbFolder, recursive: true); } catch (Exception) { }
	}

	string CacheFile => Path.Combine(dbFolder, PartialClipCache.FileName);

	FileEntry Add(string folder, string name, double durationSeconds, uint[] fingerprint) {
		string dir = Path.Combine(root, folder);
		var entry = new FileEntry {
			_Path = Path.Combine(dir, name),
			Folder = dir,
			FileSize = 1,
			invalid = false,
			IsImage = false,
			AudioFingerprint = fingerprint,
			mediaInfo = new MediaInfo { Duration = TimeSpan.FromSeconds(durationSeconds) },
		};
		DatabaseUtils.Database.Add(entry);
		added.Add(entry);
		return entry;
	}

	void Remove(FileEntry e) {
		DatabaseUtils.Database.Remove(e);
		added.Remove(e);
	}

	static readonly uint[] SourceFp =
		{ 0x0F0F0F0F, 0x12345678, 0xA5A5A5A5, 0xDEADBEEF, 0xCAFEBABE, 0x11223344, 0x55667788, 0x99AABBCC, 0x0BADF00D, 0xFEEDFACE };

	static uint[] Unrelated(int seed, int length) {
		var rng = new Random(seed);
		return Enumerable.Range(0, length).Select(_ => (uint)rng.NextInt64(1, uint.MaxValue)).ToArray();
	}

	/// <summary>Source, a clip inside it, and an unrelated video.</summary>
	void AddBasicLibrary() {
		Add("a", "source.mp4", 100, SourceFp);
		Add("b", "clip.mp4", 30, SourceFp[2..5]);
		Add("c", "other.mp4", 50, Unrelated(1, 6));
	}

	static ScanEngine NewEngine(Action<Settings>? configure = null) {
		var engine = new ScanEngine();
		engine.Settings.EnablePartialClipDetection = true;
		engine.Settings.PartialClipRequireVisualMatch = false; // no frame decoding in this test
		configure?.Invoke(engine.Settings);
		engine.ElapsedTimer.Start();
		return engine;
	}

	/// <summary>Group ids are random per run, so a group is identified by the paths in it.</summary>
	static List<string> Normalize(ScanEngine engine) {
		var groupPaths = engine.Duplicates.GroupBy(d => d.GroupId)
			.ToDictionary(g => g.Key, g => string.Join("|", g.Select(d => d.Path).OrderBy(p => p, StringComparer.Ordinal)));
		return engine.Duplicates
			.Select(d => $"{d.Path}#{groupPaths[d.GroupId]}#{d.Flags}#{d.PartialClipOffset}#{d.Similarity:R}")
			.OrderBy(s => s, StringComparer.Ordinal).ToList();
	}

	(List<string> result, long computed) Run(Action<Settings>? configure = null) {
		var engine = NewEngine(configure);
		engine.ScanForPartialDuplicates();
		return (Normalize(engine), engine.PartialPairsComputed);
	}

	(List<string> result, long computed) RunWithoutCache(Action<Settings>? configure = null) {
		File.Delete(CacheFile);
		return Run(configure);
	}

	[Fact]
	public void SecondRunOverSameEntries_GivesSameResultsAndComputesNothing() {
		AddBasicLibrary();

		var first = Run();
		Assert.True(first.computed > 0);
		Assert.True(File.Exists(CacheFile));
		Assert.Equal(2, first.result.Count); // source + clip

		var second = Run();
		Assert.Equal(first.result, second.result);
		Assert.Equal(0, second.computed);
	}

	[Fact]
	public void NewEntry_OnlyComparesPairsInvolvingIt() {
		AddBasicLibrary();
		Run();

		Add("d", "clip2.mp4", 20, SourceFp[5..7]);
		var withCache = Run();
		// The new video pairs with the 3 existing ones, none of which pair with each other afresh.
		Assert.Equal(3, withCache.computed);
		Assert.Equal(RunWithoutCache().result, withCache.result);
	}

	[Theory]
	[InlineData("threshold")]
	[InlineData("minRatio")]
	[InlineData("folderMode")]
	[InlineData("folderDepth")]
	public void ChangedSetting_IgnoresTheCache(string which) {
		AddBasicLibrary();
		Run();

		void Change(Settings s) {
			switch (which) {
				case "threshold": s.PartialClipSimilarityThreshold = 0.9; break;
				case "minRatio": s.PartialClipMinRatio = 0.25; break;
				case "folderMode": s.FolderMatchMode = FolderMatchMode.DifferentFolderOnly; break;
				case "folderDepth": s.SameFolderDepth = 2; break;
			}
		}

		var withCache = Run(Change);
		Assert.True(withCache.computed > 0);
		Assert.Equal(RunWithoutCache(Change).result, withCache.result);
	}

	[Fact]
	public void ChangedFingerprint_IsRecomputed() {
		var source = Add("a", "source.mp4", 100, SourceFp);
		var clip = Add("b", "clip.mp4", 30, SourceFp[2..5]);
		Add("c", "other.mp4", 50, Unrelated(1, 6));
		var before = Run();
		Assert.Equal(2, before.result.Count);

		// Same path, different content: the old match must not be served from the cache.
		clip.AudioFingerprint = Unrelated(2, 3);
		var withCache = Run();
		Assert.Equal(2, withCache.computed); // clip vs source and clip vs other
		Assert.Empty(withCache.result);
		Assert.Equal(RunWithoutCache().result, withCache.result);
		Assert.NotNull(source);
	}

	// An over-long 7-bit length makes BinaryReader.ReadString throw FormatException rather than IOException.
	static byte[] WithBadFirstPathLength(byte[] good) {
		using var r = new BinaryReader(new MemoryStream(good));
		// Skip the header written by Save: magic, version, the settings key fields and the identity count.
		r.ReadInt32(); r.ReadInt32(); r.ReadSingle(); r.ReadDouble(); r.ReadInt32(); r.ReadInt32(); r.ReadInt32();
		return good[..(int)r.BaseStream.Position].Concat(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }).ToArray();
	}

	[Theory]
	[InlineData("garbage")]
	[InlineData("truncated")]
	[InlineData("empty")]
	[InlineData("badLengthPrefix")]
	public void DamagedCacheFile_IsIgnored(string kind) {
		AddBasicLibrary();
		var expected = Run().result;
		byte[] good = File.ReadAllBytes(CacheFile);
		File.WriteAllBytes(CacheFile, kind switch {
			"garbage" => Enumerable.Range(0, 200).Select(i => (byte)(i * 37)).ToArray(),
			"truncated" => good[..(good.Length - 9)],
			"badLengthPrefix" => WithBadFirstPathLength(good),
			_ => Array.Empty<byte>(),
		});

		var again = Run();
		Assert.Equal(expected, again.result);
		Assert.True(again.computed > 0);
	}

	[Fact]
	public void Randomized_SecondRunWithCacheEqualsSecondRunWithout() {
		bool anyMatches = false;
		for (int seed = 1; seed <= 25; seed++) {
			foreach (var e in added.ToList())
				Remove(e);
			File.Delete(CacheFile);
			var rng = new Random(seed);
			var entries = new List<FileEntry>();
			int n = 0;

			FileEntry NewVideo() {
				int len = rng.Next(8, 40);
				uint[] fp;
				var sources = entries.Where(e => e.AudioFingerprint!.Length > len + 2).ToList();
				if (sources.Count > 0 && rng.Next(3) == 0) {
					// Planted partial match: a window of an existing video, with a few flipped bits.
					var src = sources[rng.Next(sources.Count)].AudioFingerprint!;
					int start = rng.Next(0, src.Length - len + 1);
					fp = src[start..(start + len)];
					for (int k = 0; k < len / 4; k++)
						fp[rng.Next(len)] ^= 1u << rng.Next(32);
				}
				else
					fp = Unrelated(rng.Next(), len);
				return Add("f" + rng.Next(3), $"v{n++}.mp4", len * 3 + rng.Next(5), fp);
			}

			for (int k = 0; k < 14; k++)
				entries.Add(NewVideo());
			Run();

			for (int k = 0; k < 6; k++) {
				switch (rng.Next(3)) {
					case 0: entries.Add(NewVideo()); break;
					case 1 when entries.Count > 4:
						var gone = entries[rng.Next(entries.Count)];
						entries.Remove(gone);
						Remove(gone);
						break;
					default:
						var changed = entries[rng.Next(entries.Count)];
						changed.AudioFingerprint = Unrelated(rng.Next(), changed.AudioFingerprint!.Length);
						break;
				}
			}

			var withCache = Run();
			Assert.Equal(RunWithoutCache().result, withCache.result);
			anyMatches |= withCache.result.Count > 0;
		}
		Assert.True(anyMatches, "the planted partial matches never produced a result, so the comparison proves nothing");
	}
}

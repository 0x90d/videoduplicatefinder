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
// The visual check's cache must be invisible except for speed: a run with a (possibly stale or
// damaged) cache has to give exactly the Duplicates a run without one gives, while decoding
// only what the cache cannot answer for the current files, settings and decoding path.

using VDF.Core.FFTools;
using VDF.Core.Utils;

namespace VDF.Core.Tests;

[Collection("DatabaseUtils")] // reads the shared static database and redirects the database folder
public class PartialClipVisualCacheTests : IDisposable {

	readonly List<FileEntry> added = new();
	readonly string dbFolder = Path.Combine(Path.GetTempPath(), "vdf-visualcache-" + Guid.NewGuid().ToString("N"));
	readonly string root = Path.Combine(Path.GetTempPath(), "vdf-vc-" + Guid.NewGuid().ToString("N"));

	/// <summary>The gray value every frame of a file decodes to; equal values look identical.</summary>
	readonly Dictionary<string, byte> look = new();
	/// <summary>Files whose frame at this position fails to decode.</summary>
	readonly HashSet<string> failingFrame = new();
	readonly List<string> decodes = new();

	FfmpegEngine.GrayFrameSource path = FfmpegEngine.GrayFrameSource.Native;
	FfmpegEngine.GrayFrameSource reportedPath = FfmpegEngine.GrayFrameSource.Native;
	string? build = "ffmpeg 8.0";

	public PartialClipVisualCacheTests() {
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

	string CacheFile => Path.Combine(dbFolder, PartialClipVisualCache.FileName);

	FileEntry Add(string folder, string name, double durationSeconds, uint[] fingerprint, byte gray) {
		string dir = Path.Combine(root, folder);
		var entry = new FileEntry {
			_Path = Path.Combine(dir, name),
			Folder = dir,
			FileSize = 1000,
			DateModified = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
			invalid = false,
			IsImage = false,
			AudioFingerprint = fingerprint,
			mediaInfo = new MediaInfo { Duration = TimeSpan.FromSeconds(durationSeconds) },
		};
		DatabaseUtils.Database.Add(entry);
		added.Add(entry);
		look[entry.Path] = gray;
		return entry;
	}

	void Remove(FileEntry e) {
		DatabaseUtils.Database.Remove(e);
		added.Remove(e);
	}

	static readonly uint[] SourceFp =
		{ 0x0F0F0F0F, 0x12345678, 0xA5A5A5A5, 0xDEADBEEF, 0xCAFEBABE, 0x11223344, 0x55667788, 0x99AABBCC, 0x0BADF00D, 0xFEEDFACE, 0x01020304, 0x0A0B0C0D };

	static uint[] Unrelated(int seed, int length) {
		var rng = new Random(seed);
		return Enumerable.Range(0, length).Select(_ => (uint)rng.NextInt64(1, uint.MaxValue)).ToArray();
	}

	byte[]?[] Decode(string file, IReadOnlyList<double> times, bool extendedLogging, out FfmpegEngine.GrayFrameSource decodedBy) {
		lock (decodes)
			decodes.Add(Path.GetFileName(file));
		decodedBy = reportedPath;
		byte value = look[file];
		return times.Select((_, i) => failingFrame.Contains(file) && i == 0 ? null : Enumerable.Repeat(value, 1024).ToArray()).ToArray();
	}

	ScanEngine NewEngine(Action<Settings>? configure, bool withDecoder = true) {
		var engine = new ScanEngine();
		engine.Settings.EnablePartialClipDetection = true;
		engine.Settings.PartialClipRequireVisualMatch = true;
		engine.Settings.UsePHashing = false;
		configure?.Invoke(engine.Settings);
		if (withDecoder)
			engine.DecodeGrayFrames = Decode;
		engine.CurrentGrayFrameSource = () => path;
		engine.GrayFrameDecoderBuild = () => build;
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

	(List<string> result, int decodes) Run(Action<Settings>? configure = null) {
		decodes.Clear();
		var engine = NewEngine(configure);
		engine.ScanForPartialDuplicates();
		return (Normalize(engine), decodes.Count);
	}

	List<string> RunWithoutCache(Action<Settings>? configure = null) {
		string saved = CacheFile + ".saved";
		if (File.Exists(CacheFile))
			File.Move(CacheFile, saved, overwrite: true);
		var result = Run(configure).result;
		File.Delete(CacheFile);
		if (File.Exists(saved))
			File.Move(saved, CacheFile);
		return result;
	}

	/// <summary>
	/// Two audio candidates for one clip: the longer source shares the soundtrack but not the
	/// picture (rejected), the shorter one is the clip's real source (kept).
	/// </summary>
	(FileEntry wrong, FileEntry source, FileEntry clip) AddBasicLibrary() {
		var wrong = Add("a", "wrong.mp4", 200, Unrelated(7, 3).Concat(SourceFp).ToArray(), gray: 250);
		var source = Add("b", "source.mp4", 100, SourceFp, gray: 10);
		var clip = Add("c", "clip.mp4", 30, SourceFp[2..6], gray: 10);
		Add("d", "other.mp4", 50, Unrelated(1, 6), gray: 90);
		return (wrong, source, clip);
	}

	[Fact]
	public void SecondRun_GivesSameResultsAndDecodesNothing() {
		AddBasicLibrary();

		var first = Run();
		Assert.True(first.decodes > 0);
		Assert.True(File.Exists(CacheFile));
		Assert.Equal(2, first.result.Count); // source + clip

		var second = Run();
		Assert.Equal(first.result, second.result);
		Assert.Equal(0, second.decodes);
	}

	[Fact]
	public void NewVideo_OnlyItsPairsAreDecoded() {
		AddBasicLibrary();
		Run();

		Add("e", "clip2.mp4", 20, SourceFp[6..9], gray: 10);
		var withCache = Run();
		Assert.Contains("clip2.mp4", decodes);
		Assert.DoesNotContain("clip.mp4", decodes);
		Assert.Equal(RunWithoutCache(), withCache.result);
	}

	[Theory]
	[InlineData("size")]
	[InlineData("modified")]
	public void ChangedVideo_IsDecodedAgain(string what) {
		var (_, _, clip) = AddBasicLibrary();
		Run();

		// Same audio, new picture: only the file's size or date tells.
		if (what == "size") clip.FileSize++;
		else clip.DateModified = clip.DateModified.AddMinutes(1);
		look[clip.Path] = 250;

		var withCache = Run();
		Assert.Contains("clip.mp4", decodes);
		Assert.Equal(RunWithoutCache(), withCache.result);
	}

	[Theory]
	[InlineData("pHash")]
	[InlineData("ffmpeg")]
	public void ChangedComparisonOrFfmpeg_IgnoresTheCache(string what) {
		AddBasicLibrary();
		Run();

		Action<Settings>? configure = null;
		if (what == "pHash") configure = s => s.UsePHashing = true;
		else build = "ffmpeg 8.1";

		var withCache = Run(configure);
		Assert.True(withCache.decodes > 0);
		Assert.Equal(RunWithoutCache(configure), withCache.result);
	}

	[Fact]
	public void OtherDecodingPath_DecodesAgain_AndBothPathsStayCached() {
		AddBasicLibrary();
		Run();

		path = reportedPath = FfmpegEngine.GrayFrameSource.Process;
		Assert.True(Run().decodes > 0);
		Assert.Equal(0, Run().decodes);

		path = reportedPath = FfmpegEngine.GrayFrameSource.Native;
		Assert.Equal(0, Run().decodes);
	}

	[Fact]
	public void ChangedThreshold_DecodesNothing_AndMatchesAnUncachedRun() {
		var (wrong, _, _) = AddBasicLibrary();
		look[wrong.Path] = 80; // close to the clip: only a low threshold keeps it
		var strict = Run();

		void Lenient(Settings s) => s.PartialClipVisualThreshold = 0.5;
		var lenient = Run(Lenient);
		Assert.Equal(0, lenient.decodes);
		Assert.NotEqual(strict.result, lenient.result);
		Assert.Equal(RunWithoutCache(Lenient), lenient.result);
	}

	[Fact]
	public void PairNotVisitedInARun_StaysCached() {
		var (wrong, _, _) = AddBasicLibrary();
		look[wrong.Path] = 80;
		Run(); // strict: the wrong source fails, the real one is checked and passes

		Run(s => s.PartialClipVisualThreshold = 0.5); // lenient: the wrong source passes, the real one is never reached
		Assert.Equal(0, Run().decodes); // strict again: both answers are still cached
	}

	[Fact]
	public void IncompleteOrMixedDecodes_AreNotCached() {
		var (_, source, _) = AddBasicLibrary();
		failingFrame.Add(source.Path);
		Run();
		Run();
		Assert.Contains("source.mp4", decodes);

		failingFrame.Clear();
		reportedPath = FfmpegEngine.GrayFrameSource.Mixed;
		Run();
		Run();
		Assert.Contains("source.mp4", decodes);
	}

	[Theory]
	[InlineData("garbage")]
	[InlineData("truncated")]
	[InlineData("empty")]
	public void DamagedCacheFile_IsIgnored(string kind) {
		AddBasicLibrary();
		var expected = Run().result;
		byte[] good = File.ReadAllBytes(CacheFile);
		File.WriteAllBytes(CacheFile, kind switch {
			"garbage" => Enumerable.Range(0, 200).Select(i => (byte)(i * 37)).ToArray(),
			"truncated" => good[..(good.Length - 5)],
			_ => Array.Empty<byte>(),
		});

		var again = Run();
		Assert.Equal(expected, again.result);
		Assert.True(again.decodes > 0);
	}

	[Fact]
	public void RemovedVideo_IsDroppedFromTheCache() {
		var (_, _, clip) = AddBasicLibrary();
		Run();
		long before = new FileInfo(CacheFile).Length;

		Remove(clip);
		Run();
		Assert.True(new FileInfo(CacheFile).Length < before);
	}

	[Fact]
	public void VideoOutsideThisRunButInTheDatabase_KeepsItsCachedResults() {
		var (_, source, _) = AddBasicLibrary();
		var first = Run();

		// Another folder set: the source is out of scope (invalid), yet still in the database.
		source.invalid = true;
		Run();
		source.invalid = false;

		var again = Run();
		Assert.Equal(0, again.decodes);
		Assert.Equal(first.result, again.result);
	}

	[Fact]
	public void UnavailableFfmpegBuild_RunsWithoutTheCache_AndLeavesTheFileAlone() {
		AddBasicLibrary();
		var expected = Run().result;
		byte[] before = File.ReadAllBytes(CacheFile);

		build = null;
		var uncached = Run();
		Assert.True(uncached.decodes > 0);
		Assert.Equal(expected, uncached.result);
		Assert.Equal(before, File.ReadAllBytes(CacheFile));
	}

	[Fact]
	public void Randomized_RunWithCacheEqualsRunWithout() {
		bool anyKept = false, anyDropped = false;
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
				byte gray = (byte)rng.Next(256);
				var sources = entries.Where(e => e.AudioFingerprint!.Length > len + 2).ToList();
				if (sources.Count > 0 && rng.Next(2) == 0) {
					// Planted partial match: a window of an existing video, sometimes with its picture.
					var src = sources[rng.Next(sources.Count)];
					int start = rng.Next(0, src.AudioFingerprint!.Length - len + 1);
					fp = src.AudioFingerprint[start..(start + len)];
					if (rng.Next(2) == 0) gray = look[src.Path];
				}
				else
					fp = Unrelated(rng.Next(), len);
				return Add("f" + rng.Next(3), $"v{n++}.mp4", len * 3 + rng.Next(5), fp, gray);
			}

			for (int k = 0; k < 14; k++)
				entries.Add(NewVideo());
			Run();

			for (int k = 0; k < 6; k++) {
				switch (rng.Next(4)) {
					case 0: entries.Add(NewVideo()); break;
					case 1 when entries.Count > 4:
						var gone = entries[rng.Next(entries.Count)];
						entries.Remove(gone);
						Remove(gone);
						break;
					case 2:
						var repainted = entries[rng.Next(entries.Count)];
						repainted.FileSize++;
						look[repainted.Path] = (byte)rng.Next(256);
						break;
					default:
						if (entries.Count > 0) failingFrame.Add(entries[rng.Next(entries.Count)].Path);
						break;
				}
			}
			double threshold = rng.Next(2) == 0 ? 0.85 : 0.6;
			void Configure(Settings s) => s.PartialClipVisualThreshold = threshold;

			var withCache = Run(Configure);
			Assert.Equal(RunWithoutCache(Configure), withCache.result);
			anyKept |= withCache.result.Count > 0;
			anyDropped |= withCache.decodes > 0;
			failingFrame.Clear();
		}
		Assert.True(anyKept, "no planted clip was ever kept, so the comparison proves nothing");
		Assert.True(anyDropped, "nothing was ever decoded in the second run, so the cache was never exercised past a full hit");
	}
}

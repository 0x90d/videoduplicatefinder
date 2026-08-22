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

using System.Runtime.CompilerServices;
using SkiaSharp;

namespace VDF.GUI.Tests;

// Issues #902 and #830: the GUI died with an access violation inside libSkiaSharp
// (SkDWriteFontFileStream::read) on CJK-locale machines, typically on the second launch.
// Avalonia.Skia reads a whole font file whenever it fakes a bold/oblique face
// (SkiaTypeface.TryGetStream: OpenStream, Length, Read - the wrapper is never disposed and
// never referenced after Read). SkiaSharp 3.x's SKStream.Read had no GC.KeepAlive, so in
// fully optimized code the SKStreamAsset became garbage during the native memcpy of a
// 13-20 MB CJK font; a GC from any other thread (VDF's database load / results restore at
// startup) ran its finalizer, which destroyed the native stream mid-copy. SkiaSharp 4.x
// keeps the wrapper alive for the duration of every native call, which is why VDF.GUI pins
// SkiaSharp above Avalonia's transitive 3.119.4.
//
// The stress test below is the exact Avalonia call sequence under GC churn. On SkiaSharp
// 3.119.4 it takes the test host down with 0xC0000005 within the first ~100 reads.
public class SkiaFontStreamLifetimeTests {
	[Fact]
	public void SkiaSharp_IsAtLeastVersion4_WhichKeepsStreamsAliveDuringNativeReads() {
		var version = typeof(SKTypeface).Assembly.GetName().Version;
		Assert.NotNull(version);
		Assert.True(version.Major >= 4,
			$"SkiaSharp {version} resolved - 3.x lacks GC.KeepAlive in SKStream.Read and crashes on CJK fonts (#902). " +
			"Check the SkiaSharp PackageReference in VDF.GUI.csproj.");
	}

	[Fact]
	public void ReadingWholeFontStreamLikeAvalonia_SurvivesConcurrentGarbageCollection() {
		using var typeface = PickLargestSystemFont();
		if (typeface == null) {
			// Every Windows 10+ SKU ships Malgun Gothic / Microsoft YaHei / Yu Gothic, so on Windows a miss means
			// the probe is broken, not that the machine has no fonts. Elsewhere a bare CI image may have none.
			Assert.False(OperatingSystem.IsWindows(), "no sizeable system font found through SkiaSharp on Windows");
			return;
		}

		using var stop = new CancellationTokenSource();
		var churn = new Thread(() => {
			while (!stop.IsCancellationRequested) {
				var junk = new byte[64 * 1024];
				junk[0] = 1;
			}
		}) { IsBackground = true };
		churn.Start();
		try {
			var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(4);
			int reads = 0;
			while (reads < 150 && DateTime.UtcNow < deadline) {
				int n = ReadWholeFontLikeAvalonia(typeface);
				Assert.True(n > 0);
				reads++;
			}
			Assert.True(reads > 0);
		}
		finally {
			stop.Cancel();
			churn.Join();
		}
	}

	// Mirrors Avalonia.Skia.SkiaTypeface.TryGetStream (12.0.5): the stream is neither disposed
	// nor referenced after Read, so its lifetime during the native call is SkiaSharp's problem.
	[MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
	static int ReadWholeFontLikeAvalonia(SKTypeface typeface) {
		SKStreamAsset asset = typeface.OpenStream();
		int length = asset.Length;
		byte[] buffer = new byte[length];
		return asset.Read(buffer, length);
	}

	static SKTypeface? PickLargestSystemFont() {
		string[] candidates = [
			"Malgun Gothic", "Microsoft YaHei", "Yu Gothic", "Microsoft JhengHei", "Meiryo", "SimSun",
			"Noto Sans CJK SC", "Noto Sans CJK JP", "Noto Sans CJK KR", "Hiragino Sans", "PingFang SC", "Apple SD Gothic Neo",
			"Segoe UI", "Arial", "DejaVu Sans", "Liberation Sans",
		];
		SKTypeface? best = null;
		long bestLength = 0;
		foreach (var family in candidates) {
			var typeface = SKFontManager.Default.MatchFamily(family);
			if (typeface == null)
				continue;
			long length;
			using (var stream = typeface.OpenStream())
				length = stream?.Length ?? 0;
			if (length > bestLength) {
				best?.Dispose();
				best = typeface;
				bestLength = length;
			}
			else {
				typeface.Dispose();
			}
		}
		if (bestLength < 1024 * 1024) {
			best?.Dispose();
			return null;
		}
		return best;
	}
}

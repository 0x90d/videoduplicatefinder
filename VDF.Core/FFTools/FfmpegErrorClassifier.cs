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

using System;

namespace VDF.Core.FFTools {
	/// <summary>
	/// The likely root cause of an FFmpeg decode failure, inferred from FFmpeg's own log
	/// output and/or the thrown exception message.
	/// </summary>
	internal enum FfmpegErrorCategory {
		/// <summary>Cause could not be determined — most likely worth a bug report.</summary>
		Unknown,
		/// <summary>GPU/hardware decoder can't handle this stream (codec/profile/resolution).</summary>
		HardwareAcceleration,
		/// <summary>The media file is truncated, damaged, or otherwise unreadable as a stream.</summary>
		CorruptOrTruncated,
		/// <summary>This FFmpeg build has no decoder for the file's codec.</summary>
		UnsupportedCodec,
		/// <summary>The file could not be opened (permissions, lock, or invalid path).</summary>
		FileAccess,
		/// <summary>The FFmpeg shared libraries could not be loaded or are the wrong version.</summary>
		LibraryLoad,
	}

	/// <summary>
	/// Maps raw FFmpeg diagnostics (the lines FFmpeg itself emits, e.g. "Hardware is lacking
	/// required capabilities") onto a coarse category and a plain-language hint. The native
	/// binding otherwise surfaces only an opaque <c>av_strerror</c> string, which makes it
	/// impossible for end users — and the maintainer triaging issue reports — to tell whether
	/// a failure is incompatible hardware, a damaged file, an environment problem, or a real
	/// VDF/FFmpeg bug. This turns that guesswork into a one-line verdict.
	///
	/// Pure and side-effect-free so it can be unit tested without FFmpeg present.
	/// </summary>
	internal static class FfmpegErrorClassifier {
		// Ordered most-specific / most-commonly-confused first; the first matching group wins.
		// Substrings are matched case-insensitively against the combined diagnostics text.
		static readonly (FfmpegErrorCategory Category, string[] Needles)[] Rules = {
			(FfmpegErrorCategory.HardwareAcceleration, new[] {
				"lacking required capabilities",
				"hwaccel initiali",                 // "initialisation"/"initialization" returned error
				"failed setup for format",          // "Failed setup for format cuda: ..."
				"no device available for decoder",
				"hwaccel transfer data failed",
				"cannot load nvcuda",
				"cannot load libcuda",
				"cannot load cuda",
				"hw_device_ctx",
				"device creation failed",
			}),
			(FfmpegErrorCategory.LibraryLoad, new[] {
				"unable to load shared library",
				"could not load",
				"dllnotfound",
				"the installed ffmpeg major version does not match",
				"version mismatch",
			}),
			(FfmpegErrorCategory.UnsupportedCodec, new[] {
				"decoder not found",
				"unknown decoder",
				"no decoder for",
				"codec not currently supported",
			}),
			(FfmpegErrorCategory.CorruptOrTruncated, new[] {
				"moov atom not found",
				"invalid data found when processing input",
				"invalid nal unit size",
				"error splitting the input into nal units",
				"could not find codec parameters",
				"non-existing pps",
				"non-existing sps",
				"error while decoding",
				"partial file",
				"truncat",
			}),
			(FfmpegErrorCategory.FileAccess, new[] {
				"permission denied",
				"no such file or directory",
				"operation not permitted",
				"protocol not found",
			}),
			// Weak fallback: AVERROR_EXTERNAL ("Generic error in an external library") is what the
			// native binding most often throws when hardware decoding fails, so treat it as a
			// hardware hint only after the specific patterns above had their chance.
			(FfmpegErrorCategory.HardwareAcceleration, new[] {
				"generic error in an external library",
			}),
		};

		/// <summary>Infers the failure category from the combined FFmpeg diagnostics / exception text.</summary>
		internal static FfmpegErrorCategory Categorize(string? text) {
			if (string.IsNullOrWhiteSpace(text))
				return FfmpegErrorCategory.Unknown;
			foreach (var (category, needles) in Rules) {
				foreach (var needle in needles) {
					if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
						return category;
				}
			}
			return FfmpegErrorCategory.Unknown;
		}

		/// <summary>
		/// Returns a plain-language hint for the failure, or <c>null</c> when the cause is
		/// unknown (in which case the caller should encourage a bug report with the log).
		/// </summary>
		internal static string? Classify(string? text) => HintFor(Categorize(text));

		internal static string? HintFor(FfmpegErrorCategory category) => category switch {
			FfmpegErrorCategory.HardwareAcceleration =>
				"This looks like a GPU/hardware-decoding problem — your GPU's decoder likely can't handle " +
				"this file's codec, profile, or resolution (e.g. 4K 10-bit HEVC on older GPUs). " +
				"Set Settings -> Processing -> Hardware acceleration to 'none'.",
			FfmpegErrorCategory.CorruptOrTruncated =>
				"The file appears to be truncated or corrupt. Check whether it plays in a normal media " +
				"player; if it does not, the source file is damaged.",
			FfmpegErrorCategory.UnsupportedCodec =>
				"This FFmpeg build has no decoder for the file's codec. Install or point VDF at a full FFmpeg build.",
			FfmpegErrorCategory.FileAccess =>
				"VDF could not read the file (permissions, a locked file, or an invalid/too-long path).",
			FfmpegErrorCategory.LibraryLoad =>
				"The FFmpeg shared libraries could not be loaded or are the wrong version. Disable " +
				"'Use native FFmpeg binding', or install an FFmpeg matching the bundled version.",
			_ => null,
		};
	}
}

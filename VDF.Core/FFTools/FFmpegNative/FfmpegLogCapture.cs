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
using System.Runtime.InteropServices;
using System.Text;
using FFmpeg.AutoGen;

namespace VDF.Core.FFTools.FFmpegNative {
	/// <summary>
	/// Captures FFmpeg's own diagnostic log lines so a native-binding failure can report the
	/// real reason instead of only an opaque <c>av_strerror</c> string.
	///
	/// When VDF decodes through the native binding, FFmpeg's most informative messages
	/// ("Hardware is lacking required capabilities", "moov atom not found", ...) are written to
	/// FFmpeg's internal log and normally discarded — only the generic errno-style return value
	/// survives into the thrown exception. By installing an <c>av_log</c> callback we keep the
	/// last few warning/error lines per thread; <see cref="FfmpegEngine"/> appends them (and a
	/// classified hint, see <see cref="FfmpegErrorClassifier"/>) to the failure it logs.
	///
	/// The buffer is <see cref="ThreadStaticAttribute">[ThreadStatic]</see> because the relevant
	/// setup/decode errors are emitted synchronously on the calling worker thread; FFmpeg's own
	/// internal decode threads (which would land in a different bucket) only emit frame-level
	/// noise we do not need. The callback is global and installed once for the process.
	/// </summary>
	static unsafe class FfmpegLogCapture {
		const int MaxLines = 8;
		const int MaxLineLength = 1024;

		[ThreadStatic] static string[]? _lines;
		[ThreadStatic] static int _next;   // next write slot
		[ThreadStatic] static int _count;  // valid entries (capped at MaxLines)

		static readonly object _installLock = new();
		static bool _installed;
		// Held for the process lifetime so the GC cannot collect the delegate handed to native code.
		static av_log_set_callback_callback? _callback;

		/// <summary>Installs the log callback once. Cheap (a volatile bool check) after the first call.</summary>
		internal static void EnsureInstalled() {
			if (_installed)
				return;
			lock (_installLock) {
				if (_installed)
					return;
				_callback = LogCallback;
				ffmpeg.av_log_set_callback(_callback);
				_installed = true;
			}
		}

		/// <summary>Ensures the callback is installed and clears this thread's captured lines.
		/// Call immediately before a native decode attempt so the snapshot reflects only it.</summary>
		internal static void Reset() {
			EnsureInstalled();
			Clear();
		}

		/// <summary>Clears the current thread's captured lines.</summary>
		internal static void Clear() {
			_next = 0;
			_count = 0;
		}

		/// <summary>
		/// Returns the warning/error lines FFmpeg emitted on this thread since the last
		/// <see cref="Clear"/>, oldest first, joined by " | ". Empty when nothing was captured.
		/// </summary>
		internal static string GetRecent() {
			if (_lines == null || _count == 0)
				return string.Empty;
			int start = (_next - _count + MaxLines) % MaxLines;
			var sb = new StringBuilder();
			for (int i = 0; i < _count; i++) {
				if (sb.Length > 0)
					sb.Append(" | ");
				sb.Append(_lines[(start + i) % MaxLines]);
			}
			return sb.ToString();
		}

		/// <summary>
		/// Stores one already-formatted log line, honoring the warning-or-worse level filter.
		/// Split out from the native callback so the ring-buffer behavior is unit testable
		/// without FFmpeg present.
		/// </summary>
		internal static void Record(int level, string? message) {
			// Lower numeric level == more severe; keep WARNING/ERROR/FATAL/PANIC, drop INFO and below.
			if (level > ffmpeg.AV_LOG_WARNING)
				return;
			if (string.IsNullOrWhiteSpace(message))
				return;
			string line = message.Trim();
			if (line.Length == 0)
				return;
			_lines ??= new string[MaxLines];
			_lines[_next] = line;
			_next = (_next + 1) % MaxLines;
			if (_count < MaxLines)
				_count++;
		}

		static void LogCallback(void* avcl, int level, string fmt, byte* vl) {
			// A log callback must never let an exception propagate back into native code.
			try {
				if (level > ffmpeg.AV_LOG_WARNING)
					return;
				byte* line = stackalloc byte[MaxLineLength];
				int printPrefix = 0;
				ffmpeg.av_log_format_line2(avcl, level, fmt, vl, line, MaxLineLength, &printPrefix);
				Record(level, Marshal.PtrToStringAnsi((IntPtr)line));
			}
			catch {
				// Swallow: capturing a diagnostic must not destabilize decoding.
			}
		}
	}
}

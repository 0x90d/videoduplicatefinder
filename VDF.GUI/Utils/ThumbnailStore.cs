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
//     along with VideoDuplicateFinder.  If not, see <https://www.gnu.org/licenses/>.
// */
//

using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using DynamicData;
using VDF.GUI.Utils;

namespace VDF.GUI.Utils {
	internal static class ThumbCacheHelpers {
		public static ThumbPack? Provider { get; set; }
		public static string XxHash64Hex(string s) {
			ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(s.AsSpan());
			byte[] hash = System.IO.Hashing.XxHash64.Hash(bytes);
			return Convert.ToHexStringLower(hash);
		}
		public static void DeletePackFolder(string? folder) {
			try {
				if (Directory.Exists(folder))
					Directory.Delete(folder, recursive: true);
			}
			catch { /* ignore */ }
		}

		public static string EnsureFolder(string baseFolder, string name) {
			var f = Path.Combine(baseFolder, name);
			Directory.CreateDirectory(f);
			return f;
		}

		/// <summary>
		/// Deletes the ThumbPack if the thumbnail width setting changed since the cache was created.
		/// Stores the current width in a marker file alongside the pack.
		/// </summary>
		public static void InvalidateIfWidthChanged(string packFolder, int currentWidth) {
			try {
				var markerPath = Path.Combine(packFolder, "thumbwidth.txt");
				if (File.Exists(markerPath)) {
					var stored = File.ReadAllText(markerPath).Trim();
					if (int.TryParse(stored, out var oldWidth) && oldWidth == currentWidth)
						return; // width unchanged, cache is valid
				}
				// Width changed or marker doesn't exist — delete the pack so it regenerates
				DeletePackFolder(packFolder);
				Directory.CreateDirectory(packFolder);
				File.WriteAllText(Path.Combine(packFolder, "thumbwidth.txt"), currentWidth.ToString());
			}
			catch { /* ignore */ }
		}

		public static void SetActiveProvider(ThumbPack? provider) {
			var path = Provider?.GetDirectory();
			try { Provider?.Dispose(); } catch { }
			DeletePackFolder(path);

			Provider = provider;
		}
	}

	/// <summary>
	/// A large, append-only thumbnail cache:
	///  - thumbs.pack : Binary data (JPEGs in sequence)
	///  - thumbs.idx  : JSON { key -> (offset,length) }
	/// </summary>
	public sealed class ThumbPack : IDisposable {
		readonly FileStream _fs;
		readonly string _packPath;
		readonly string _idxPath;
		readonly Dictionary<string, (long off, int len)> _idx;
		readonly object _gate = new();
		public readonly string Folder;

		private ThumbPack(FileStream fs, string idxPath, Dictionary<string, (long, int)> idx, string packPath, string folder) {
			_fs = fs; _idxPath = idxPath; _idx = idx; Folder = folder; _packPath = packPath;
		}

		public static ThumbPack Open(string folder) {

			Directory.CreateDirectory(folder);
			string packPath = System.IO.Path.Combine(folder, "thumbs.pack");
			string idxPath = System.IO.Path.Combine(folder, "thumbs.idx");
			var fs = new FileStream(packPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
			Dictionary<string, (long, int)> idx = File.Exists(idxPath)
							? System.Text.Json.JsonSerializer.Deserialize(
			File.ReadAllBytes(idxPath), Data.GuiJsonFieldsContext.Default.ThumbPackIndex) ?? new()
							: new();
			return new ThumbPack(fs, idxPath, idx, packPath, folder);
		}

		public bool Contains(string key) {
			lock (_gate) return _idx.ContainsKey(key);
		}

		/// <summary>
		/// Inserts JPEG from src into the pack (if key does not exist OR existing entry
		/// is zero-length — see below). Returns with (offset, length).
		///
		/// Zero-length entries are treated as "missing" for two reasons (issue #751):
		///  - A 0-byte write means the producer (e.g. JoinImages) failed to write a JPEG.
		///    Recording a (off, 0) entry would mean the next OpenKey returns an empty slice,
		///    Bitmap construction throws, the UI shows blank, AND the per-path key is
		///    permanently latched, so explicit "Load thumbnails for group" would no-op
		///    forever.
		///  - Lets a corrupted pack from a prior session self-heal: empty entries become
		///    overwritable, and the next attempt that actually writes bytes wins.
		/// </summary>
		public (long off, int len) AppendIfMissing(string key, Action<Stream> writeJpeg) {
			lock (_gate) {
				if (_idx.TryGetValue(key, out var e) && e.Item2 > 0) return e;
			}
			// Run writeJpeg OUTSIDE the lock: it is expensive (decode frames, compose the
			// grid, FFmpeg-encode). Holding _gate through it serialized every thumbnail
			// worker AND blocked the UI thread — whose Thumbnail getter takes the same
			// lock via OpenKey — behind the whole worker convoy for the duration of a
			// retrieval pass (Linux forum report: minutes-long GUI stalls on big scans).
			using var buffer = new MemoryStream();
			writeJpeg(buffer);
			int len = checked((int)buffer.Length);
			lock (_gate) {
				// A concurrent writer may have finished the same key while we encoded.
				if (_idx.TryGetValue(key, out var e) && e.Item2 > 0) return e;
				_fs.Seek(0, SeekOrigin.End);
				long off = _fs.Position;
				if (len == 0) {
					// Don't record an empty entry. Leaving _idx untouched (or unchanged
					// if a previous empty entry existed) means OpenKey returns null, the
					// Thumbnail getter sees no key match, and the next retry re-attempts
					// extraction instead of serving back broken data forever.
					return (off, 0);
				}
				buffer.Position = 0;
				buffer.CopyTo(_fs);
				// OpenKey reads through an independent handle on the pack file; without a
				// flush the bytes sit in _fs's write buffer and read back as zeros.
				_fs.Flush();
				_idx[key] = (off, len);
				return (off, len);
			}
		}

		public bool TryGetEntry(string key, out long off, out int len) {
			lock (_gate) {
				if (_idx.TryGetValue(key, out var e)) { off = e.off; len = e.len; return true; }
				off = 0; len = 0; return false;
			}
		}

		public Stream? OpenKey(string key) {
			lock (_gate) {
				if (!_idx.TryGetValue(key, out var e)) return null;
				var rfs = new FileStream(_packPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 128 * 1024, useAsync: false);
				// The slice must OWN rfs (leaveOpen: false): nothing else references it, so
				// leaveOpen leaked one file descriptor per thumbnail load until a GC ran a
				// finalizer pass — enough to exhaust the default Linux fd limit while
				// scrolling a large result list, strangling the whole process.
				return new StreamSlice(rfs, e.off, e.len, leaveOpen: false);
			}
		}
		public void FlushIndex() {
			lock (_gate) {
				var json = System.Text.Json.JsonSerializer.Serialize(_idx, Data.GuiJsonFieldsContext.Default.ThumbPackIndex);
				File.WriteAllText(_idxPath, json);
			}
		}

		/// <summary>
		/// Consistent point-in-time view for export: the flushed pack length plus an index
		/// (as UTF-8 JSON) containing only entries that lie entirely within that length.
		/// Entries appended afterwards end up in neither, so the exported pair stays
		/// coherent while <see cref="CopyPackTo"/> runs without any lock held — the old
		/// whole-pack copy under _gate blocked every OpenKey (and with it the UI thread)
		/// for the duration of a potentially multi-GB copy.
		/// </summary>
		public (long PackLength, byte[] IndexJson) SnapshotForExport() {
			lock (_gate) {
				_fs.Flush();
				long len = _fs.Length;
				Dictionary<string, (long, int)> snap = new(_idx.Count);
				foreach (var kv in _idx)
					if (kv.Value.off + kv.Value.len <= len)
						snap[kv.Key] = kv.Value;
				byte[] json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(snap, Data.GuiJsonFieldsContext.Default.ThumbPackIndex);
				return (len, json);
			}
		}

		/// <summary>
		/// Copies the first <paramref name="length"/> bytes of the pack (a length captured
		/// by <see cref="SnapshotForExport"/>) through an independent read handle. No lock
		/// is held; concurrent appends and reads proceed unhindered.
		/// </summary>
		public void CopyPackTo(Stream destination, long length) {
			using var rfs = new FileStream(_packPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 128 * 1024, useAsync: false);
			byte[] buffer = new byte[128 * 1024];
			long remaining = length;
			while (remaining > 0) {
				int n = rfs.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
				if (n <= 0) break;
				destination.Write(buffer, 0, n);
				remaining -= n;
			}
		}

		public void Dispose() { FlushIndex(); _fs.Dispose(); }

		public string? GetDirectory() => Folder;
	}

	/// <summary> Stream slice without copy (reads range [offset, offsetlength) from shared FileStream). </summary>
	internal sealed class StreamSlice : Stream {
		readonly FileStream _fs;
		readonly long _start;
		readonly long _len;
		long _pos;
		readonly bool _leaveOpen;
		public StreamSlice(FileStream fs, long start, int len, bool leaveOpen) {
			_fs = fs; _start = start; _len = len; _pos = 0; _leaveOpen = leaveOpen;
		}
		public override bool CanRead => true;
		public override bool CanSeek => true;
		public override bool CanWrite => false;
		public override long Length => _len;
		public override long Position { get => _pos; set => Seek(value, SeekOrigin.Begin); }
		public override void Flush() { }
		public override int Read(byte[] buffer, int offset, int count) {
			count = (int)Math.Min(count, _len - _pos);
			if (count <= 0) return 0;
			lock (_fs) {
				_fs.Seek(_start + _pos, SeekOrigin.Begin);
				int n = _fs.Read(buffer, offset, count);
				_pos += n; return n;
			}
		}
		public override long Seek(long offset, SeekOrigin origin) {
			long np = origin switch {
				SeekOrigin.Begin => offset,
				SeekOrigin.Current => _pos + offset,
				SeekOrigin.End => _len + offset,
				_ => _pos
			};
			_pos = Math.Max(0, Math.Min(_len, np));
			return _pos;
		}
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
		protected override void Dispose(bool disposing) { if (!disposing || _leaveOpen) return; try { _fs.Dispose(); } catch { } }
	}

	/// <summary> Small, size-limited LRU cache for UI bitmaps (RAM capped). </summary>
	internal static class LRUBitmapCache {
		static readonly object gate = new();
		static readonly LinkedList<string> lru = new();
		static readonly Dictionary<string, (Avalonia.Media.Imaging.Bitmap bmp, LinkedListNode<string> node, long size)> map = new();
		static long currentBytes;
		public static long MaxBytes { get; set; } = 128L * 1024 * 1024; // 128 MB

		static long ApproxSize(Avalonia.Media.Imaging.Bitmap bmp)
			=> (long)bmp.PixelSize.Width * bmp.PixelSize.Height * 4;

		public static Avalonia.Media.Imaging.Bitmap? GetOrCreate(string key, Func<Avalonia.Media.Imaging.Bitmap?> loader) {
			lock (gate) {
				if (map.TryGetValue(key, out var e)) {
					lru.Remove(e.node); lru.AddFirst(e.node); return e.bmp;
				}
			}
			var bmp = loader();
			if (bmp == null) return null;
			var size = ApproxSize(bmp);
			lock (gate) {
				var node = new LinkedListNode<string>(key);
				lru.AddFirst(node);
				map[key] = (bmp, node, size);
				currentBytes += size;
				EvictIfNeeded();
			}
			return bmp;
		}

		static void EvictIfNeeded() {
			while (currentBytes > MaxBytes && lru.Last != null) {
				var key = lru.Last.Value;
				lru.RemoveLast();
				if (map.Remove(key, out var e)) {
					currentBytes -= e.size;
					// Important: do not dispose – UI may still display the bitmap.
					//try { e.bmp.Dispose(); } catch { }
				}
			}
		}
	}

}

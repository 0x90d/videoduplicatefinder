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
	public sealed class ThumbPack  {
		readonly FileStream _fs;
		readonly string _packPath;
		readonly string _idxPath;
		readonly Dictionary<string, (long off, int len)> _idx;
		readonly object _gate = new();
		bool _dirty;
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
							? System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, (long, int)>>(
			File.ReadAllBytes(idxPath), IdxJson) ?? new()
							: new();
			return new ThumbPack(fs, idxPath, idx, packPath, folder);
		}

		public bool Contains(string key) {
			lock (_gate) return _idx.ContainsKey(key);
		}

		/// <summary> Inserts JPEG from src into the pack (if key does not exist). Returns with (offset,length). </summary>
		public (long off, int len) AppendIfMissing(string key, Action<Stream> writeJpeg) {
			lock (_gate) {
				if (_idx.TryGetValue(key, out var e)) return e;
				_fs.Seek(0, SeekOrigin.End);
				long off = _fs.Position;
				using (var limiting = new LengthCountingStream(_fs)) {
					writeJpeg(limiting);
					limiting.Flush();
					int len = checked((int)limiting.BytesWritten);
					_idx[key] = (off, len);
					_dirty = true;
					return (off, len);
				}
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
				return new StreamSlice(rfs, e.off, e.len, leaveOpen: true);
			}
		}
		private static readonly System.Text.Json.JsonSerializerOptions IdxJson = new() { IncludeFields = true };
		public void FlushIndex() {
			lock (_gate) {
				var json = System.Text.Json.JsonSerializer.Serialize(_idx, IdxJson);
				File.WriteAllText(_idxPath, json);
				_dirty = false;
			}
		}

		public void CopyTo(Stream destination) {
			lock (_gate) {
				_fs.Flush();
				_fs.Seek(0, SeekOrigin.Begin);
				_fs.CopyTo(destination);
				_fs.Seek(0, SeekOrigin.End);
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

	internal sealed class LengthCountingStream : Stream {
		readonly Stream _inner;
		public long BytesWritten { get; private set; }
		public LengthCountingStream(Stream inner) => _inner = inner;
		public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
		public override long Length => throw new NotSupportedException();
		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
		public override void Flush() => _inner.Flush();
		public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) {
			_inner.Write(buffer, offset, count);
			BytesWritten += count;
		}

		public override void Write(ReadOnlySpan<byte> buffer) {
			_inner.Write(buffer);
			BytesWritten += buffer.Length;
		}

		public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) {
			BytesWritten += buffer.Length;
			return _inner.WriteAsync(buffer, ct);
		}
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

		public static Avalonia.Media.Imaging.Bitmap GetOrCreate(string key, Func<Avalonia.Media.Imaging.Bitmap> loader) {
			lock (gate) {
				if (map.TryGetValue(key, out var e)) {
					lru.Remove(e.node); lru.AddFirst(e.node); return e.bmp;
				}
			}
			var bmp = loader();
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

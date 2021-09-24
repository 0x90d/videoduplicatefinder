// /*
//     Copyright (C) 2021 0x90d
//     This file is part of VideoDuplicateFinder
//     VideoDuplicateFinder is free software: you can redistribute it and/or modify
//     it under the terms of the GPLv3 as published by
//     the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
//     VideoDuplicateFinder is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU General Public License for more details.
//     You should have received a copy of the GNU General Public License
//     along with VideoDuplicateFinder.  If not, see <http://www.gnu.org/licenses/>.
// */
//

using System.Linq;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace VDF.Core.FFTools.FFmpegNative {
	static class FFmpegHelper {
		private static bool ffmpegLibraryFound;
		public static unsafe string? av_strerror(int error) {
			const int bufferSize = 1024;
			byte* buffer = stackalloc byte[bufferSize];
			ffmpeg.av_strerror(error, buffer, bufferSize).ThrowExceptionIfError();
			string? message = Marshal.PtrToStringAnsi((IntPtr)buffer);
			return message;
		}

		public static int ThrowExceptionIfError(this int error) {
			if (error < 0)
				throw new FFInvalidExitCodeException(av_strerror(error) ?? "Unknown error");
			return error;
		}

		public static AVPixelFormat GetHWPixelFormat(AVHWDeviceType hWDevice) {
			return hWDevice switch {
				AVHWDeviceType.AV_HWDEVICE_TYPE_NONE => AVPixelFormat.AV_PIX_FMT_NONE,
				AVHWDeviceType.AV_HWDEVICE_TYPE_VDPAU => AVPixelFormat.AV_PIX_FMT_VDPAU,
				AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA => AVPixelFormat.AV_PIX_FMT_CUDA,
				AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI => AVPixelFormat.AV_PIX_FMT_VAAPI,
				AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2 => AVPixelFormat.AV_PIX_FMT_NV12,
				AVHWDeviceType.AV_HWDEVICE_TYPE_QSV => AVPixelFormat.AV_PIX_FMT_QSV,
				AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX => AVPixelFormat.AV_PIX_FMT_VIDEOTOOLBOX,
				AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA => AVPixelFormat.AV_PIX_FMT_NV12,
				AVHWDeviceType.AV_HWDEVICE_TYPE_DRM => AVPixelFormat.AV_PIX_FMT_DRM_PRIME,
				AVHWDeviceType.AV_HWDEVICE_TYPE_OPENCL => AVPixelFormat.AV_PIX_FMT_OPENCL,
				AVHWDeviceType.AV_HWDEVICE_TYPE_MEDIACODEC => AVPixelFormat.AV_PIX_FMT_MEDIACODEC,
				_ => AVPixelFormat.AV_PIX_FMT_NONE
			};
		}

		public static bool DoFFmpegLibraryFilesExist {
			get {
				if (ffmpegLibraryFound) return true;
				try {

					string? path = FFToolsUtils.GetPath(FFToolsUtils.FFTool.FFmpeg);
					if (path != null && CheckForFfmpegLibraryFilesInFolder(Path.GetDirectoryName(path)!))
						return true;

					path = Utils.CoreUtils.CurrentFolder;
					if (CheckForFfmpegLibraryFilesInFolder(path))
						return true;

					if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
						string firstLibrary = $"lib{ffmpeg.LibraryVersionMap.Keys.First()}.so.{ffmpeg.LibraryVersionMap.Values.First()}";
						List<string> filesList = Directory.EnumerateFiles("/usr/lib/", firstLibrary, new EnumerationOptions {
							IgnoreInaccessible = true,
							RecurseSubdirectories = true
						}).ToList();
						if (filesList.Count == 0) return false;
						foreach (string file in filesList) {
							string currentDirectory = Path.GetDirectoryName(file)!;
							if (CheckForFfmpegLibraryFilesInFolder(currentDirectory))
								return true;
						}
					}
					var environmentVariables = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator);
					if (environmentVariables == null)
						return false;

					foreach (var environmentPath in environmentVariables) {
						if (!Directory.Exists(environmentPath))
							continue;
						if (CheckForFfmpegLibraryFilesInFolder(environmentPath))
							return true;
					}
					return false;
				}
				catch (Exception e) {
					Utils.Logger.Instance.Info($"Failed to look for ffmpeg libraries: {e}");
					return false;
				}
			}
		}

		static bool CheckForFfmpegLibraryFilesInFolder(string path) {

			foreach (KeyValuePair<string, int> item in ffmpeg.LibraryVersionMap) {
				string libraryName = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ?
					Path.Combine(path, $"lib{item.Key}.so.{item.Value}") :
					Path.Combine(path, $"{item.Key}-{item.Value}.dll");
				if (!File.Exists(libraryName))
					return false;
			}
			ffmpeg.RootPath = path;
			return true;

		}
	}
}

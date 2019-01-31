using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace DuplicateFinderEngine.FFmpegWrapper {
	sealed class FFmpegWrapper : IDisposable {
		private Process FFMpegProcess;
		private readonly TimeSpan ExecutionTimeout = new TimeSpan(0, 0, 15);
		private string InputFile;
		public byte[] GetVideoThumbnail(string inputFile, float frameTime, bool grayScale) {
			InputFile = inputFile;
			var settings = new FFmpegSettings {
				Seek = frameTime,
				//fetching only keyframes is about ~10x faster for hvec videos
				//but it might be confusing the the user, since the timestamps don't neccessarly match
				KeyframeOnly = false, //!grayScale,
				OutputFormat = grayScale ? "rawvideo -pix_fmt gray" : "mjpeg",
				VideoFrameSize = grayScale ? "-s 16x16" : "-vf scale=300:169:force_original_aspect_ratio=decrease",
			};

			return RunFFmpeg(inputFile, settings);
		}

		void WaitFFMpegProcessForExit() {
			if (FFMpegProcess.HasExited) return;

			var milliseconds = (int)ExecutionTimeout.TotalMilliseconds;
			if (FFMpegProcess.WaitForExit(milliseconds)) return;
			EnsureFFMpegProcessStopped();
			Logger.Instance.Info(string.Format(Properties.Resources.FFMpegTimeoutFile, InputFile));
			throw new FFMpegException(-2,
				string.Format(Properties.Resources.FFMpegTimeoutFile, InputFile));
		}
		void EnsureFFMpegProcessStopped() {
			if (FFMpegProcess == null || FFMpegProcess.HasExited) return;
			try {
				FFMpegProcess.Kill();
			}
			catch { }
		}

		internal byte[] RunFFmpeg(string input, FFmpegSettings settings) {
			byte[] data;
			try {
				var onlyKeyframes = settings.KeyframeOnly ? " -skip_frame nokey" : "";
				var arguments = $"-hide_banner -loglevel panic -y -ss {settings.Seek.ToString(CultureInfo.InvariantCulture)}{onlyKeyframes} -i \"{input}\" -f {settings.OutputFormat} -vframes 1 {settings.VideoFrameSize} \"-\"";
				var processStartInfo =
					new ProcessStartInfo(Utils.FfmpegPath, arguments) {
						WindowStyle = ProcessWindowStyle.Hidden,
						CreateNoWindow = true,
						UseShellExecute = false,
						WorkingDirectory = Path.GetDirectoryName(Utils.FfmpegPath) ?? string.Empty,
						RedirectStandardInput = true, //required to avoid ffmpeg printing to console
						RedirectStandardOutput = true,
					};

				if (FFMpegProcess != null)
					throw new InvalidOperationException(Properties.Resources.FFMpegProcessIsAlreadyStarted);

				FFMpegProcess = Process.Start(processStartInfo);
				if (FFMpegProcess == null) {
					Logger.Instance.Info(Properties.Resources.FFMpegProcessWasAborted);
					throw new FFMpegException(-1, Properties.Resources.FFMpegProcessWasAborted);
				}


				var ms = new MemoryStream();
				//start reading here, otherwise the streams fill up and ffmpeg will block forever
				var imgDataTask = FFMpegProcess.StandardOutput.BaseStream.CopyToAsync(ms);

				WaitFFMpegProcessForExit();

				imgDataTask.Wait(ExecutionTimeout);
				data = ms.ToArray();

				FFMpegProcess?.Close();

			}
			catch (Exception) {
				EnsureFFMpegProcessStopped();
				throw;
			}
			return data;
		}

		public void Dispose() => FFMpegProcess?.Dispose();
	}
}

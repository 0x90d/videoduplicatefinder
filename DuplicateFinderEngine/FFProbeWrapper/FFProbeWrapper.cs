using System;
using System.Diagnostics;
using System.IO;

namespace DuplicateFinderEngine.FFProbeWrapper {
	sealed class FFProbeWrapper : IDisposable {
		private Process? FFprobeProcess;
		public TimeSpan ExecutionTimeout => new TimeSpan(0, 0, 15);
		private string? InputFile;

		public MediaInfo? GetMediaInfo(string inputFile) {
			InputFile = inputFile;
			var ms = new MemoryStream();
			try {
				FFprobeProcess = Process.Start(
					new ProcessStartInfo(Utils.FfprobePath,
						$" -hide_banner -loglevel error -print_format json -sexagesimal -show_format -show_streams  \"{inputFile}\"") {
						WindowStyle = ProcessWindowStyle.Hidden,
						CreateNoWindow = true,
						UseShellExecute = false,
						WorkingDirectory = Path.GetDirectoryName(Utils.FfprobePath),
						RedirectStandardInput = false,
						RedirectStandardOutput = true,
					});

				if (FFprobeProcess == null) {
					Logger.Instance.Info(Properties.Resources.FFMpegProcessWasAborted);
					throw new FFProbeException(-1, Properties.Resources.FFprobeProcessWasAborted);
				}

				//start reading here, otherwise the streams fill up and ffmpeg will block forever
				var imgDataTask = FFprobeProcess.StandardOutput.BaseStream.CopyToAsync(ms);

				WaitProcessForExit();

				if (!imgDataTask.Wait((int)ExecutionTimeout.TotalMilliseconds))
					throw new TimeoutException("Copying ffprobe output timed out.");
				if (!ms.TryGetBuffer(out var buf))
					throw new TimeoutException("Failed to get memory buffer.");
				var result = FFProbeJsonReader.Read(buf.AsSpan(), inputFile);
				FFprobeProcess?.Close();
				return result;
			}
			catch (Exception ex) {
				if (FFprobeProcess != null)
					EnsureProcessStopped();
				Logger.Instance.Info(string.Format(Properties.Resources.FFprobeError, ex.Message, inputFile));
				return null;
			}
			finally {
				ms.Dispose();
			}
		}

		

		private void WaitProcessForExit() {
			if (FFprobeProcess == null || FFprobeProcess.HasExited) return;

			var milliseconds = (int)ExecutionTimeout.TotalMilliseconds;
			if (FFprobeProcess.WaitForExit(milliseconds)) return;
			EnsureProcessStopped();
			Logger.Instance.Info(string.Format(Properties.Resources.FFProbeProcessExceededExecutionTimeout, ExecutionTimeout, InputFile));

			throw new FFProbeException(-2,
				string.Format(Properties.Resources.FFProbeProcessExceededExecutionTimeout, ExecutionTimeout, InputFile));
		}

		private void EnsureProcessStopped() {
			if (FFprobeProcess == null || FFprobeProcess.HasExited) return;
			try {
				FFprobeProcess.Kill();
			}
			catch { }
		}
		

		public void Dispose() => FFprobeProcess?.Dispose();
	}
}

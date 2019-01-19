using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace DuplicateFinderEngine.FFProbeWrapper {
	sealed class FFProbeWrapper : IDisposable {
		private Process FFprobeProcess;
		public string CustomArgs { get; set; }
		public TimeSpan ExecutionTimeout => new TimeSpan(0, 0, 15);
		private string InputFile;

		public MediaInfo GetMediaInfo(string inputFile) {
			InputFile = inputFile;
			try {
				var stringBuilder = new StringBuilder();
				stringBuilder.Append(" -hide_banner -loglevel error -print_format json -sexagesimal -show_format -show_streams");
				if (!string.IsNullOrEmpty(CustomArgs)) {
					stringBuilder.Append(CustomArgs);
				}
				stringBuilder.AppendFormat(" \"{0}\" ", inputFile);
				FFprobeProcess = Process.Start(new ProcessStartInfo(Utils.FfprobePath, stringBuilder.ToString()) {
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

				var ms = new MemoryStream();
				//start reading here, otherwise the streams fill up and ffmpeg will block forever
				var imgDataTask = FFprobeProcess.StandardOutput.BaseStream.CopyToAsync(ms);

				WaitProcessForExit();

				imgDataTask.Wait(1000);
				var result = FFProbeJsonReader.Read(ms.ToArray(), inputFile);
				FFprobeProcess?.Close();
				return result;
			}
			catch (Exception ex) {
				if (FFprobeProcess != null)
					EnsureProcessStopped();
				Logger.Instance.Info(string.Format(Properties.Resources.FFprobeError, ex.Message, inputFile));
				return null;
			}
		}



		private void WaitProcessForExit() {
			if (FFprobeProcess.HasExited) return;

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

		private static bool CheckExitCode(int exitCode, string lastErrLine) {
			if (exitCode != 0)
				Trace.TraceError(exitCode + Environment.NewLine + lastErrLine);

			return exitCode == 0;
		}

		public void Dispose() => FFprobeProcess?.Dispose();
	}
}

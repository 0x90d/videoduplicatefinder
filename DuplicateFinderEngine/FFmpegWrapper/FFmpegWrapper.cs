using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace DuplicateFinderEngine.FFmpegWrapper
{
   sealed class FFmpegWrapper
    {
        private Process FFMpegProcess;
        private readonly TimeSpan ExecutionTimeout = new TimeSpan(0, 0, 15);
        public ProcessPriorityClass FFMpegProcessPriority { get; set; } = ProcessPriorityClass.Normal;
        private string InputFile;
        public byte[] GetVideoThumbnail(string inputFile, float? frameTime, bool grayScale)
        {
            InputFile = inputFile;
            var input = new Media
            {
                Filename = inputFile
            };
            var output = new Media
            {
                Format = "mjpeg"
            };
            var settings = new FFmpegSettings
            {
                VideoFrameCount = 1,
                Seek = frameTime,
                MaxDuration = 1f,
            };
            if (grayScale)
            {
                settings.VideoFrameSize = "16x16";
            }
            else
            {
                settings.ResizeWidth = 100;
            }

            return RunFFmpeg(input, output, settings);
        }
        private static string CommandArgParameter(string arg)
        {
            return '"' + arg + '"';
        }

        void WaitFFMpegProcessForExit()
        {
            if (FFMpegProcess == null)
            {
                Logger.Instance.Info(Properties.Resources.FFMpegProcessWasAborted);
                throw new FFMpegException(-1, Properties.Resources.FFMpegProcessWasAborted);
            }
            if (FFMpegProcess.HasExited)
            {
                return;
            }
            var milliseconds = (int)ExecutionTimeout.TotalMilliseconds;
            if (FFMpegProcess.WaitForExit(milliseconds)) return;
            EnsureFFMpegProcessStopped();
            Logger.Instance.Info(string.Format(Properties.Resources.FFMpegTimeoutFile, InputFile));
            throw new FFMpegException(-2,
	            string.Format(Properties.Resources.FFMpegTimeoutFile, InputFile));
        }
        void EnsureFFMpegProcessStopped()
        {
            if (FFMpegProcess == null || FFMpegProcess.HasExited) return;
            try
            {
                FFMpegProcess.Kill();
                FFMpegProcess = null;
            }
            catch (Exception)
            {
            }
        }

        static void BuildFFmpegCommandlineArgs(StringBuilder outputArgs, string outputFormat, FFmpegSettings settings)
        {
            if (settings == null)
            {
                return;
            }
            if (settings.MaxDuration != null)
            {
                outputArgs.AppendFormat(CultureInfo.InvariantCulture, " -t {0}", new object[]
                {
                    settings.MaxDuration
                });
            }
            if (outputFormat != null)
                outputArgs.AppendFormat(" -f {0} ", outputFormat);

            if (settings.ResizeWidth != null)
                outputArgs.AppendFormat(" -vf scale={0}:-1", settings.ResizeWidth);
            if (settings.VideoFrameCount != null)
                outputArgs.AppendFormat(" -vframes {0}", settings.VideoFrameCount);
            if (settings.VideoFrameSize != null)
                outputArgs.AppendFormat(" -s {0}", settings.VideoFrameSize);

        }

        static string BuildFFmpegCommandlineArgs(string inputFile, string inputFormat, string outputFile, string outputFormat, FFmpegSettings settings)
        {
            var stringBuilder = new StringBuilder();

            if (settings.Seek != null)
            {
                stringBuilder.AppendFormat(CultureInfo.InvariantCulture, " -ss {0}", new object[]
                {
                    settings.Seek
                });
            }
            if (inputFormat != null)
            {
                stringBuilder.Append(" -f " + inputFormat);
            }

            var stringBuilder2 = new StringBuilder();
            BuildFFmpegCommandlineArgs(stringBuilder2, outputFormat, settings);

            return
                $"-y {stringBuilder} -i {CommandArgParameter(inputFile)} {stringBuilder2} {CommandArgParameter(outputFile)}";
        }


        internal byte[] RunFFmpeg(Media input, Media output, FFmpegSettings settings)
        {
            try
            {
                var arguments = BuildFFmpegCommandlineArgs(input.Filename, input.Format, "-", output.Format, settings);
                var processStartInfo =
                    new ProcessStartInfo(Utils.FfmpegPath, arguments)
                    {
                        WindowStyle = ProcessWindowStyle.Hidden,
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WorkingDirectory = Path.GetDirectoryName(Utils.FfmpegPath) ?? string.Empty,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                if (FFMpegProcess != null)
                {
                    throw new InvalidOperationException(Properties.Resources.FFMpegProcessIsAlreadyStarted);
                }
                FFMpegProcess = Process.Start(processStartInfo);
                if (FFMpegProcessPriority != ProcessPriorityClass.Normal && FFMpegProcess != null)
                {
                    FFMpegProcess.PriorityClass = FFMpegProcessPriority;
                }

                var ffmpegProgress = new FFMpegProgress();
                if (settings != null)
                {
                    ffmpegProgress.Seek = settings.Seek;
                    ffmpegProgress.MaxDuration = settings.MaxDuration;
                }
				
	            var ms = new MemoryStream();
				//start reading here, otherwise the streams fill up and ffmpeg will block forever
	            var ffmpegLogTask = FFMpegProcess.StandardError.ReadToEndAsync();
	            var imgDataTask = FFMpegProcess.StandardOutput.BaseStream.CopyToAsync(ms);

                WaitFFMpegProcessForExit();

	            var ffmpegLog = ffmpegLogTask.Result;
	            imgDataTask.Wait(1000);
	            output.Bytes = ms.ToArray();
	            
                FFMpegProcess?.Close();
                FFMpegProcess = null;

            }
            catch (Exception)
            {
                EnsureFFMpegProcessStopped();
                throw;
            }
            return output.Bytes;
        }

        static bool IsFileLocked(FileInfo file)
        {
            FileStream stream = null;

            try
            {
                stream = file.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                return true;
            }
            finally
            {
                stream?.Close();
                stream?.Dispose();
            }
            return false;
        }
    }
}

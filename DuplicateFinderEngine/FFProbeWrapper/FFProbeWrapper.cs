using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Xml.XPath;

namespace DuplicateFinderEngine.FFProbeWrapper
{
    sealed class FFProbeWrapper
    {
        public string CustomArgs { get; set; }
        public TimeSpan ExecutionTimeout => new TimeSpan(0, 0, 15);
        
        public MediaInfo GetMediaInfo(string inputFile)
        {
            var info = GetInfoInternal(inputFile);
            return info == null ? null : new MediaInfo(info);
        }

        private XPathDocument GetInfoInternal(string input)
        {

            XPathDocument result;
            Process process = null;
            try
            {
                var stringBuilder = new StringBuilder();
                stringBuilder.Append(" -hide_banner -loglevel error -print_format xml -sexagesimal -show_format -show_streams");
                if (!string.IsNullOrEmpty(CustomArgs))
                {
                    stringBuilder.Append(CustomArgs);
                }
                stringBuilder.AppendFormat(" \"{0}\" ", input);
                process = Process.Start(new ProcessStartInfo(Utils.FfprobePath, stringBuilder.ToString())
                {
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(Utils.FfprobePath),
                    RedirectStandardInput = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });

                var lastErrorLine = new StringBuilder();
                process.ErrorDataReceived += delegate (object o, DataReceivedEventArgs args)
                {
                    if (args.Data == null)
                    {
                        return;
                    }
                    lastErrorLine.AppendLine(args.Data);
                };
                process.BeginErrorReadLine();
                var output = process.StandardOutput.ReadToEnd();
                WaitProcessForExit(process, input);
                if (!CheckExitCode(process.ExitCode, lastErrorLine.ToString()))
                {
                    process.Close();
                    return null;
                }
                process.Close();
                using (var reader = new StringReader(output))
                {
                    result = new XPathDocument(reader);
                }

            }
            catch (Exception ex)
            {
                if (process != null)
                    EnsureProcessStopped(process);
                Logger.Instance.Info(string.Format(Properties.Resources.FFprobeError, ex.Message, input));
                throw new Exception(ex.Message, ex);
            }
            return result;
        }

        private void WaitProcessForExit(Process proc, string input)
        {
            if (proc.WaitForExit((int)ExecutionTimeout.TotalMilliseconds)) return;
            EnsureProcessStopped(proc);
            throw new FFProbeException(-2,
                string.Format(Properties.Resources.FFProbeProcessExceededExecutionTimeout, ExecutionTimeout, input));
        }

        private static void EnsureProcessStopped(Process proc)
        {
            if (!proc.HasExited)
            {
                try
                {
                    proc.Kill();
                    proc.Close();
                    return;
                }
                catch { return; }
            }
            proc.Close();
        }

        private static bool CheckExitCode(int exitCode, string lastErrLine)
        {
            if (exitCode != 0)
            {
                Trace.TraceError(exitCode + Environment.NewLine + lastErrLine);
            }

            return exitCode == 0;
        }
    }
}

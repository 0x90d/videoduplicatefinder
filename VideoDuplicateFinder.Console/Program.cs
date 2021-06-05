using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using DuplicateFinderEngine;

namespace VideoDuplicateFinderConsole {
	class Program {
		static int Main(string[] args) {
			Console.OutputEncoding = Encoding.UTF8;
			var result = new VideoDuplicateFinderConsole().Run(args);
			return result;
		}


		class VideoDuplicateFinderConsole {
			static readonly char PATHS_SEP = Path.PathSeparator;

			readonly ConsoleScanSettings settings = new ConsoleScanSettings();


			public int Run(string[] args) {
				try {
					ParseCommandLine(args);
					if (settings.CleanupDatabase) {
						StartCleanup();
						Console.ReadLine();
						return 0;
					}
					if (settings.IncludeFolders.Count == 0)
						throw new ParseException(Properties.Resources.CmdException_MissingIncludePath);
					EnsureFFFilesExist();
					Console.Error.WriteLine(Environment.NewLine + Environment.NewLine);
					StartScan();
					Console.ReadLine();
				}
				catch (ParseException ex) {
					PrintHelp();
					Console.Error.WriteLine();
					Console.Error.WriteLine(string.Format(Properties.Resources.Cmd_InvalidArgs, ex.Message));
					return 1;
				}
				catch (Exception ex) {
					Console.Error.WriteLine();
					ConsoleHelpers.WriteException(ex);
					return 1;
				}
				return 0;
			}


			static void PrintHelp() {
				Console.Error.WriteLine(Properties.Resources.CmdUsageHeader);
				Console.Error.WriteLine();
				foreach (var info in helpInfos) {
					var arg = info.Option;
					if (info.Args != null)
						arg = arg + " " + info.Args;
					Console.Error.WriteLine("  {0,-12}   {1}", arg, string.Format(info.ArgsDescription, PATHS_SEP));
				}
				Console.Error.WriteLine();
			}

			static void EnsureFFFilesExist() {
				if (!File.Exists(Utils.FfprobePath) && !File.Exists(Utils.FfprobePath + ".exe")) {
					throw new ParseException(RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
						? string.Format(Properties.Resources.CmdException_FFprobeMissingWindows, Utils.FFprobeExecutableName)
						: string.Format(Properties.Resources.CmdException_FFprobeMissingLinux, Utils.FFprobeExecutableName));

				}
				if (!File.Exists(Utils.FfmpegPath) && !File.Exists(Utils.FfmpegPath + ".exe")) {
					throw new ParseException(RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
						? string.Format(Properties.Resources.CmdException_FFprobeMissingWindows, Utils.FFmpegExecutableName)
						: string.Format(Properties.Resources.CmdException_FFprobeMissingLinux, Utils.FFmpegExecutableName));
				}
				if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
					Console.Error.WriteLine("-- Note: When the app crashes with 'The type initializer for 'Gdip' threw an exception ...' exception you will have to install ");
					Console.Error.WriteLine("libgdiplus");
				}

			}

			void StartScan() {
				var engine = new Scanner(settings);
				engine.StartSearch();
			}

			void StartCleanup() {
				var engine = new Scanner(settings);
				engine.StartCleanup();
			}


			readonly struct HelpInfo {
				public string Option { get; }
				public string Args { get; }
				public string ArgsDescription { get; }
				public HelpInfo(string option, string args, string argsDescription) {
					Option = option;
					Args = args;
					ArgsDescription = argsDescription;
				}
			}
			static readonly HelpInfo[] helpInfos = {
				new HelpInfo("-i", Properties.Resources.CmdPath,Properties.Resources.CmdDescription_IncludeFolder),
				new HelpInfo("-e", Properties.Resources.CmdPath,Properties.Resources.CmdDescription_ExcludeFolder),
				new HelpInfo("-r", string.Empty,Properties.Resources.CmdDescription_Recursive),
				new HelpInfo("-q", string.Empty,Properties.Resources.CmdDescription_Quiet),
				new HelpInfo("-clean", string.Empty,Properties.Resources.CmdDescription_Clean),
				new HelpInfo("-j", string.Empty,Properties.Resources.CmdDescription_IncludeImages),
				new HelpInfo("-p", Properties.Resources.CmdFloat,Properties.Resources.CmdDescription_Percent),
				new HelpInfo("-o", Properties.Resources.CmdPath,Properties.Resources.CmdDescription_Output),
				new HelpInfo("--json", string.Empty,Properties.Resources.CmdDescription_json),
			};



			void ParseCommandLine(string[] args) {
				if (args.Length == 0)
					throw new ParseException(Properties.Resources.CmdException_MissingArgs);

				for (int i = 0; i < args.Length; i++) {
					var arg = args[i];
					var next = i + 1 < args.Length ? args[i + 1] : null;
					if (arg.Length == 0)
						continue;

					if (arg[0] == '-') {
						switch (arg) {

						case "-r":
							settings.IsRecursive = true;
							break;

						case "-q":
							settings.IsQuiet = true;
							break;

						case "-j":
							settings.IncludeImages = true;
							break;

						case "-clean":
							settings.CleanupDatabase = true;
							break;

						case "-p":
							if (next == null)
								throw new ParseException(Properties.Resources.CmdException_MissingPercent);
							settings.Percent = ParseFloat(next);
							i++;
							break;

						case "-o":
							if (next == null)
								throw new ParseException(Properties.Resources.CmdException_MissingOutputPath);
							settings.OutputFolder = Path.GetFullPath(next);
							if (!Directory.Exists(settings.OutputFolder))
								throw new ParseException(Properties.Resources.CmdException_PathNotExist);
							i++;
							break;

						case "-i":
							if (next == null)
								throw new ParseException(Properties.Resources.CmdException_MissingIncludePath);
							var path = Path.GetFullPath(next);
							if (!Directory.Exists(path))
								throw new ParseException(Properties.Resources.CmdException_PathNotExist);
							if (!settings.IncludeFolders.Contains(path))
								settings.IncludeFolders.Add(path);
							i++;
							break;

						case "-e":
							if (next == null)
								throw new ParseException(Properties.Resources.CmdException_MissingExcludePath);
							var expath = Path.GetFullPath(next);
							if (!Directory.Exists(expath))
								throw new ParseException(Properties.Resources.CmdException_PathNotExist);
							if (!settings.ExcludeFolders.Contains(expath))
								settings.ExcludeFolders.Add(expath);
							i++;
							break;
						case "--json":
							settings.OutputJson = true;
							break;
						}

						
					}
					else
						throw new ParseException(string.Format(Properties.Resources.Cmd_UnknownCommand, arg));

				}
			}

			static float ParseFloat(string s) {
				if (float.TryParse(s, out float result) && result >= 0f && result <= 100f)
					return result;
				throw new ParseException(string.Format(Properties.Resources.CmdException_InvalidArg, s));
			}
		}


		sealed class ParseException : Exception {
			public ParseException(string message)
				: base(message) {
			}
		}
	}
}

using System;

namespace VideoDuplicateFinderConsole {
	public static class ConsoleHelpers {
		public static void WriteColored(string line, ConsoleColor color) {
			var defaultColor = Console.ForegroundColor;
			Console.ForegroundColor = color;
			Console.Write(line);
			Console.ForegroundColor = defaultColor;
		}
		public static void WriteLineColored(string line, ConsoleColor color) {
			var defaultColor = Console.ForegroundColor;
			Console.ForegroundColor = color;
			Console.WriteLine(line);
			Console.ForegroundColor = defaultColor;
		}

		public static void WriteException(Exception e) {
			var defaultColor = Console.ForegroundColor;
			Console.ForegroundColor = ConsoleColor.Red;
			const string exceptionTitle = "EXCEPTION";
			Console.WriteLine(" ");
			Console.WriteLine(exceptionTitle);
			Console.WriteLine(new string('#', exceptionTitle.Length));
			Console.WriteLine(e.Message);
			Console.ForegroundColor = defaultColor;
			Console.WriteLine();
			Console.WriteLine(e.StackTrace);

		}
	}
}

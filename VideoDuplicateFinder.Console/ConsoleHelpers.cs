using System;

namespace VideoDuplicateFinderConsole {
	public static class ConsoleHelpers {
		public static void WriteException(Exception e) {
			const string exceptionTitle = "EXCEPTION";
			Console.Error.WriteLine(" ");
			Console.Error.WriteLine(exceptionTitle);
			Console.Error.WriteLine(new string('#', exceptionTitle.Length));
			Console.Error.WriteLine(e.Message);
			Console.Error.WriteLine();
			Console.Error.WriteLine(e.StackTrace);

		}
	}
}

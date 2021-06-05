using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DuplicateFinderEngine {
	public static class OutputUtils {
		public static string ToJson<T>(this ICollection<T> listOfClassObjects) {
			if (listOfClassObjects == null || !listOfClassObjects.Any()) return "";

			string json = JsonSerializer.Serialize(listOfClassObjects);
			return json;
		}


		/// <summary>
		/// Prints content of collection to html table
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="listOfClassObjects"></param>
		/// <param name="outputFile"></param>
		/// <returns></returns>
		public static void ToHtmlTable<T>(this ICollection<T> listOfClassObjects, string outputFile) {
			string output = ToHtmlTableString(listOfClassObjects, outputFile);
			File.WriteAllText(outputFile, output);
		}

		/// <summary>
		/// Get contents of collection as html table
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="listOfClassObjects"></param>
		/// <param name="outputFile"></param>
		/// <returns>string containing html representation of collection</returns>
		public static string ToHtmlTableString<T>(this ICollection<T> listOfClassObjects, string outputFile) {
			var sb = new StringBuilder();

			if (listOfClassObjects == null || !listOfClassObjects.Any()) return "";


			//TODO: Add combobox to select the column the search field applies to, UP for volunteers, someone?

			//Some fancy stuff
			sb.Append(@"<!DOCTYPE html>
<html>
<head>
<style>
#duplicates {
  font-family: ""Trebuchet MS"", Arial, Helvetica, sans-serif;
  border-collapse: collapse;
  width: 100%;
}

#duplicates td, #duplicates th {
  border: 1px solid #ddd;
  padding: 8px;
}

#myInput {
  width: 100%; 
  font-size: 16px;
  padding: 12px 20px 12px 40px
  border: 1px solid #ddd;
  margin-bottom: 12px;
}

#duplicates tr:nth-child(even){background-color: #f2f2f2;}

#duplicates tr:hover {background-color: #ddd;}

#duplicates th {
  padding-top: 12px;
  padding-bottom: 12px;
  text-align: left;
  background-color: #2cccc6;
  color: white;
}
</style>
</head>
<body>

<input type=""text"" id=""myInput"" onkeyup=""myFunction()"" placeholder=""Search Path..."">
");
			//Add Rows
			var ret = string.Empty;
			sb.AppendLine("<table id=\"duplicates\">" +
#pragma warning disable CS8602 // Dereference of a possibly null reference.
			   listOfClassObjects.First().GetType().GetProperties().GetDisplayName().ToColumnHeaders() +
#pragma warning restore CS8602 // Dereference of a possibly null reference.
			   listOfClassObjects.Aggregate(ret, (current, t) => current + t.ToHtmlTableRow(outputFile)) +
			   "</table>");

			//Table sorting
			sb.AppendLine(@"<script>
function sortTable(n) {
  var table, rows, switching, i, x, y, shouldSwitch, dir, switchcount = 0;
  table = document.getElementById(""duplicates"");
  switching = true;
  dir = ""asc"";
  while (switching) {
    switching = false;
    rows = table.rows;
    for (i = 1; i < (rows.length - 1); i++) {
      shouldSwitch = false;
      x = rows[i].getElementsByTagName(""TD"")[n];
      y = rows[i + 1].getElementsByTagName(""TD"")[n];
      if (dir == ""asc"") {
        if (x.innerHTML.toLowerCase() > y.innerHTML.toLowerCase()) {
          shouldSwitch = true;
          break;
        }
      } else if (dir == ""desc"") {
        if (x.innerHTML.toLowerCase() < y.innerHTML.toLowerCase()) {
          shouldSwitch = true;
          break;
        }
      }
    }
    if (shouldSwitch) {
      rows[i].parentNode.insertBefore(rows[i + 1], rows[i]);
      switching = true;
      switchcount ++;
    } else {
      if (switchcount == 0 && dir == ""asc"") {
        dir = ""desc"";
        switching = true;
      }
    }
  }
}
</script>");
			//Search function
			sb.AppendLine(@"
<script>
function myFunction() {
  // Declare variables
  var input, filter, table, tr, td, i, txtValue;
  input = document.getElementById(""myInput"");
  filter = input.value.toUpperCase();
  table = document.getElementById(""duplicates"");
  tr = table.getElementsByTagName(""tr"");

  for (i = 0; i < tr.length; i++) {
    td = tr[i].getElementsByTagName(""td"")[1]; // <------------ Search column
    if (td) {
      txtValue = td.textContent || td.innerText;
      if (txtValue.toUpperCase().indexOf(filter) > -1) {
        tr[i].style.display = """";
      } else {
        tr[i].style.display = ""none"";
      }
    }
  }
}
</script>");

			sb.AppendLine(@"
</body>
</html>");

			string output = sb.ToString();
			return output;
			
		}

		public static List<string> GetDisplayName(this IEnumerable<System.Reflection.PropertyInfo> infos) {
			var displayNames = new List<string>();
			foreach (var info in infos) {
				var attribute = info.GetCustomAttributes(typeof(DisplayNameAttribute), true)?
					.Cast<DisplayNameAttribute>().ToList();
				if (attribute.Count == 0) {
					continue;
				}

				displayNames.Add(attribute[0].DisplayName);
			}

			return displayNames;
		}
		public static List<System.Reflection.PropertyInfo> GetPropsWithDisplayName(this IEnumerable<System.Reflection.PropertyInfo> infos) {
			var displayNames = new List<System.Reflection.PropertyInfo>();
			foreach (var info in infos) {
				var attribute = info.GetCustomAttributes(typeof(DisplayNameAttribute), true)?
					.Cast<DisplayNameAttribute>().ToList();
				if (attribute.Count == 0) {
					continue;
				}

				displayNames.Add(info);
			}

			return displayNames;
		}

		private static string ToColumnHeaders<T>(this List<T> listOfProperties) {
			var ret = string.Empty;

			var result = ret;
			for (var i = 0; i < listOfProperties.Count; i++) {
				var property = listOfProperties[i];
				result = result + $"<th onclick=\"sortTable({i})\" style='cursor:pointer'>" + Convert.ToString(property) + "</th>";
			}

			return !listOfProperties.Any()
				? ret
				: "<tr>" +
				  result +
				  "</tr>";
		}

		private static string ToHtmlTableRow<T>(this T classObject, string outputFile) {
			var ret = string.Empty;

			return classObject == null
				? ret
				: "<tr>" +
				  classObject.GetType()
					  .GetProperties().GetPropsWithDisplayName()
					  .Aggregate(ret,
						  (current, prop) =>
							  current + "<td>" + prop.PropValueToString(classObject, outputFile) + " </td>") + "</tr>";
		}

		private static string PropValueToString(this System.Reflection.PropertyInfo property, object classObject, string outputFile) {
#pragma warning disable CS8600, CS8605 // Converting null literal or possible null value to non-nullable type.
			switch (property.Name) {
			case nameof(Data.DuplicateItem.Path):
				var prop = Convert.ToString(property.GetValue(classObject, null));
#pragma warning disable CS8604 // Possible null reference argument.
				return "<a href=\"file:///" + prop + $"\">{Path.GetFullPath(prop)}</a>";
#pragma warning restore CS8604 // Possible null reference argument.

			case nameof(Data.DuplicateItem.Duration):
				return ((TimeSpan)property.GetValue(classObject, null)).TrimMiliseconds().ToString();

			case nameof(Data.DuplicateItem.Thumbnail):
				var l = (List<Image>)property.GetValue(classObject, null);
				if (l == null) return string.Empty;
#pragma warning disable CS8604 // Possible null reference argument.
				var dir = new DirectoryInfo(Utils.SafePathCombine(Path.GetDirectoryName(outputFile), "thumbnails"));
#pragma warning restore CS8604 // Possible null reference argument.
				if (!dir.Exists)
					dir.Create();
				var propValue = string.Empty;
				foreach (var img in l) {
					var imgPath = Utils.SafePathCombine(dir.FullName, Utils.GetRandomNumber() + ".jpeg");
					while (File.Exists(imgPath))
						imgPath = Utils.SafePathCombine(dir.FullName, Utils.GetRandomNumber() + ".jpeg");
					//Create new bitmap to avoid 'A generic error occurred in GDI+,' exception
					using var bmp = new Bitmap(img);
					bmp.Save(imgPath, System.Drawing.Imaging.ImageFormat.Jpeg);
					propValue += $"<img src=\"thumbnails/{Path.GetFileName(imgPath)}\" alt=\"{Path.GetFileName(imgPath)}\" />";
				}
				return propValue;
			}
			return Convert.ToString(property.GetValue(classObject, null)) ?? string.Empty;
#pragma warning restore CS8600, CS8605 // Converting null literal or possible null value to non-nullable type.
		}
	}
}

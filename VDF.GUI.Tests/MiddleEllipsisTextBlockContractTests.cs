// /*
//     Copyright (C) 2026 0x90d
//     This file is part of VideoDuplicateFinder
//     VideoDuplicateFinder is free software: you can redistribute it and/or modify
//     it under the terms of the GNU Affero General Public License as published by
//     the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
//     VideoDuplicateFinder is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU Affero General Public License for more details.
//     You should have received a copy of the GNU Affero General Public License
//     along with VideoDuplicateFinder.  If not, see <http://www.gnu.org/licenses/>.
// */
//

using System.Reflection;
using VDF.GUI.Controls;

namespace VDF.GUI.Tests {
	/// <summary>
	/// The CLR wrappers on MiddleEllipsisTextBlock are the XAML contract for its styled
	/// properties: a wrapper without a setter makes a literal attribute such as
	/// Foreground="Red" on the control fail XAML compilation (AVLN3000), while Style setters
	/// and bindings keep working, so the break only surfaces when someone next writes that
	/// attribute. An "unused accessor" cleanup stripped two of the four in #914 because no
	/// C# caller sets them (the values arrive by property inheritance); this pins all four.
	/// </summary>
	public class MiddleEllipsisTextBlockContractTests {
		[Theory]
		[InlineData(nameof(MiddleEllipsisTextBlock.Text))]
		[InlineData(nameof(MiddleEllipsisTextBlock.Foreground))]
		[InlineData(nameof(MiddleEllipsisTextBlock.FontSize))]
		[InlineData(nameof(MiddleEllipsisTextBlock.FontFamily))]
		public void StyledPropertyWrappers_KeepPublicGetterAndSetter(string name) {
			PropertyInfo? property = typeof(MiddleEllipsisTextBlock).GetProperty(
				name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
			Assert.NotNull(property);
			Assert.True(property!.GetMethod?.IsPublic == true, name + " must keep a public getter");
			Assert.True(property.SetMethod?.IsPublic == true, name + " must keep a public setter so XAML attributes compile");
		}
	}
}

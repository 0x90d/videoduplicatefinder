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

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using DynamicExpresso;
using VDF.Core.ViewModels;

namespace VDF.GUI.Utils {
	/// <summary>Compiles the custom-selection expression ("item.Path.Contains(...)").</summary>
	internal static class SelectionExpression {
		public const string Identifier = "item";

		// Release builds are Native AOT: the trimmer drops every member nothing
		// references statically, but DynamicExpresso resolves members via reflection
		// at runtime - so without these roots even string.Contains fails with
		// "No applicable method 'Contains' exists in type 'String'" (#844). Every
		// type an expression can touch must be listed here.
		[DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.PublicProperties, typeof(string))]
		[DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.PublicProperties, typeof(Regex))]
		[DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.PublicProperties, typeof(Match))]
		[DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(Math))]
		[DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.PublicProperties, typeof(TimeSpan))]
		[DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.PublicProperties, typeof(DateTime))]
		[DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.PublicProperties, typeof(long))]
		[DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.PublicProperties, typeof(int))]
		[DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.PublicProperties, typeof(float))]
		[DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.PublicProperties, typeof(double))]
		// decimal is special: DynamicExpresso probes int->decimal coercion during overload
		// resolution, and that goes through Decimal.op_Implicit - without this root even
		// "item.Duration.Minutes > 15" fails to parse ("Invalid Operation").
		[DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.PublicProperties, typeof(decimal))]
		[DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.PublicProperties, typeof(Guid))]
		[DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.PublicProperties, typeof(DuplicateItem))]
		internal static Func<DuplicateItem, bool> Compile(string expression) {
			// Use PrimitiveTypes only - avoids registering types like Convert, Activator, etc.
			// that could be abused if a malicious expression is loaded from a crafted settings file.
			// Assignments are disabled: "item.IsBestSize = true" (a typo for ==) would otherwise
			// WRITE the property on every item and match everything; now it's a parse error.
			return new Interpreter(InterpreterOptions.PrimitiveTypes | InterpreterOptions.SystemKeywords)
				.EnableAssignment(AssignmentOperators.None)
				.Reference(typeof(TimeSpan))
				.Reference(typeof(Math))
				.Reference(typeof(Regex))
				.ParseAsDelegate<Func<DuplicateItem, bool>>(expression, Identifier);
		}
	}
}

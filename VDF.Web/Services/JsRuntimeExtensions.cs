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

using Microsoft.JSInterop;
using VDF.Core.Utils;

namespace VDF.Web.Services {
	/// <summary>
	/// JS calls that only adjust the page (scroll, theme, compare mode). On Blazor Server the
	/// browser can be gone by the time a call arrives (tab closed, reconnecting), and an
	/// exception escaping an event handler would tear down the whole circuit over a scroll.
	/// </summary>
	public static class JsRuntimeExtensions {
		public static async Task TryInvokeVoidAsync(this IJSRuntime js, string identifier, params object?[] args) {
			try {
				await js.InvokeVoidAsync(identifier, args);
			}
			catch (JSDisconnectedException) { }
			catch (TaskCanceledException) { }
			catch (JSException e) {
				Logger.Instance.Warn($"Web UI script call {identifier} failed: {e.Message}");
			}
		}
	}
}

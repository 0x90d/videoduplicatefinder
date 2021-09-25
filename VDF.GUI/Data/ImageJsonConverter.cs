// /*
//     Copyright (C) 2021 0x90d
//     This file is part of VideoDuplicateFinder
//     VideoDuplicateFinder is free software: you can redistribute it and/or modify
//     it under the terms of the GPLv3 as published by
//     the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
//     VideoDuplicateFinder is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU General Public License for more details.
//     You should have received a copy of the GNU General Public License
//     along with VideoDuplicateFinder.  If not, see <http://www.gnu.org/licenses/>.
// */
//

using System.Text.Json;
using System.Text.Json.Serialization;
using SixLabors.ImageSharp;
using VDF.GUI.Utils;

namespace VDF.GUI.Data {
	internal class ImageJsonConverter : JsonConverter<Image> {
		public override Image Read(
			ref Utf8JsonReader reader,
			Type typeToConvert,
			JsonSerializerOptions options) {
			using var ms = new MemoryStream(reader.GetBytesFromBase64());
			return Image.Load(ms);
		}

		public override void Write(
			Utf8JsonWriter writer,
			Image image,
			JsonSerializerOptions options) =>
				writer.WriteBase64StringValue(image.ToByteArray());
	}
}

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

namespace VDF.TestSupport;

public static class TestModels {
	/// <summary>
	/// Deterministic stand-in for the DINOv2 model: same I/O contract
	/// (pixel_values [B,3,224,224] float → [B,384] float) built from
	/// AveragePool → Flatten → seeded fixed random projection. Content-sensitive
	/// enough for same-frame matching tests, tiny enough to check in.
	/// </summary>
	public static string TinyEmbedderPath =>
		Path.Combine(AppContext.BaseDirectory, "TestData", "tiny-embedder.onnx");
}

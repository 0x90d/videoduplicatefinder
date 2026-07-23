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

namespace VDF.Core {
	[Flags]
	public enum DuplicateFlags : short
	{
		None = 0,
		Flipped = 1,
		PartialClip = 2,  // This item is a partial clip (audio- or AI-matched) of another item in the same group
		AiMatched = 4,    // Found only by the AI embedding pass, not by the classic gray-bytes/pHash check
		// Which classic algorithm(s) matched the pair. Only set by the combined
		// grayscale+pHash mode (#842) - single-algorithm scans would badge every
		// group with the same flag, which carries no information.
		GrayscaleMatched = 8,
		PHashMatched = 16,
	};
}

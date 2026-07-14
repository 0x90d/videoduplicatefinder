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

namespace VDF.GUI.ViewModels {
	/// <summary>
	/// The per-file part of a delete-from-disk operation, extracted so the outcome
	/// decisions are unit-testable with fake file operations.
	/// </summary>
	internal static class DiskDeletion {
		internal enum Outcome {
			/// <summary>The file was deleted (or moved to trash) by this call and is verified gone.</summary>
			Deleted,
			/// <summary>The batched Windows recycle operation already moved this file to the bin.</summary>
			AlreadyRecycled,
			/// <summary>
			/// The file was not found on disk, so only its entry gets removed. Nothing was
			/// deleted; callers must surface this to the user, because for a whole batch it
			/// means an unavailable drive rather than an intended deletion.
			/// </summary>
			MissingEntryOnly,
		}

		/// <summary>Deletes one file from disk and verifies it is actually gone. Throws when it is not.</summary>
		internal static Outcome DeleteOne(string path, bool permanently, bool batchedRecycle, bool batchRecycled,
				Func<string, bool> fileExists, Action<string> deleteFile, Func<string, bool> moveToTrash) {
			if (!fileExists(path))
				return batchRecycled ? Outcome.AlreadyRecycled : Outcome.MissingEntryOnly;
			if (batchedRecycle)
				// The batch ran but this file is still there.
				throw new Exception("the shell did not move the file to the recycle bin");
			if (permanently)
				deleteFile(path);
			else if (!moveToTrash(path)) // Linux/macOS: system trash, fall back to permanent delete
				deleteFile(path);
			if (fileExists(path))
				throw new Exception("the delete call reported success but the file is still on disk");
			return Outcome.Deleted;
		}
	}
}

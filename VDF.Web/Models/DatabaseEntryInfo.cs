// /*
//     Copyright (C) 2026 0x90d
//     This file is part of Video Duplicate Finder
//     Video Duplicate Finder is free software: you can redistribute it and/or modify
//     it under the terms of the GNU Affero General Public License as published by
//     the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
// */

namespace VDF.Web.Models {
    public sealed class DatabaseEntryInfo {
        public string Path { get; init; } = string.Empty;
        public string Folder { get; init; } = string.Empty;
        public long FileSize { get; init; }
        public DateTime DateCreated { get; init; }
        public DateTime DateModified { get; init; }
        public string Flags { get; init; } = string.Empty;
        public bool IsManuallyExcluded { get; init; }
        public bool HasMetadataError { get; init; }
        public bool HasThumbnailError { get; init; }
        public int GrayBytesCount { get; init; }
        public int PHashCount { get; init; }

        public string FileSizeText => FileSize.ToString("N0");
    }
}

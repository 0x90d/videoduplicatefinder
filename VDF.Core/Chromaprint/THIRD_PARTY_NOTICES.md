# Third-Party Notices

## AcoustID.NET

The audio fingerprinting pipeline in this directory (`VDF.Core/Chromaprint/`) is
derived from **AcoustID.NET** by wo80.

- Source: https://github.com/wo80/AcoustID.NET
- NuGet: https://www.nuget.org/packages/AcoustID.NET (v1.3.3)
- License: GNU Lesser General Public License v2.1 (LGPL-2.1)
- Copyright: Copyright (C) wo80

The algorithm classes were copied into VDF.Core as vendored source, retargeted
to net9.0, and modernized (fixed allocations, removed finalizer, replaced FFT).
The original copyright headers are preserved in each file.

Because VDF is published as open source under AGPLv3 on GitHub, the LGPL
requirement to make modified source available is automatically satisfied.

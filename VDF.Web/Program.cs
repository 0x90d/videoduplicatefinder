// /*
//     Copyright (C) 2025 0x90d
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

using VDF.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
	.AddInteractiveServerComponents();

// ScanService is a singleton — one scan at a time, shared across all connections.
builder.Services.AddSingleton<ScanService>();

// Thumbnail endpoint needs HttpContext access
builder.Services.AddHttpContextAccessor();

var app = builder.Build();

if (!app.Environment.IsDevelopment()) {
	app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseAntiforgery();

// Thumbnail streaming endpoint
app.MapGet("/thumbnail", async (HttpContext ctx, ScanService scan) => {
	string? path = ctx.Request.Query["path"];
	if (string.IsNullOrEmpty(path) || !File.Exists(path)) {
		ctx.Response.StatusCode = 404;
		return;
	}

	// Only allow files the scan engine knows about (prevent arbitrary file reads)
	var known = scan.Duplicates.Any(d => d.Path == path);
	if (!known) {
		ctx.Response.StatusCode = 403;
		return;
	}

	string ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
	string mime = ext switch {
		"jpg" or "jpeg" => "image/jpeg",
		"png" => "image/png",
		"gif" => "image/gif",
		"webp" => "image/webp",
		"bmp" => "image/bmp",
		_ => "application/octet-stream"
	};

	ctx.Response.ContentType = mime;
	await ctx.Response.SendFileAsync(path);
});

app.MapRazorComponents<VDF.Web.Components.App>()
	.AddInteractiveServerRenderMode();

app.Run();

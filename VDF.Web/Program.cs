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

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using VDF.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
	.AddInteractiveServerComponents();

builder.Services.AddSingleton<WebSettingsService>();
// ScanService is a singleton — one scan at a time, shared across all connections.
builder.Services.AddSingleton<ScanService>();

var app = builder.Build();

// Route unhandled exceptions from ScanEngine's async void methods (post-await) to ScanService
// so they appear in the UI instead of crashing the process silently.
var scanService = app.Services.GetRequiredService<ScanService>();

AppDomain.CurrentDomain.UnhandledException += (_, e) => {
	var ex = e.ExceptionObject as Exception
		?? new Exception(e.ExceptionObject?.ToString() ?? "Unknown error");
	app.Logger.LogError(ex, "Unhandled exception in background thread");
	scanService.SetError(ex);
};

TaskScheduler.UnobservedTaskException += (_, e) => {
	app.Logger.LogError(e.Exception, "Unobserved task exception");
	scanService.SetError(e.Exception);
	e.SetObserved();
};

if (!app.Environment.IsDevelopment()) {
	app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseAntiforgery();

// Thumbnail endpoint — encodes the extracted thumbnail from DuplicateItem.ImageList.
// Works for both image files and video files (extracted frames).
app.MapGet("/thumbnail", async (HttpContext ctx, ScanService scan) => {
	string? path = ctx.Request.Query["path"];
	if (string.IsNullOrEmpty(path)) {
		ctx.Response.StatusCode = 400;
		return;
	}

	var item = scan.Duplicates.FirstOrDefault(d => d.Path == path);
	if (item == null) {
		ctx.Response.StatusCode = 404;
		return;
	}

	var images = item.ImageList;
	if (images.Count == 0) {
		// Thumbnail not yet retrieved — return 204 so the browser shows nothing
		ctx.Response.StatusCode = 204;
		return;
	}

	ctx.Response.ContentType = "image/jpeg";
	ctx.Response.Headers.CacheControl = "public, max-age=3600";
	await images[0].SaveAsync(ctx.Response.Body, new JpegEncoder { Quality = 85 });
});

app.MapRazorComponents<VDF.Web.Components.App>()
	.AddInteractiveServerRenderMode();

app.Run();

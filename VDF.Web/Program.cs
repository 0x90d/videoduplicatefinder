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

using System.Collections.Concurrent;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using VDF.Core;
using VDF.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
	.AddInteractiveServerComponents();

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<WebSettingsService>();
// ScanService is a singleton — one scan at a time, shared across all connections.
builder.Services.AddSingleton<ScanService>();
builder.Services.AddSingleton<FFmpegSetupService>();

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

// Authentication gate — redirect unauthenticated requests to /login
var authService = app.Services.GetRequiredService<AuthService>();
app.Use(async (ctx, next) => {
	var path = ctx.Request.Path.Value ?? "/";
	// Always allow: login page, static files, Blazor framework resources
	if (!authService.AuthEnabled
		|| path.StartsWith("/login", StringComparison.OrdinalIgnoreCase)
		|| path.StartsWith("/auth/", StringComparison.OrdinalIgnoreCase)
		|| path.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase)
		|| path.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase)
		|| path.StartsWith("/app.css", StringComparison.OrdinalIgnoreCase)
		|| path.StartsWith("/app.js", StringComparison.OrdinalIgnoreCase)) {
		await next();
		return;
	}
	if (!authService.IsAuthenticated(ctx)) {
		var returnUrl = Uri.EscapeDataString(path);
		ctx.Response.Redirect($"/login?returnUrl={returnUrl}");
		return;
	}
	await next();
});

// Login form POST handler — sets the auth cookie (can't do this from Blazor Server interactive mode)
app.MapPost("/auth/login", async (HttpContext ctx, AuthService auth) => {
	var form = await ctx.Request.ReadFormAsync();
	var password = form["password"].ToString();
	var returnUrl = form["returnUrl"].ToString();

	if (auth.ValidatePassword(password)) {
		var token = auth.IssueToken();
		auth.SetAuthCookie(ctx, token);
		ctx.Response.Redirect(string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl);
	}
	else {
		var qs = "?error=1";
		if (!string.IsNullOrEmpty(returnUrl))
			qs += $"&returnUrl={Uri.EscapeDataString(returnUrl)}";
		ctx.Response.Redirect($"/login{qs}");
	}
});

// Thumbnail endpoint — encodes the extracted thumbnail from DuplicateItem.ImageList.
// Works for both image files and video files (extracted frames).
app.MapGet("/thumbnail", async (HttpContext ctx, ScanService scan) => {
	string? path = ctx.Request.Query["path"];
	if (string.IsNullOrEmpty(path)) {
		ctx.Response.StatusCode = 400;
		return;
	}

	path = Path.GetFullPath(path);
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

// HQ thumbnail endpoint — extracts a fresh frame using configurable resolution and quality.
// Used by the card-based results view for crisp thumbnails.
var hqThumbCache = new ConcurrentDictionary<string, byte[]>();
var webSettings = app.Services.GetRequiredService<WebSettingsService>();
app.MapGet("/thumbnail/hq", async (HttpContext ctx, ScanService scan) => {
	string? path = ctx.Request.Query["path"];
	if (string.IsNullOrEmpty(path)) { ctx.Response.StatusCode = 400; return; }

	path = Path.GetFullPath(path);
	var item = scan.Duplicates.FirstOrDefault(d => d.Path == path);
	if (item == null) { ctx.Response.StatusCode = 404; return; }

	int width = Math.Clamp(webSettings.ThumbnailWidth, 48, 960);
	int quality = Math.Clamp(webSettings.ThumbnailJpegQuality, 10, 95);

	var position = item.ThumbnailTimestamps.Count > 0
		? item.ThumbnailTimestamps[0]
		: TimeSpan.FromSeconds(item.Duration.TotalSeconds * 0.1);

	string cacheKey = $"{path}|{position.TotalSeconds:F2}|{width}|{quality}";

	if (!hqThumbCache.TryGetValue(cacheKey, out var jpeg)) {
		jpeg = await Task.Run(() => ScanEngine.ExtractThumbnailJpeg(path, position, width));
		if (jpeg != null && jpeg.Length > 0 && quality < 90) {
			// Re-encode at requested quality if lower than extraction default (90)
			using var ms = new MemoryStream(jpeg);
			using var img = SixLabors.ImageSharp.Image.Load(ms);
			using var outMs = new MemoryStream();
			await img.SaveAsJpegAsync(outMs, new JpegEncoder { Quality = quality });
			jpeg = outMs.ToArray();
		}
		if (jpeg == null || jpeg.Length == 0) { ctx.Response.StatusCode = 204; return; }
		hqThumbCache.TryAdd(cacheKey, jpeg);
	}

	ctx.Response.ContentType = "image/jpeg";
	ctx.Response.Headers.CacheControl = "public, max-age=3600";
	await ctx.Response.Body.WriteAsync(jpeg);
});

// Full-resolution thumbnail endpoint — extracts at original resolution for the comparison modal.
var fullThumbCache = new ConcurrentDictionary<string, byte[]>();
app.MapGet("/thumbnail/full", async (HttpContext ctx, ScanService scan) => {
	string? path = ctx.Request.Query["path"];
	if (string.IsNullOrEmpty(path)) { ctx.Response.StatusCode = 400; return; }

	path = Path.GetFullPath(path);
	var item = scan.Duplicates.FirstOrDefault(d => d.Path == path);
	if (item == null) { ctx.Response.StatusCode = 404; return; }

	var position = item.ThumbnailTimestamps.Count > 0
		? item.ThumbnailTimestamps[0]
		: TimeSpan.FromSeconds(item.Duration.TotalSeconds * 0.1);

	string cacheKey = $"{path}|{position.TotalSeconds:F2}|full";

	if (!fullThumbCache.TryGetValue(cacheKey, out var jpeg)) {
		jpeg = await Task.Run(() => ScanEngine.ExtractThumbnailJpeg(path, position, 0));
		if (jpeg == null || jpeg.Length == 0) { ctx.Response.StatusCode = 204; return; }
		fullThumbCache.TryAdd(cacheKey, jpeg);
	}

	ctx.Response.ContentType = "image/jpeg";
	ctx.Response.Headers.CacheControl = "public, max-age=3600";
	await ctx.Response.Body.WriteAsync(jpeg);
});

app.MapRazorComponents<VDF.Web.Components.App>()
	.AddInteractiveServerRenderMode();

// Kick off FFmpeg availability check / auto-download in background
var ffmpegSetup = app.Services.GetRequiredService<FFmpegSetupService>();
_ = ffmpegSetup.CheckAndSetupAsync();

app.Run();

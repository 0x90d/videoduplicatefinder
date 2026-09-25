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

using Microsoft.AspNetCore.HttpOverrides;
using VDF.Core;
using VDF.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Reverse-proxy support: honor X-Forwarded-Proto so Secure-cookie handling knows
// the original scheme, but only from explicitly trusted proxies (loopback is
// trusted by ASP.NET's defaults). Unknown proxies' headers are ignored, so a
// client cannot spoof the scheme. With no env vars set, behavior for direct and
// plain-HTTP (Docker) deployments is unchanged. Invalid entries only warn —
// a typo in an env var must not crash-loop the container.
var trustedProxies = TrustedProxyParser.Parse(
	Environment.GetEnvironmentVariable("VDF_TRUSTED_PROXIES"),
	Environment.GetEnvironmentVariable("VDF_TRUSTED_PROXY_NETWORKS"));
builder.Services.Configure<ForwardedHeadersOptions>(options => {
	options.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
	options.ForwardLimit = 1;
	foreach (var proxy in trustedProxies.Proxies)
		options.KnownProxies.Add(proxy);
	foreach (var network in trustedProxies.Networks)
		options.KnownIPNetworks.Add(network);
});

// Serve the Blazor framework files (_framework/blazor.server.js) in every environment.
// Only Development loads the build's static web assets manifest by itself, and without
// launchSettings.json a plain `dotnet run` is Production: blazor.server.js 404s and the
// page never goes interactive. Published and Docker builds have no manifest (the files
// are copied to wwwroot), so there this is a no-op.
builder.WebHost.UseStaticWebAssets();

builder.Services.AddRazorComponents()
	.AddInteractiveServerComponents();

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<WebSettingsService>();
// ScanService is a singleton — one scan at a time, shared across all connections.
builder.Services.AddSingleton<ScanService>();
builder.Services.AddSingleton<FFmpegSetupService>();

var app = builder.Build();

foreach (string warning in trustedProxies.Warnings)
	app.Logger.LogWarning("{Warning}", warning);

// Must run before anything that reads Request.Scheme / Request.IsHttps.
app.UseForwardedHeaders();

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
	bool remember = form["remember"] == "true";

	if (auth.ValidatePassword(password)) {
		var token = auth.IssueToken();
		auth.SetAuthCookie(ctx, token, remember);
		ctx.Response.Redirect(string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl);
	}
	else {
		var qs = "?error=1";
		if (!string.IsNullOrEmpty(returnUrl))
			qs += $"&returnUrl={Uri.EscapeDataString(returnUrl)}";
		ctx.Response.Redirect($"/login{qs}");
	}
});

// Frame endpoints of the results page: HQ for the cards, full resolution for the
// comparison modal. Both take the position to show as "t", see ThumbnailEndpoints.
app.MapGet("/thumbnail/hq", (HttpContext ctx, ScanService scan, WebSettingsService webSettings) =>
	ThumbnailEndpoints.Hq(ctx, scan, webSettings));
app.MapGet("/thumbnail/full", (HttpContext ctx, ScanService scan) =>
	ThumbnailEndpoints.Full(ctx, scan));

app.MapRazorComponents<VDF.Web.Components.App>()
	.AddInteractiveServerRenderMode();

// Kick off FFmpeg availability check / auto-download in background
var ffmpegSetup = app.Services.GetRequiredService<FFmpegSetupService>();
_ = ffmpegSetup.CheckAndSetupAsync();

app.Run();

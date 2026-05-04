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

using System.Security.Cryptography;
using System.Text.Json;

namespace VDF.Web.Services {
	/// <summary>
	/// Manages authentication for the WebUI.  On first launch a random password is
	/// generated and printed to the console.  Users can override it via the
	/// VDF_WEB_PASSWORD environment variable or disable auth entirely with VDF_WEB_AUTH=false.
	/// </summary>
	public sealed class AuthService {
		const string CookieName = "vdf_auth";
		const int TokenExpirationDays = 30;
		static readonly TimeSpan CookieMaxAge = TimeSpan.FromDays(TokenExpirationDays);

		readonly string _password;
		readonly bool _authEnabled;
		readonly HashSet<string> _validTokens = new();
		readonly string _credentialsPath;
		readonly ILogger<AuthService> _logger;

		public bool AuthEnabled => _authEnabled;

		public AuthService(ILogger<AuthService> logger) {
			_logger = logger;
			// Allow disabling auth entirely
			var authEnv = Environment.GetEnvironmentVariable("VDF_WEB_AUTH");
			if (string.Equals(authEnv, "false", StringComparison.OrdinalIgnoreCase)) {
				_authEnabled = false;
				_password = string.Empty;
				_credentialsPath = string.Empty;
				return;
			}

			_authEnabled = true;
			_credentialsPath = GetCredentialsPath();

			// Priority: env var > saved file > generate new
			var envPassword = Environment.GetEnvironmentVariable("VDF_WEB_PASSWORD");
			if (!string.IsNullOrWhiteSpace(envPassword)) {
				_password = envPassword;
			}
			else {
				_password = LoadOrGeneratePassword();
			}

			PrintPasswordBanner();
		}

		public bool ValidatePassword(string password) => _password == password;

		public string IssueToken() {
			var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
			lock (_validTokens)
				_validTokens.Add(token);
			return token;
		}

		public bool ValidateToken(string? token) {
			if (string.IsNullOrEmpty(token)) return false;
			lock (_validTokens)
				return _validTokens.Contains(token);
		}

		public bool IsAuthenticated(HttpContext ctx) {
			if (!_authEnabled) return true;
			return ctx.Request.Cookies.TryGetValue(CookieName, out var token) && ValidateToken(token);
		}

		public void SetAuthCookie(HttpContext ctx, string token) {
			ctx.Response.Cookies.Append(CookieName, token, new CookieOptions {
				HttpOnly = true,
				SameSite = SameSiteMode.Strict,
				MaxAge = CookieMaxAge,
				IsEssential = true,
			});
		}

		string LoadOrGeneratePassword() {
			// Try loading saved password
			if (File.Exists(_credentialsPath)) {
				try {
					var json = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(_credentialsPath));
					if (json.TryGetProperty("password", out var saved)) {
						var pw = saved.GetString();
						if (!string.IsNullOrWhiteSpace(pw))
							return pw;
					}
				}
				catch { }
			}

			// Generate new password
			var password = GeneratePassword();
			SavePassword(password);
			return password;
		}

		void SavePassword(string password) {
			try {
				Directory.CreateDirectory(Path.GetDirectoryName(_credentialsPath)!);
				File.WriteAllText(_credentialsPath,
					JsonSerializer.Serialize(new { password }, new JsonSerializerOptions { WriteIndented = true }));
			}
			catch { }
		}

		void PrintPasswordBanner() {
			// Log via ILogger so it shows up in VS Code Debug Console / structured logging
			_logger.LogInformation("============================================");
			_logger.LogInformation("  Web UI password:  {Password}", _password);
			_logger.LogInformation("============================================");

			var envOverride = Environment.GetEnvironmentVariable("VDF_WEB_PASSWORD");
			if (!string.IsNullOrWhiteSpace(envOverride))
				_logger.LogInformation("  (using password from VDF_WEB_PASSWORD environment variable)");
			else
				_logger.LogInformation("  Tip: Set VDF_WEB_PASSWORD environment variable to use your own password.");

			_logger.LogInformation("  Set VDF_WEB_AUTH=false to disable authentication entirely.");

			// Also write to stdout for Docker users (docker logs)
			Console.WriteLine();
			Console.WriteLine("============================================");
			Console.WriteLine($"  Web UI password:  {_password}");
			Console.WriteLine("============================================");
			Console.WriteLine();
		}

		static string GeneratePassword() {
			const string chars = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKMNPQRSTUVWXYZ23456789";
			return string.Create(10, chars, (span, c) => {
				Span<byte> random = stackalloc byte[span.Length];
				RandomNumberGenerator.Fill(random);
				for (int i = 0; i < span.Length; i++)
					span[i] = c[random[i] % c.Length];
			});
		}

		static string GetCredentialsPath() {
			string folder;
			if (OperatingSystem.IsWindows())
				folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VDF");
			else if (OperatingSystem.IsMacOS())
				folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Preferences", "VDF");
			else
				folder = Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
					?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "VDF");
			return Path.Combine(folder, "web-credentials.json");
		}
	}
}

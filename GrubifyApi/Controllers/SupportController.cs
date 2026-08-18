using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace GrubifyApi.Controllers;

/// <summary>
/// Restricted diagnostics for on-call support.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SupportController : ControllerBase
{
    private const int MaximumLogSizeBytes = 1_048_576;
    private const string SupportApiKeyHeader = "X-Support-Api-Key";

    private readonly ILogger<SupportController> _logger;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public SupportController(
        ILogger<SupportController> logger,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        _logger = logger;
        _configuration = configuration;
        _environment = environment;
    }

    /// <summary>
    /// Lets support confirm that an approved upstream host is reachable.
    /// </summary>
    [HttpGet("ping")]
    public async Task<ActionResult> Ping([FromQuery] string host)
    {
        if (!IsAuthorized() ||
            !TryGetAllowedValue("AllowedPingHosts", host, out var allowedHost))
        {
            return NotFound();
        }

        _logger.LogInformation("Support ping requested");

        using var ping = new Ping();
        PingReply reply;
        try
        {
            reply = await ping.SendPingAsync(allowedHost, 5_000);
        }
        catch (PingException)
        {
            return StatusCode(StatusCodes.Status502BadGateway);
        }

        return Ok(new
        {
            host = allowedHost,
            status = reply.Status.ToString(),
            roundTripTimeMs = reply.RoundtripTime,
        });
    }

    /// <summary>
    /// Returns an approved diagnostic log file.
    /// </summary>
    [HttpGet("logs")]
    public async Task<ActionResult> ReadLog([FromQuery] string name)
    {
        if (!IsAuthorized() ||
            !TryGetAllowedValue("AllowedLogFiles", name, out var allowedLogFile))
        {
            return NotFound();
        }

        if (!string.Equals(allowedLogFile, Path.GetFileName(allowedLogFile), StringComparison.Ordinal))
        {
            return BadRequest("invalid log name");
        }

        var logDirectory = Path.Combine(_environment.ContentRootPath, "logs");
        var target = Path.GetFullPath(Path.Combine(logDirectory, allowedLogFile));
        var relativePath = Path.GetRelativePath(logDirectory, target);
        if (relativePath.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relativePath))
        {
            return BadRequest("invalid log name");
        }

        var fileInfo = new FileInfo(target);
        if (!fileInfo.Exists)
        {
            return NotFound();
        }

        if (fileInfo.Length > MaximumLogSizeBytes)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var contents = await System.IO.File.ReadAllTextAsync(target, HttpContext.RequestAborted);
        return Content(contents, "text/plain");
    }

    private bool IsAuthorized()
    {
        var configuredApiKey = _configuration["Support:ApiKey"];
        if (string.IsNullOrEmpty(configuredApiKey) ||
            !Request.Headers.TryGetValue(SupportApiKeyHeader, out var providedApiKey) ||
            providedApiKey.Count != 1 ||
            providedApiKey[0] is null ||
            configuredApiKey.Length != providedApiKey[0]!.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(configuredApiKey),
            Encoding.UTF8.GetBytes(providedApiKey[0]!));
    }

    private bool TryGetAllowedValue(string settingName, string requestedValue, out string allowedValue)
    {
        allowedValue = string.Empty;
        if (string.IsNullOrWhiteSpace(requestedValue))
        {
            return false;
        }

        var configuredValues = _configuration.GetSection($"Support:{settingName}").Get<string[]>();
        var configuredValue = configuredValues?.FirstOrDefault(value =>
            string.Equals(value, requestedValue, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrEmpty(configuredValue))
        {
            return false;
        }

        allowedValue = configuredValue;
        return true;
    }
}

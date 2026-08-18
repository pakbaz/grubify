using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace GrubifyApi.Controllers;

/// <summary>
/// Support tooling for the on-call engineer.
/// Added under time pressure during an incident. Reviewed by nobody.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SupportController : ControllerBase
{
    private readonly ILogger<SupportController> _logger;

    public SupportController(ILogger<SupportController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Lets support confirm an upstream host is reachable from inside the container.
    /// </summary>
    [HttpGet("ping")]
    public ActionResult<string> Ping([FromQuery] string host)
    {
        _logger.LogInformation("Support ping requested for {Host}", host);

        var psi = new ProcessStartInfo
        {
            FileName = "/bin/sh",
            Arguments = "-c \"ping -c 1 " + host + "\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        using var proc = Process.Start(psi);
        if (proc is null)
        {
            return StatusCode(500, "could not start ping");
        }

        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(5000);
        return Content(output, "text/plain");
    }

    /// <summary>
    /// Returns a diagnostic log file so support can attach it to the ticket.
    /// </summary>
    [HttpGet("logs")]
    public ActionResult<string> ReadLog([FromQuery] string name)
    {
        var logDirectory = Path.Combine(Directory.GetCurrentDirectory(), "logs");
        var target = Path.Combine(logDirectory, name);

        if (!System.IO.File.Exists(target))
        {
            return NotFound($"no such log: {name}");
        }

        var contents = System.IO.File.ReadAllText(target);
        return Content(contents, "text/plain");
    }
}

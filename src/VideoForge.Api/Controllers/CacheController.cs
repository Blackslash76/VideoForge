using Microsoft.AspNetCore.Mvc;
using VideoForge.Core.Interfaces;

namespace VideoForge.Api.Controllers;

[ApiController]
[Route("api/cache")]
public class CacheController : ControllerBase
{
    private readonly IAnimationCache _cache;

    public CacheController(IAnimationCache cache)
    {
        _cache = cache;
    }

    [HttpGet("stats")]
    public async Task<ActionResult<CacheStats>> GetStats()
    {
        return await _cache.GetStatsAsync();
    }

    [HttpDelete]
    public async Task<ActionResult> ClearCache([FromQuery] int? maxAgeDays = null)
    {
        var maxAge = maxAgeDays.HasValue ? TimeSpan.FromDays(maxAgeDays.Value) : (TimeSpan?)null;
        await _cache.ClearAsync(maxAge);
        return Ok(new { message = "Cache svuotata" });
    }
}

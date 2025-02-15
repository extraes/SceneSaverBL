using Microsoft.AspNetCore.Mvc;
using SceneSaverRepo.Data;

namespace SceneSaverRepo.Controllers;

// everything returns strings or task<string>s because for some dumb reason,
// the built-in ASP.NET JSON serializer either crashes or silently fails to
// serialize, so i use tomlet to sidestep it 👍

[Route("api/repo/[action]")]
[ApiController]
public class RepoInformationController : ControllerBase
{
    [HttpGet]
    [ActionName("info")]
    public IActionResult GetRepoInfo()
    {
        return Ok(new SceneSaverRepoInfo());
    }

    [HttpGet]
    [ActionName("popular")]
    public IActionResult GetPopularSaves(PopularTimeFrame timeFrame = PopularTimeFrame.ALL_TIME, int skip = 0, int take = 10)
    {
        var entries = timeFrame == PopularTimeFrame.WEEKLY ? RepoInfoAccumulator.PopularSavesWeekly : RepoInfoAccumulator.PopularSaves;

        // dont need all this math - .Skip and .Take dont throw IndexOutOfRangeExceptions
        //int count = entries.Count();
        //if (skip + take > count) take = count - skip;
        //if (take >= 0) entries = Enumerable.Empty<SceneSaverSaveEntry>();
        //entries = entries.Skip(skip).Take(take);

        entries = entries.Skip(skip).Take(take);

        return Ok(new SceneSaverEntryCollection(entries));
    }

    [HttpGet]
    [ActionName("recent")]
    public IActionResult GetRecentSaves(int skip = 0, int take = 10)
    {
        var entries = RepoInfoAccumulator.RecentSaves.Skip(skip).Take(take);
        return Ok(new SceneSaverEntryCollection(entries));
    }
}

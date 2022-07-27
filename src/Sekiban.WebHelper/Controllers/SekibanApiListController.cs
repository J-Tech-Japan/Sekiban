using Microsoft.AspNetCore.Mvc;
namespace Sekiban.WebHelper.Controllers;

[ApiController]
public class SekibanApiListController : ControllerBase
{
    [HttpGet]
    [Route("createCommands")]
    public async Task<IActionResult> CreateCommandList() =>
        Ok(true);
}

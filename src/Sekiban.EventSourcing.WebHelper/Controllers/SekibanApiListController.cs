using Microsoft.AspNetCore.Mvc;
namespace Sekiban.EventSourcing.WebHelper.Controllers;

[ApiController]
public class SekibanApiListController<T> : ControllerBase
{
    [HttpGet]
    [Route("createCommands")]
    public async Task<IActionResult> CreateCommandList() =>
        Ok(true);
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace UnauthorizedApis.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ValuesController : ControllerBase
    {
        public ActionResult<IEnumerable<int>> Get()
        {
            return new[] { 1, 2, 3 };
        }

        [HttpGet("problem")]
        [AllowAnonymous]
        public ActionResult GetProblem()
        {
            return Problem();
        }
    }
}

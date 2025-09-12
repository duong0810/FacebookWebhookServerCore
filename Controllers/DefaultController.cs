using Microsoft.AspNetCore.Mvc;

namespace FacebookWebhookServerCore.Controllers
{
    public class DefaultController : Controller
    {
        [Route("/")]
        [HttpGet]
        public IActionResult Index()
        {
            return Content("<h1>Server is running</h1>", "text/html");
        }

    }
}
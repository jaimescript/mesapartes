using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

using MesaPartes.Models;
using System.Web.Mvc;

namespace MesaPartes.Controllers
{
    public class ErrorsController : Controller
    {
        public ActionResult InvalidUser()
        {
            return PartialView("_InvalidUser");
        }
        public ActionResult UnAuthorizedUser()
        {
            return PartialView("_UNAuthorizedUser");
        }
    }
}
using FutaMeetWeb.Models;
using FutaMeetWeb.Services;
using Microsoft.AspNetCore.Mvc;

namespace FutaMeetWeb.ViewComponents
{
    public class NavBarViewComponent : ViewComponent
    {
        public IViewComponentResult Invoke()
        {
            var matricNo = HttpContext.Session.GetString("MatricNo");
            var model = new NavBarViewModel
            {
                MatricNo = matricNo,
                IsLecturer = !string.IsNullOrEmpty(matricNo) &&
                 MockApiService.GetLecturers().Any(l => l.MatricNo == matricNo),
                IsAdmin = MockApiService.GetUsers().Any(u => u.Role == Role.Admin)
            };
            return View(model);
        }
    }
    public class NavBarViewModel
    {
        public string MatricNo { get; set; }
        public bool IsLecturer { get; set; }
        public bool IsAdmin { get; set; }
    }
}
